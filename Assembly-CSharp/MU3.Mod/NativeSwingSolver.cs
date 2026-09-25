using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using MU3.Mod.Native;
using UnityEngine;

namespace MU3.Mod;

/// <summary>
/// Managed surface of the embedded mu3swing.dll direct-mode SwingJoint
/// solver. Struct layouts must stay in sync with Native/mu3swing/src/lib.rs.
/// </summary>
internal static class NativeSwingSolver
{
    /// <summary>
    /// Per-joint input for the direct native-transform mode: MonoObject*
    /// of the joint transforms plus the managed-field parameters (manager
    /// overwrites already applied). Mirrored by Rust JointRef (Pack = 8,
    /// pointers first; NowChild/OldChild are in-out verlet state).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct JointRef
    {
        public IntPtr Transform;
        public IntPtr Child;
        public Quaternion InitLocalRot;
        public Vector3 NodeAxis;
        public float NodeLength;
        public float RadiusBase;
        public float ChildPull;
        public float Inertial;
        public Vector3 StaticForce;
        public float PhysicsRatio;
        public Vector3 NowChild;
        public Vector3 OldChild;
    }

    private const string NativeLibraryName = "mu3swing.dll";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeInitDelegate(
        IntPtr getPositionMethod,
        IntPtr getRotationMethod,
        IntPtr getLossyScaleMethod,
        IntPtr setLocalRotationMethod,
        IntPtr setRotationMethod);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int VerifyTransformDelegate(
        IntPtr transformObj,
        IntPtr expectCachedPtr,
        ref Vector3 expectPos,
        ref Quaternion expectRot,
        ref Vector3 expectLossy,
        out Quaternion postRot);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int SolveDirectDelegate(
        JointRef* joints,
        int njoints,
        MU3.SwingJointColliderInformation* colliders,
        int ncolliders,
        float* plane);

    private static bool _probed;
    private static bool _available;
    private static NativeInitDelegate _nativeInit;
    private static VerifyTransformDelegate _verifyTransform;
    private static SolveDirectDelegate _solveDirect;

    /// <summary>True when the embedded native image and all exports resolve.</summary>
    internal static bool Available
    {
        get
        {
            if (_probed)
                return _available;

            _probed = true;
            try
            {
                BindEmbeddedImage();
                _available = true;
            }
            catch (Exception exception)
            {
                _available = false;
                Debug.LogWarning("[Steroid][SwingNative] embedded mu3swing image unavailable; using managed solver. "
                    + exception.Message);
            }
            return _available;
        }
    }

    private static void BindEmbeddedImage()
    {
        if (IntPtr.Size != 8)
            throw new PlatformNotSupportedException("mu3swing requires a 64-bit process.");

        var module = ModulesRegistry.LoadLibrary(NativeLibraryName);
        _nativeInit = module.GetFunction<NativeInitDelegate>("mu3_swing_native_init");
        _verifyTransform = module.GetFunction<VerifyTransformDelegate>(
            "mu3_swing_verify_transform");
        _solveDirect = module.GetFunction<SolveDirectDelegate>("mu3_swing_solve_direct");
    }

    // ------------------------------------------------------------------
    // m_CachedPtr reader for the direct native-transform mode
    // ------------------------------------------------------------------

    private static bool _ptrReaderProbed;
    private static Func<UnityEngine.Object, IntPtr> _readCachedPtr;

