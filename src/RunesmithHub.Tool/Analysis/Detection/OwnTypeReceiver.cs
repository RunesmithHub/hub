using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace RunesmithHub.Tool.Analysis.Detection;

/// <summary>
/// Proves from the IL alone that a call on a <see cref="Type"/> is made on <c>typeof(T)</c> with <c>T</c> defined in the plugin's
/// contracts or implementation assembly. Anything it cannot prove, such as a type from <c>GetType()</c>, a parameter or a field, is not proven.
/// </summary>
internal static class OwnTypeReceiver
{
    public static bool IsProven(MethodBody body, int callIndex, IReadOnlyList<LoadedAssembly> ownAssemblies)
    {
        var instructions = body.Instructions;
        if (instructions[callIndex] is not { OpCode.Code: Code.Call or Code.Callvirt, Operand: MethodReference { HasThis: true } called })
            return false;

        var targets = BranchTargets(body);
        return ReceiverSource(instructions, callIndex, called.Parameters.Count, targets) is { } source
            && source > 0
            && IsGetTypeFromHandle(instructions[source])
            && !targets.Contains(instructions[source])
            && instructions[source - 1] is { OpCode.Code: Code.Ldtoken, Operand: TypeReference type }
            && IsDefinedIn(type, ownAssemblies);
    }

    /// <summary>Walks back through straight-line code to the instruction that pushed the receiver, below <paramref name="above"/> argument values.</summary>
    private static int? ReceiverSource(Collection<Instruction> instructions, int callIndex, int above, HashSet<Instruction> targets)
    {
        for (var i = callIndex - 1; i >= 0; i--)
        {
            if (targets.Contains(instructions[i + 1]) || StackEffect(instructions[i]) is not (int pops, int pushes))
                return null;
            if (above < pushes)
                return pushes == 1 ? i : null;
            above += pops - pushes;
        }

        return null;
    }

    private static HashSet<Instruction> BranchTargets(MethodBody body)
    {
        var targets = new HashSet<Instruction>();
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is Instruction target)
                targets.Add(target);
            else if (instruction.Operand is Instruction[] cases)
                targets.UnionWith(cases);
        }

        foreach (var handler in body.ExceptionHandlers)
            targets.UnionWith(new[] { handler.TryStart, handler.HandlerStart, handler.FilterStart }.OfType<Instruction>());
        return targets;
    }

    /// <summary>Gets how many values an instruction pops and pushes, or null for control flow and stack shapes the walk does not follow.</summary>
    private static (int Pops, int Pushes)? StackEffect(Instruction instruction)
    {
        if (instruction.OpCode.FlowControl is not (FlowControl.Next or FlowControl.Call or FlowControl.Cond_Branch))
            return null;
        if (instruction.OpCode.Code is Code.Calli or Code.Jmp || instruction.OpCode.StackBehaviourPop is StackBehaviour.PopAll)
            return null;

        var method = instruction.Operand as MethodReference;
        if (method?.CallingConvention == MethodCallingConvention.VarArg)
            return null;
        int? pops = instruction.OpCode.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
                or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4
                or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref => 3,
            StackBehaviour.Varpop when method is not null => method.Parameters.Count + (method.HasThis && instruction.OpCode.Code != Code.Newobj ? 1 : 0),
            _ => null,
        };
        int? pushes = instruction.OpCode.StackBehaviourPush switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
            StackBehaviour.Push1_push1 => 2,
            StackBehaviour.Varpush when method is not null => IsVoid(method.ReturnType) ? 0 : 1,
            _ => null,
        };
        return pops is { } popped && pushes is { } pushed ? (popped, pushed) : null;
    }

    private static bool IsVoid(TypeReference type) => type is IModifierType modified ? IsVoid(modified.ElementType) : type.MetadataType == MetadataType.Void;

    private static bool IsGetTypeFromHandle(Instruction instruction) =>
        instruction.OpCode.Code == Code.Call
        && instruction.Operand is MethodReference
        {
            Name: nameof(Type.GetTypeFromHandle),
            DeclaringType: { FullName: "System.Type", Scope: AssemblyNameReference { Name: "System.Runtime" or "System.Private.CoreLib" or "mscorlib" or "netstandard" } },
            Parameters: [{ ParameterType.FullName: "System.RuntimeTypeHandle" }],
        };

    /// <summary>Whether a type is defined in one of the given assemblies, looked up among their type definitions so a type forward does not count.</summary>
    private static bool IsDefinedIn(TypeReference type, IReadOnlyList<LoadedAssembly> assemblies)
    {
        var defined = type is GenericInstanceType generic ? generic.ElementType : type;
        if (defined is TypeSpecification or GenericParameter || AssemblyName(defined) is not { } name)
            return false;
        return assemblies.Where(assembly => assembly.Name == name)
            .SelectMany(assembly => assembly.Definition.Modules)
            .Any(module => module.GetType(defined.FullName) is not null);
    }

    private static string? AssemblyName(TypeReference type) => type switch
    {
        TypeDefinition definition => definition.Module.Assembly.Name.Name,
        _ => type.Scope switch
        {
            AssemblyNameReference reference => reference.Name,
            ModuleDefinition module => module.Assembly.Name.Name,
            _ => null,
        },
    };
}
