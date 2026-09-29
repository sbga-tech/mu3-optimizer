using System;
using UnityEngine;

namespace MU3.Mod.SwingJointPhysics;

// Native solver accessors used by SwingJointManagerPatch's direct mode.
public partial class SwingJointPatch
{
    /// <summary>
    /// Fills the direct-mode input: MonoObject* of both transforms plus
    /// managed-field parameters. No icalls - every transform access happens
    /// inside mu3swing.dll through the engine's registered icall
    /// implementations. Returns false when either native object is gone
    /// (m_CachedPtr == 0, Unity's fake-null), matching the managed null
    /// check; the native side re-checks before every call.
    /// </summary>
    internal bool fillRef(ref NativeSwingSolver.JointRef input)
    {
        if ((object)_transform == null || (object)_childNode == null)
            return false;

        var cachedPtr = NativeSwingSolver.CachedPtrReader;
        if (cachedPtr(_transform) == IntPtr.Zero || cachedPtr(_childNode) == IntPtr.Zero)
            return false;

        var objectPtr = NativeSwingSolver.ObjectPtrReader;
        input.Transform = objectPtr(_transform);
        input.Child = objectPtr(_childNode);

        input.InitLocalRot = _initialLocalRotation;
        var manager = _manager;
        input.RadiusBase = (manager != null && manager.overwriteRadius) ? manager.collisionRadius : _collisionRadius;
        var overwriteDynamic = manager != null && manager.overwriteDynamic;
        input.ChildPull = overwriteDynamic ? manager.childPullForce : _childPullForce;
        input.Inertial = overwriteDynamic ? manager.inertialForce : _inertialForce;
        input.StaticForce = (manager != null && manager.overwriteStatic) ? manager.staticForce : _staticForce;
        input.NodeAxis = _nodeAxis;
        input.NodeLength = _nodeLength;
        input.PhysicsRatio = _physicsRatio;
        input.NowChild = _nowChildPos;
        input.OldChild = _oldChildPos;
        return true;
    }

    internal void storeChild(ref NativeSwingSolver.JointRef input)
    {
        _oldChildPos = input.OldChild;
        _nowChildPos = input.NowChild;
    }

    /// <summary>
    /// One-time direct-mode validation on a live joint transform: native
    /// reads must bit-match the managed properties read this frame, and a
    /// native SetRotation must be visible to a managed re-read bit-exactly.
    /// 0 ok; -40/-41 joint unusable (try another); other codes are real
    /// mismatches that disable the mode.
    /// </summary>
    internal int verifyTransform()
    {
        if ((object)_transform == null)
            return -40;
        var cachedPtr = NativeSwingSolver.CachedPtrReader(_transform);
        if (cachedPtr == IntPtr.Zero)
            return -41;
        var transformObj = NativeSwingSolver.ObjectPtrReader(_transform);

        var pos = _transform.position;
        var rot = _transform.rotation;
        var lossy = _transform.lossyScale;
        Quaternion postRot;
        var status = NativeSwingSolver.mu3_swing_verify_transform(
            transformObj, cachedPtr, ref pos, ref rot, ref lossy, out postRot);
        if (status != 0)
            return status;

        var managed = _transform.rotation;
        if (managed.x != postRot.x || managed.y != postRot.y || managed.z != postRot.z || managed.w != postRot.w)
            return -25;
        return 0;
    }
}
