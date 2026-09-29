using System.Collections.Generic;
using FX;
using MU3.Battle;
using UnityEngine;

namespace MU3.Mod.GameplayPrewarm;

// One-shot wave/overkill effects, initialized then held silent until Take.
// Disabled FX scripts delay FX_Destroy.Start so the original lifetime starts
// at the chart event, not at preparation.
internal sealed class GameplayPrewarmEffects : MonoBehaviour
{
    private static readonly Vector3 ParkPosition = new(0f, -25000f, 0f);

    private GameObject _parked;
    private Queue<GameplayPrewarmEffect> _waves;
    private Queue<GameplayPrewarmEffect> _overkills;

    internal void Prepare(AssetAssign.StageEffect prefabs, int waveCount)
    {
        Clear();
        try
        {
            _waves = new Queue<GameplayPrewarmEffect>();
            _overkills = new Queue<GameplayPrewarmEffect>();
            _parked = new GameObject(nameof(GameplayPrewarmEffects));
            _parked.SetActive(false);
            _parked.transform.SetParent(transform, false);
            Create(prefabs.waveShift, waveCount - 1, _waves);
            Create(prefabs.overkill, waveCount > 1 ? 2 : waveCount, _overkills);
            if (_waves.Count + _overkills.Count == 0)
                return;

            _parked.transform.localPosition = ParkPosition;
            _parked.SetActive(true);
            foreach (var effect in _waves)
                effect.Park();
            foreach (var effect in _overkills)
                effect.Park();
        }
        catch
        {
            Clear();
            throw;
        }
    }

    internal bool TryTake(bool overkill, out GameObject effect)
    {
        var queue = overkill ? _overkills : _waves;
        if (queue == null || queue.Count == 0)
        {
            effect = null;
            return false;
        }

        effect = queue.Dequeue().Activate();
        return true;
    }

    internal void Clear()
    {
        Release(_waves);
        Release(_overkills);
        _waves = null;
        _overkills = null;
        if (_parked != null)
        {
            Object.Destroy(_parked);
            _parked = null;
        }
    }

    private void Create(GameObject prefab, int count, Queue<GameplayPrewarmEffect> into)
    {
        if (prefab == null)
            return;
        for (var i = 0; i < count; i++)
        {
            var instance = Object.Instantiate(prefab, _parked.transform, false);
            var effect = instance.AddComponent<GameplayPrewarmEffect>();
            into.Enqueue(effect);
            effect.Freeze();
        }
    }

    private static void Release(Queue<GameplayPrewarmEffect> effects)
    {
        if (effects == null)
            return;
        foreach (var effect in effects)
        {
            if (effect != null)
                effect.ReleaseMaterials();
        }
    }

    private void OnDestroy()
    {
        Clear();
    }
}

internal sealed class GameplayPrewarmEffect : MonoBehaviour
{
    private struct FrozenAnimator
    {
        internal Animator Animator;
        internal float Speed;
        internal AnimatorCullingMode Culling;
        internal bool ApplyRootMotion;
        internal bool FireEvents;

        internal void Restore()
        {
            Animator.applyRootMotion = ApplyRootMotion;
            Animator.cullingMode = Culling;
            Animator.speed = Speed;
            Animator.fireEvents = FireEvents;
        }
    }

    private struct LocalTransform
    {
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal Vector3 Scale;
    }

    private Material[] _materials;
    private MonoBehaviour[] _behaviours;
    private Renderer[] _renderers;
    private ParticleSystem[] _particles;
    private FrozenAnimator[] _animators;
    private LocalTransform _root;

    internal void Freeze()
    {
        var t = transform;
        _root = new LocalTransform
        {
            Position = t.localPosition,
            Rotation = t.localRotation,
            Scale = t.localScale,
        };

        var renderers = GetComponentsInChildren<Renderer>(true);
        CaptureClonedMaterials(renderers);

        var enabledRenderers = 0;
        for (var i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i].enabled)
                continue;
            renderers[enabledRenderers++] = renderers[i];
            renderers[i].enabled = false;
        }
        _renderers = Compact(renderers, enabledRenderers);

        var found = GetComponentsInChildren<MonoBehaviour>(true);
        var enabledBehaviours = 0;
        for (var i = 0; i < found.Length; i++)
        {
            var behaviour = found[i];
            if (behaviour == this || !behaviour.enabled)
                continue;
            behaviour.enabled = false;
            found[enabledBehaviours++] = behaviour;
        }
        _behaviours = Compact(found, enabledBehaviours);

        var particles = GetComponentsInChildren<ParticleSystem>(true);
        var silenced = 0;
        for (var i = 0; i < particles.Length; i++)
        {
            var main = particles[i].main;
            if (!main.playOnAwake)
                continue;
            main.playOnAwake = false;
            particles[silenced++] = particles[i];
        }
        _particles = Compact(particles, silenced);

        var animators = GetComponentsInChildren<Animator>(true);
        _animators = new FrozenAnimator[animators.Length];
        for (var i = 0; i < animators.Length; i++)
        {
            var animator = animators[i];
            _animators[i] = new FrozenAnimator
            {
                Animator = animator,
                Speed = animator.speed,
                Culling = animator.cullingMode,
                ApplyRootMotion = animator.applyRootMotion,
                FireEvents = animator.fireEvents,
            };
            animator.applyRootMotion = false;
            animator.speed = 0f;
            animator.fireEvents = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    internal void Park()
    {
        foreach (var frozen in _animators)
            frozen.Animator.cullingMode = AnimatorCullingMode.CullCompletely;
    }

    internal GameObject Activate()
    {
        transform.localPosition = _root.Position;
        transform.localRotation = _root.Rotation;
        transform.localScale = _root.Scale;
        transform.SetParent(null, false);

        foreach (var frozen in _animators)
            frozen.Restore();
        foreach (var particle in _particles)
        {
            var main = particle.main;
            main.playOnAwake = true;
            if (particle.gameObject.activeInHierarchy)
                particle.Play(false);
        }
        foreach (var renderer in _renderers)
            renderer.enabled = true;
        foreach (var behaviour in _behaviours)
            behaviour.enabled = true;
        return gameObject;
    }

    internal void ReleaseMaterials()
    {
        var materials = _materials;
        if (materials == null)
            return;
        _materials = null;
        foreach (var material in materials)
            Object.Destroy(material);
    }

    private void OnDestroy()
    {
        ReleaseMaterials();
    }

    private void CaptureClonedMaterials(Renderer[] renderers)
    {
        var originals = new Material[renderers.Length];
        for (var i = 0; i < renderers.Length; i++)
            originals[i] = renderers[i].sharedMaterial;
        _materials = new Material[renderers.Length];

        try
        {
            // Only these consumers instantiate Renderer.material; particles retain shared materials.
            var wave = GetComponent<Evt_WaveShift>();
            if (wave != null)
                wave.setParam(Color.white, Color.white);
            foreach (var parameter in GetComponentsInChildren<FX_CopyMat_SetParam>(true))
            {
                var renderer = parameter.GetComponent<Renderer>();
                if (renderer != null)
                    _ = renderer.material;
            }
        }
        finally
        {
            // Register clones even if a later material consumer throws during preparation.
            var cloned = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                var material = renderers[i].sharedMaterial;
                if (material != originals[i])
                    _materials[cloned++] = material;
            }
            _materials = Compact(_materials, cloned);
        }
    }

    private static T[] Compact<T>(T[] items, int count)
    {
        if (count == items.Length)
            return items;
        var compact = new T[count];
        for (var i = 0; i < count; i++)
            compact[i] = items[i];
        return compact;
    }
}
