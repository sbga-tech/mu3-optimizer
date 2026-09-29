using MonoMod;
using MU3.Sequence;

namespace MU3.Mod.GameplayPrewarm;

[MonoModIfFlag(nameof(PatchConfig.GameplayPrewarm))]
[MonoModPatch("global::MU3.Sequence.PlayMusic")]
public class PlayMusicPatch : PlayMusic
{
    [MonoModIgnore] private MU3.Battle.GameEngine _gameEngine;
    private bool _gameplayPrewarmMusicPending;

    private extern void orig_Enter_WaitPlay();
    private extern void orig_Execute_WaitPlay();

    private void Enter_WaitPlay()
    {
        _gameplayPrewarmMusicPending = !((GameEnginePatch)(object)_gameEngine).gameplayPrewarm.Complete;
        if (!_gameplayPrewarmMusicPending)
            orig_Enter_WaitPlay();
    }

    private void Execute_WaitPlay()
    {
        if (_gameplayPrewarmMusicPending)
        {
            if (!((GameEnginePatch)(object)_gameEngine).gameplayPrewarm.Complete)
                return;
            _gameplayPrewarmMusicPending = false;
            orig_Enter_WaitPlay();
        }
        orig_Execute_WaitPlay();
    }
}
