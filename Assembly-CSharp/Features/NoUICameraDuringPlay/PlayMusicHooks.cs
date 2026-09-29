using MonoMod;
using MU3.Sequence;

namespace MU3.Mod.NoUICameraDuringPlay;

// Chart playback spans PlayMusic's WaitPlay entry through its Play exit. GameplayPrewarm owns
// Enter_WaitPlay; state hooks observe the transitions without owning either method.
internal static class PlayMusicHooks
{
    internal static bool IsPlayingMusic { get; private set; }

    [OnStateEnter(nameof(PlayMusic.EState.WaitPlay))]
    [MonoModIfFlag(nameof(PatchConfig.NoUICameraDuringPlay))]
    internal static void EnterWaitPlay(PlayMusic playMusic)
    {
        IsPlayingMusic = true;
    }

    [OnStateLeave(nameof(PlayMusic.EState.Play))]
    [MonoModIfFlag(nameof(PatchConfig.NoUICameraDuringPlay))]
    internal static void LeavePlay(PlayMusic playMusic)
    {
        IsPlayingMusic = false;
    }
}
