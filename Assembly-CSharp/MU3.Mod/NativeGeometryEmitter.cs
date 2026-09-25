using System;
using System.Runtime.InteropServices;
using MU3.Mod.Native;
using UnityEngine;

namespace MU3.Mod;

/// <summary>
/// Managed surface of the embedded mu3geometry.dll PrimitiveMesh emitter.
/// Struct layouts must stay in sync with Native/mu3geometry/src/lib.rs.
/// </summary>
internal static class NativeGeometryEmitter
{
    internal const int Prim = 0;
    internal const int Wall = 1;
    internal const int QuadRange = 2;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct Command
    {
        public int Kind;
        public int Flags;
        public float P0;
        public float P1;
        public float P2;
        public float P3;
        public float P4;
        public float P5;
        public float P6;
        public float P7;
        public float P8;
        public float P9;
        public float P10;
        public float P11;
        public float P12;
        public float Color0R;
        public float Color0G;
        public float Color0B;
        public float Color0A;
        public float Color1R;
        public float Color1G;
        public float Color1B;
        public float Color1A;

        public void SetColors(ref Color color0, ref Color color1)
        {
            Color0R = color0.r;
            Color0G = color0.g;
            Color0B = color0.b;
            Color0A = color0.a;
            Color1R = color1.r;
            Color1G = color1.g;
            Color1B = color1.b;
            Color1A = color1.a;
        }
    }

    private const string NativeLibraryName = "mu3geometry.dll";
    private const int ExpectedCommandSize = 92;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CommandSizeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int EmitDelegate(
        Command* commands,
        int commandCount,
        Vector3* vertices,
        int vertexCapacity,
        Color* colors,
        int colorCapacity,
        Vector2* uvs,
        int uvCapacity,
        int* triangles,
        int triangleCapacity,
        out int vertexCount,
        out int uvCount,
        out int triangleCount);

    private static bool _probed;
    private static bool _available;
    private static bool _failureLogged;
    private static EmitDelegate _emit;

    internal static bool Available
    {
        get
        {
            if (_probed)
                return _available;

            _probed = true;
            try
            {
                if (IntPtr.Size != 8)
                    throw new PlatformNotSupportedException("mu3geometry requires a 64-bit process.");
                if (Marshal.SizeOf(typeof(Command)) != ExpectedCommandSize
                    || Marshal.SizeOf(typeof(Vector3)) != 12
                    || Marshal.SizeOf(typeof(Color)) != 16
                    || Marshal.SizeOf(typeof(Vector2)) != 8)
                    throw new TypeLoadException("mu3geometry managed ABI layout mismatch.");

                var module = ModulesRegistry.LoadLibrary(NativeLibraryName);
                var commandSize = module.GetFunction<CommandSizeDelegate>(
                    "mu3_geometry_command_size");
                _emit = module.GetFunction<EmitDelegate>("mu3_geometry_emit");
                var nativeCommandSize = commandSize();
                if (nativeCommandSize != ExpectedCommandSize)
                    throw new TypeLoadException(
                        "mu3geometry command ABI mismatch: native=" + nativeCommandSize
                        + ", managed=" + ExpectedCommandSize + ".");
                _available = true;
            }
            catch (Exception exception)
            {
                Disable("embedded image unavailable; using managed emission. " + exception.Message);
            }
            return _available;
        }
    }

    internal static unsafe bool TryEmit(
        Command[] commands,
        int commandCount,
        Vector3[] vertices,
        Color[] colors,
        Vector2[] uvs,
        int[] triangles,
        int expectedVertexCount,
        int expectedUvCount,
        int expectedTriangleCount,
        out int vertexCount,
        out int uvCount,
        out int triangleCount)
    {
        vertexCount = 0;
        uvCount = 0;
        triangleCount = 0;
        if (!Available)
            return false;

        try
        {
            fixed (Command* commandPtr = commands)
            fixed (Vector3* vertexPtr = vertices)
            fixed (Color* colorPtr = colors)
            fixed (Vector2* uvPtr = uvs)
            fixed (int* trianglePtr = triangles)
            {
                var status = _emit(
                    commandPtr,
                    commandCount,
                    vertexPtr,
                    vertices.Length,
                    colorPtr,
                    colors.Length,
                    uvPtr,
                    uvs.Length,
                    trianglePtr,
                    triangles.Length,
                    out vertexCount,
                    out uvCount,
                    out triangleCount);
                if (status != 0)
                {
                    Disable("native emission failed with status " + status + ".");
                    return false;
                }
            }

            if (vertexCount != expectedVertexCount
                || uvCount != expectedUvCount
                || triangleCount != expectedTriangleCount)
            {
                Disable(
                    "native emission returned invalid counts "
                    + vertexCount + "/" + uvCount + "/" + triangleCount + ".");
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            Disable("native emission threw; using managed emission. " + exception.Message);
            return false;
        }
    }

    private static void Disable(string reason)
    {
        _available = false;
        if (_failureLogged)
            return;
        _failureLogged = true;
        Debug.LogWarning("[Steroid][GeometryNative] " + reason);
    }
}
