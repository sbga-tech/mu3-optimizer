using System.Collections.Generic;
using MonoMod;
using MU3.Mod;
using MU3.Notes;
using UnityEngine;

[MonoModIfFlag("PrimitiveMeshEmission")]
public class patch_PrimitiveMesh : PrimitiveMesh
{
    [MonoModIgnore] private Mesh _mesh;
    [MonoModIgnore] private bool _isMeshValid;
    [MonoModIgnore] private List<Vector3> _vertices;
    [MonoModIgnore] private List<Color> _colors;
    [MonoModIgnore] private List<Vector2> _uv;
    [MonoModIgnore] private List<int> _triangles;
    [MonoModIgnore] private int _stripVertexCount;

    // Fast emission: JointUtil quad data is written straight into the List
    // backing arrays; LateUpdate fixes the sizes up once before upload.
    // Bound after every setCapacity (the only place _items can reallocate).
    private Vector3[] _fastVerts;
    private Color[] _fastCols;
    private Vector2[] _fastUV;
    private int[] _fastTris;
    internal int _fastV;
    internal int _fastU;
    internal int _fastT;
    private bool _fastBound;

    internal bool fastReady { get { return _fastBound; } }

    public extern void orig_setCapacity(int verticesMax, int trianglesMax);

    public new void setCapacity(int verticesMax, int trianglesMax)
    {
        orig_setCapacity(verticesMax, trianglesMax);
        bindFast();
    }

    private void bindFast()
    {
        _fastBound = false;
        _fastV = 0;
        _fastU = 0;
        _fastT = 0;

        var getVerts = ListInternals<Vector3>.GetItems;
        var getCols = ListInternals<Color>.GetItems;
        var getUV = ListInternals<Vector2>.GetItems;
        var getTris = ListInternals<int>.GetItems;
        if (getVerts == null || getCols == null || getUV == null || getTris == null
            || ListInternals<Vector3>.SetSize == null || ListInternals<Color>.SetSize == null
            || ListInternals<Vector2>.SetSize == null || ListInternals<int>.SetSize == null)
            return;

        _fastVerts = getVerts(_vertices);
        _fastCols = getCols(_colors);
        _fastUV = getUV(_uv);
        _fastTris = getTris(_triangles);
        _fastBound = _fastVerts != null && _fastCols != null && _fastUV != null && _fastTris != null;
    }

    /// <summary>addJointNotePrim, writing the backing arrays directly.</summary>
    internal void fastJointPrim(float x0L, float x0R, float z0, float x1L, float x1R, float z1,
        float u0L, float u0R, float u1L, float u1R, float v0, float v1, float y,
        ref Color col0, ref Color col1)
    {
        int count = _fastV;
        var verts = _fastVerts;
        if (count + 4 > verts.Length)
            return;

        int u = _fastU;
        var uv = _fastUV;
        if (z0 < z1)
        {
            verts[count] = new Vector3(x0L, y, z0);
            verts[count + 1] = new Vector3(x0R, y, z0);
            verts[count + 2] = new Vector3(x1L, y, z1);
            verts[count + 3] = new Vector3(x1R, y, z1);
            uv[u] = new Vector2(u0L, v0);
            uv[u + 1] = new Vector2(u0R, v0);
            uv[u + 2] = new Vector2(u1L, v1);
            uv[u + 3] = new Vector2(u1R, v1);
        }
        else
        {
            verts[count] = new Vector3(x0R, y, z0);
            verts[count + 1] = new Vector3(x0L, y, z0);
            verts[count + 2] = new Vector3(x1R, y, z1);
            verts[count + 3] = new Vector3(x1L, y, z1);
            uv[u] = new Vector2(u0R, v0);
            uv[u + 1] = new Vector2(u0L, v0);
            uv[u + 2] = new Vector2(u1R, v1);
            uv[u + 3] = new Vector2(u1L, v1);
        }
        var cols = _fastCols;
        cols[count] = col0;
        cols[count + 1] = col0;
        cols[count + 2] = col1;
        cols[count + 3] = col1;
        int t = _fastT;
        var tris = _fastTris;
        tris[t] = count;
        tris[t + 1] = count + 1;
        tris[t + 2] = count + 2;
        tris[t + 3] = count + 2;
        tris[t + 4] = count + 1;
        tris[t + 5] = count + 3;
        _fastV = count + 4;
        _fastU = u + 4;
        _fastT = t + 6;
    }

