using MonoMod;
using MU3.Sequence;

namespace MU3;

[MonoModIfFlag("NoAMDuringPlay")]
public class patch_RootScript : RootScript 
{
    [MonoModIgnore]
    private Root _sequenceRoot;
    
    public Root getSequenceRoot()
    {
        return _sequenceRoot;
    }

    public bool isPlayingMusic()
    {
        var seqroot = getSequenceRoot();
        var isGame = seqroot.getCurrentState() == Root.EState.Game;
        if (!isGame) return false;
        var game = seqroot.childState as Sequence.Game;
        var isPlay = game != null && game.getCurrentState() == Sequence.Game.EState.Play;
        if (!isPlay) return false;
        var play = game.childState as Play;
        var isPlayingMusic = play != null && play.getCurrentState() == Play.EState.PlayMusic;
        return isPlayingMusic;
    }
}