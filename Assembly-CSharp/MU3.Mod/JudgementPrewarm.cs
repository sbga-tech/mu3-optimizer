using System;
using System.Reflection;
using MU3.Data;
using MU3.Battle;
using MU3.Game;
using MU3.Notes;
using UnityEngine;

namespace MU3.Mod;

internal static class JudgementPrewarmCoordinator
{
    private enum PrewarmState
    {
        Idle,
        Running,
        Complete,
    }

    private static PrewarmState _state;
    private static patch_GameBGM _deferredBgm;
    private static MusicData _deferredMusic;
    private static int _deferredSelector;

    internal static bool IsRunning => _state == PrewarmState.Running;

    internal static void Begin()
    {
        _state = PrewarmState.Running;
        _deferredBgm = null;
        _deferredMusic = null;
        _deferredSelector = -1;
    }

    internal static bool TryDeferMusic(patch_GameBGM bgm, MusicData music, int selector)
    {
        if (_state != PrewarmState.Running)
            return false;

        _deferredBgm = bgm;
        _deferredMusic = music;
        _deferredSelector = selector;
        return true;
    }

    internal static void Complete()
    {
        _state = PrewarmState.Complete;

        var bgm = _deferredBgm;
        var music = _deferredMusic;
        var selector = _deferredSelector;
        _deferredBgm = null;
        _deferredMusic = null;
        _deferredSelector = -1;

        if (bgm != null && music != null)
            bgm.playMusicAfterJudgementPrewarm(music, selector);
    }
}

internal sealed class JudgementPrewarmGameplaySnapshot
{
    private readonly Counters _counters;
    private readonly NotesManager _notesManager;
    private readonly int _life;
    private readonly int[] _scores;
    private readonly int[] _combos;
    private readonly int[] _maxCombos;
    private readonly bool _notesPlaying;
    private readonly float _noteFrame;

    private JudgementPrewarmGameplaySnapshot(GameEngine engine)
    {
        _counters = engine.counters;
        _notesManager = engine.notesManager;
        _life = _counters.getLifeXM;
        _scores = new int[(int)ScoreType.Max];
        for (var i = 0; i < _scores.Length; i++)
            _scores[i] = _counters.getScore((ScoreType)i);

        _combos = new int[(int)ComboType.Max];
        _maxCombos = new int[_combos.Length];
        for (var i = 0; i < _combos.Length; i++)
        {
            _combos[i] = _counters.getCombo((ComboType)i);
            _maxCombos[i] = _counters.getMaxCombo((ComboType)i);
        }

        _notesPlaying = _notesManager.isPlaying;
        _noteFrame = _notesManager.getCurrentFrame();
    }

    internal static JudgementPrewarmGameplaySnapshot Capture(GameEngine engine)
    {
        if (engine == null || engine.counters == null || engine.notesManager == null)
            return null;
        return new JudgementPrewarmGameplaySnapshot(engine);
    }

    internal bool Matches()
    {
        if (_counters == null || _notesManager == null)
            return false;
        if (_counters.getLifeXM != _life)
            return false;
        for (var i = 0; i < _scores.Length; i++)
        {
            if (_counters.getScore((ScoreType)i) != _scores[i])
                return false;
        }
        for (var i = 0; i < _combos.Length; i++)
        {
            if (_counters.getCombo((ComboType)i) != _combos[i])
                return false;
            if (_counters.getMaxCombo((ComboType)i) != _maxCombos[i])
                return false;
        }
        return _notesManager.isPlaying == _notesPlaying
            && _notesManager.getCurrentFrame() == _noteFrame;
    }
}

internal sealed class JudgementPrewarmPoolCursorSnapshot
{
    private static readonly BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly object _pool;
    private readonly FieldInfo _nextIndexField;
    private readonly int _nextIndex;

    private JudgementPrewarmPoolCursorSnapshot(object pool, FieldInfo nextIndexField, int nextIndex)
    {
        _pool = pool;
        _nextIndexField = nextIndexField;
        _nextIndex = nextIndex;
    }

    internal int Count { get; private set; }

