using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace MonoMod;

// Game state machines (RD1.SSS.StateMachine and its MU3.Util wrappers) bind Enter_<State>,
// Execute_<State>, and Leave_<State> by name on the method holder when they are constructed.
// These hooks observe a transition without owning the transition method: after MonoMod
// composition, the rule inserts a call into whichever body the holder ends up with.
//
// A hook is a non-private `static void Hook(THolder holder)` gated by its switch's
// MonoModIfFlag; a disabled hook is never added, so no call is emitted. THolder must declare
// Execute_<State>. A missing Enter_/Leave_ method is created.

/// <summary>Calls the hook after the holder's <c>Enter_&lt;state&gt;</c> body, in registration order.</summary>
[MonoModCustomMethodAttribute(nameof(MonoModRules.OnStateEnter))]
[AttributeUsage(AttributeTargets.Method)]
class OnStateEnterAttribute : Attribute
{
    public OnStateEnterAttribute(string state) { }
}

/// <summary>Calls the hook before the holder's <c>Leave_&lt;state&gt;</c> body, in reverse registration order.</summary>
[MonoModCustomMethodAttribute(nameof(MonoModRules.OnStateLeave))]
[AttributeUsage(AttributeTargets.Method)]
class OnStateLeaveAttribute : Attribute
{
    public OnStateLeaveAttribute(string state) { }
}

static partial class MonoModRules
{
    public static void OnStateEnter(MethodDefinition hook, CustomAttribute attribute)
    {
        var method = GetStateMethod(hook, attribute, "Enter_");
        method.Body.SimplifyMacros();
        var il = method.Body.GetILProcessor();
        var instructions = method.Body.Instructions;
        // The final return becomes the hook call's first instruction; every other return branches to it.
        // Instructions change in place, so branches into a return keep their target.
        var call = instructions[instructions.Count - 1];
        if (call.OpCode != OpCodes.Ret)
        {
            call = il.Create(OpCodes.Ldarg_0);
            il.Append(call);
        }

        foreach (var instruction in instructions)
        {
            if (instruction.OpCode == OpCodes.Ret && instruction != call)
            {
                instruction.OpCode = OpCodes.Br;
                instruction.Operand = call;
            }
        }

        call.OpCode = OpCodes.Ldarg_0;
        il.Append(il.Create(OpCodes.Call, method.Module.ImportReference(hook)));
        il.Append(il.Create(OpCodes.Ret));
        method.Body.OptimizeMacros();
    }

    public static void OnStateLeave(MethodDefinition hook, CustomAttribute attribute)
    {
        var method = GetStateMethod(hook, attribute, "Leave_");
        method.Body.SimplifyMacros();
        var il = method.Body.GetILProcessor();
        var body = method.Body.Instructions[0];
        il.InsertBefore(body, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(body, il.Create(OpCodes.Call, method.Module.ImportReference(hook)));
        method.Body.OptimizeMacros();
    }

    private static MethodDefinition GetStateMethod(MethodDefinition hook, CustomAttribute attribute, string phase)
    {
        var state = (string)attribute.ConstructorArguments[0].Value;
        if (!hook.IsStatic || hook.IsPrivate || hook.ReturnType.MetadataType != MetadataType.Void
            || hook.Parameters.Count != 1 || IsNestedPrivate(hook.DeclaringType))
            throw new InvalidOperationException(
                "State hook must be an accessible static void(THolder): " + hook.FullName);

        var holder = hook.Parameters[0].ParameterType.Resolve();
        if (holder == null || holder.Module != hook.Module || FindStateMethod(holder, "Execute_" + state) == null)
            throw new InvalidOperationException(hook.FullName + ": " + hook.Parameters[0].ParameterType.FullName
                                                + " does not declare Execute_" + state + "()");

        var method = FindStateMethod(holder, phase + state);
        if (method != null)
            return method;

        method = new MethodDefinition(phase + state, MethodAttributes.Private | MethodAttributes.HideBySig,
            hook.Module.TypeSystem.Void);
        method.Body.GetILProcessor().Emit(OpCodes.Ret);
        holder.Methods.Add(method);
        return method;
    }

    private static MethodDefinition FindStateMethod(TypeDefinition holder, string name)
    {
        foreach (var method in holder.Methods)
        {
            if (method.Name != name)
                continue;
            if (method.IsStatic || method.HasParameters || method.ReturnType.MetadataType != MetadataType.Void
                || !method.HasBody)
                throw new InvalidOperationException("State method is not an instance void " + name + "(): "
                                                    + method.FullName);
            return method;
        }

        return null;
    }

    private static bool IsNestedPrivate(TypeDefinition type)
    {
        for (; type != null; type = type.DeclaringType)
        {
            if (type.IsNestedPrivate)
                return true;
        }

        return false;
    }
}
