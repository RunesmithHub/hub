using Mono.Cecil;
using Mono.Cecil.Cil;
using static RunesmithHub.Protocol.Capabilities;

namespace RunesmithHub.Tool.Analysis.Detection;

/// <summary>
/// Finds capability use in compiled code: calls to the APIs in <see cref="CapabilityApiTable"/>, platform invoke, function pointer calls,
/// unsafe code, private member access and reflection over non-public members, and native libraries in the package.
/// </summary>
internal static class CapabilityScanner
{
    private const int NonPublicBindingFlag = 0x20;
    private const int BindingFlagsLookBehind = 6;
    private const string UnverifiableCode = "System.Security.UnverifiableCodeAttribute";
    private const string UnsafeAccessor = "System.Runtime.CompilerServices.UnsafeAccessorAttribute";

    public static DetectedCapabilities Scan(IEnumerable<LoadedAssembly> assemblies, IEnumerable<string> nativeLibraries)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(nativeLibraries);
        var detected = new DetectedCapabilities();
        foreach (var assembly in assemblies)
            Scan(assembly, detected);
        foreach (var library in nativeLibraries)
            detected.Add(Native, new CallSite(library, library, "a native library in the package"));
        return detected;
    }

    private static void Scan(LoadedAssembly assembly, DetectedCapabilities detected)
    {
        foreach (var module in assembly.Definition.Modules)
        {
            if (module.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == UnverifiableCode))
                detected.Add(Native, new CallSite(assembly.File, assembly.Name, "unsafe code"));

            foreach (var method in module.GetTypes().SelectMany(type => type.Methods))
            {
                if (method.IsPInvokeImpl)
                    detected.Add(Native, new CallSite(assembly.File, MemberNames.Of(method), $"platform invoke into {method.PInvokeInfo?.Module?.Name ?? "a native library"}"));
                if (method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == UnsafeAccessor))
                    detected.Add(DynamicCode, new CallSite(assembly.File, MemberNames.Of(method), "an unsafe accessor to private members"));
                if (method.HasBody)
                    ScanBody(assembly, method, detected);
            }
        }
    }

    private static void ScanBody(LoadedAssembly assembly, MethodDefinition method, DetectedCapabilities detected)
    {
        var instructions = method.Body.Instructions;
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (instruction.OpCode.Code == Code.Calli)
            {
                detected.Add(Native, new CallSite(assembly.File, MemberNames.Of(method), "a function pointer call"));
                continue;
            }

            if (instruction.Operand is not MethodReference called || !IsCallLike(instruction.OpCode.Code))
                continue;
            if (CapabilityApiTable.Match(called) is { } api)
                detected.Add(api.Capability, new CallSite(assembly.File, MemberNames.Of(method), MemberNames.Of(called)));
            else if (IsNonPublicReflection(called, instructions, i))
                detected.Add(DynamicCode, new CallSite(assembly.File, MemberNames.Of(method), $"{MemberNames.Of(called)} with BindingFlags.NonPublic"));
        }
    }

    private static bool IsCallLike(Code code) => code is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn;

    private static bool IsNonPublicReflection(MethodReference called, Mono.Collections.Generic.Collection<Instruction> instructions, int index)
    {
        if (!CapabilityApiTable.ReflectionLookupTypes.Contains(CapabilityApiTable.TypeName(called.DeclaringType))
            || !called.Parameters.Any(parameter => parameter.ParameterType.FullName == "System.Reflection.BindingFlags"))
            return false;

        for (var i = index - 1; i >= Math.Max(0, index - BindingFlagsLookBehind); i--)
        {
            if (Constant(instructions[i]) is { } value && (value & NonPublicBindingFlag) != 0)
                return true;
        }

        return false;
    }

    private static int? Constant(Instruction instruction) => instruction.OpCode.Code switch
    {
        Code.Ldc_I4 => (int)instruction.Operand,
        Code.Ldc_I4_S => (sbyte)instruction.Operand,
        _ => null,
    };
}
