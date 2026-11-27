using MonoMod;
using MU3.Battle;
using MU3.DataStudio;

namespace MU3.Mod.RenderLayers;


// Refresh the owning battle's native camera chain throughout known transitions.

[MonoModIfFlag(nameof(PatchConfig.RenderLayers))]
[MonoModPatch("global::MU3.Battle.GameEngine")]
public class GameEnginePatch : GameEngine
{
    //These are durations from AnimationClips
    private const float BattleStartDuration = 13f;
    private const float SkipDuration = 1.0f;
    private const float DamageDuration = 0.8f;
    private const float OverkillDuration = 4.98f;
    private const float WaveShiftDuration = 3.17f;

    public extern void orig_startStartCutscene(bool disableSound);

    public new void startStartCutscene(bool disableSound = false)
    {
        ((BattleCameraPatch)(object)battleCamera).RequestLayerRefresh(BattleStartDuration);
        orig_startStartCutscene(disableSound);
    }

    public extern void orig_skipStartCutscene();

    public new void skipStartCutscene()
    {
        ((BattleCameraPatch)(object)battleCamera).CutLayerRefresh(SkipDuration);
        orig_skipStartCutscene();
    }

    public extern void orig_playDamageCameraLow();

    public new void playDamageCameraLow()
    {
        ((BattleCameraPatch)(object)battleCamera).RequestLayerRefresh(DamageDuration);
        orig_playDamageCameraLow();
    }

    public extern void orig_playDamageCameraHigh();

    public new void playDamageCameraHigh()
    {
        ((BattleCameraPatch)(object)battleCamera).RequestLayerRefresh(DamageDuration);
        orig_playDamageCameraHigh();
    }

    public extern void orig_startOverkillEffect();

    public new void startOverkillEffect()
    {
        ((BattleCameraPatch)(object)battleCamera).RequestLayerRefresh(OverkillDuration);
        orig_startOverkillEffect();
    }

    public extern void orig_startWaveShiftEffect(AttributeType attrBef, AttributeType attrAft);

    public new void startWaveShiftEffect(AttributeType attrBef, AttributeType attrAft)
    {
        ((BattleCameraPatch)(object)battleCamera).RequestLayerRefresh(WaveShiftDuration);
        orig_startWaveShiftEffect(attrBef, attrAft);
    }
}