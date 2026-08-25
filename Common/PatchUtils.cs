using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.InlineRT;

namespace MonoMod;

static partial class MonoModRules
{
    internal static void InitializePatch()
    {
        var module = GetCurrentRulesModule();
        var patchConfig = module.GetType("MonoMod.PatchConfig") ??
                          throw new InvalidOperationException("MonoMod.PatchConfig not found in " + module.Name);

        InitializeConfig(module);
        ApplyPatchFlags(patchConfig);
        module.Types.Remove(patchConfig);
        PropagateNestedConditions(module);
    }

    private static void ApplyPatchFlags(TypeDefinition patchConfig)
    {
        MethodDefinition constructor = null;
        foreach (var method in patchConfig.Methods)
        {
            if (method.IsConstructor && method.IsStatic)
            {
                constructor = method;
                break;
            }
        }

        if (constructor == null || !constructor.HasBody)
            throw new InvalidOperationException("MonoMod.PatchConfig static constructor not found");

        foreach (var field in patchConfig.Fields)
        {
            if (!field.IsPublic || !field.IsStatic || field.FieldType.MetadataType != MetadataType.Boolean)
                continue;

            Instruction valueLoad = null;
            foreach (var instruction in constructor.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Stsfld &&
                    instruction.Operand is FieldReference reference &&
                    reference.Resolve() == field)
                {
                    valueLoad = instruction.Previous;
                    break;
                }
            }

            if (valueLoad == null)
                throw new InvalidOperationException("Patch flag value not generated for " + field.Name);
            MonoModRule.Flag.Set(field.Name, ReadBooleanConstant(valueLoad));
        }
    }

    private static bool ReadBooleanConstant(Instruction instruction)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Ldc_I4_0:
                return false;
            case Code.Ldc_I4_1:
                return true;
            case Code.Ldc_I4:
                return (int)instruction.Operand != 0;
            case Code.Ldc_I4_S:
                return (sbyte)instruction.Operand != 0;
            default:
                throw new InvalidOperationException("Patch flag is not a constant boolean: " + instruction);
        }
    }

    private static ModuleDefinition GetCurrentRulesModule()
    {
        // MonoMod parses Mods in order and removes each module's MonoModRules
        // immediately after executing it. During this static constructor, the
        // first module whose rules type remains is the module being parsed.
        foreach (ModuleDefinition module in MonoModRule.Modder.Mods)
        {
            if (module.GetType("MonoMod.MonoModRules") != null)
                return module;
        }

        throw new InvalidOperationException("Current MonoMod patch module not found");
    }

    private static void PropagateNestedConditions(ModuleDefinition module)
    {
        foreach (var type in module.Types)
            PropagateNestedConditions(type);
    }

    private static void PropagateNestedConditions(TypeDefinition type)
    {
        foreach (var nested in type.NestedTypes)
        {
            foreach (var attribute in type.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == "MonoMod.MonoModIfFlag" &&
                    !HasFlagCondition(nested, attribute))
                {
                    nested.CustomAttributes.Add(CloneAttribute(attribute));
                }
            }

            PropagateNestedConditions(nested);
        }
    }

    private static bool HasFlagCondition(TypeDefinition type, CustomAttribute expected)
    {
        var expectedKey = (string)expected.ConstructorArguments[0].Value;
        foreach (var attribute in type.CustomAttributes)
        {
            if (attribute.AttributeType.FullName == "MonoMod.MonoModIfFlag" &&
                (string)attribute.ConstructorArguments[0].Value == expectedKey)
            {
                return true;
            }
        }

        return false;
    }

    private static CustomAttribute CloneAttribute(CustomAttribute source)
    {
        var clone = new CustomAttribute(source.Constructor);
        foreach (var argument in source.ConstructorArguments)
            clone.ConstructorArguments.Add(argument);
        foreach (var field in source.Fields)
            clone.Fields.Add(field);
        foreach (var property in source.Properties)
            clone.Properties.Add(property);
        return clone;
    }
}