    /// <summary>
    /// Compiled ldfld reader for UnityEngine.Object.m_CachedPtr (the native
    /// object pointer; zeroed by Unity when the native side is destroyed).
    /// Null when the field or DynamicMethod support is unavailable.
    /// </summary>
    internal static Func<UnityEngine.Object, IntPtr> CachedPtrReader
    {
        get
        {
            if (_ptrReaderProbed)
                return _readCachedPtr;

            _ptrReaderProbed = true;
            try
            {
                var cachedPtrField = typeof(UnityEngine.Object).GetField(
                    "m_CachedPtr", BindingFlags.Instance | BindingFlags.NonPublic);
                if (cachedPtrField == null || cachedPtrField.FieldType != typeof(IntPtr))
                    return null;

                var method = new DynamicMethod(
                    "ReadCachedPtr", typeof(IntPtr), new[] { typeof(UnityEngine.Object) },
                    typeof(NativeSwingSolver).Module, true);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, cachedPtrField);
                il.Emit(OpCodes.Ret);
                _readCachedPtr = (Func<UnityEngine.Object, IntPtr>)method.CreateDelegate(
                    typeof(Func<UnityEngine.Object, IntPtr>));
            }
            catch (Exception exception)
            {
                _readCachedPtr = null;
                Debug.LogWarning("[Steroid][SwingNative] m_CachedPtr reader unavailable; using managed solver. "
                    + exception.Message);
            }
            return _readCachedPtr;
        }
    }

    // ------------------------------------------------------------------
    // MonoObject* reader for the direct native-transform mode
    // ------------------------------------------------------------------

    private static bool _objReaderProbed;
    private static Func<UnityEngine.Object, IntPtr> _readObjectPtr;

    /// <summary>
    /// Compiled (ldarg.0; conv.i) reader returning the MonoObject* behind a
    /// managed reference. Stable under Unity's non-moving Boehm GC; the
    /// native probe verifies the pointer by comparing its m_CachedPtr field
    /// with the managed-read value before the direct mode is enabled.
    /// </summary>
    internal static Func<UnityEngine.Object, IntPtr> ObjectPtrReader
    {
        get
        {
            if (_objReaderProbed)
                return _readObjectPtr;

            _objReaderProbed = true;
            try
            {
                var method = new DynamicMethod(
                    "ReadObjectPtr", typeof(IntPtr), new[] { typeof(UnityEngine.Object) },
                    typeof(NativeSwingSolver).Module, true);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Conv_I);
                il.Emit(OpCodes.Ret);
                _readObjectPtr = (Func<UnityEngine.Object, IntPtr>)method.CreateDelegate(
                    typeof(Func<UnityEngine.Object, IntPtr>));
            }
            catch (Exception exception)
            {
                _readObjectPtr = null;
                Debug.LogWarning("[Steroid][SwingNative] MonoObject reader unavailable; using managed solver. "
                    + exception.Message);
            }
            return _readObjectPtr;
        }
    }

    /// <summary>
    /// Resolves the five Transform icalls inside mu3swing.dll through
    /// mono_lookup_internal_call, keyed by MonoMethod* handles. 0 ok;
    /// -15 when a MethodInfo is missing (unexpected UnityEngine surface).
    /// </summary>
    internal static int InitDirect()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var transformType = typeof(Transform);
        var getPosition = transformType.GetMethod("INTERNAL_get_position", flags);
        var getRotation = transformType.GetMethod("INTERNAL_get_rotation", flags);
        var getLossyScale = transformType.GetMethod("INTERNAL_get_lossyScale", flags);
        var setLocalRotation = transformType.GetMethod("INTERNAL_set_localRotation", flags);
        var setRotation = transformType.GetMethod("INTERNAL_set_rotation", flags);
        if (getPosition == null || getRotation == null || getLossyScale == null
            || setLocalRotation == null || setRotation == null)
            return -15;

        return _nativeInit(
            getPosition.MethodHandle.Value,
            getRotation.MethodHandle.Value,
            getLossyScale.MethodHandle.Value,
            setLocalRotation.MethodHandle.Value,
            setRotation.MethodHandle.Value);
    }

    /// <summary>
    /// Bitwise read/write verification against values read through managed
    /// Transform properties during the same frame.
    /// </summary>
    internal static int mu3_swing_verify_transform(
        IntPtr transformObj,
        IntPtr expectCachedPtr,
        ref Vector3 expectPos,
        ref Quaternion expectRot,
        ref Vector3 expectLossy,
        out Quaternion postRot)
    {
        return _verifyTransform(transformObj, expectCachedPtr, ref expectPos,
            ref expectRot, ref expectLossy, out postRot);
    }

    internal static unsafe int mu3_swing_solve_direct(
        JointRef* joints,
        int njoints,
        MU3.SwingJointColliderInformation* colliders,
        int ncolliders,
        float* plane)
    {
        return _solveDirect(joints, njoints, colliders, ncolliders, plane);
    }
}
