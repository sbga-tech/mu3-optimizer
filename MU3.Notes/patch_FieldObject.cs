using System.Collections.Generic;
using MonoMod;
using MU3.Battle;
using MU3.Util;
using UnityEngine;

namespace MU3.Notes;

[MonoModIfFlag("BetterNotes")]
public class patch_FieldObject : FieldObject
{
    private int[] _lanesForeIdx;

    [MonoModIgnore] private List<NotesLane> _notesLanes;
    [MonoModIgnore] private List<NotesOneWay> _notesOneWayList;
    [MonoModIgnore] private float _alphaGame;
    [MonoModIgnore] private float _widthAmp;

    private extern void orig_initNotesLanes();

    private void initNotesLanes()
    {
        orig_initNotesLanes();
        _lanesForeIdx = null; // forces realloc + zero-reset in moveNotesLanes()
    }

    [MonoModReplace]
    private void moveNotesLanes()
    {
        if (getCurrentState() == EState.None)
            return;

        // GameDeviceManager gameDeviceManager = SingletonMonoBehaviour<GameEngine>.instance.gameDeviceManager;

        var currentFrame = ntMgr.getCurrentFrame();
        var frameVisible = ntMgr.getFrameVisible();
        var isStealthField = GameOption.isStealthField;
        var count = _notesLanes.Count;

        // Allocate / reset cursor array when lane count changes or after a rebuild.
        if (_lanesForeIdx == null || _lanesForeIdx.Length != count)
            _lanesForeIdx = new int[count];

        for (var i = 0; i < count; i++)
        {
            var notesLane = _notesLanes[i];
            notesLane._isDraw = false;

            var lanedata = notesLane._lanedata;
            if (lanedata.Count == 0 || (isStealthField && lanedata.wall != Walls.MAX))
                continue;

            var frame  = lanedata[0].frame;
            if (frame > frameVisible)
                continue;

            var frame2 = lanedata[lanedata.Count - 1].frame;
            if (frame2 < currentFrame)
                continue;

            var laneParam = ntMgr.holdMisc.laneParam[(int)notesLane.lane];
            notesLane._jointParam.lineWidthW = laneParam.widthW;
            notesLane._jointParam.lineWidthH = laneParam.widthH;
            notesLane._jointParam.alpha      = _alphaGame;
            notesLane._jointParam.widthAmp   = _widthAmp;
            notesLane._jointParam.height     = laneParam.height;

            var shapeData = lanedata.shapeData;
            var count2 = shapeData.Count;
            var flag  = false;
            var flag2 = false;

            // Start from the cursor: all entries before it are already in the
            // past (frameB <= currentFrame <= frameVisible) and match neither
            // condition, so skipping them is safe.
            var jStart = _lanesForeIdx[i];

            for (var j = jStart; j < count2; j++)
            {
                if (flag && flag2) break;

                var shape = shapeData[j];

                if (!flag && shape.frameF <= currentFrame && currentFrame < shape.frameB)
                {
                    flag = true;
                    notesLane._jointParam.indexFore = j;
                    notesLane._jointParam.rateFore  = CalcRate(shape.frameF, shape.frameB, currentFrame);
                    _lanesForeIdx[i] = j; // advance cursor for next frame
                }
                if (!flag2 && shape.frameF <= frameVisible && frameVisible < shape.frameB)
                {
                    flag2 = true;
                    notesLane._jointParam.indexRear = j;
                    notesLane._jointParam.rateRear  = CalcRate(shape.frameF, shape.frameB, frameVisible);
                }
            }

            if (!flag)
            {
                notesLane._jointParam.indexFore = 0;
                notesLane._jointParam.rateFore  = 0f;
                _lanesForeIdx[i] = 0; // nothing found: keep cursor at start
            }
            if (!flag2)
            {
                notesLane._jointParam.indexRear = shapeData.Count - 1;
                notesLane._jointParam.rateRear  = 1f;
            }

            notesLane._isDraw = true;
        }
    }

    [MonoModReplace]
    private void moveNotesOneways()
    {
        if (getCurrentState() == EState.None || ntMgr.isTutorial() || GameOption.isStealthField)
            return;

        // Dead code
        // GameDeviceManager gameDeviceManager = SingletonMonoBehaviour<GameEngine>.instance.gameDeviceManager;

        var isFieldBugFix = Singleton<MU3.Battle.Debug>.instance._isFieldBugFix;
        var currentFrame = ntMgr.getCurrentFrame();
        var num = ntMgr.getFrameInvisible();
        if (!isFieldBugFix)
            num = currentFrame - 300f;

        var frameVisible  = ntMgr.getFrameVisible();
        var noteAppearZ   = ntMgr.getNoteAppearZ();

        foreach (var notesOneWay in _notesOneWayList)
        {
            var lanedata = notesOneWay._lanedata;
            if (lanedata.Count == 0)
                continue;

            var frame = lanedata[0].frame;
            if (frame > frameVisible)
                continue;

            var frame2 = lanedata[lanedata.Count - 1].frame;
            if (frame2 < num)
                continue;

            for (var i = 0; i < lanedata.Count; i++)
            {
                var flag = i == lanedata.Count - 1;
                Node node;
                Node node2;
                if (flag)
                {
                    node  = lanedata[lanedata.Count - 1];
                    node2 = lanedata[lanedata.Count - 1];
                }
                else
                {
                    node  = lanedata[i];
                    node2 = lanedata[i + 1];
                }

                var frame3 = node.frame;
                var frame4 = node2.frame;

                if (node2.frame < num || node.frame >= frameVisible)
                    continue;

                if (!flag)
                {
                    var num2 = num;
                    if (frame3 < num2 && num2 <= frame4)
                    {
                        var rate = CalcRate(frame3, frame4, num2);
                        notesOneWay.makeJointLimitParam(ntMgr, node, node2, rate, _widthAmp);
                    }
                }

                if (node.frame >= num && node.frame < frameVisible)
                    notesOneWay.makeJointLimitParam(ntMgr, node, _widthAmp);

                if (!flag)
                {
                    // Compute once; reused by both the bidirectional clip
                    // and the unidirectional break below.
                    var num3 = ntMgr.calcNotePosZ(frame3 - currentFrame);
                    var num4 = ntMgr.calcNotePosZ(frame4 - currentFrame);

                    if ((num3 <= noteAppearZ && noteAppearZ < num4)
                        || (num4 <= noteAppearZ && noteAppearZ < num3))
                    {
                        var rate2 = CalcRate(num3, num4, noteAppearZ);
                        notesOneWay.makeJointLimitParam(ntMgr, node, node2, rate2, _widthAmp, force: true);
                    }
                    
                    if (num3 <= noteAppearZ && noteAppearZ < num4)
                    {
                        var rate3 = CalcRate(num3, num4, noteAppearZ);
                        notesOneWay.makeJointLimitParam(ntMgr, node, node2, rate3, _widthAmp);
                        break;
                    }
                }

                if (!flag)
                {
                    var num7 = frameVisible;
                    if (frame3 < num7 && num7 <= frame4)
                    {
                        var rate4 = CalcRate(frame3, frame4, num7);
                        notesOneWay.makeJointLimitParam(ntMgr, node, node2, rate4, _widthAmp);
                    }
                }
            }
        }
    }
    public static float CalcRate(float from, float to, float mid, float minimum = 0.01f)
    {
        float num;
        if (to > from)
        {
            num = Mathf.Max(to - from, minimum);
        }
        else
        {
            if (!(to < from))
            {
                return from;
            }
            num = Mathf.Min(to - from, 0f - minimum);
        }
        return (mid - from) / num;
    }
}