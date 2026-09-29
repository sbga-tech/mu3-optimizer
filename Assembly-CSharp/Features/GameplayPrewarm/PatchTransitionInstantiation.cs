using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace MonoMod;

[MonoModCustomMethodAttribute(nameof(MonoModRules.PatchTransitionInstantiation))]
class PatchTransitionInstantiationAttribute : Attribute { }

static partial class MonoModRules
{
    public static void PatchTransitionInstantiation(MethodDefinition factory, CustomAttribute attribute)
    {
        // Run after patch composition: keep the original camera/color ordering and
        // any RenderLayers wrapper, replacing only the two prefab instantiations.
        foreach (var name in new[] { "startWaveShiftEffect", "startOverkillEffect" })
        {
            var replaced = 0;
            foreach (var method in factory.DeclaringType.Methods)
            {
                if ((method.Name != name && method.Name != "orig_" + name) || !method.HasBody)
                    continue;
                using (var context = new ILContext(method))
                {
                    var cursor = new ILCursor(context);
                    while (cursor.TryGotoNext(MoveType.Before, instruction =>
                        instruction.OpCode == OpCodes.Call
                        && instruction.Operand is GenericInstanceMethod call
                        && call.DeclaringType.FullName == "UnityEngine.Object"
                        && call.Name == "Instantiate" && call.Parameters.Count == 1
                        && call.GenericArguments.Count == 1
                        && call.GenericArguments[0].FullName == "UnityEngine.GameObject"))
                    {
                        cursor.Emit(OpCodes.Ldarg_0);
                        cursor.Emit(name == "startOverkillEffect" ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                        cursor.Next.Operand = factory;
                        cursor.Index++;
                        replaced++;
                    }
                }
            }
            if (replaced != 1)
                throw new InvalidOperationException("Expected one transition instantiation in " + name
                    + ", found " + replaced);
        }
    }
}
