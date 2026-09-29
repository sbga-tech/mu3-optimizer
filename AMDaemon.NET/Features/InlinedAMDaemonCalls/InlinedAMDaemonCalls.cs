using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.InlineRT;

namespace MonoMod;

static partial class MonoModRules
{
    static void InlineAMDaemonCalls()
    {
        if (!MonoModRule.Flag.Get(nameof(PatchConfig.InlinedAMDaemonCalls)))
            return;
        var result = Rewrite(GetCurrentModder().Module);
        if (!result.StartsWith("rewrote ", StringComparison.Ordinal))
            throw new InvalidOperationException("InlinedAMDaemonCalls rewrite failed: " + result);
    }

    static string Rewrite(ModuleDefinition module)
    {
        TypeDefinition api = null;
        foreach (var t in module.Types)
        {
            if (t.FullName == "AMDaemon.Api")
            {
                api = t;
                break;
            }
        }
        if (api == null)
            return "AMDaemon.Api not found";

        MethodDefinition check = null;
        foreach (var m in api.Methods)
        {
            if (m.Name == "CheckException" && m.Parameters.Count == 0)
            {
                check = m;
                break;
            }
        }
        if (check == null)
            return "Api.CheckException not found";

        foreach (var thunk in api.Methods)
        {
            if ((thunk.Name == "Call" || thunk.Name == "CallAction") && thunk.HasBody
                && !IsCheckExceptionThunk(thunk, check))
                return "refuse: " + thunk.FullName + " is not invoke+CheckException";
        }

        var helpers = new Dictionary<uint, MethodDefinition>();
        var rewritten = 0;
        var skipped = 0;
        foreach (var type in AllTypes(module))
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                    continue;
                if (method.DeclaringType == api && (method.Name == "Call" || method.Name == "CallAction" || method.Name == "CheckException"))
                    continue;
                rewritten += RewriteMethod(method, api, check, helpers, ref skipped);
            }
        }

        foreach (var helper in helpers.Values)
            api.Methods.Add(helper);

        foreach (var type in AllTypes(module))
        {
            foreach (var method in type.Methods)
            {
                if (method.HasBody)
                    method.Body.OptimizeMacros();
            }
        }

        return "rewrote " + rewritten + " callsites, " + helpers.Count + " helpers, skipped " + skipped;
    }

    static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            yield return t;
            foreach (var n in t.NestedTypes)
                stack.Push(n);
        }
    }

    static bool IsCheckExceptionThunk(MethodDefinition method, MethodDefinition check)
    {
        if (!method.HasBody || !method.Body.HasExceptionHandlers)
            return false;
        foreach (var h in method.Body.ExceptionHandlers)
        {
            if (h.HandlerType != ExceptionHandlerType.Finally)
                return false;
        }
        var invokes = 0;
        var checks = 0;
        foreach (var ins in method.Body.Instructions)
        {
            var inv = ins.Operand as MethodReference;
            if (ins.OpCode == OpCodes.Callvirt && inv != null && inv.Name == "Invoke")
                invokes++;
            if (ins.OpCode == OpCodes.Call && inv != null)
            {
                var r = inv.Resolve();
                if (r == check)
                    checks++;
            }
        }
        return invokes == 1 && checks == 1;
    }

    static bool IsApiCall(MethodReference mr)
    {
        var name = mr.GetElementMethod().Name;
        if (name != "Call" && name != "CallAction")
            return false;
        return mr.DeclaringType.FullName == "AMDaemon.Api";
    }

    static bool IsDelegateCtor(MethodReference mr)
    {
        if (mr.Name != ".ctor")
            return false;
        var n = mr.DeclaringType.Name;
        return n == "Action" || n.StartsWith("Action`") || n.StartsWith("Func`")
               || n.StartsWith("RefFunc`") || n.StartsWith("RefAction`");
    }

    static int RewriteMethod(
        MethodDefinition method,
        TypeDefinition api,
        MethodDefinition check,
        Dictionary<uint, MethodDefinition> helpers,
        ref int skipped)
    {
        var instrs = method.Body.Instructions;
        var n = 0;
        for (var i = 0; i < instrs.Count - 3; i++)
        {
            var ldnull = instrs[i];
            var ldftn = instrs[i + 1];
            var newobj = instrs[i + 2];
            var call = instrs[i + 3];
            if (ldnull.OpCode != OpCodes.Ldnull)
                continue;
            if (ldftn.OpCode != OpCodes.Ldftn)
                continue;
            if (newobj.OpCode != OpCodes.Newobj)
                continue;
            if (call.OpCode != OpCodes.Call)
                continue;
            var target = ldftn.Operand as MethodReference;
            var ctor = newobj.Operand as MethodReference;
            var callMr = call.Operand as MethodReference;
            if (target == null || ctor == null || callMr == null)
                continue;
            if (!IsDelegateCtor(ctor) || !IsApiCall(callMr))
                continue;
            var resolved = target.Resolve();
            if (resolved == null || resolved.DeclaringType != api || !resolved.IsStatic)
            {
                skipped++;
                continue;
            }
            if (!SignaturesMatch(resolved, ctor.DeclaringType))
            {
                skipped++;
                continue;
            }
            var helper = GetOrCreateHelper(resolved, api, check, helpers);
            call.Operand = helper;
            instrs.RemoveAt(i);
            instrs.RemoveAt(i);
            instrs.RemoveAt(i);
            n++;
        }
        return n;
    }

    static bool SignaturesMatch(MethodDefinition pinvoke, TypeReference delegateType)
    {
        var git = delegateType as GenericInstanceType;
        var defName = delegateType.GetElementType().Name;
        var isAction = defName == "Action" || defName.StartsWith("Action`");
        var isFunc = defName.StartsWith("Func`");
        if (!isAction && !isFunc)
            return true;
        var delCount = git == null ? 0 : git.GenericArguments.Count;
        if (isAction)
        {
            if (pinvoke.ReturnType.MetadataType != MetadataType.Void)
                return false;
            if (delCount != pinvoke.Parameters.Count)
                return false;
            for (var i = 0; i < delCount; i++)
            {
                if (git.GenericArguments[i].FullName != pinvoke.Parameters[i].ParameterType.FullName)
                    return false;
            }
            return true;
        }
        if (delCount != pinvoke.Parameters.Count + 1)
            return false;
        for (var i = 0; i < pinvoke.Parameters.Count; i++)
        {
            if (git.GenericArguments[i].FullName != pinvoke.Parameters[i].ParameterType.FullName)
                return false;
        }
        return git.GenericArguments[delCount - 1].FullName == pinvoke.ReturnType.FullName;
    }

    static MethodDefinition GetOrCreateHelper(
        MethodDefinition pinvoke,
        TypeDefinition api,
        MethodDefinition check,
        Dictionary<uint, MethodDefinition> helpers)
    {
        var token = pinvoke.MetadataToken.ToUInt32();
        MethodDefinition existing;
        if (helpers.TryGetValue(token, out existing))
            return existing;

        var name = "Direct_" + pinvoke.Name;
        foreach (var m in api.Methods)
        {
            if (m.Name == name)
            {
                name = name + "_" + token.ToString("x");
                break;
            }
        }
        foreach (var m in helpers.Values)
        {
            if (m.Name == name)
            {
                name = name + "_" + token.ToString("x");
                break;
            }
        }

        var helper = new MethodDefinition(
            name,
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            pinvoke.ReturnType);

        foreach (var p in pinvoke.Parameters)
            helper.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));

        var body = helper.Body;
        var il = body.GetILProcessor();
        var isVoid = pinvoke.ReturnType.MetadataType == MetadataType.Void;
        VariableDefinition result = null;
        if (!isVoid)
        {
            result = new VariableDefinition(pinvoke.ReturnType);
            body.Variables.Add(result);
            body.InitLocals = true;
        }

        Instruction tryStart;
        if (pinvoke.Parameters.Count == 0)
        {
            tryStart = il.Create(OpCodes.Call, pinvoke);
            il.Append(tryStart);
        }
        else
        {
            tryStart = LoadArg(il, helper, 0);
            il.Append(tryStart);
            for (var i = 1; i < pinvoke.Parameters.Count; i++)
                il.Append(LoadArg(il, helper, i));
            il.Append(il.Create(OpCodes.Call, pinvoke));
        }

        if (!isVoid)
            il.Append(il.Create(OpCodes.Stloc, result));

        var end = isVoid ? il.Create(OpCodes.Ret) : il.Create(OpCodes.Ldloc, result);
        il.Append(il.Create(OpCodes.Leave, end));

        var finallyStart = il.Create(OpCodes.Call, check);
        il.Append(finallyStart);
        il.Append(il.Create(OpCodes.Endfinally));
        il.Append(end);
        if (!isVoid)
            il.Append(il.Create(OpCodes.Ret));

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = tryStart,
            TryEnd = finallyStart,
            HandlerStart = finallyStart,
            HandlerEnd = end,
        });

        helpers[token] = helper;
        return helper;
    }

    static Instruction LoadArg(ILProcessor il, MethodDefinition helper, int index)
    {
        switch (index)
        {
            case 0: return il.Create(OpCodes.Ldarg_0);
            case 1: return il.Create(OpCodes.Ldarg_1);
            case 2: return il.Create(OpCodes.Ldarg_2);
            case 3: return il.Create(OpCodes.Ldarg_3);
            default: return il.Create(OpCodes.Ldarg, helper.Parameters[index]);
        }
    }
}
