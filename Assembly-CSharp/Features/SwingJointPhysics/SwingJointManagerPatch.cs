using System;
using System.Collections.Generic;
using MonoMod;
using UnityEngine;

namespace MU3.Mod.SwingJointPhysics;

[MonoModIfFlag(nameof(PatchConfig.SwingJointPhysics))]
[MonoModPatch("global::MU3.SwingJointManager")]
public class SwingJointManagerPatch : SwingJointManager
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
    private SwingJointPatch[] _scratch;
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
        var fps = MonoMod.SwingJointPhysicsConfig.SwingJointFPS;
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

        if (MonoMod.SwingJointPhysicsConfig.SwingJointNative && NativeSwingSolver.Available
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
            Debug.LogWarning("[Steroid][SwingNative] direct transform mode unavailable (init " + init + "); using managed solver.");
            return false;
        }
        for (var i = 0; i < _joints.Count; i++)
        {
            var status = ((SwingJointPatch)(object)_joints[i]).verifyTransform();
            if (status == -40 || status == -41)
                continue;
            if (status == 0)
            {
                _directState = 1;
                return true;
            }
            _directState = -1;
            Debug.LogWarning("[Steroid][SwingNative] transform verification failed (" + status + "); using managed solver.");
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
                _scratch = new SwingJointPatch[_joints.Count];
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
                var joint = (SwingJointPatch)(object)_joints[i];
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
            Debug.LogWarning("[Steroid][SwingNative] direct solver failed; using managed solver. " + exception);
            return false;
        }
    }
}
