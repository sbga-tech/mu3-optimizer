using MonoMod;
using MU3.Util;
using UnityEngine;

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

    private extern void orig_onFinishTips(GameObject tips);
    
    [MonoModIfFlag("NoUICameraDuringPlay")]
    private void onFinishTips(GameObject tips)
    {
        orig_onFinishTips(tips);
        if (SystemUI.Exists)
        {
            SingletonMonoBehaviour<SystemUI>.instance.removeCanvas(Graphics.Const.SortOrder.Dialog, forceDestroy: true);
        }
    }   
}