    /// <summary>addJointNoteWall, writing the backing arrays directly.</summary>
    internal void fastJointWall(float x0, float z0, float x1, float z1, float yBtm, float yTop,
        ref Color colBtm, ref Color colTop)
    {
        int count = _fastV;
        var verts = _fastVerts;
        if (count + 4 > verts.Length)
            return;

        if (z0 < z1)
        {
            verts[count] = new Vector3(x0, yBtm, z0);
            verts[count + 1] = new Vector3(x1, yBtm, z1);
            verts[count + 2] = new Vector3(x0, yTop, z0);
            verts[count + 3] = new Vector3(x1, yTop, z1);
        }
        else
        {
            verts[count] = new Vector3(x1, yBtm, z1);
            verts[count + 1] = new Vector3(x0, yBtm, z0);
            verts[count + 2] = new Vector3(x1, yTop, z1);
            verts[count + 3] = new Vector3(x0, yTop, z0);
        }
        var cols = _fastCols;
        cols[count] = colBtm;
        cols[count + 1] = colBtm;
        cols[count + 2] = colTop;
        cols[count + 3] = colTop;
        // The original appends no UVs for walls: the Wall mesh only ever
        // receives drawWall quads, so its UV list stays empty and SetUVs
        // clears the channel. _fastU intentionally not advanced.
        int t = _fastT;
        var tris = _fastTris;
        tris[t] = count;
        tris[t + 1] = count + 1;
        tris[t + 2] = count + 2;
        tris[t + 3] = count + 2;
        tris[t + 4] = count + 1;
        tris[t + 5] = count + 3;
        _fastV = count + 4;
        _fastT = t + 6;
    }

    /// <summary>addJointNoteQuadRange, writing the backing arrays directly.</summary>
    internal void fastJointQuadRange(ref Vector2 posLD, ref Vector2 posRD, ref Vector2 posLU,
        ref Vector2 posRU, float vD, float vU, float y, ref Color colD, ref Color colU, bool isRight)
    {
        int count = _fastV;
        var verts = _fastVerts;
        if (count + 4 > verts.Length)
            return;

        int u = _fastU;
        var uv = _fastUV;
        if (!isRight)
        {
            verts[count] = new Vector3(posLD.x, y, posLD.y);
            verts[count + 1] = new Vector3(posRD.x, y, posRD.y);
            verts[count + 2] = new Vector3(posLU.x, y, posLU.y);
            verts[count + 3] = new Vector3(posRU.x, y, posRU.y);
            uv[u] = new Vector2(0f, vD);
            uv[u + 1] = new Vector2(1f, vD);
            uv[u + 2] = new Vector2(0f, vU);
            uv[u + 3] = new Vector2(1f, vU);
        }
        else
        {
            verts[count] = new Vector3(posRD.x, y, posRD.y);
            verts[count + 1] = new Vector3(posLD.x, y, posLD.y);
            verts[count + 2] = new Vector3(posRU.x, y, posRU.y);
            verts[count + 3] = new Vector3(posLU.x, y, posLU.y);
            uv[u] = new Vector2(1f, vD);
            uv[u + 1] = new Vector2(0f, vD);
            uv[u + 2] = new Vector2(1f, vU);
            uv[u + 3] = new Vector2(0f, vU);
        }
        var cols = _fastCols;
        cols[count] = colD;
        cols[count + 1] = colD;
        cols[count + 2] = colU;
        cols[count + 3] = colU;
        int t = _fastT;
        var tris = _fastTris;
        tris[t] = count;
        tris[t + 1] = count + 1;
        tris[t + 2] = count + 2;
        tris[t + 3] = count + 2;
        tris[t + 4] = count + 1;
        tris[t + 5] = count + 3;
        _fastV = count + 4;
        _fastU = u + 4;
        _fastT = t + 6;
    }

    [MonoModReplace]
    private void LateUpdate()
    {
        if (!_isMeshValid)
            return;

        if (_fastV > 0)
        {
            if (_vertices.Count > 0)
            {
                // List.Add path was also used this frame (never happens in
                // gameplay: JointUtil is the only production emitter). The
                // relative order is unknowable, so keep the List content.
                _fastV = 0;
                _fastU = 0;
                _fastT = 0;
            }
            else
            {
                ListInternals<Vector3>.SetSize(_vertices, _fastV);
                ListInternals<Color>.SetSize(_colors, _fastV);
                ListInternals<Vector2>.SetSize(_uv, _fastU);
                ListInternals<int>.SetSize(_triangles, _fastT);
                _fastV = 0;
                _fastU = 0;
                _fastT = 0;
            }
        }

        if (_vertices.Count == 0)
        {
            if (_mesh.vertexCount != 0)
                _mesh.Clear();
        }
        else
        {
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _uv);
            _mesh.SetTriangles(_triangles, 0);
        }

        _stripVertexCount = -1;
        _vertices.Clear();
        _colors.Clear();
        _uv.Clear();
        _triangles.Clear();
    }
}