    internal static bool TryCapture(UIJudge judge, out JudgementPrewarmPoolCursorSnapshot snapshot)
    {
        snapshot = null;
        if (judge == null)
            return false;

        var poolField = typeof(UIJudge).GetField("_judgeAnmList", InstanceFields);
        var pool = poolField == null ? null : poolField.GetValue(judge);
        if (pool == null)
            return false;

        var countProperty = pool.GetType().GetProperty("Count", InstanceFields);
        var nextIndexField = pool.GetType().GetField("nextIndex", InstanceFields);
        if (countProperty == null || nextIndexField == null)
            return false;

        snapshot = new JudgementPrewarmPoolCursorSnapshot(
            pool,
            nextIndexField,
            (int)nextIndexField.GetValue(pool));
        snapshot.Count = (int)countProperty.GetValue(pool, null);
        return snapshot.Count > 0;
    }

    internal bool RestoreAndValidate()
    {
        _nextIndexField.SetValue(_pool, _nextIndex);
        return (int)_nextIndexField.GetValue(_pool) == _nextIndex;
    }
}

internal sealed class JudgementPrewarmEffectSnapshot
{
    private static readonly BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo EffectCachesField =
        typeof(EffectManager).GetField("_effCacheList", InstanceFields);
    private static readonly FieldInfo CacheObjectsField =
        typeof(ObjectCache).GetField("objects_", InstanceFields);
    private static readonly FieldInfo CacheSizeField =
        typeof(ObjectCache).GetField("size_", InstanceFields);
    private static readonly FieldInfo AttachParentField =
        typeof(Attach).GetField("_parent", InstanceFields);
    private static readonly FieldInfo AutoPushCacheField =
        typeof(AutoPushToCacheParticle).GetField("objectCache_", InstanceFields);
    private static readonly FieldInfo AutoPushTimeField =
        typeof(AutoPushToCacheParticle).GetField("time_", InstanceFields);

    private static readonly string[] CounterFieldNames =
    {
        "numSimultaneous",
        "numSimultaneousMax",
        "frameCooltime",
        "numCreated",
        "numUsingMax",
    };

    private readonly ObjectCache _cache;
    private readonly object _cacheObject;
    private readonly FieldInfo[] _counterFields;
    private readonly int[] _counterValues;
    private readonly GameObject[] _objectOrder;
    private readonly int _size;
    private readonly GameObject _expectedObject;
    private readonly Vector3 _localPosition;
    private readonly Quaternion _localRotation;
    private readonly Vector3 _localScale;
    private readonly Transform _attachParent;
    private readonly Transform _parent;
    private readonly AutoPushToCacheParticle _autoPush;
    private readonly object _autoPushCache;
    private readonly float _autoPushTime;
    private readonly ParticleSystem[] _particles;
    private readonly uint[] _particleSeeds;
    private readonly bool[] _particleAutoSeeds;
    private readonly float[] _particleTimes;

    private JudgementPrewarmEffectSnapshot(
        ObjectCache cache,
        object cacheObject,
        FieldInfo[] counterFields,
        int[] counterValues,
        GameObject[] objectOrder,
        int size)
    {
        _cache = cache;
        _cacheObject = cacheObject;
        _counterFields = counterFields;
        _counterValues = counterValues;
        _objectOrder = objectOrder;
        _size = size;
        _expectedObject = size > 0 ? objectOrder[size - 1] : null;
        if (_expectedObject != null)
        {
            var transform = _expectedObject.transform;
            _parent = transform.parent;
            _localPosition = transform.localPosition;
            _localRotation = transform.localRotation;
            _localScale = transform.localScale;
            var attach = _expectedObject.GetComponent<Attach>();
            _attachParent = attach == null || AttachParentField == null
                ? null
                : (Transform)AttachParentField.GetValue(attach);
            _autoPush = _expectedObject.GetComponent<AutoPushToCacheParticle>();
            if (_autoPush != null && AutoPushCacheField != null && AutoPushTimeField != null)
            {
                _autoPushCache = AutoPushCacheField.GetValue(_autoPush);
                _autoPushTime = (float)AutoPushTimeField.GetValue(_autoPush);
            }
            _particles = _expectedObject.GetComponentsInChildren<ParticleSystem>(true);
            _particleSeeds = new uint[_particles.Length];
            _particleAutoSeeds = new bool[_particles.Length];
            _particleTimes = new float[_particles.Length];
            for (var i = 0; i < _particles.Length; i++)
            {
                _particleSeeds[i] = _particles[i].randomSeed;
                _particleAutoSeeds[i] = _particles[i].useAutoRandomSeed;
                _particleTimes[i] = _particles[i].time;
            }
        }
    }

    internal GameObject ActivatedObject
    {
        get
        {
            if (_expectedObject != null && _expectedObject.activeSelf)
                return _expectedObject;
            return null;
        }
    }

