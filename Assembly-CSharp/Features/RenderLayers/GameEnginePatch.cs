using MonoMod;
using MU3.Battle;
using MU3.DataStudio;

namespace MU3.Mod.RenderLayers;


// Refresh the owning battle's native camera chain throughout known transitions.

[MonoModIfFlag(nameof(PatchConfig.RenderLayers))]
[MonoModPatch("global::MU3.Battle.GameEngine")]
public class GameEnginePatch : GameEngine
{
    private const float Margin = 0f;
    
    //These are durations from AnimationClips
    private const float BattleStartDuration = 13f + Margin;
    private const float SkipDuration = 1.0f + Margin;
    private const float DamageDuration = 0.8f + Margin;
    private const float OverkillDuration = 4.98f + Margin;
    private const float WaveShiftDuration = 3.17f + Margin;

    public extern void orig_startStartCutscene(bool disableSound);

    public new void startStartCutscene(bool disableSound = false)
    {
        ((BattleCameraPatch)(object)battleCamera).RequestLayerRefresh(BattleStartDuration);
        orig_startStartCutscene(disableSound);
    }

    public extern void orig_skipStartCutscene();

    public new void skipStartCutscene()
    {
        ((BattleCameraPatch)(object)battleCamera).RequestLayerRefresh(SkipDuration);
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