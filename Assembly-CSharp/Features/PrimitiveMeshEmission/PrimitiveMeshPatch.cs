using System.Collections.Generic;
using MonoMod;
using MU3.Notes;
using UnityEngine;

namespace MU3.Mod.PrimitiveMeshEmission;

[MonoModIfFlag(nameof(PatchConfig.PrimitiveMeshEmission))]
[MonoModPatch("global::PrimitiveMesh")]
public class PrimitiveMeshPatch : PrimitiveMesh
{
    [MonoModIgnore] private Mesh _mesh;
    [MonoModIgnore] private bool _isMeshValid;
    [MonoModIgnore] private List<Vector3> _vertices;
    [MonoModIgnore] private List<Color> _colors;
    [MonoModIgnore] private List<Vector2> _uv;
    [MonoModIgnore] private List<int> _triangles;
    [MonoModIgnore] private int _stripVertexCount;

    // JointUtil writes into the List backing arrays. When the embedded native
    // emitter is available, commands are queued and emitted in one call from
    // LateUpdate. The same managed writers remain the exact fallback.
    private Vector3[] _fastVerts;
    private Color[] _fastCols;
    private Vector2[] _fastUV;
    private int[] _fastTris;
    private NativeGeometryEmitter.Command[] _nativeCommands;
    private int _nativeCommandCount;
    internal int _fastV;
    internal int _fastU;
    internal int _fastT;
    private bool _fastBound;
    private bool _managedEmission;
    private bool _listFallback;

    internal bool fastReady { get { return _fastBound && !_listFallback; } }

    public extern void orig_setCapacity(int verticesMax, int trianglesMax);

    public new void setCapacity(int verticesMax, int trianglesMax)
    {
        orig_setCapacity(verticesMax, trianglesMax);
        bindFast();
    }

    private void bindFast()
    {
        _fastBound = false;
        resetFastFrame();

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
        if (!_fastBound)
            return;

        var commandCapacity = _fastVerts.Length / 4;
        var triangleCapacity = _fastTris.Length / 6;
        if (triangleCapacity < commandCapacity)
            commandCapacity = triangleCapacity;
        if (_nativeCommands == null || _nativeCommands.Length != commandCapacity)
            _nativeCommands = new NativeGeometryEmitter.Command[commandCapacity];
    }

    private void resetFastFrame()
    {
        _nativeCommandCount = 0;
        _fastV = 0;
        _fastU = 0;
        _fastT = 0;
        _managedEmission = false;
        _listFallback = false;
    }

    private bool hasCapacity(bool usesUv)
    {
        return _fastV + 4 <= _fastVerts.Length
            && _fastV + 4 <= _fastCols.Length
            && (!usesUv || _fastU + 4 <= _fastUV.Length)
            && _fastT + 6 <= _fastTris.Length;
    }

    private bool canQueueNative()
    {
        return !_managedEmission
            && _nativeCommands != null
            && _nativeCommandCount < _nativeCommands.Length
            && NativeGeometryEmitter.Available;
    }

    private void queueCommand(ref NativeGeometryEmitter.Command command, bool usesUv)
    {
        _nativeCommands[_nativeCommandCount++] = command;
        _fastV += 4;
        if (usesUv)
            _fastU += 4;
        _fastT += 6;
    }

    private void replayQueuedManaged()
    {
        if (_nativeCommandCount == 0)
            return;

        var vertex = 0;
        var uv = 0;
        var triangle = 0;
        for (var i = 0; i < _nativeCommandCount; i++)
        {
            var command = _nativeCommands[i];
            switch (command.Kind)
            {
                case NativeGeometryEmitter.Prim:
                    writeJointPrim(ref command, ref vertex, ref uv, ref triangle);
                    break;
                case NativeGeometryEmitter.Wall:
                    writeJointWall(ref command, ref vertex, ref triangle);
                    break;
                case NativeGeometryEmitter.QuadRange:
                    writeJointQuadRange(ref command, ref vertex, ref uv, ref triangle);
                    break;
            }
        }
        _nativeCommandCount = 0;
        _managedEmission = true;
    }

    private void prepareListFallback()
    {
        replayQueuedManaged();
        ListInternals<Vector3>.SetSize(_vertices, _fastV);
        ListInternals<Color>.SetSize(_colors, _fastV);
        ListInternals<Vector2>.SetSize(_uv, _fastU);
        ListInternals<int>.SetSize(_triangles, _fastT);
        _fastV = 0;
        _fastU = 0;
        _fastT = 0;
        _listFallback = true;
    }

