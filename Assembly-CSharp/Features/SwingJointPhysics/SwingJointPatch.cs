using MonoMod;
using UnityEngine;

namespace MU3.Mod.SwingJointPhysics;

[MonoModIfFlag(nameof(PatchConfig.SwingJointPhysics))]
[MonoModPatch("global::MU3.SwingJoint")]
public partial class SwingJointPatch : SwingJoint
{
    [MonoModIgnore] private Transform _childNode;
    [MonoModIgnore] private Vector3 _nodeAxis;
    [MonoModIgnore] private float _nodeLength;
    [MonoModIgnore] private Quaternion _initialLocalRotation;
    [MonoModIgnore] private Vector3 _nowChildPos;
    [MonoModIgnore] private Vector3 _oldChildPos;
    [MonoModIgnore] private Transform _transform;
    [MonoModIgnore] private SwingJointManager _manager;

    // Same Verlet + collision as orig, but:
    // - physics params already copied onto the public fields in Start()
    // - no UnityEngine.Object null checks / manager getters per joint
    // - one lossyScale sample, CustomMath.normalize inlined
    [MonoModReplace]
    public new void update()
    {
        var t = _transform;
        var prevRot = t.rotation;
        t.localRotation = _initialLocalRotation;
        var position = t.position;
        var restRot = t.rotation;
        var childScale = _childNode.lossyScale.x;

        var pull = restRot * _nodeAxis * _childPullForce + _staticForce;
        pull *= childScale;
        pull += (_oldChildPos - _nowChildPos) * _inertialForce;

        var childPos = 2f * _nowChildPos - _oldChildPos + pull;
        childPos = NormalizeChild(childPos, position, childScale);

        var radius = _collisionRadius * t.lossyScale.x;
        var cands = _manager.getCollideCandidates(out var start, out var end, childPos, radius);
        for (var i = start; i < end; i++)
        {
            if (cands[i].getCollisionPointWithSphere(ref childPos, ref childPos, radius))
                childPos = NormalizeChild(childPos, position, childScale);
        }

        var from = t.TransformDirection(_nodeAxis);
        var q = Quaternion.FromToRotation(from, childPos - position);
        t.rotation = Quaternion.Lerp(prevRot, q * restRot, _physicsRatio);
        _oldChildPos = _nowChildPos;
        _nowChildPos = childPos;
    }

    private Vector3 NormalizeChild(Vector3 childPos, Vector3 position, float childScale)
    {
        var v = childPos - position;
        var mag = Mathf.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
        if (mag <= 1e-06f)
            return position;
        v *= (1f / mag) * _nodeLength * childScale;
        return position + v;
    }
}
