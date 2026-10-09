using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Detection;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Analysis;

public sealed class CapabilityScannerTests
{
    private static DetectedCapabilities Scan(string body) => CapabilityScanner.Scan([Assemblies.Method(body)], []);

    [Theory]
    [InlineData("new System.Net.Http.HttpClient();", "network", "System.Net.Http.HttpClient..ctor")]
    [InlineData("new System.Net.Sockets.TcpClient();", "network", "System.Net.Sockets.TcpClient..ctor")]
    [InlineData("System.Net.Dns.GetHostAddresses(\"example.com\");", "network", "System.Net.Dns.GetHostAddresses")]
    [InlineData("System.Diagnostics.Process.Start(\"git\");", "process", "System.Diagnostics.Process.Start")]
    [InlineData("launcher.Open(\"https://example.com\");", "process", "Runesmith.Sdk.Shell.ILauncher.Open")]
    [InlineData("System.IO.File.ReadAllText(\"a.txt\");", "filesystem", "System.IO.File.ReadAllText")]
    [InlineData("new System.IO.StreamReader(\"a.txt\");", "filesystem", "System.IO.StreamReader..ctor")]
    [InlineData("new System.IO.FileSystemWatcher();", "filesystem", "System.IO.FileSystemWatcher..ctor")]
    [InlineData("Environment.GetEnvironmentVariable(\"HOME\");", "environment", "System.Environment.GetEnvironmentVariable")]
    [InlineData("return Environment.UserName;", "environment", "System.Environment.get_UserName")]
    [InlineData("secrets.GetAsync(\"lumen.todo/token\");", "credentials", "Runesmith.Sdk.Shell.ISecretStore.GetAsync")]
    [InlineData("System.Runtime.InteropServices.NativeLibrary.Load(\"git2\");", "native", "System.Runtime.InteropServices.NativeLibrary.Load")]
    [InlineData("System.Runtime.InteropServices.Marshal.AllocHGlobal(4);", "native", "System.Runtime.InteropServices.Marshal.AllocHGlobal")]
    [InlineData("Assembly.LoadFrom(\"other.dll\");", "dynamic-code", "System.Reflection.Assembly.LoadFrom")]
    [InlineData("return Type.GetType(\"Other.Plugin, Other\");", "dynamic-code", "System.Type.GetType")]
    [InlineData("return Activator.CreateInstance(\"Other\", \"Other.Plugin\");", "dynamic-code", "System.Activator.CreateInstance")]
    [InlineData("new System.Runtime.Loader.AssemblyLoadContext(\"mine\");", "dynamic-code", "System.Runtime.Loader.AssemblyLoadContext..ctor")]
    [InlineData("new System.Reflection.Emit.DynamicMethod(\"m\", null, null);", "dynamic-code", "System.Reflection.Emit.DynamicMethod..ctor")]
    [InlineData("System.Linq.Expressions.Expression.Lambda<Func<int>>(System.Linq.Expressions.Expression.Constant(1)).Compile();", "dynamic-code", "System.Linq.Expressions.Expression`1.Compile")]
    [InlineData("return typeof(string).GetField(\"_length\", BindingFlags.NonPublic | BindingFlags.Instance);", "dynamic-code", "System.Type.GetField with BindingFlags.NonPublic")]
    public void DetectsTheCapabilityAnApiMeans(string body, string capability, string api)
    {
        var detected = Scan(body);

        Assert.Equal([capability], detected.Ids);
        Assert.Equal(new CallSite("Sample.dll", "Sample.Code.Run", api), Assert.Single(detected.SitesOf(capability)));
    }

    [Theory]
    [InlineData("return typeof(string).GetMethod(\"Trim\", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);")]
    [InlineData("return Activator.CreateInstance(typeof(object));")]
    [InlineData("return new System.IO.StreamReader(new System.IO.MemoryStream());")]
    [InlineData("return Environment.ProcessorCount;")]
    [InlineData("return System.Diagnostics.Process.GetCurrentProcess().Id;")]
    [InlineData("return System.IO.Path.Combine(\"a\", \"b\");")]
    public void IgnoresApisThatNeedNoCapability(string body) => Assert.Empty(Scan(body).Ids);

