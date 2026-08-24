using MonoMod;
using MU3.Battle;
using MU3.User;
using MU3.Util;
using UnityEngine;

namespace MU3.Notes;

[MonoModIfFlag("PrimitiveMeshEmission")]
public static class patch_JointUtil
{
    [MonoModIgnore] private static PrimitiveMesh.JointNotePrimParam _jointNotePrimParam;
    [MonoModIgnore] private static PrimitiveMesh.JointNoteWallParam _jointNoteWallParam;
    [MonoModIgnore] private static PrimitiveMesh.JointNoteQuadRangeParam _jointNoteQuadRangeParam;

    private static patch_NotesPrimitiveManager _prim;
    private static int _primFrame = int.MinValue;
    private static float _zColRear;
    private static int _zColRearFrame = int.MinValue;

    private static patch_NotesPrimitiveManager Prim()
    {
        var f = Time.frameCount;
        if (_primFrame != f)
        {
            _primFrame = f;
            _prim = (patch_NotesPrimitiveManager)SingletonMonoBehaviour<GameEngine>.instance.notesPrimitiveManager;
        }
        return _prim;
    }

    [MonoModReplace]
    public static float getZColRear()
    {
        var f = Time.frameCount;
        if (_zColRearFrame != f)
        {
            _zColRearFrame = f;
            _zColRear = 40f - (float)Singleton<UserManager>.instance.userOption.currentSet.FieldWall * (13f / 32f);
        }
        return _zColRear;
    }

    [MonoModReplace]
    public static void drawPrim(NotesPrimitiveManager.MeshType type, float x0L, float x0R, float z0, float x1L, float x1R, float z1, float u0L, float u0R, float u1L, float u1R, float v0, float v1, float y, Color col0, Color col1)
    {
        if (CustomMath.Abs(x0L - x0R) + CustomMath.Abs(x1L - x1R) < 1e-05f || CustomMath.Abs(z1 - z0) < 1e-05f)
            return;

        var mgr = Prim();
        var fast = mgr.fastMesh((int)type);
        if (fast != null)
        {
            fast.fastJointPrim(x0L, x0R, z0, x1L, x1R, z1, u0L, u0R, u1L, u1R, v0, v1, y,
                ref col0, ref col1);
            return;
        }

        _jointNotePrimParam.x0L = x0L;
        _jointNotePrimParam.x0R = x0R;
        _jointNotePrimParam.z0 = z0;
        _jointNotePrimParam.x1L = x1L;
        _jointNotePrimParam.x1R = x1R;
        _jointNotePrimParam.z1 = z1;
        _jointNotePrimParam.u0L = u0L;
        _jointNotePrimParam.u0R = u0R;
        _jointNotePrimParam.u1L = u1L;
        _jointNotePrimParam.u1R = u1R;
        _jointNotePrimParam.v0 = v0;
        _jointNotePrimParam.v1 = v1;
        _jointNotePrimParam.y = y;
        _jointNotePrimParam.col0 = col0;
        _jointNotePrimParam.col1 = col1;
        mgr.addJointNotePrim(type, _jointNotePrimParam);
    }

    [MonoModReplace]
    public static void drawWall(NotesPrimitiveManager.MeshType type, float x0, float z0, float x1, float z1, float yBtm, float yTop, Color colBtm, Color colTop)
    {
        var mgr = Prim();
        var fast = mgr.fastMesh((int)type);
        if (fast != null)
        {
            fast.fastJointWall(x0, z0, x1, z1, yBtm, yTop, ref colBtm, ref colTop);
            return;
        }

        _jointNoteWallParam.x0 = x0;
        _jointNoteWallParam.z0 = z0;
        _jointNoteWallParam.x1 = x1;
        _jointNoteWallParam.z1 = z1;
        _jointNoteWallParam.yBtm = yBtm;
        _jointNoteWallParam.yTop = yTop;
        _jointNoteWallParam.colBtm = colBtm;
        _jointNoteWallParam.colTop = colTop;
        mgr.addJointNoteWall(type, _jointNoteWallParam);
    }

    [MonoModReplace]
    public static void drawQuadRange(NotesPrimitiveManager.MeshType type, Vector2 posLD, Vector2 posRD, Vector2 posLU, Vector2 posRU, float vD, float vU, float y, Color colD, Color colU)
    {
        var edgeH = posRD - posLD;
        var edgeV = posLU - posLD;
        if ((CustomMath.Abs(edgeH.sqrMagnitude) < 1e-06f && CustomMath.Abs((posRU - posLU).sqrMagnitude) < 1e-06f)
            || (CustomMath.Abs(edgeV.sqrMagnitude) < 1e-06f && CustomMath.Abs((posRU - posRD).sqrMagnitude) < 1e-06f))
            return;

        var isRight = edgeH.x * edgeV.y - edgeV.x * edgeH.y < 0f;
        var mgr = Prim();
        var fast = mgr.fastMesh((int)type);
        if (fast != null)
        {
            fast.fastJointQuadRange(ref posLD, ref posRD, ref posLU, ref posRU, vD, vU, y,
                ref colD, ref colU, isRight);
            return;
        }

        _jointNoteQuadRangeParam.posLD = posLD;
        _jointNoteQuadRangeParam.posRD = posRD;
        _jointNoteQuadRangeParam.posLU = posLU;
        _jointNoteQuadRangeParam.posRU = posRU;
        _jointNoteQuadRangeParam.vD = vD;
        _jointNoteQuadRangeParam.vU = vU;
        _jointNoteQuadRangeParam.y = y;
        _jointNoteQuadRangeParam.colD = colD;
        _jointNoteQuadRangeParam.colU = colU;
        _jointNoteQuadRangeParam.isRight = isRight;
        mgr.addJointNoteQuadRange(type, _jointNoteQuadRangeParam);
    }
}
