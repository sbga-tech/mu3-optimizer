using MonoMod;
using MU3.Battle;
using MU3.Sys;
using MU3.Util;
using UnityEngine;

namespace MU3.Notes;


[MonoModIfFlag("BetterNotes")]
public class patch_TapNoteCore : TapNoteCore
{
    private bool _activeItemTap;
    private bool _activeItemTapBad;
    private bool _activeItemFoot;

    private extern void orig_initToReuse();

    public new void initToReuse()
    {
        orig_initToReuse();
        _activeItemTap    = false;
        _activeItemTapBad = false;
        _activeItemFoot   = false;
    }

    // Eliminates redundant SetActive (which is quite expensive!)
    [MonoModReplace]
    public new void drawModel()
    {
        var shouldDraw = Singleton<MU3.Battle.Debug>.instance.isDrawNotes
                         && ntMgr.isFrameVisible(param.frame, param.pattern)
                         && !isJudged;

        if (!shouldDraw)
        {
            SetActiveCached(itemTap,    false, ref _activeItemTap);
            SetActiveCached(itemTapBad, false, ref _activeItemTapBad);
            SetActiveCached(itemFoot,   false, ref _activeItemFoot);
            return;
        }

        var currentFrame = ntMgr.getCurrentFrame();
        posModel.z = ntMgr.calcNotePosZ(param.frame - currentFrame, param.pattern);
        var notInArea = !ntMgr.isPlayerInArea();

        if (itemTap != null)
        {
            itemTap.go.transform.position = posModel;
            SetActiveCached(itemTap, !notInArea, ref _activeItemTap);
        }
        if (itemTapBad != null)
        {
            itemTapBad.go.transform.position = posModel;
            SetActiveCached(itemTapBad, notInArea, ref _activeItemTapBad);
        }
        if (itemFoot != null)
        {
            itemFoot.go.transform.position = posModel;
            SetActiveCached(itemFoot, true, ref _activeItemFoot);
        }
    }

    // Call SetActive only when it differs from the cached shadow.
    private static void SetActiveCached(NotesCacheItem item, bool value, ref bool shadow)
    {
        if (item == null || shadow == value) return;
        shadow = value;
        item.go.SetActive(value);
    }
}