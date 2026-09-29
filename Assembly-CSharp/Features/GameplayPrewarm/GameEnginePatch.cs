using MonoMod;
using MU3.Battle;
using MU3.Game;
using UnityEngine;

namespace MU3.Mod.GameplayPrewarm;

[MonoModIfFlag(nameof(PatchConfig.GameplayPrewarm))]
[MonoModPatch("global::MU3.Battle.GameEngine")]
public class GameEnginePatch : GameEngine
{
    internal GameplayPrewarmSetup gameplayPrewarm;

    private extern void orig_initialize(SessionInfo sessionInfo);

    public new void initialize(SessionInfo sessionInfo)
    {
        orig_initialize(sessionInfo);
        gameplayPrewarm = gameObject.AddComponent<GameplayPrewarmSetup>();
        gameplayPrewarm.Initialize(this);
    }

    // RenderLayers owns startOverkillEffect/startWaveShiftEffect; only their prefab
    // Instantiate call is redirected here after MonoMod composition.
    [PatchTransitionInstantiation]
    private static GameObject instantiateGameplayPrewarmEffect(GameObject prefab, GameEngine owner, bool overkill)
    {
        var prewarm = ((GameEnginePatch)(object)owner).gameplayPrewarm;
        return prewarm != null
            ? prewarm.AcquireTransitionEffect(prefab, overkill)
            : UnityEngine.Object.Instantiate(prefab);
    }
}
