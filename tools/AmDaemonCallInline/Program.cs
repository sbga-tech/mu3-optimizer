using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: AmDaemonCallInline <AMDaemon.NET.dll> <out.dll>");
    return 1;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(input)!);
using var asm = AssemblyDefinition.ReadAssembly(input, new ReaderParameters
{
    ReadWrite = false,
    ReadSymbols = false,
    AssemblyResolver = resolver,
});
var module = asm.MainModule;
var api = module.Types.FirstOrDefault(t => t.FullName == "AMDaemon.Api")
          ?? throw new InvalidOperationException("AMDaemon.Api not found");
var check = api.Methods.FirstOrDefault(m => m.Name == "CheckException" && !m.HasParameters)
            ?? throw new InvalidOperationException("Api.CheckException not found");

foreach (var thunk in api.Methods.Where(m => m.Name is "Call" or "CallAction" && m.HasBody))
{
    if (!IsCheckExceptionThunk(thunk, check))
    {
        Console.Error.WriteLine($"refuse: {thunk.FullName} is not invoke+CheckException");
        return 2;
    }
}

var helpers = new Dictionary<MetadataToken, MethodDefinition>();
var rewritten = 0;
var skipped = 0;

foreach (var type in AllTypes(module))
{
    foreach (var method in type.Methods)
    {
        if (!method.HasBody)
            continue;
        if (method.DeclaringType == api && method.Name is "Call" or "CallAction" or "CheckException")
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

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
asm.Write(output);

Console.WriteLine($"rewrote {rewritten} callsites, {helpers.Count} helpers, skipped {skipped} -> {output}");
return 0;

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
    if (method.Body.ExceptionHandlers.Any(h => h.HandlerType != ExceptionHandlerType.Finally))
        return false;
    var invokes = 0;
    var checks = 0;
    foreach (var ins in method.Body.Instructions)
    {
        if (ins.OpCode == OpCodes.Callvirt && ins.Operand is MethodReference inv && inv.Name == "Invoke")
            invokes++;
        if (ins.OpCode == OpCodes.Call && ins.Operand is MethodReference mr && mr.Resolve() == check)
            checks++;
    }
    return invokes == 1 && checks == 1;
}

static bool IsApiCall(MethodReference mr)
{
    var name = mr.GetElementMethod().Name;
    if (name is not ("Call" or "CallAction"))
        return false;
    return mr.DeclaringType.FullName == "AMDaemon.Api";
}

static bool IsDelegateCtor(MethodReference mr)
{
    if (mr.Name != ".ctor")
        return false;
    var n = mr.DeclaringType.Name;
    return n is "Action" || n.StartsWith("Action`", StringComparison.Ordinal)
           || n.StartsWith("Func`", StringComparison.Ordinal)
           || n.StartsWith("RefFunc`", StringComparison.Ordinal)
           || n.StartsWith("RefAction`", StringComparison.Ordinal);
}

static int RewriteMethod(
    MethodDefinition method,
    TypeDefinition api,
    MethodDefinition check,
    Dictionary<MetadataToken, MethodDefinition> helpers,
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
        if (ldftn.Operand is not MethodReference target)
            continue;
        if (newobj.Operand is not MethodReference ctor || !IsDelegateCtor(ctor))
            continue;
        if (call.Operand is not MethodReference callMr || !IsApiCall(callMr))
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
    var isAction = defName == "Action" || defName.StartsWith("Action`", StringComparison.Ordinal);
    var isFunc = defName.StartsWith("Func`", StringComparison.Ordinal);
    if (!isAction && !isFunc)
        return true;
    var delParams = git != null ? git.GenericArguments.ToArray() : Array.Empty<TypeReference>();
    if (isAction)
    {
        if (pinvoke.ReturnType.MetadataType != MetadataType.Void)
            return false;
        if (delParams.Length != pinvoke.Parameters.Count)
            return false;
        for (var i = 0; i < delParams.Length; i++)
        {
            if (delParams[i].FullName != pinvoke.Parameters[i].ParameterType.FullName)
                return false;
        }
        return true;
    }
    if (delParams.Length != pinvoke.Parameters.Count + 1)
        return false;
    for (var i = 0; i < pinvoke.Parameters.Count; i++)
    {
        if (delParams[i].FullName != pinvoke.Parameters[i].ParameterType.FullName)
            return false;
    }
    return delParams[^1].FullName == pinvoke.ReturnType.FullName;
}

static MethodDefinition GetOrCreateHelper(
    MethodDefinition pinvoke,
    TypeDefinition api,
    MethodDefinition check,
    Dictionary<MetadataToken, MethodDefinition> helpers)
{
    if (helpers.TryGetValue(pinvoke.MetadataToken, out var existing))
        return existing;

    var name = "Direct_" + pinvoke.Name;
    if (api.Methods.Any(m => m.Name == name) || helpers.Values.Any(m => m.Name == name))
        name += "_" + pinvoke.MetadataToken.ToUInt32().ToString("x");

    var helper = new MethodDefinition(
        name,
        MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
        pinvoke.ReturnType);

    foreach (var p in pinvoke.Parameters)
        helper.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));

    var body = helper.Body;
    var il = body.GetILProcessor();
    var isVoid = pinvoke.ReturnType.MetadataType == MetadataType.Void;
    VariableDefinition? result = null;
    if (!isVoid)
    {
        result = new VariableDefinition(pinvoke.ReturnType);
        body.Variables.Add(result);
        body.InitLocals = true;
    }

    Instruction LoadArg(int index) => index switch
    {
        0 => il.Create(OpCodes.Ldarg_0),
        1 => il.Create(OpCodes.Ldarg_1),
        2 => il.Create(OpCodes.Ldarg_2),
        3 => il.Create(OpCodes.Ldarg_3),
        _ => il.Create(OpCodes.Ldarg, helper.Parameters[index]),
    };

    Instruction tryStart;
    if (pinvoke.Parameters.Count == 0)
    {
        tryStart = il.Create(OpCodes.Call, pinvoke);
        il.Append(tryStart);
    }
    else
    {
        tryStart = LoadArg(0);
        il.Append(tryStart);
        for (var i = 1; i < pinvoke.Parameters.Count; i++)
            il.Append(LoadArg(i));
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

    helpers[pinvoke.MetadataToken] = helper;
    return helper;
}
