using MonoMod;
using MU3.DataStudio;
using MU3.Util;

namespace MU3.Battle;


// Hooks all GameEngine methods that trigger FX_Background (L30) effects,
// notifying StageCompositor to enable the L30 render pass on demand.

[MonoModIfFlag("RenderLayers")]
public class patch_GameEngine : GameEngine
{
    private const float Margin = 0f;
    
    //These are durations from AnimationClips
    private const float BattleStartDuration = 13f + Margin;
    private const float SkipDuration = 1.0f + Margin;
    private const float DamageDuration = 0.8f + Margin;
    private const float OverkillDuration = 4.98f + Margin;
    private const float WaveShiftDuration = 3.17f + Margin;

    private PreparedTransitionEffects _preparedTransitionEffects;

    public extern void orig_startStartCutscene(bool disableSound);

    public new void startStartCutscene(bool disableSound = false)
    {
        if (_preparedTransitionEffects == null)
            _preparedTransitionEffects = gameObject.AddComponent<PreparedTransitionEffects>();
        _preparedTransitionEffects.Prepare(SingletonMonoBehaviour<AssetAssign>.instance.stageEffect,
            notesManager.waveDetailDataList.Count);
        StageCompositor.RequestFXLayer(BattleStartDuration);
        orig_startStartCutscene(disableSound);
    }

    public extern void orig_skipStartCutscene();

    public new void skipStartCutscene()
    {
        StageCompositor.RequestFXLayer(SkipDuration);
        orig_skipStartCutscene();
    }

    public extern void orig_playDamageCameraLow();

    public new void playDamageCameraLow()
    {
        StageCompositor.RequestFXLayer(DamageDuration);
        orig_playDamageCameraLow();
    }

    public extern void orig_playDamageCameraHigh();

    public new void playDamageCameraHigh()
    {
        StageCompositor.RequestFXLayer(DamageDuration);
        orig_playDamageCameraHigh();
    }

    public extern void orig_startOverkillEffect();

    public new void startOverkillEffect()
    {
        StageCompositor.RequestFXLayer(OverkillDuration);
        var prefab = SingletonMonoBehaviour<AssetAssign>.instance.stageEffect.overkill;
        var prepared = _preparedTransitionEffects != null
            ? _preparedTransitionEffects.TakeOverkill(prefab) : null;
        if (prepared == null)
        {
            orig_startOverkillEffect();
            return;
        }

        battleCamera.startOverDamage();
        PreparedTransitionEffects.Activate(prepared);
    }

    public extern void orig_startWaveShiftEffect(AttributeType attrBef, AttributeType attrAft);

    public new void startWaveShiftEffect(AttributeType attrBef, AttributeType attrAft)
    {
        StageCompositor.RequestFXLayer(WaveShiftDuration);
        var prefab = SingletonMonoBehaviour<AssetAssign>.instance.stageEffect.waveShift;
        var prepared = _preparedTransitionEffects != null
            ? _preparedTransitionEffects.TakeWave(prefab) : null;
        if (prepared == null)
        {
            orig_startWaveShiftEffect(attrBef, attrAft);
            return;
        }

        PreparedTransitionEffects.Activate(prepared);
        var wave = prepared.GetComponent<Evt_WaveShift>();
        if (wave != null)
            wave.setParam(notesManager.notesColor.attr[(int)attrBef].c,
                notesManager.notesColor.attr[(int)attrAft].c);
    }
}