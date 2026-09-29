using System.Collections;
using System.Collections.Generic;
using MU3.CustomUI;
using MU3.Battle;
using MU3.Notes;
using MU3.Util;
using UnityEngine;

namespace MU3.Mod.GameplayPrewarm;

// Owns only disposable presentation objects. Live judgement/effect pools are never borrowed.
internal sealed class GameplayPrewarmSetup : MonoBehaviour
{
    private GameEngine _engine;
    private GameObject _presentation;
    private RenderTexture _target;
    private readonly List<Object> _resources = new();

    internal GameplayPrewarmEffects Effects { get; private set; }
    internal bool Complete { get; private set; }

    internal void Initialize(GameEngine engine)
    {
        _engine = engine;
        Effects = gameObject.AddComponent<GameplayPrewarmEffects>();
    }

    internal GameObject AcquireTransitionEffect(GameObject prefab, bool overkill)
    {
        if (Effects != null && Effects.TryTake(overkill, out var retained))
            return retained;
        return Object.Instantiate(prefab);
    }

    private IEnumerator Start()
    {
        try
        {
            // initBattle creates counters whose digit caches are initialized in Start.
            yield return null;

            Camera camera;
            var random = Random.state;
            try
            {
                GameplayPrewarmCompilation.Prepare();
                Effects.Prepare(SingletonMonoBehaviour<AssetAssign>.instance.stageEffect,
                    _engine.notesManager.waveDetailDataList.Count);
                camera = CreatePresentation();
            }
            finally
            {
                Random.state = random;
            }

            // The disposable UI has its own Start callbacks; do not rebuild it early either.
            yield return null;
            foreach (var counter in _presentation.GetComponentsInChildren<MU3UICounter>(true))
                counter.setForceDirty();
            Render(camera);
            yield return new WaitForEndOfFrame();
            Render(camera);
            Complete = true;
        }
        finally
        {
            Release();
        }
    }

    private Camera CreatePresentation()
    {
        _presentation = new GameObject("GameplayPrewarmPresentation");
        _presentation.transform.SetParent(transform, false);
        _presentation.SetActive(false);

        var camera = _presentation.AddComponent<Camera>();
        var main = Camera.main;
        camera.CopyFrom(main);
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.transform.position = main.transform.position + main.transform.right * 10000f;
        camera.transform.rotation = main.transform.rotation;
        _target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32)
        {
            name = "GameplayPrewarmTarget",
        };
        _target.Create();
        camera.targetTexture = _target;

        var position = camera.transform.position + camera.transform.forward * 10f;
        var prefab = SingletonMonoBehaviour<AssetAssign>.instance.noteEffect.assigns[
            (int)AssetAssign.NoteEffect.Type.normalCBreak];
        var effect = Instantiate(prefab, _presentation.transform, false);
        effect.transform.position = position;
        // The copy is rendered explicitly; it must not run cache-return or lifetime scripts.
        foreach (var script in effect.GetComponentsInChildren<MonoBehaviour>(true))
            script.enabled = false;
        var particles = effect.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var particle in particles)
        {
            var settings = particle.main;
            settings.playOnAwake = false;
        }

        var canvasObject = new GameObject("GameplayPrewarmScore", typeof(RectTransform), typeof(Canvas));
        canvasObject.transform.SetParent(_presentation.transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        var score = Instantiate(_engine.battleUI.GetComponentInChildren<ANM_PLY_score>(true),
            canvas.transform, false);
        score.GetComponent<Animator>().enabled = false;

        var judgement = Instantiate(_engine.battleUI.GetComponentInChildren<ANM_PLY_Judgment_00>(true),
            _presentation.transform, false);
        _presentation.SetActive(true);
        judgement.initialize(_engine.battleUI.GetComponentInChildren<UIJudge>(true));
        judgement.play(position, Vector2.zero, Judge.Perfect, Timing.Just, 1, 1f, false);
        foreach (var animator in judgement.GetComponentsInChildren<Animator>(true))
        {
            animator.fireEvents = false;
            animator.Update(0f);
            animator.speed = 0f;
        }
        for (var i = 0; i < (int)ScoreType.Max; i++)
            score.setScore((ScoreType)i, _engine.counters.getScore((ScoreType)i));
        foreach (var counter in judgement.GetComponentsInChildren<MU3CounterBase>(true))
            _resources.Add(counter.GetComponent<MeshFilter>().sharedMesh);
        var renderQueue = 2700;
        foreach (var renderer in effect.GetComponentsInChildren<Renderer>(true))
        {
            var material = renderer.material;
            _resources.Add(material);
            material.renderQueue = renderQueue++;
        }
        foreach (var particle in particles)
            particle.Simulate(1f / 60f, false, true);
        return camera;
    }

    private static void Render(Camera camera)
    {
        var random = Random.state;
        var target = RenderTexture.active;
        try
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
        }
        finally
        {
            RenderTexture.active = target;
            Random.state = random;
        }
    }

    private void Release()
    {
        // These checks describe partial resource ownership, not optional gameplay dependencies.
        if (_presentation != null)
        {
            _presentation.SetActive(false);
            _presentation.GetComponent<Camera>().targetTexture = null;
            Destroy(_presentation);
            _presentation = null;
        }
        if (_target != null)
        {
            _target.Release();
            Destroy(_target);
            _target = null;
        }
        foreach (var resource in _resources)
            Destroy(resource);
        _resources.Clear();
        if (!Complete)
            Effects.Clear();
    }

    private void OnDisable()
    {
        // Unity can stop an owner's coroutine without disposing its iterator.
        StopAllCoroutines();
        Release();
    }
}
