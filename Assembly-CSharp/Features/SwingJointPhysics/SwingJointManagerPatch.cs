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

    // SwingJointFPS gate state, shared by every manager so the characters
    // stay coherent; the decision is made once per rendered frame. All
    // fields deliberately work from CLR defaults (patch-class field
    // initializers never run).
    private static int _fpsFrame;
    private static bool _fpsRun;
    private static FrameRateLimiter _fpsLimiter;

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
            _fpsRun = _fpsLimiter.Tick(Time.unscaledTime, fps);
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

        _simpleKDTree.update(_centerNode);
        for (var j = 0; j < _joints.Count; j++)
        {
            if (_joints[j].gameObject.activeInHierarchy)
                _joints[j].update();
        }
    }
}
