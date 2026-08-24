using MonoMod;
using MU3.Battle;
using MU3.Util;
using UnityEngine;

namespace MU3.Notes;

[MonoModIfFlag("LaneGeometryCulling")]
public class patch_JointLane : JointLane
{
    [MonoModIgnore] private JointManager.LaneInitParam _initParam;

    [MonoModReplace]
    public new void set(JointManager.LaneSetParam param, int pattern = 0)
    {
        if (!_initParam.parts.enable)
            return;
        reset();
        var shapeData = _initParam.shapeData;
        if (shapeData.Count <= 0)
            return;

        var notesManager = SingletonMonoBehaviour<GameEngine>.instance.notesManager;
        var zOff = notesManager.getCurrentZ(pattern) - notesManager.getJudgePosZ();
        var col = param.col;
        if (_initParam.isColorfulLane)
            col = JointUtil.alphaColor(col, EffectUberShader.BlendControl.Basic, GameOption.getBrightnessLane(_initParam.parts.colorfulLaneBrightnessID));
        else if (_initParam.isWall)
            col = JointUtil.alphaColor(col, EffectUberShader.BlendControl.Basic, GameOption.brightnessWall);
        else if (!_initParam.isHold)
            col = JointUtil.alphaColor(col, EffectUberShader.BlendControl.Basic, GameOption.brightnessLane);
        col = JointUtil.alphaColor(col, EffectUberShader.BlendControl.Basic, param.alpha);
        if (_initParam.isTransparent)
            return;

        var indexFore = param.indexFore;
        var indexRear = Mathf.Min(param.indexRear, shapeData.Count - 1);
        if (indexRear < 0)
            indexRear = shapeData.Count - 1;

        var zColRear = JointUtil.getZColRear();
        var widthAmp = param.widthAmp;
        var height = param.height;
        var meshType = _initParam.parts.primMgrTypes;
        var rateFore = param.rateFore;
        var rateRear = param.rateRear;

        for (var i = indexFore; i <= indexRear; i++)
        {
            var shape = shapeData[i];
            var posLD = shape.posFL;
            var posRD = shape.posFR;
            if (i == indexFore && rateFore > 1e-06f)
            {
                posLD = shape.getPosCut(isLeft: true, rateFore);
                posRD = shape.getPosCut(isLeft: false, rateFore);
            }
            var posLU = shape.posBL;
            var posRU = shape.posBR;
            if (i == indexRear && rateRear < 0.999999f)
            {
                posLU = shape.getPosCut(isLeft: true, rateRear);
                posRU = shape.getPosCut(isLeft: false, rateRear);
            }
            posLD.x *= widthAmp;
            posRD.x *= widthAmp;
            posLU.x *= widthAmp;
            posRU.x *= widthAmp;
            posLD.y -= zOff;
            posRD.y -= zOff;
            posLU.y -= zOff;
            posRU.y -= zOff;
            posLD.y = Mathf.Min(posLD.y, zColRear);
            posRD.y = Mathf.Min(posRD.y, zColRear);
            posLU.y = Mathf.Min(posLU.y, zColRear);
            posRU.y = Mathf.Min(posRU.y, zColRear);
            JointUtil.drawQuadRange(meshType, posLD, posRD, posLU, posRU, 0f, 1f, height, col, col);
        }
    }
}
