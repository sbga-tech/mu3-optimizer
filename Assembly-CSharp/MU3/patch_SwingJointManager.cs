using System;
using System.Collections.Generic;
using MonoMod;
using MU3.Mod;
using UnityEngine;

namespace MU3;

// Second patch class for MU3.SwingJoint: this adds the native solver accessors
// used by the optimized swing-joint update path.
[MonoModIfFlag("SwingJointPhysics")]
[MonoModPatch("global::MU3.SwingJoint")]
public class patch_SwingJointNative : SwingJoint
{
    [MonoModIgnore] private Transform _childNode;
    [MonoModIgnore] private Vector3 _nodeAxis;
    [MonoModIgnore] private float _nodeLength;
    [MonoModIgnore] private Quaternion _initialLocalRotation;
    [MonoModIgnore] private Vector3 _nowChildPos;
    [MonoModIgnore] private Vector3 _oldChildPos;
    [MonoModIgnore] private Transform _transform;
    [MonoModIgnore] private SwingJointManager _manager;

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

[MonoModIfFlag("SwingJointPhysics")]
public class patch_SwingJointManager : SwingJointManager
{
    [MonoModIgnore] private List<SwingJoint> _joints;
    [MonoModIgnore] private SwingJointCollider[] _colliders;
    [MonoModIgnore] private SwingJointColliderInformation[] _colliderInformation;
    [MonoModIgnore] private Transform _centerNode;
    [MonoModIgnore] private SimpleKDTree _simpleKDTree;

    // Direct native-transform mode: 0 unknown (verification pending), 1 proven,
    // -1 unavailable (identity/verification/solve failure; managed solver stays).
    private static int _directState;
    private NativeSwingSolver.JointRef[] _refs;
    private patch_SwingJointNative[] _scratch;
    // MonoMod does not run patch-class field initializers; allocated in
    // solveDirect() with the other scratch arrays.
    private float[] _plane;

    // SwingJointFPS gate state, shared by every manager so the characters
    // stay coherent; the decision is made once per rendered frame. All
    // fields deliberately work from CLR defaults (patch-class field
    // initializers never run).
    private static int _fpsFrame;
    private static bool _fpsRun;
    private static float _fpsLastSolve;

    /// <summary>
    /// SwingJoint verlet has no deltaTime term: at 120+ fps render the sway
    /// runs faster than the 60 fps cabinet spec. Capping the simulation at
    /// SwingJointFPS (default 60) both restores the intended dynamics and
    /// sheds the extra solves; 0 or negative solves every frame.
    /// </summary>
    private static bool fpsGate()
    {
        var fps = SwingJointPhysicsConfig.SwingJointFPS;
        if (fps <= 0f)
            return true;

        var frame = Time.frameCount;
        if (frame != _fpsFrame)
        {
            _fpsFrame = frame;
            var interval = 1f / fps;
            var now = Time.unscaledTime;
            _fpsRun = now - _fpsLastSolve >= interval;
            if (_fpsRun)
            {
                // Carry the schedule instead of resetting to now, so frame
                // quantization does not erode the average rate; resync after
                // stalls to avoid burst catch-up solves.
                _fpsLastSolve += interval;
                if (now - _fpsLastSolve >= interval)
                    _fpsLastSolve = now;
            }
        }
        return _fpsRun;
    }

    [MonoModReplace]
    private void LateUpdate()
    {
        if (!fpsGate())
            return;

        for (var i = 0; i < _colliders.Length; i++)
            _colliders[i].updateCache(ref _colliderInformation[i]);

        if (SwingJointPhysicsConfig.SwingJointNative && NativeSwingSolver.Available
            && ensureDirect() && solveDirect())
            return;

        _simpleKDTree.update(_centerNode);
        for (var j = 0; j < _joints.Count; j++)
        {
            if (_joints[j].gameObject.activeInHierarchy)
                _joints[j].update();
        }
    }

    /// <summary>
    /// Lazily verifies the direct native-transform mode: reader support,
    /// mono_lookup_internal_call resolution of the Transform icalls inside
    /// mu3swing.dll, then a bitwise read/write probe on the first live
    /// joint (which also validates the assumed MonoObject layout). Stays
    /// pending (0) until a live joint exists; any real mismatch latches -1.
    /// </summary>
    private bool ensureDirect()
    {
        if (_directState != 0)
            return _directState > 0;

        if (NativeSwingSolver.CachedPtrReader == null || NativeSwingSolver.ObjectPtrReader == null)
        {
            _directState = -1;
            return false;
        }
        var init = NativeSwingSolver.InitDirect();
        if (init != 0)
        {
            _directState = -1;
            Debug.Log("[SwingNative] Direct transform mode unavailable (init " + init + "); managed solver stays.");
            return false;
        }
        for (var i = 0; i < _joints.Count; i++)
        {
            var status = ((patch_SwingJointNative)(object)_joints[i]).verifyTransform();
            if (status == -40 || status == -41)
                continue;
            if (status == 0)
            {
                _directState = 1;
                Debug.Log("[SwingNative] Direct transform mode enabled (transform verified).");
                return true;
            }
            _directState = -1;
            Debug.LogError("[SwingNative] Transform verification failed (" + status + "); managed solver stays.");
            return false;
        }
        return false;
    }

    /// <summary>
    /// Direct-mode frame: fill native pointers + parameters (no transform
    /// icalls), then one mu3_swing_solve_direct call walks the joints in
    /// original list order, reading/writing transforms through the engine's
    /// own member functions - the exact sequential semantics of the managed
    /// update() loop.
    /// </summary>
    private unsafe bool solveDirect()
    {
        try
        {
            if (_refs == null || _refs.Length != _joints.Count)
                _refs = new NativeSwingSolver.JointRef[_joints.Count];
            if (_scratch == null || _scratch.Length < _joints.Count)
                _scratch = new patch_SwingJointNative[_joints.Count];
            if (_plane == null)
                _plane = new float[4];

            // Plane replica of SimpleKDTree.update(_centerNode).
            var forward = _centerNode.forward;
            var center = _centerNode.position;
            _plane[0] = forward.x;
            _plane[1] = forward.y;
            _plane[2] = forward.z;
            _plane[3] = 0f - (center.x * forward.x + center.y * forward.y + center.z * forward.z);

            var count = 0;
            for (var i = 0; i < _joints.Count; i++)
            {
                var joint = (patch_SwingJointNative)(object)_joints[i];
                if (!joint.gameObject.activeInHierarchy)
                    continue;
                if (joint.fillRef(ref _refs[count]))
                    _scratch[count++] = joint;
            }
            if (count == 0)
                return true;

            int status;
            fixed (NativeSwingSolver.JointRef* refs = _refs)
            fixed (float* plane = _plane)
            fixed (SwingJointColliderInformation* colliders = _colliderInformation)
            {
                status = NativeSwingSolver.mu3_swing_solve_direct(
                    refs, count, colliders, _colliderInformation.Length, plane);
            }
            if (status != 0)
                throw new InvalidOperationException("direct solve status " + status);

            for (var k = 0; k < count; k++)
                _scratch[k].storeChild(ref _refs[k]);
            return true;
        }
        catch (Exception exception)
        {
            // Argument/status failures happen before any native write; the
            // managed fallback stays usable this frame.
            _directState = -1;
            Debug.LogError("[SwingNative] Direct solver failed; managed solver stays. " + exception);
            return false;
        }
    }
}
