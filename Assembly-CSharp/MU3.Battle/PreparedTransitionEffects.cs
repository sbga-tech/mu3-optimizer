using FX;
using UnityEngine;

namespace MU3.Battle;

// FX_Destroy starts its lifetime in Start. Keeping these one-shot instances under
// an inactive parent preserves that lifetime and avoids resetting animated FX.
internal sealed class PreparedTransitionEffects : MonoBehaviour
{
    private GameObject _storage;
    private GameObject _wavePrefab;
    private GameObject _overkillPrefab;
    private PreparedTransitionEffect[] _waves;
    private PreparedTransitionEffect[] _overkills;
    private int _nextWave;
    private int _nextOverkill;

    internal void Prepare(AssetAssign.StageEffect prefabs, int waveCount)
    {
        Clear();
        _wavePrefab = prefabs.waveShift;
        _overkillPrefab = prefabs.overkill;
        _storage = new GameObject("Prepared transition effects");
        _storage.SetActive(false);
        _storage.transform.SetParent(transform, false);
        _waves = Create(_wavePrefab, Mathf.Max(0, waveCount - 1));
        // Boss entry (when not the first wave), then the first boss defeat.
        _overkills = Create(_overkillPrefab, waveCount > 1 ? 2 : waveCount);
    }

    internal GameObject TakeWave(GameObject prefab)
    {
        return prefab == _wavePrefab ? Take(_waves, ref _nextWave) : null;
    }

    internal GameObject TakeOverkill(GameObject prefab)
    {
        return prefab == _overkillPrefab ? Take(_overkills, ref _nextOverkill) : null;
    }

    private PreparedTransitionEffect[] Create(GameObject prefab, int count)
    {
        if (prefab == null || !prefab.activeSelf || count <= 0)
            return null;

        var instances = new PreparedTransitionEffect[count];
        for (var i = 0; i < instances.Length; i++)
        {
            var instance = Object.Instantiate(prefab, _storage.transform, false);
            var effect = instance.AddComponent<PreparedTransitionEffect>();
            effect.PrepareMaterials();
            instances[i] = effect;
        }
        return instances;
    }

    private static GameObject Take(PreparedTransitionEffect[] instances, ref int next)
    {
        if (instances == null || next >= instances.Length)
            return null;

        var effect = instances[next];
        instances[next++] = null;
        if (effect == null)
            return null;

        return effect.gameObject;
    }

    internal static void Activate(GameObject instance)
    {
        // Restore the prefab's root-local transform in world space. Its original
        // activeSelf value now starts Awake/OnEnable/Start at the event timestamp.
        instance.transform.SetParent(null, false);
    }

    private static void Release(PreparedTransitionEffect[] instances)
    {
        if (instances == null)
            return;
        foreach (var effect in instances)
        {
            // Inactive instances need explicit cleanup: Unity need not call
            // OnDestroy on a component that was never active in the scene.
            if (!ReferenceEquals(effect, null))
                effect.ReleaseMaterials();
        }
    }

    private void Clear()
    {
        Release(_waves);
        Release(_overkills);
        _waves = null;
        _overkills = null;
        _nextWave = 0;
        _nextOverkill = 0;
        if (_storage != null)
            Object.Destroy(_storage);
        _storage = null;
    }

    private void OnDestroy()
    {
        Clear();
    }
}

internal sealed class PreparedTransitionEffect : MonoBehaviour
{
    private Material[] _materials;

    internal void PrepareMaterials()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        var originals = new Material[renderers.Length];
        for (var i = 0; i < renderers.Length; i++)
            originals[i] = renderers[i].sharedMaterial;

        // Only warm consumers which instantiate Renderer.material. Cloning every
        // particle material would unnecessarily break their shared-material path.
        var wave = GetComponent<Evt_WaveShift>();
        if (wave != null)
            wave.setParam(Color.white, Color.white);
        foreach (var parameter in GetComponentsInChildren<FX_CopyMat_SetParam>(true))
        {
            var renderer = parameter.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
                _ = renderer.material;
        }

        _materials = new Material[renderers.Length];
        for (var i = 0; i < renderers.Length; i++)
        {
            var material = renderers[i].sharedMaterial;
            if (material != originals[i])
                _materials[i] = material;
        }
    }

    internal void ReleaseMaterials()
    {
        if (_materials == null)
            return;
        foreach (var material in _materials)
        {
            if (material != null)
                Object.Destroy(material);
        }
        _materials = null;
    }

    private void OnDestroy()
    {
        ReleaseMaterials();
    }
}
