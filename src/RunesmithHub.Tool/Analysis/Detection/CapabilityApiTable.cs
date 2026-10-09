using Mono.Cecil;
using RunesmithHub.Protocol;
using static RunesmithHub.Protocol.Capabilities;

namespace RunesmithHub.Tool.Analysis.Detection;

/// <summary>The APIs whose use means a capability. Constructs that are not calls, such as platform invoke, are detected by the scanner.</summary>
internal static class CapabilityApiTable
{
    public static IReadOnlyList<CapabilityApi> Apis { get; } =
    [
        new(Network, "System.Net.Http.*"),
        new(Network, "System.Net.Sockets.*"),
        new(Network, "System.Net.WebSockets.*"),
        new(Network, "System.Net.Quic.*"),
        new(Network, "System.Net.Dns"),
        new(Network, "System.Net.WebClient"),
        new(Network, "System.Net.WebRequest"),
        new(Network, "System.Net.HttpWebRequest"),
        new(Network, "System.Net.FtpWebRequest"),
        new(Network, "System.Net.HttpListener"),
        new(Network, "System.Net.Security.SslStream"),
        new(Network, "System.Net.Mail.SmtpClient"),
        new(Network, "System.Net.NetworkInformation.Ping"),

        new(Process, "System.Diagnostics.Process", "Start"),
        new(Process, "System.Diagnostics.Process", ".ctor"),
        new(Process, "Runesmith.Sdk.Shell.ILauncher"),

        new(FileSystem, "System.IO.File"),
        new(FileSystem, "System.IO.Directory"),
        new(FileSystem, "System.IO.FileInfo"),
        new(FileSystem, "System.IO.DirectoryInfo"),
        new(FileSystem, "System.IO.FileSystemInfo"),
        new(FileSystem, "System.IO.FileStream", ".ctor"),
        new(FileSystem, "System.IO.StreamReader", ".ctor", ApiCondition.FirstParameterIsString),
        new(FileSystem, "System.IO.StreamWriter", ".ctor", ApiCondition.FirstParameterIsString),
        new(FileSystem, "System.IO.FileSystemWatcher"),
        new(FileSystem, "System.IO.RandomAccess"),
        new(FileSystem, "System.IO.Path", "GetTempFileName"),
        new(FileSystem, "System.IO.MemoryMappedFiles.*"),
        new(FileSystem, "System.IO.Compression.ZipFile"),
        new(FileSystem, "System.IO.Compression.ZipFileExtensions"),

        new(Capabilities.Environment, "System.Environment", "GetEnvironmentVariable"),
        new(Capabilities.Environment, "System.Environment", "GetEnvironmentVariables"),
        new(Capabilities.Environment, "System.Environment", "SetEnvironmentVariable"),
        new(Capabilities.Environment, "System.Environment", "ExpandEnvironmentVariables"),
        new(Capabilities.Environment, "System.Environment", "GetFolderPath"),
        new(Capabilities.Environment, "System.Environment", "GetLogicalDrives"),
        new(Capabilities.Environment, "System.Environment", "get_UserName"),
        new(Capabilities.Environment, "System.Environment", "get_UserDomainName"),
        new(Capabilities.Environment, "System.Environment", "get_MachineName"),
        new(Capabilities.Environment, "Microsoft.Win32.Registry"),
        new(Capabilities.Environment, "Microsoft.Win32.RegistryKey"),

        new(Credentials, "Runesmith.Sdk.Shell.ISecretStore"),

        new(Native, "System.Runtime.InteropServices.NativeLibrary"),
        new(Native, "System.Runtime.InteropServices.Marshal"),
        new(Native, "System.Runtime.InteropServices.NativeMemory"),

        new(DynamicCode, "System.Reflection.Assembly", "Load"),
        new(DynamicCode, "System.Reflection.Assembly", "LoadFrom"),
        new(DynamicCode, "System.Reflection.Assembly", "LoadFile"),
        new(DynamicCode, "System.Reflection.Assembly", "UnsafeLoadFrom"),
        new(DynamicCode, "System.Reflection.Assembly", "LoadWithPartialName"),
        new(DynamicCode, "System.Reflection.Assembly", "ReflectionOnlyLoad"),
        new(DynamicCode, "System.Reflection.Assembly", "ReflectionOnlyLoadFrom"),
        new(DynamicCode, "System.Reflection.Assembly", "GetType", ApiCondition.FirstParameterIsString),
        new(DynamicCode, "System.Reflection.Assembly", "CreateInstance"),
        new(DynamicCode, "System.AppDomain", "Load"),
        new(DynamicCode, "System.AppDomain", "add_AssemblyResolve"),
        new(DynamicCode, "System.AppDomain", "GetAssemblies"),
        new(DynamicCode, "System.Runtime.Loader.*"),
        new(DynamicCode, "System.Reflection.Emit.*"),
        new(DynamicCode, "System.Linq.Expressions.LambdaExpression", "Compile"),
        new(DynamicCode, "System.Linq.Expressions.Expression`1", "Compile"),
        new(DynamicCode, "System.Type", "GetType", ApiCondition.FirstParameterIsString),
        new(DynamicCode, "System.Activator", "CreateInstance", ApiCondition.FirstParameterIsString),
        new(DynamicCode, "System.Activator", "CreateInstanceFrom"),
        new(DynamicCode, "System.Reflection.TypeInfo", "get_DeclaredMembers"),
        new(DynamicCode, "System.Reflection.TypeInfo", "get_DeclaredMethods"),
        new(DynamicCode, "System.Reflection.TypeInfo", "get_DeclaredFields"),
        new(DynamicCode, "System.Reflection.TypeInfo", "get_DeclaredProperties"),
        new(DynamicCode, "System.Reflection.TypeInfo", "get_DeclaredConstructors"),
        new(DynamicCode, "System.Reflection.TypeInfo", "GetDeclaredMethod"),
        new(DynamicCode, "System.Reflection.TypeInfo", "GetDeclaredField"),
        new(DynamicCode, "System.Reflection.TypeInfo", "GetDeclaredProperty"),
    ];

    /// <summary>Gets the reflection lookups that reach non-public members when given <c>BindingFlags.NonPublic</c>.</summary>
    public static IReadOnlyList<string> ReflectionLookupTypes { get; } = ["System.Type", "System.Reflection.TypeInfo"];

    /// <summary>Gets the capability a called method means, or null when it means none.</summary>
    public static CapabilityApi? Match(MethodReference method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var typeName = TypeName(method.DeclaringType);
        return Apis.FirstOrDefault(api => api.Matches(typeName, method));
    }

    /// <summary>Gets a type's full name without generic arguments, with <c>.</c> between nested types.</summary>
    public static string TypeName(TypeReference type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return (type is GenericInstanceType generic ? generic.ElementType : type).FullName.Replace('/', '.');
    }
}
