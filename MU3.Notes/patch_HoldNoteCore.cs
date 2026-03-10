using MonoMod;
using MU3.Battle;
using MU3.Util;
using UnityEngine;

namespace MU3.Notes;

[MonoModIfFlag("BetterNotes")]
public class patch_HoldNoteCore : HoldNoteCore
{
    private bool _activeItemEnd;
    private bool _activeItemEndGood;
    private bool _activeItemEndBad;
    private bool _activeGoEffect;

    [MonoModIgnore] private NotesCacheItem _itemEnd;
    [MonoModIgnore] private NotesCacheItem _itemEndGood;
    [MonoModIgnore] private NotesCacheItem _itemEndBad;
    [MonoModIgnore] private GameObject _goEffect;
    [MonoModIgnore] private DrawTbl _drawTbl;
    [MonoModIgnore] private Vector3 _posEnd;
    [MonoModIgnore] private float _placeHold;
    [MonoModIgnore] private bool _isKeep;

    [MonoModIgnore]
    private extern void drawJoint();

    private extern void orig_initToReuse();

    public new void initToReuse()
    {
        orig_initToReuse();
        _activeItemEnd     = false;
        _activeItemEndGood = false;
        _activeItemEndBad  = false;
        _activeGoEffect    = false;
    }

    [MonoModReplace]
    public new void drawModel()
    {
        if (!Singleton<MU3.Battle.Debug>.instance.isDrawNotes)
        {
            // Original does not touch _goEffect here.
            SetActiveCached(_itemEnd,     false, ref _activeItemEnd);
            SetActiveCached(_itemEndGood, false, ref _activeItemEndGood);
            SetActiveCached(_itemEndBad,  false, ref _activeItemEndBad);
            return;
        }

        if (isJudged)
        {
            SetActiveCached(_itemEnd,     false, ref _activeItemEnd);
            SetActiveCached(_itemEndGood, false, ref _activeItemEndGood);
            SetActiveCached(_itemEndBad,  false, ref _activeItemEndBad);
            SetActiveCached(_goEffect,    false, ref _activeGoEffect);
            return;
        }

        drawJoint();

        var notesManager = SingletonMonoBehaviour<GameEngine>.instance.notesManager;
        var currentFrame = notesManager.getCurrentFrame();

        if (!notesManager.isFrameVisible(frameEnd, param.pattern))
        {
            SetActiveCached(_itemEnd,     false, ref _activeItemEnd);
            SetActiveCached(_itemEndGood, false, ref _activeItemEndGood);
            SetActiveCached(_itemEndBad,  false, ref _activeItemEndBad);
        }
        else
        {
            _posEnd.z = notesManager.calcNotePosZ(frameEnd - currentFrame, param.pattern);

            if (_itemEnd != null)
            {
                var show = _drawTbl.isDrawEndNormal;
                if (show) _itemEnd.go.transform.position = _posEnd;
                SetActiveCached(_itemEnd, show, ref _activeItemEnd);
            }
            if (_itemEndGood != null)
            {
                var show = _drawTbl.isDrawEndWhite;
                if (show) _itemEndGood.go.transform.position = _posEnd;
                SetActiveCached(_itemEndGood, show, ref _activeItemEndGood);
            }
            if (_itemEndBad != null)
            {
                var show = _drawTbl.isDrawEndBlack;
                if (show) _itemEndBad.go.transform.position = _posEnd;
                SetActiveCached(_itemEndBad, show, ref _activeItemEndBad);
            }
        }

        if (_goEffect != null)
        {
            if (!isJudging)
            {
                SetActiveCached(_goEffect, false, ref _activeGoEffect);
                return;
            }
            var position = default(Vector3);
            position.x = notesManager.calcNotePosX(_placeHold);
            position.y = 0f;
            position.z = notesManager.getJudgePosZ();
            _goEffect.transform.position = position;
            SetActiveCached(_goEffect, _isKeep, ref _activeGoEffect);
        }
    }

    private static void SetActiveCached(NotesCacheItem item, bool value, ref bool shadow)
    {
        if (item == null || shadow == value) return;
        shadow = value;
        item.go.SetActive(value);
    }

    private static void SetActiveCached(GameObject go, bool value, ref bool shadow)
    {
        if (go == null || shadow == value) return;
        shadow = value;
        go.SetActive(value);
    }
}