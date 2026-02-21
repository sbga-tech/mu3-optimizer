using System;
using MonoMod;
using MU3.Skill;

namespace MU3.Operation;


[MonoModIfFlag("BoostLoginRequests")]
public class patch_GameSetting : GameSetting
{
    
    private const int REASONABLY_LARGE_NUMBER = Int32.MaxValue / 2;
    [MonoModIgnore]
    public new int maxCountCharacter { get; private set; }
    [MonoModIgnore]
    public new int maxCountItem { get; private set; }
    [MonoModIgnore]
    public new int maxCountMusic { get; private set; }
    [MonoModIgnore]
    public new int maxCountCard { get; private set; }
    [MonoModIgnore]
    public new int maxCountMusicItem { get; private set; }
    [MonoModIgnore]
    public new int maxCountRivalMusic { get; private set; }
    

    [MonoModReplace]
    [MonoModConstructor]
    public void ctor() {
        maxCountCharacter = REASONABLY_LARGE_NUMBER;
        maxCountItem = REASONABLY_LARGE_NUMBER;
        maxCountMusic = REASONABLY_LARGE_NUMBER;
        maxCountCard = REASONABLY_LARGE_NUMBER;
        maxCountMusicItem = REASONABLY_LARGE_NUMBER;
        maxCountRivalMusic = REASONABLY_LARGE_NUMBER;
    }
    
    public extern void orig_copyFrom(MU3.Client.GameSetting gameSetting);

    public new void copyFrom(MU3.Client.GameSetting gameSetting) {
        orig_copyFrom(gameSetting);
        maxCountCharacter = REASONABLY_LARGE_NUMBER;
        maxCountItem = REASONABLY_LARGE_NUMBER;
        maxCountMusic = REASONABLY_LARGE_NUMBER;
        maxCountCard = REASONABLY_LARGE_NUMBER;
        maxCountMusicItem = REASONABLY_LARGE_NUMBER;
        maxCountRivalMusic = REASONABLY_LARGE_NUMBER;
    }

}