    [Fact]
    public void DetectsPlatformInvokeFunctionPointersAndUnsafeCode()
    {
        var assembly = Assemblies.Load(Compiler.Compile("Native", """
            namespace Native
            {
                public static unsafe class Calls
                {
                    [System.Runtime.InteropServices.DllImport("libc")]
                    private static extern int getpid();

                    public static void Call(delegate* unmanaged<void> pointer) => pointer();
                }
            }
            """, unsafeCode: true));

        var sites = CapabilityScanner.Scan([assembly], ["lib/native/linux-x64/libgit2.so"]).SitesOf("native");

        Assert.Contains(new CallSite("Native.dll", "Native", "unsafe code"), sites);
        Assert.Contains(new CallSite("Native.dll", "Native.Calls.getpid", "platform invoke into libc"), sites);
        Assert.Contains(new CallSite("Native.dll", "Native.Calls.Call", "a function pointer call"), sites);
        Assert.Contains(new CallSite("lib/native/linux-x64/libgit2.so", "lib/native/linux-x64/libgit2.so", "a native library in the package"), sites);
    }

    [Fact]
    public void NamesAsyncMethodsAndLambdasAsTheSourceDoes()
    {
        var assembly = Assemblies.Load(Compiler.Compile("Sample", """
            namespace Sample
            {
                public sealed class Client
                {
                    public async System.Threading.Tasks.Task<string> FetchAsync() => await new System.Net.Http.HttpClient().GetStringAsync("https://example.com");

                    public System.Func<string> Reader() => () => System.IO.File.ReadAllText("a.txt");
                }
            }
            """));

        var detected = CapabilityScanner.Scan([assembly], []);

        Assert.All(detected.SitesOf("network"), site => Assert.Equal("Sample.Client.FetchAsync", site.Member));
        Assert.Equal("Sample.Client.Reader", Assert.Single(detected.SitesOf("filesystem")).Member);
    }

    [Fact]
    public void IgnoresNonPublicReflectionOnItsOwnTypesInGeneratedJsonMetadata()
    {
        var detected = ScanSource("""
            namespace Sample
            {
                public sealed class Memory { private string Name { get; set; } = ""; }

                internal sealed class MemoryJson
                {
                    private const BindingFlags InstanceMemberBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                    public Func<ICustomAttributeProvider?> NameProvider() =>
                        static () => typeof(Memory).GetProperty("Name", InstanceMemberBindingFlags, null, typeof(string), Array.Empty<Type>(), null);
                }
            }
            """);

        Assert.Empty(detected.Ids);
    }

    [Theory]
    [InlineData("return typeof(Sample.Secret).GetField(\"value\", BindingFlags.NonPublic | BindingFlags.Instance);")]
    [InlineData("return typeof(Sample.Contracts.Shared).GetField(\"value\", BindingFlags.NonPublic | BindingFlags.Instance);")]
    public void IgnoresNonPublicReflectionOnTypesItDefines(string body) => Assert.Empty(ScanSource(Own(body)).Ids);

    [Theory]
    [InlineData("return typeof(Other.Hidden).GetField(\"value\", BindingFlags.NonPublic | BindingFlags.Instance);")]
    [InlineData("return new Sample.Secret().GetType().GetField(\"value\", BindingFlags.NonPublic | BindingFlags.Instance);")]
    [InlineData("return type.GetField(\"value\", BindingFlags.NonPublic | BindingFlags.Instance);")]
    [InlineData("static object? Find<T>() => typeof(T).GetField(\"value\", BindingFlags.NonPublic | BindingFlags.Instance); return Find<Sample.Secret>();")]
    [InlineData("return (flag ? typeof(Sample.Secret) : type).GetField(\"value\", BindingFlags.NonPublic | BindingFlags.Instance);")]
    public void DetectsNonPublicReflectionItCannotProveIsOnItsOwnTypes(string body) =>
        Assert.Equal(["dynamic-code"], ScanSource(Own(body)).Ids);

    private static string Own(string body) => $$"""
        namespace Sample
        {
            public sealed class Secret { private int value; public int Value => value; }

            public static class Code
            {
                public static object? Run(Type type, bool flag)
                {
                    {{body}}
                }

            }
        }
        """;

    private static DetectedCapabilities ScanSource(string source)
    {
        var contracts = Compiler.Compile("Sample.Contracts", "namespace Sample.Contracts { public sealed class Shared { private int value; public int Value => value; } }");
        var other = Compiler.Compile("Other", "namespace Other { public sealed class Hidden { private int value; public int Value => value; } }");
        var implementation = Compiler.Compile("Sample", "using System;\nusing System.Reflection;\n" + source, contracts, other);
        return CapabilityScanner.Scan(
            [Assemblies.Load(implementation), Assemblies.Load(contracts, AssemblyRole.Contracts), Assemblies.Load(other, AssemblyRole.Bundled)], []);
    }
}
