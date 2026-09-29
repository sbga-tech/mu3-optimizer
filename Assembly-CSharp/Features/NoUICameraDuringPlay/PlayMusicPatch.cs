using MonoMod;
using MU3.Sequence;
using MU3.Util;
using UnityEngine;

namespace MU3.Mod.NoUICameraDuringPlay;

[MonoModIfFlag(nameof(PatchConfig.NoUICameraDuringPlay))]
[MonoModPatch("global::MU3.Sequence.PlayMusic")]
public class PlayMusicPatch : PlayMusic
{
    private extern void orig_onFinishTips(GameObject tips);
    
    private void onFinishTips(GameObject tips)
    {
        orig_onFinishTips(tips);
        if (SystemUI.Exists)
        {
            SingletonMonoBehaviour<SystemUI>.instance.removeCanvas(Graphics.Const.SortOrder.Dialog, forceDestroy: true);
        }
    }   
}