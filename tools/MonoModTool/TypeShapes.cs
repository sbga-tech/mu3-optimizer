using Mono.Cecil;
using Mono.Cecil.Cil;

static class TypeShapes
{
    public static IEnumerable<TypeDefinition> All(ModuleDefinition module) => SelfAndNested(module.Types);

    public static IEnumerable<TypeDefinition> SelfAndNested(IEnumerable<TypeDefinition> roots)
    {
        foreach (var type in roots)
        {
            yield return type;
            foreach (var nested in SelfAndNested(type.NestedTypes))
                yield return nested;
        }
    }

    public static TypeDefinition? Find(ModuleDefinition module, string fullName) =>
        All(module).FirstOrDefault(type => type.FullName == fullName);

    public static Dictionary<string, string> Fingerprints(ModuleDefinition module) =>
        All(module).ToDictionary(type => type.FullName, Fingerprint);

    public static string Fingerprint(TypeDefinition type)
    {
        var parts = new List<string>
        {
            type.Attributes.ToString(),
            type.BaseType?.FullName ?? "",
        };
        parts.AddRange(type.Interfaces.Select(item => "I:" + item.InterfaceType.FullName));
        parts.AddRange(type.Fields.Select(field => "F:" + field.Attributes + ":" + field.FieldType.FullName + ":" + field.Name));
        parts.AddRange(type.Properties.Select(property => "P:" + property.PropertyType.FullName + ":" + property.Name));
        foreach (var method in type.Methods)
        {
            parts.Add("M:" + method.Attributes + ":" + method.FullName);
            if (!method.HasBody)
                continue;
            foreach (var instruction in method.Body.Instructions)
                parts.Add("IL:" + instruction.OpCode.Code + ":" + Operand(instruction.Operand));
        }
        return string.Join("\n", parts);
    }

    static string Operand(object? operand) => operand switch
    {
        null => "",
        Instruction instruction => "IL_" + instruction.Offset.ToString("X4"),
        Instruction[] instructions => string.Join(",", instructions.Select(item => "IL_" + item.Offset.ToString("X4"))),
        MethodReference method => method.FullName,
        FieldReference field => field.FullName,
        TypeReference type => type.FullName,
        ParameterDefinition parameter => "ARG:" + parameter.Index + ":" + parameter.ParameterType.FullName,
        VariableDefinition variable => "VAR:" + variable.Index + ":" + variable.VariableType.FullName,
        _ => operand.ToString() ?? "",
    };
}
