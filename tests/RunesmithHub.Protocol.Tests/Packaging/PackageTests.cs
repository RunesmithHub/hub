using System.Buffers.Binary;
using System.IO.Compression;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Protocol.Tests.Support;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Tests.Packaging;

public sealed class PackageTests : IDisposable
{
    private static readonly byte[] Contracts = "contracts assembly"u8.ToArray();
    private static readonly byte[] Implementation = "implementation assembly"u8.ToArray();

    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static PackagedManifest Manifest(bool declaresProcess = false)
    {
        var developer = new DeveloperManifest
        {
            SchemaVersion = 1,
            Id = "lumen.todo",
            Name = "Lumen Todo",
            Version = "1.0.0",
            Summary = "Todos.",
            Authors = ["Lumen"],
            License = "MIT",
            Repository = "https://github.com/lumen/todo",
            RunesmithApi = ">=0.1.0",
            Projects = new ManifestProjects { Contracts = "A.Contracts.csproj", Implementation = "A.csproj" },
            Dependencies = [new ManifestDependency { Id = "harbor.issues", Range = ">=1.2.0" }],
            Capabilities =
            [
                new ManifestCapability { Id = "network", Reason = "Reads issues." },
                .. declaresProcess ? [new ManifestCapability { Id = "process", Reason = "Runs the build host." }] : Array.Empty<ManifestCapability>(),
            ],
            NetworkHosts = ["api.github.com"],
            Icon = "icon.png",
            Categories = ["Productivity"],
        };
        var assemblies = new PackagedAssemblies
        {
            Contracts = new AssemblyFile { File = "Lumen.Todo.Contracts.dll", Sha256 = Hashes.Sha256(Contracts) },
            Implementation = new AssemblyFile { File = "Lumen.Todo.dll", Sha256 = Hashes.Sha256(Implementation) },
        };
        return PackagedManifest.Create(developer, assemblies, new PackageBuild { Commit = new string('c', 40), Run = "local", Sdk = "10.0.400" });
    }

    private static List<PackageFile> ValidFiles() =>
    [
        new("plugin.json", IndexJson.Serialize(Manifest())),
        new("lib/Lumen.Todo.Contracts.dll", Contracts),
        new("lib/Lumen.Todo.dll", Implementation),
        new("icon/64.png", new byte[] { 137, 80, 78, 71 }),
        new("LICENSE", "MIT License"u8.ToArray()),
        new("README.md", "# Lumen Todo"u8.ToArray()),
    ];

    /// <summary>Writes a ZIP without any of the writer's checks, so tests can break the rules.</summary>
    private static MemoryStream RawZip(params (string Name, byte[] Content, int Attributes)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content, attributes) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.ExternalAttributes = attributes;
                using var output = entry.Open();
                output.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream RawZipWith(params string[] names) =>
        RawZip([("plugin.json", IndexJson.Serialize(Manifest()), 0), .. names.Select(name => (name, "x"u8.ToArray(), 0))]);

    private static PackageException Rejects(Stream stream, PackageLimits? limits = null) => Assert.Throws<PackageException>(() => PackageArchive.Open(stream, limits));

    [Fact]
    public void WritesTheSameBytesForTheSameFilesInAnyOrder()
    {
        var files = ValidFiles();

        var first = PackageWriter.Write(files);
        files.Reverse();
        var second = PackageWriter.Write(files);

        Assert.Equal(first, second);
    }

