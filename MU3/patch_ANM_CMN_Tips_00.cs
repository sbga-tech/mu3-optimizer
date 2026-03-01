using System;
using MU3.Graphics;
using MU3.Util;
using UnityEngine;

namespace MU3;

public class patch_ANM_CMN_Tips_00 : ANM_CMN_Tips_00
{
    public extern void orig_startTips(TipsID tipsId, Action<GameObject> onFinish = null);
    
    //Fix that tips aren't properly removed
    public new void startTips(TipsID tipsId, Action<GameObject> onFinish = null)
    {
        var newOnFinish = new Action<GameObject>(obj =>
        {
            onFinish?.Invoke(obj);
            if (SystemUI.Exists)
            {
                SingletonMonoBehaviour<SystemUI>.instance.removeCanvas(Const.SortOrder.Dialog, forceDestroy: true);
            }
        });
        orig_startTips(tipsId, newOnFinish);
    }
}