    internal static bool TryCapture(
        EffectManager manager,
        AssetAssign.NoteEffect.Type type,
        out JudgementPrewarmEffectSnapshot snapshot)
    {
        snapshot = null;
        if (manager == null || EffectCachesField == null
            || CacheObjectsField == null || CacheSizeField == null)
            return false;

        var caches = EffectCachesField.GetValue(manager) as Array;
        var index = (int)type;
        if (caches == null || index < 0 || index >= caches.Length)
            return false;

        var cacheObject = caches.GetValue(index);
        var cache = cacheObject as ObjectCache;
        if (cacheObject == null || cache == null)
            return false;

        var objects = CacheObjectsField.GetValue(cache) as GameObject[];
        var size = (int)CacheSizeField.GetValue(cache);
        if (objects == null || size <= 0 || size > objects.Length)
            return false;

        var counterFields = new FieldInfo[CounterFieldNames.Length];
        var counterValues = new int[counterFields.Length];
        var cacheType = cacheObject.GetType();
        for (var i = 0; i < counterFields.Length; i++)
        {
            var field = cacheType.GetField(CounterFieldNames[i], InstanceFields);
            if (field == null)
                return false;
            counterFields[i] = field;
            counterValues[i] = (int)field.GetValue(cacheObject);
        }

        snapshot = new JudgementPrewarmEffectSnapshot(
            cache,
            cacheObject,
            counterFields,
            counterValues,
            (GameObject[])objects.Clone(),
            size);
        return snapshot._expectedObject != null && !snapshot._expectedObject.activeSelf
            && snapshot._autoPush != null
            && AutoPushCacheField != null && AutoPushTimeField != null;
    }

    internal bool ReturnRestoreAndValidate(GameObject effect)
    {
        var target = effect != null ? effect : ActivatedObject;
        if (target == null)
            return false;

        if (target.activeSelf)
        {
            for (var i = 0; i < _particles.Length; i++)
                _particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _cache.push(target);
        }

        var objects = CacheObjectsField.GetValue(_cache) as GameObject[];
        if (objects == null || objects.Length != _objectOrder.Length)
        {
            objects = (GameObject[])_objectOrder.Clone();
            CacheObjectsField.SetValue(_cache, objects);
        }
        else
        {
            Array.Copy(_objectOrder, objects, _objectOrder.Length);
        }
        CacheSizeField.SetValue(_cache, _size);

        for (var i = 0; i < _counterFields.Length; i++)
            _counterFields[i].SetValue(_cacheObject, _counterValues[i]);

        var transform = target.transform;
        transform.SetParent(_parent, false);
        transform.localPosition = _localPosition;
        transform.localRotation = _localRotation;
        transform.localScale = _localScale;
        var attach = target.GetComponent<Attach>();
        if (attach != null && AttachParentField != null)
            AttachParentField.SetValue(attach, _attachParent);
        AutoPushCacheField.SetValue(_autoPush, _autoPushCache);
        AutoPushTimeField.SetValue(_autoPush, _autoPushTime);
        for (var i = 0; i < _particles.Length; i++)
        {
            _particles[i].randomSeed = _particleSeeds[i];
            _particles[i].useAutoRandomSeed = _particleAutoSeeds[i];
            _particles[i].time = _particleTimes[i];
        }

        objects = CacheObjectsField.GetValue(_cache) as GameObject[];
        if (objects == null || objects.Length != _objectOrder.Length
            || (int)CacheSizeField.GetValue(_cache) != _size
            || target.activeSelf)
            return false;
        for (var i = 0; i < _objectOrder.Length; i++)
        {
            if (!ReferenceEquals(objects[i], _objectOrder[i]))
                return false;
        }
        for (var i = 0; i < _counterFields.Length; i++)
        {
            if ((int)_counterFields[i].GetValue(_cacheObject) != _counterValues[i])
                return false;
        }
        if (!ReferenceEquals(AutoPushCacheField.GetValue(_autoPush), _autoPushCache)
            || (float)AutoPushTimeField.GetValue(_autoPush) != _autoPushTime)
            return false;
        for (var i = 0; i < _particles.Length; i++)
        {
            if (_particles[i].randomSeed != _particleSeeds[i]
                || _particles[i].useAutoRandomSeed != _particleAutoSeeds[i]
                || _particles[i].time != _particleTimes[i])
                return false;
        }
        return true;
    }
}
