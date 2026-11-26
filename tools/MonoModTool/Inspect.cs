using Mono.Cecil;
using Mono.Cecil.Cil;

static class Inspect
{
    public static int Run(string assembly, string[] queries)
    {
        using var module = ModuleDefinition.ReadModule(assembly);
        foreach (var query in queries)
        {
            if (query.StartsWith("calls:", StringComparison.Ordinal))
                PrintCallers(module, query[6..]);
            else if (query.StartsWith("types:", StringComparison.Ordinal))
                PrintTypes(module, query[6..]);
            else if (query.StartsWith("members:", StringComparison.Ordinal))
                PrintMembers(module, query[8..]);
            else if (query.StartsWith("diff:", StringComparison.Ordinal))
                PrintDiff(module, query[5..]);
            else
                PrintMember(module, query);
        }
        return 0;
    }

    static void PrintTypes(ModuleDefinition module, string text)
    {
        foreach (var type in TypeShapes.All(module).Where(type => type.FullName.Contains(text, StringComparison.Ordinal)))
            Console.WriteLine($"TYPE {type.FullName}");
    }

    static void PrintMembers(ModuleDefinition module, string typeName)
    {
        var type = TypeShapes.Find(module, typeName);
        if (type == null)
        {
            Console.WriteLine($"MISSING TYPE {typeName}");
            return;
        }
        foreach (var field in type.Fields)
            Console.WriteLine($"FIELD {field.FullName} CONSTANT={field.Constant}");
        foreach (var property in type.Properties)
            Console.WriteLine($"PROPERTY {property.FullName}");
        foreach (var method in type.Methods)
            Console.WriteLine($"METHOD {method.FullName}");
    }

    static void PrintDiff(ModuleDefinition module, string otherPath)
    {
        using var other = ModuleDefinition.ReadModule(otherPath);
        var left = TypeShapes.Fingerprints(module);
        var right = TypeShapes.Fingerprints(other);
        foreach (var name in left.Keys.Union(right.Keys).OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!left.TryGetValue(name, out var leftValue))
                Console.WriteLine($"ONLY OTHER {name}");
            else if (!right.TryGetValue(name, out var rightValue))
                Console.WriteLine($"ONLY INPUT {name}");
            else if (!StringComparer.Ordinal.Equals(leftValue, rightValue))
                Console.WriteLine($"CHANGED {name}");
        }
    }

    static void PrintMember(ModuleDefinition module, string query)
    {
        var separator = query.IndexOf("::", StringComparison.Ordinal);
        var typeName = separator >= 0 ? query[..separator] : query;
        var memberName = separator >= 0 ? query[(separator + 2)..] : null;
        var type = TypeShapes.Find(module, typeName);
        if (type == null)
        {
            Console.WriteLine($"MISSING TYPE {typeName}");
            return;
        }
        if (memberName == null)
        {
            Console.WriteLine($"TYPE {type.FullName}");
            return;
        }

        var methods = type.Methods.Where(method => method.Name == memberName).ToArray();
        if (methods.Length == 0)
        {
            var fields = type.Fields.Where(field => field.Name == memberName).ToArray();
            if (fields.Length == 0)
                Console.WriteLine($"MISSING MEMBER {type.FullName}::{memberName}");
            foreach (var field in fields)
                Console.WriteLine($"FIELD {field.FullName}");
            return;
        }

        foreach (var method in methods)
        {
            Console.WriteLine($"METHOD {method.FullName}");
            if (!method.HasBody)
                continue;
            foreach (var instruction in method.Body.Instructions)
                Console.WriteLine($"  {instruction.Offset:X4}: {instruction.OpCode} {instruction.Operand}");
        }
    }

    static void PrintCallers(ModuleDefinition module, string target)
    {
        foreach (var method in TypeShapes.All(module).SelectMany(type => type.Methods).Where(method => method.HasBody))
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj))
                continue;
            if (instruction.Operand is MethodReference called &&
                (called.FullName.Contains(target, StringComparison.Ordinal) ||
                 $"{called.DeclaringType.FullName}::{called.Name}".Contains(target, StringComparison.Ordinal)))
            {
                Console.WriteLine($"CALLER {method.FullName} -> {called.FullName}");
            }
        }
    }
}