    /// <summary>Queues or emits addJointNotePrim. False selects the original List path.</summary>
    internal bool fastJointPrim(float x0L, float x0R, float z0, float x1L, float x1R, float z1,
        float u0L, float u0R, float u1L, float u1R, float v0, float v1, float y,
        ref Color col0, ref Color col1)
    {
        if (!hasCapacity(true))
        {
            prepareListFallback();
            return false;
        }

        var command = new NativeGeometryEmitter.Command();
        command.Kind = NativeGeometryEmitter.Prim;
        command.P0 = x0L;
        command.P1 = x0R;
        command.P2 = z0;
        command.P3 = x1L;
        command.P4 = x1R;
        command.P5 = z1;
        command.P6 = u0L;
        command.P7 = u0R;
        command.P8 = u1L;
        command.P9 = u1R;
        command.P10 = v0;
        command.P11 = v1;
        command.P12 = y;
        command.SetColors(ref col0, ref col1);
        if (canQueueNative())
            queueCommand(ref command, true);
        else
        {
            replayQueuedManaged();
            writeJointPrim(ref command, ref _fastV, ref _fastU, ref _fastT);
        }
        return true;
    }

    /// <summary>Queues or emits addJointNoteWall. False selects the original List path.</summary>
    internal bool fastJointWall(float x0, float z0, float x1, float z1, float yBtm, float yTop,
        ref Color colBtm, ref Color colTop)
    {
        if (!hasCapacity(false))
        {
            prepareListFallback();
            return false;
        }

        var command = new NativeGeometryEmitter.Command();
        command.Kind = NativeGeometryEmitter.Wall;
        command.P0 = x0;
        command.P1 = z0;
        command.P2 = x1;
        command.P3 = z1;
        command.P4 = yBtm;
        command.P5 = yTop;
        command.SetColors(ref colBtm, ref colTop);
        if (canQueueNative())
            queueCommand(ref command, false);
        else
        {
            replayQueuedManaged();
            writeJointWall(ref command, ref _fastV, ref _fastT);
        }
        return true;
    }

    /// <summary>Queues or emits addJointNoteQuadRange. False selects the original List path.</summary>
    internal bool fastJointQuadRange(ref Vector2 posLD, ref Vector2 posRD, ref Vector2 posLU,
        ref Vector2 posRU, float vD, float vU, float y, ref Color colD, ref Color colU, bool isRight)
    {
        if (!hasCapacity(true))
        {
            prepareListFallback();
            return false;
        }

        var command = new NativeGeometryEmitter.Command();
        command.Kind = NativeGeometryEmitter.QuadRange;
        command.Flags = isRight ? 1 : 0;
        command.P0 = posLD.x;
        command.P1 = posLD.y;
        command.P2 = posRD.x;
        command.P3 = posRD.y;
        command.P4 = posLU.x;
        command.P5 = posLU.y;
        command.P6 = posRU.x;
        command.P7 = posRU.y;
        command.P8 = vD;
        command.P9 = vU;
        command.P10 = y;
        command.SetColors(ref colD, ref colU);
        if (canQueueNative())
            queueCommand(ref command, true);
        else
        {
            replayQueuedManaged();
            writeJointQuadRange(ref command, ref _fastV, ref _fastU, ref _fastT);
        }
        return true;
    }

    private void writeJointPrim(ref NativeGeometryEmitter.Command command,
        ref int vertex, ref int uv, ref int triangle)
    {
        if (command.P2 < command.P5)
        {
            _fastVerts[vertex] = new Vector3(command.P0, command.P12, command.P2);
            _fastVerts[vertex + 1] = new Vector3(command.P1, command.P12, command.P2);
            _fastVerts[vertex + 2] = new Vector3(command.P3, command.P12, command.P5);
            _fastVerts[vertex + 3] = new Vector3(command.P4, command.P12, command.P5);
            _fastUV[uv] = new Vector2(command.P6, command.P10);
            _fastUV[uv + 1] = new Vector2(command.P7, command.P10);
            _fastUV[uv + 2] = new Vector2(command.P8, command.P11);
            _fastUV[uv + 3] = new Vector2(command.P9, command.P11);
        }
        else
        {
            _fastVerts[vertex] = new Vector3(command.P1, command.P12, command.P2);
            _fastVerts[vertex + 1] = new Vector3(command.P0, command.P12, command.P2);
            _fastVerts[vertex + 2] = new Vector3(command.P4, command.P12, command.P5);
            _fastVerts[vertex + 3] = new Vector3(command.P3, command.P12, command.P5);
            _fastUV[uv] = new Vector2(command.P7, command.P10);
            _fastUV[uv + 1] = new Vector2(command.P6, command.P10);
            _fastUV[uv + 2] = new Vector2(command.P9, command.P11);
            _fastUV[uv + 3] = new Vector2(command.P8, command.P11);
        }
        writeColors(ref command, vertex);
        writeTriangles(vertex, triangle);
        vertex += 4;
        uv += 4;
        triangle += 6;
    }

