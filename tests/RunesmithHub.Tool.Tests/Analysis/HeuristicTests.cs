using System.Text;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Heuristics;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Analysis;

public sealed class HeuristicTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private HeuristicRuleSet Rules(string json)
    {
        var path = _folder.Combine($"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return HeuristicRuleSet.Read(path);
    }

    private IReadOnlyList<HeuristicMatch> Run(string rules, TestPackage package)
    {
        using var snapshot = PackageSnapshot.Open(package.Write(_folder.Combine($"{Guid.NewGuid():N}.rsplugin")), PackageLimits.Default);
        return HeuristicEngine.Run(Rules(rules), snapshot);
    }

    private static TestPackage Suspicious => new()
    {
        ImplementationSource = """
            namespace Lumen.Todo
            {
                [Obfuscated]
                public sealed class TodoService
                {
                    public const string Hook = "https://hooks.example/webhook/123";
                    public bool Check() => System.Diagnostics.Debugger.IsAttached && new System.Net.Http.HttpClient() is not null;
                    public string Path() => "~/.ssh/id_ed25519";
                }

                public sealed class ObfuscatedAttribute : System.Attribute { }
            }
            """,
        ExtraFiles = [new PackageFile("lib/payload.json", (byte[])[0x4D, 0x5A, 0x90, 0x00, .. Enumerable.Range(0, 300).Select(i => (byte)(i * 151 % 256))])],
    };

    [Fact]
    public void MatchesEveryKindOfRule()
    {
        var matches = Run("""
            {
              "rules": [
                { "id": "ssh-keys", "kind": "string", "pattern": "\\.ssh/" },
                { "id": "webhooks", "kind": "string", "pattern": "webhook" },
                { "id": "random", "kind": "entropy", "minLength": 200, "minBitsPerByte": 7.5 },
                { "id": "executables", "kind": "magic", "signatures": ["4D5A", "7F454C46"] },
                { "id": "obfuscators", "kind": "attribute", "names": ["ObfuscatedAttribute"] },
                { "id": "evasion", "kind": "api-combination", "apis": ["System.Diagnostics.Debugger.get_IsAttached", "System.Net.Http.*"] },
                { "id": "never", "kind": "string", "pattern": "nothing-like-this" }
              ]
            }
            """, Suspicious);

        Assert.Equal(["ssh-keys", "webhooks", "random", "executables", "obfuscators", "evasion"], matches.Select(match => match.Rule).Distinct());
        Assert.Contains(new HeuristicMatch("ssh-keys", "lib/Lumen.Todo.dll", "Lumen.Todo.TodoService.Path", "~/.ssh/id_ed25519"), matches);
        Assert.Contains(matches, match => match.Rule == "webhooks" && match.Location == "Lumen.Todo.TodoService.Hook");
        Assert.Contains(matches, match => match.Rule == "executables" && match.File == "lib/payload.json");
    }

    [Fact]
    public void MatchesResourcesByNameAndSize()
    {
        var library = Compiler.CompileWithResource("Helpers", "namespace Helpers { public sealed class Helper { } }", "Helpers.payload.bin", new byte[4096]);
        var package = new TestPackage { Bundled = [("Helpers", library, null, null)] };

        var matches = Run("""{ "rules": [{ "id": "large", "kind": "resource", "pattern": "payload", "minBytes": 4096 }, { "id": "huge", "kind": "resource", "minBytes": 5000 }] }""", package);

        Assert.Equal([new HeuristicMatch("large", "lib/Helpers.dll", "resource Helpers.payload.bin", "4096 bytes")], matches);
    }

    [Fact]
    public void MeasuresEntropyInBitsPerByte()
    {
        Assert.Equal(0, HeuristicEngine.Entropy(Encoding.UTF8.GetBytes(new string('x', 2000))));
        Assert.Equal(8, HeuristicEngine.Entropy([.. Enumerable.Range(0, 256).Select(i => (byte)i)]), 6);
    }

    [Theory]
    [InlineData("""{ "rules": [{ "id": "a", "kind": "string" }] }""", "needs pattern")]
    [InlineData("""{ "rules": [{ "id": "a", "kind": "magic", "signatures": ["XYZ"] }] }""", "not valid")]
    [InlineData("""{ "rules": [{ "id": "a", "kind": "string", "pattern": "(" }] }""", "not valid")]
    [InlineData("""{ "rules": [{ "id": "a", "kind": "string", "pattern": "a" }, { "id": "a", "kind": "string", "pattern": "b" }] }""", "unique")]
    [InlineData("""{ "rules": [{ "id": "a", "kind": "string", "pattern": "a", "typo": 1 }] }""", "not valid")]
    [InlineData("""{ "rules": [{ "id": "a", "kind": "unknown" }] }""", "not valid")]
    public void RejectsInvalidRules(string json, string message)
    {
        var exception = Assert.Throws<ToolException>(() => Rules(json));

        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }
}
