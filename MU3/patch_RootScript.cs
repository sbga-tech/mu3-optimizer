using MonoMod;
using MU3.Sequence;

namespace MU3;

public class patch_RootScript : RootScript 
{
    [MonoModIgnore]
    private Root _sequenceRoot;
    
    public Root getSequenceRoot()
    {
        return _sequenceRoot;
    }
    
    private Play getPlay()
    {
        var seqroot = getSequenceRoot();
        var isGame = seqroot.getCurrentState() == Root.EState.Game;
        if (!isGame) return null;
        var game = seqroot.childState as Sequence.Game;
        var isPlay = game != null && game.getCurrentState() == Sequence.Game.EState.Play;
        if (!isPlay) return null;
        return game.childState as Play;
    }

    public bool isPlayingMusic()
    {
        var play = getPlay();
        var isPlayingMusic = play != null && play.getCurrentState() == Play.EState.PlayMusic;
        return isPlayingMusic;
    }
    
    public PlayMusic.EState? getPlayMusicState()
    {
        var play = getPlay();
        var isPlayingMusic = play != null && play.getCurrentState() == Play.EState.PlayMusic;
        if (!isPlayingMusic) return null;
        var playMusic = play.childState as PlayMusic;
        return playMusic?.getCurrentState();
    }
}