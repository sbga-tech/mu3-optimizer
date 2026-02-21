using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace MonoMod;

[MonoModCustomMethodAttribute(nameof(MonoModRules.PatchDictAlloc))]
class PatchDictAllocAttribute : Attribute { }

static partial class MonoModRules
{
    public static void PatchDictAlloc(MethodDefinition method, CustomAttribute attrib)
    {
        if (method == null || !method.HasBody)
            return;

        var module = method.Module;
        using (var context = new ILContext(method))
        {
            var c = new ILCursor(context);

            while (c.TryGotoNext(MoveType.Before, instr =>
                       instr.OpCode == OpCodes.Newobj &&
                       instr.Operand is MethodReference ctorRef &&
                       ctorRef.DeclaringType.Resolve()?.FullName == "System.Collections.Generic.Dictionary`2" &&
                       ctorRef.Parameters.Count == 1 &&
                       ctorRef.Parameters[0].ParameterType.FullName == "System.Int32"))
            {
                var oldCtorRef = (MethodReference)c.Next.Operand;

                var dictTypeDef = oldCtorRef.DeclaringType.Resolve();
                var paramlessCtorDef = dictTypeDef.Methods
                    .FirstOrDefault(m => m.IsConstructor && !m.HasParameters);

                if (paramlessCtorDef == null)
                {
                    c.Index++;
                    continue;
                }

                var declaringTypeForCtor = oldCtorRef.DeclaringType;

                var newCtorRef = new MethodReference(".ctor", module.TypeSystem.Void, declaringTypeForCtor)
                {
                    HasThis = true
                };
                c.Emit(OpCodes.Pop);
                c.Next.Operand = module.ImportReference(newCtorRef);
                c.Index++;
            }
        }
    }

}