    [Fact]
    public void WritesOnlyTheSpecFieldsOfTheAssemblies()
    {
        var json = System.Text.Encoding.UTF8.GetString(IndexJson.Serialize(Manifest()));

        Assert.DoesNotContain("\"all\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("isBuiltFromRepository", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesEntriesSortedWithFixedTimesAndNoAttributes()
    {
        using var archive = new ZipArchive(new MemoryStream(PackageWriter.Write(ValidFiles())), ZipArchiveMode.Read);

        Assert.Equal(ValidFiles().Select(file => file.Path).Order(StringComparer.Ordinal), archive.Entries.Select(entry => entry.FullName));
        Assert.All(archive.Entries, entry =>
        {
            Assert.Equal(new DateTime(1980, 1, 1, 0, 0, 0), entry.LastWriteTime.DateTime);
            Assert.Equal(0, entry.ExternalAttributes);
        });
    }

    [Fact]
    public void ReadsTheManifestAndExtractsEveryFile()
    {
        using var archive = PackageArchive.Open(new MemoryStream(PackageWriter.Write(ValidFiles())));
        var staging = Path.Combine(_folder.Path, "staging");

        var manifest = archive.ReadManifest();
        archive.ExtractTo(staging);

        Assert.Equal("lumen.todo", manifest.Id);
        Assert.Equal(">=0.1.0 <0.2.0", manifest.RunesmithApi.ToString());
        Assert.Equal(">=1.2.0 <2.0.0", manifest.Dependencies[0].Range.ToString());
        Assert.Equal(Implementation, File.ReadAllBytes(Path.Combine(staging, "lib", "Lumen.Todo.dll")));
        Assert.Empty(PackageVerifier.CheckAssemblies(staging, manifest));
    }

    [Fact]
    public void FindsAssembliesThatDoNotMatchTheManifest()
    {
        using var archive = PackageArchive.Open(new MemoryStream(PackageWriter.Write([.. ValidFiles().Where(f => f.Path != "lib/Lumen.Todo.dll"), new("lib/Lumen.Todo.dll", "changed"u8.ToArray()), new("lib/Extra.dll", "x"u8.ToArray())])));
        var staging = Path.Combine(_folder.Path, "staging");
        archive.ExtractTo(staging);
        File.Delete(Path.Combine(staging, "lib", "Lumen.Todo.Contracts.dll"));

        var problems = PackageVerifier.CheckAssemblies(staging, archive.ReadManifest());

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("Lumen.Todo.dll does not match", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Extra.dll is not listed", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Contracts.dll is listed in the packaged manifest but missing", StringComparison.Ordinal));
    }

    [Fact]
    public void ChecksListedFilesInSubfoldersAndRequiresEveryProgramFileToBeListed()
    {
        byte[] host = "build host"u8.ToArray();
        byte[] settings = "{}"u8.ToArray();
        var manifest = Manifest(declaresProcess: true) with
        {
            Assemblies = Manifest().Assemblies with
            {
                Bundled =
                [
                    new BundledAssembly { File = "BuildHost/Host.exe", Sha256 = Hashes.Sha256(host), Package = "Sample.Host", PackageVersion = "1.0.0" },
                    new BundledAssembly { File = "BuildHost/Host.runtimeconfig.json", Sha256 = Hashes.Sha256(settings), Package = "Sample.Host", PackageVersion = "1.0.0" },
                ],
            },
        };
        List<PackageFile> files =
        [
            .. ValidFiles().Where(file => file.Path != "plugin.json"),
            new("plugin.json", IndexJson.Serialize(manifest)),
            new("lib/BuildHost/Host.exe", host),
            new("lib/BuildHost/Host.runtimeconfig.json", "{ }"u8.ToArray()),
            new("lib/BuildHost/Host.deps.json", settings),
            new("lib/BuildHost/Host.pdb", settings),
        ];
        using var archive = PackageArchive.Open(new MemoryStream(PackageWriter.Write(files)));
        var staging = Path.Combine(_folder.Path, "staging");
        archive.ExtractTo(staging);

        var problems = PackageVerifier.CheckAssemblies(staging, archive.ReadManifest());

        Assert.Equal(["lib/BuildHost/Host.runtimeconfig.json does not match its SHA-256 in the packaged manifest.", "lib/BuildHost/Host.deps.json is not listed in the packaged manifest."], problems);
    }

    [Fact]
    public void RejectsListedFilesOutsideTheLibFolder()
    {
        var manifest = Manifest() with
        {
            Assemblies = Manifest().Assemblies with { Bundled = [new BundledAssembly { File = "../plugin.json", Sha256 = Hashes.Sha256(Contracts) }] },
        };
        var staging = Path.Combine(_folder.Path, "staging");
        Directory.CreateDirectory(Path.Combine(staging, "lib"));
        File.WriteAllBytes(Path.Combine(staging, "lib", "Lumen.Todo.Contracts.dll"), Contracts);
        File.WriteAllBytes(Path.Combine(staging, "lib", "Lumen.Todo.dll"), Implementation);

        var problem = Assert.Single(PackageVerifier.CheckAssemblies(staging, manifest));

        Assert.Contains("not a safe path", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparesTheManifestWithTheVersionRecord()
    {
        var manifest = Manifest();
        var record = Records.Version("1.0.0", dependencies: [("harbor.issues", ">=1.2.0")]) with
        {
            Capabilities = new CapabilityReport { Declared = [new DeclaredCapability { Id = "network", Reason = "Reads issues." }], Detected = ["network"] },
            NetworkHosts = ["api.github.com"],
        };

        Assert.Empty(PackageVerifier.CheckAgainstRecord(manifest, "lumen.todo", record));
        Assert.Equal(5, PackageVerifier.CheckAgainstRecord(manifest, "lumen.tasks", record with
        {
            Version = SemanticVersion.Parse("1.0.1"),
            Dependencies = [],
            Capabilities = new CapabilityReport { Declared = [], Detected = [] },
            NetworkHosts = ["*"],
        }).Count);
        Assert.Equal(["The package's network hosts differ from the record's."], PackageVerifier.CheckAgainstRecord(manifest, "lumen.todo", record with { NetworkHosts = [] }));
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("lib/../../evil.dll")]
    [InlineData("/etc/evil.dll")]
    [InlineData("lib\\evil.dll")]
    [InlineData("C:/evil.dll")]
    [InlineData("lib/./evil.dll")]
    [InlineData("lib//evil.dll")]
    [InlineData("lib/évil.dll")]
    [InlineData("web/../evil.js")]
    [InlineData("web//app.js")]
    [InlineData("web\\app.js")]
    public void RejectsUnsafeNames(string name)
    {
        Assert.Contains(Rejects(RawZipWith(name)).Problems, problem => problem.Contains("not a safe relative path", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsNamesThatDifferOnlyInCase()
    {
        Assert.Contains(Rejects(RawZipWith("lib/Helper.dll", "lib/helper.DLL")).Problems, problem => problem.Contains("more than once", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(unchecked((int)0xA1FF0000))]
    [InlineData(0x400)]
    public void RejectsSymbolicLinks(int attributes)
    {
        var stream = RawZip(("plugin.json", IndexJson.Serialize(Manifest()), 0), ("lib/link.dll", "/etc/passwd"u8.ToArray(), attributes));

        Assert.Contains(Rejects(stream).Problems, problem => problem.Contains("is a link", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnEntryThatCompressesTooWell()
    {
        var stream = RawZip(("plugin.json", IndexJson.Serialize(Manifest()), 0), ("lib/bomb.txt", new byte[1024 * 1024], 0));

        Assert.Contains(Rejects(stream).Problems, problem => problem.Contains("compresses more than 100 to 1", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsTooManyEntries()
    {
        var names = Enumerable.Range(0, PackageLimits.MaxEntries).Select(i => $"lib/f{i}.txt").ToArray();

        Assert.Contains("501 entries", Rejects(RawZipWith(names)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllowsAWebUiOfHundredsOfFiles()
    {
        var names = Enumerable.Range(0, 400).Select(i => $"web/assets/chunk{i}.js").ToArray();

        using var archive = PackageArchive.Open(RawZipWith(names));

        Assert.Equal(401, archive.EntryNames.Count);
    }

    [Theory]
    [InlineData("web/index.html")]
    [InlineData("web/legacy.htm")]
    [InlineData("web/app.css")]
    [InlineData("web/app.js")]
    [InlineData("web/assets/app.mjs")]
    [InlineData("web/assets/app.js.map")]
    [InlineData("web/icons/logo.svg")]
    [InlineData("web/images/a.png")]
    [InlineData("web/images/a.JPG")]
    [InlineData("web/images/a.jpeg")]
    [InlineData("web/images/a.gif")]
    [InlineData("web/images/a.webp")]
    [InlineData("web/favicon.ico")]
    [InlineData("web/fonts/a.woff")]
    [InlineData("web/fonts/a.woff2")]
    [InlineData("web/fonts/a.ttf")]
    [InlineData("web/fonts/a.otf")]
    [InlineData("web/engine.wasm")]
    [InlineData("web/data.json")]
    [InlineData("web/notes.txt")]
    [InlineData("web/media/a.mp3")]
    [InlineData("web/media/a.mp4")]
    [InlineData("web/media/a.webm")]
    public void AcceptsWebPageFilesUnderWeb(string name)
    {
        using var archive = PackageArchive.Open(RawZipWith(name));

        Assert.Contains(name, archive.EntryNames);
    }

    [Theory]
    [InlineData("web/run.sh")]
    [InlineData("web/Lumen.Todo.dll")]
    [InlineData("web/Lumen.Todo.pdb")]
    [InlineData("web/README.md")]
    [InlineData("web/docs.xml")]
    [InlineData("web/Host.exe")]
    [InlineData("web/page.php")]
    [InlineData("web/archive.zip")]
    [InlineData("web/LICENSE")]
    [InlineData("web/.htaccess")]
    public void RejectsOtherFileTypesUnderWeb(string name)
    {
        Assert.Contains(Rejects(RawZipWith(name)).Problems, problem => problem.Contains("file type web pages may not contain", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("app.js")]
    [InlineData("lib/app.js")]
    [InlineData("assets/app.wasm")]
    [InlineData("lib/index.html")]
    public void RejectsWebPageFilesOutsideWeb(string name)
    {
        Assert.Contains(Rejects(RawZipWith(name)).Problems, problem => problem.Contains("file type packages may not contain", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Web/app.js")]
    [InlineData("WEB/index.html")]
    [InlineData("Web/logo.png")]
    public void RejectsTheWebFolderInAnotherCase(string name)
    {
        Assert.Contains(Rejects(RawZipWith(name)).Problems, problem => problem.Contains("spelled in lowercase", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("web/app.js", true)]
    [InlineData("web/a/b.mjs", true)]
    [InlineData("web/engine.wasm", true)]
    [InlineData("web/app.js.map", false)]
    [InlineData("web/index.html", false)]
    [InlineData("lib/app.js", false)]
    public void TellsWebCodeFromOtherFiles(string name, bool isCode)
    {
        Assert.Equal(isCode, PackageEntryRules.IsWebCode(name));
    }

    [Fact]
    public void TheWriterPacksAWebFolder()
    {
        var package = PackageWriter.Write([.. ValidFiles(), new("web/index.html", "<!doctype html>"u8.ToArray()), new("web/app.js", "run();"u8.ToArray())]);

        using var archive = PackageArchive.Open(new MemoryStream(package));
        Assert.Contains("web/index.html", archive.EntryNames);
        Assert.Contains("web/app.js", archive.EntryNames);
        Assert.Throws<PackageException>(() => PackageWriter.Write([.. ValidFiles(), new("web/run.sh", "x"u8.ToArray())]));
    }

    [Fact]
    public void RejectsPackagesAndContentsOverTheSizeLimits()
    {
        var content = new byte[2048];
        Random.Shared.NextBytes(content);
        var stream = RawZip(("plugin.json", IndexJson.Serialize(Manifest()), 0), ("lib/data.txt", content, 0));

        Assert.Contains("unpacks to", Rejects(stream, new PackageLimits { MaxUnpackedBytes = 1024 }).Message, StringComparison.Ordinal);
        stream.Position = 0;
        Assert.Contains("at most 100 are allowed", Rejects(stream, new PackageLimits { MaxPackageBytes = 100 }).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lib/Avalonia.Base.dll")]
    [InlineData("lib/HammerUI.dll")]
    [InlineData("lib/runesmith.sdk.dll")]
    [InlineData("lib/System.Composition.Hosting.dll")]
    public void RejectsCopiesOfHostSharedAssemblies(string name)
    {
        Assert.Contains(Rejects(RawZipWith(name)).Problems, problem => problem.Contains("shares with every plugin", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("lib/run.sh")]
    [InlineData("lib/libgit2.so")]
    [InlineData("lib/native/libgit2.so")]
    [InlineData("lib/native/linux-x64/sub/libgit2.so")]
    [InlineData("README")]
    [InlineData("lib/archive.zip")]
    public void RejectsFileTypesPackagesMayNotContain(string name)
    {
        Assert.Contains(Rejects(RawZipWith(name)).Problems, problem => problem.Contains("file type", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("lib/BuildHost/Host.exe")]
    [InlineData("lib/BuildHost/Host.exe.config")]
    [InlineData("lib/BuildHost/Host.deps.json")]
    [InlineData("lib/BuildHost/Host.runtimeconfig.json")]
    public void AllowsProgramFilesUnderLibOnlyWithTheProcessCapability(string name)
    {
        var without = Rejects(RawZipWith(name));
        using var with = PackageArchive.Open(RawZip(("plugin.json", IndexJson.Serialize(Manifest(declaresProcess: true)), 0), (name, "x"u8.ToArray(), 0)));

        Assert.Contains(without.Problems, problem => problem.Contains("process capability", StringComparison.Ordinal));
        Assert.Contains(name, with.EntryNames);
        Assert.NotNull(PackageEntryRules.Check(name));
        Assert.Null(PackageEntryRules.Check(name, declaresProcess: true));
    }

    [Theory]
    [InlineData("Host.exe")]
    [InlineData("tools/Host.exe")]
    [InlineData("lib/native/linux-x64/Host.exe")]
    public void NeverAllowsProgramFilesOutsideLib(string name)
    {
        Assert.False(PackageEntryRules.IsProcessFile(name));
        Assert.NotNull(PackageEntryRules.Check(name, declaresProcess: true));
    }

    [Fact]
    public void TheWriterAllowsProgramFilesWhenTheManifestDeclaresProcess()
    {
        List<PackageFile> files = [.. ValidFiles().Where(file => file.Path != "plugin.json"), new("lib/BuildHost/Host.exe", "x"u8.ToArray())];

        var allowed = PackageWriter.Write([.. files, new("plugin.json", IndexJson.Serialize(Manifest(declaresProcess: true)))]);

        Assert.NotEmpty(allowed);
        Assert.Throws<PackageException>(() => PackageWriter.Write([.. files, new("plugin.json", IndexJson.Serialize(Manifest()))]));
    }

    [Theory]
    [InlineData("lib/native/linux-x64/libgit2.so")]
    [InlineData("lib/native/osx-arm64/libgit2.dylib")]
    [InlineData("lib/native/win-x64/git2.dll")]
    [InlineData("LICENSE")]
    [InlineData("sbom.cdx.json")]
    [InlineData("THIRD-PARTY-NOTICES.md")]
    [InlineData("lib/Lumen.Todo.xml")]
    [InlineData("icon/128.png")]
    public void AcceptsTheFilesOfThePackageLayout(string name)
    {
        using var archive = PackageArchive.Open(RawZipWith(name));

        Assert.Contains(name, archive.EntryNames);
    }

    [Fact]
    public void RequiresAManifest()
    {
        var stream = RawZip(("lib/A.dll", "x"u8.ToArray(), 0));

        Assert.Contains(Rejects(stream).Problems, problem => problem.Contains("no plugin.json", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsSomethingThatIsNotAZip()
    {
        Rejects(new MemoryStream("not a zip"u8.ToArray()));
    }

    [Fact]
    public void TheWriterRefusesFilesThatBreakTheRules()
    {
        var exception = Assert.Throws<PackageException>(() => PackageWriter.Write([.. ValidFiles(), new("../evil.dll", "x"u8.ToArray()), new("lib/Avalonia.dll", "x"u8.ToArray())]));

        Assert.Equal(2, exception.Problems.Count);
    }

    [Fact]
    public void NeverWritesMoreThanAnEntryDeclares()
    {
        var stream = RawZip(("plugin.json", IndexJson.Serialize(Manifest()), 0), ("lib/data.txt", "0123456789abcdef"u8.ToArray(), 0));
        ShrinkDeclaredSize(stream.GetBuffer(), "lib/data.txt", 4);
        using (var raw = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            Assert.Equal(4, raw.GetEntry("lib/data.txt")!.Length);
        stream.Position = 0;
        using var archive = PackageArchive.Open(stream);
        var staging = Path.Combine(_folder.Path, "staging");

        var exception = Record.Exception(() => archive.ExtractTo(staging));

        Assert.True(exception is PackageException || new FileInfo(Path.Combine(staging, "lib", "data.txt")).Length <= 4);
    }

    [Fact]
    public void ExtractsOnlyIntoAnEmptyFolder()
    {
        using var archive = PackageArchive.Open(new MemoryStream(PackageWriter.Write(ValidFiles())));
        File.WriteAllText(Path.Combine(_folder.Path, "existing.txt"), "x");

        Assert.Throws<IOException>(() => archive.ExtractTo(_folder.Path));
    }

    /// <summary>Rewrites an entry's uncompressed size in its local header and the central directory.</summary>
    private static void ShrinkDeclaredSize(byte[] zip, string name, int size)
    {
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        for (var i = 0; i < zip.Length - 46; i++)
        {
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i));
            if (signature == 0x04034b50 && zip.AsSpan(i + 30, nameBytes.Length).SequenceEqual(nameBytes))
                BinaryPrimitives.WriteInt32LittleEndian(zip.AsSpan(i + 22), size);
            if (signature == 0x02014b50 && zip.AsSpan(i + 46, Math.Min(nameBytes.Length, zip.Length - i - 46)).SequenceEqual(nameBytes))
                BinaryPrimitives.WriteInt32LittleEndian(zip.AsSpan(i + 24), size);
        }
    }
}
