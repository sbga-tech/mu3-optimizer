using MonoMod;
using MU3.Battle;
using MU3.Mod.RenderLayers;
using MU3.Util;
using UnityEngine;

[MonoModIfFlag(nameof(PatchConfig.RenderLayers))]
[MonoModPatch("global::Evt_WaveShift")]
public class Evt_WaveShiftPatch : Evt_WaveShift
{
    public extern void orig_setParam(Color colBef, Color colAft);

    public new void setParam(Color colBef, Color colAft)
    {
        var engine = SingletonMonoBehaviour<GameEngine>.instance;
        var camera = engine != null ? (BattleCameraPatch)(object)engine.battleCamera : null;
        if (camera != null)
            camera.RebindEffectTextures(gameObject);
        orig_setParam(colBef, colAft);
    }
}