    private void writeJointWall(ref NativeGeometryEmitter.Command command,
        ref int vertex, ref int triangle)
    {
        if (command.P1 < command.P3)
        {
            _fastVerts[vertex] = new Vector3(command.P0, command.P4, command.P1);
            _fastVerts[vertex + 1] = new Vector3(command.P2, command.P4, command.P3);
            _fastVerts[vertex + 2] = new Vector3(command.P0, command.P5, command.P1);
            _fastVerts[vertex + 3] = new Vector3(command.P2, command.P5, command.P3);
        }
        else
        {
            _fastVerts[vertex] = new Vector3(command.P2, command.P4, command.P3);
            _fastVerts[vertex + 1] = new Vector3(command.P0, command.P4, command.P1);
            _fastVerts[vertex + 2] = new Vector3(command.P2, command.P5, command.P3);
            _fastVerts[vertex + 3] = new Vector3(command.P0, command.P5, command.P1);
        }
        writeColors(ref command, vertex);
        writeTriangles(vertex, triangle);
        vertex += 4;
        triangle += 6;
    }

    private void writeJointQuadRange(ref NativeGeometryEmitter.Command command,
        ref int vertex, ref int uv, ref int triangle)
    {
        if ((command.Flags & 1) == 0)
        {
            _fastVerts[vertex] = new Vector3(command.P0, command.P10, command.P1);
            _fastVerts[vertex + 1] = new Vector3(command.P2, command.P10, command.P3);
            _fastVerts[vertex + 2] = new Vector3(command.P4, command.P10, command.P5);
            _fastVerts[vertex + 3] = new Vector3(command.P6, command.P10, command.P7);
            _fastUV[uv] = new Vector2(0f, command.P8);
            _fastUV[uv + 1] = new Vector2(1f, command.P8);
            _fastUV[uv + 2] = new Vector2(0f, command.P9);
            _fastUV[uv + 3] = new Vector2(1f, command.P9);
        }
        else
        {
            _fastVerts[vertex] = new Vector3(command.P2, command.P10, command.P3);
            _fastVerts[vertex + 1] = new Vector3(command.P0, command.P10, command.P1);
            _fastVerts[vertex + 2] = new Vector3(command.P6, command.P10, command.P7);
            _fastVerts[vertex + 3] = new Vector3(command.P4, command.P10, command.P5);
            _fastUV[uv] = new Vector2(1f, command.P8);
            _fastUV[uv + 1] = new Vector2(0f, command.P8);
            _fastUV[uv + 2] = new Vector2(1f, command.P9);
            _fastUV[uv + 3] = new Vector2(0f, command.P9);
        }
        writeColors(ref command, vertex);
        writeTriangles(vertex, triangle);
        vertex += 4;
        uv += 4;
        triangle += 6;
    }

    private void writeColors(ref NativeGeometryEmitter.Command command, int vertex)
    {
        var color0 = new Color(command.Color0R, command.Color0G, command.Color0B, command.Color0A);
        var color1 = new Color(command.Color1R, command.Color1G, command.Color1B, command.Color1A);
        _fastCols[vertex] = color0;
        _fastCols[vertex + 1] = color0;
        _fastCols[vertex + 2] = color1;
        _fastCols[vertex + 3] = color1;
    }

    private void writeTriangles(int vertex, int triangle)
    {
        _fastTris[triangle] = vertex;
        _fastTris[triangle + 1] = vertex + 1;
        _fastTris[triangle + 2] = vertex + 2;
        _fastTris[triangle + 3] = vertex + 2;
        _fastTris[triangle + 4] = vertex + 1;
        _fastTris[triangle + 5] = vertex + 3;
    }

    [MonoModReplace]
    private void LateUpdate()
    {
        if (!_isMeshValid)
            return;

        if (_nativeCommandCount > 0)
        {
            int vertexCount;
            int uvCount;
            int triangleCount;
            if (!NativeGeometryEmitter.TryEmit(
                _nativeCommands,
                _nativeCommandCount,
                _fastVerts,
                _fastCols,
                _fastUV,
                _fastTris,
                _fastV,
                _fastU,
                _fastT,
                out vertexCount,
                out uvCount,
                out triangleCount))
                replayQueuedManaged();
            else
                _nativeCommandCount = 0;
        }

        if (_fastV > 0)
        {
            if (_vertices.Count > 0)
            {
                // JointUtil is the only production emitter. Preserve the
                // original List content if an unexpected mixed path occurs.
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

        var rebind = _listFallback;
        _stripVertexCount = -1;
        _vertices.Clear();
        _colors.Clear();
        _uv.Clear();
        _triangles.Clear();
        resetFastFrame();
        if (rebind)
            bindFast();
    }
}
