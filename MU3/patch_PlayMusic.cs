namespace MU3.Sequence;

public class patch_PlayMusic : PlayMusic
{

    public static bool IsPlayingMusic { get; private set; }

    private extern void orig_Enter_WaitPlay();

    private void Enter_WaitPlay()
    {
        orig_Enter_WaitPlay();
        IsPlayingMusic = true;
    }

    private void Leave_Play()
    {
        IsPlayingMusic = false;
    }
}