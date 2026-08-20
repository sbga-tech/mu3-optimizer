using System.Collections.Generic;
using MonoMod;
using UnityEngine;

[MonoModIfFlag("BetterNotes")]
public class patch_PrimitiveMesh : PrimitiveMesh
{
    [MonoModIgnore] private Mesh _mesh;
    [MonoModIgnore] private bool _isMeshValid;
    [MonoModIgnore] private List<Vector3> _vertices;
    [MonoModIgnore] private List<Color> _colors;
    [MonoModIgnore] private List<Vector2> _uv;
    [MonoModIgnore] private List<int> _triangles;
    [MonoModIgnore] private int _stripVertexCount;

    private static readonly List<int> EmptyTriangles = new List<int>();

    [MonoModReplace]
    private void LateUpdate()
    {
        if (!_isMeshValid)
            return;

        if (_vertices.Count == 0)
        {
            if (_mesh.vertexCount != 0)
                _mesh.Clear();
        }
        else
        {
            // Unity 5 rejects SetVertices smaller than the live index buffer.
            _mesh.SetTriangles(EmptyTriangles, 0);
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _uv);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
        }

        _stripVertexCount = -1;
        _vertices.Clear();
        _colors.Clear();
        _uv.Clear();
        _triangles.Clear();
    }
}
