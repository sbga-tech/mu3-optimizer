using MonoMod;
using MU3.Battle;
using UnityEngine;
using UnityEngine.Rendering;

namespace MU3.Mod.RenderLayers;

internal static class BattleCameraHooks
{
    [OnStateLeave(nameof(BattleCamera.EState.StartCutscene))]
    [MonoModIfFlag(nameof(PatchConfig.RenderLayers))]
    internal static void LeaveStartCutscene(BattleCamera camera)
    {
        if (!MonoMod.RenderLayersConfig.DisableShadows)
            return;
        foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
            light.shadows = LightShadows.None;
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
