using MonoMod;
using MU3.Battle;

namespace MU3.Sequence;

[MonoModIfFlag("ScorePresentation")]
[MonoModPatch("global::MU3.Sequence.PlayMusic")]
public class patch_PlayMusicScorePresentation : PlayMusic
{
    [MonoModIgnore] private GameEngine _gameEngine;

    private extern bool orig_updateState(float deltaTime);

    public override bool updateState(float deltaTime)
    {
        var result = orig_updateState(deltaTime);
        if (_gameEngine != null)
            ((patch_Counters)(object)_gameEngine.counters).flushScorePresentation();
        return result;
    }
}
