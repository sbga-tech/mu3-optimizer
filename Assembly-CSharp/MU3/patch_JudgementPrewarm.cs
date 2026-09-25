using System;
using System.Collections;
using MonoMod;
using MU3.Battle;
using MU3.CustomUI;
using MU3.Data;
using MU3.Game;
using MU3.Mod;
using MU3.Notes;
using MU3.Sys;
using MU3.Util;
using UnityEngine;

namespace MU3
{
    [MonoModIfFlag("JudgementPrewarm")]
    [MonoModPatch("global::MU3.BattleUI")]
    public class patch_BattleUIJudgementPrewarm : BattleUI
    {
        [MonoModIgnore] private UIJudge _judgeInfo;

        private bool _judgementPrewarmStarted;

        private extern void orig_initBattle(SessionInfo sessionInfo, bool showBattleResult);

        public new void initBattle(SessionInfo sessionInfo, bool showBattleResult)
        {
            orig_initBattle(sessionInfo, showBattleResult);
            if (_judgementPrewarmStarted)
                return;

            _judgementPrewarmStarted = true;
            JudgementPrewarmCoordinator.Begin();
            try
            {
                StartCoroutine(runJudgementPrewarm());
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError("[Steroid][JudgementPrewarm] startup failed: " + exception);
                JudgementPrewarmCoordinator.Complete();
            }
        }

        private IEnumerator runJudgementPrewarm()
        {
            var randomState = UnityEngine.Random.state;
            var session = new Session();
            Exception failure = null;

            try
            {
                session.Setup(this, _judgeInfo);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            UnityEngine.Random.state = randomState;

            if (failure == null)
            {
                yield return null;
                try
                {
                    var renderRandomState = UnityEngine.Random.state;
                    try
                    {
                        session.Render();
                    }
                    finally
                    {
                        UnityEngine.Random.state = renderRandomState;
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            if (failure == null)
            {
                yield return new WaitForEndOfFrame();
                try
                {
                    var renderRandomState = UnityEngine.Random.state;
                    try
                    {
                        session.Render();
                    }
                    finally
                    {
                        UnityEngine.Random.state = renderRandomState;
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            var restored = false;
            try
            {
                restored = session.Restore();
            }
            catch (Exception exception)
            {
                if (failure == null)
                    failure = exception;
            }

            JudgementPrewarmCoordinator.Complete();

            if (failure != null)
            {
                UnityEngine.Debug.LogError("[Steroid][JudgementPrewarm] execution failed: " + failure);
                yield break;
            }
            if (!restored)
            {
                UnityEngine.Debug.LogError("[Steroid][JudgementPrewarm] state restoration failed.");
                yield break;
            }

        }

        private sealed class Session
        {
            private JudgementPrewarmGameplaySnapshot _gameplay;
            private JudgementPrewarmPoolCursorSnapshot _poolCursor;
            private ANM_PLY_Judgment_00[] _judgements;
            private JudgementPrewarmSnapshot[] _judgementSnapshots;
            private JudgementPrewarmEffectSnapshot _normalEffectSnapshot;
            private GameObject _normalEffect;
            private GameObject _cameraObject;
            private Camera _warmCamera;
            private RenderTexture _renderTexture;
            private Transform _warmAnchor;
            private Canvas[] _canvases;
            private Camera[] _canvasCameras;
            private bool _restored;


            internal void Setup(patch_BattleUIJudgementPrewarm owner, UIJudge judgeInfo)
            {
                var engine = SingletonMonoBehaviour<GameEngine>.instance;
                if (engine == null || engine.effectManager == null
                    || engine.notesManager == null || engine.player == null)
                    throw new InvalidOperationException("gameplay objects are not initialized");
                if (judgeInfo == null)
                    throw new InvalidOperationException("judgement UI is not initialized");

                _gameplay = JudgementPrewarmGameplaySnapshot.Capture(engine);
                if (_gameplay == null)
                    throw new InvalidOperationException("gameplay snapshot unavailable");
                if (!JudgementPrewarmPoolCursorSnapshot.TryCapture(judgeInfo, out _poolCursor))
                    throw new InvalidOperationException("judgement pool cursor unavailable");

                _judgements = owner.GetComponentsInChildren<ANM_PLY_Judgment_00>(true);
                if (_judgements.Length != _poolCursor.Count)
                    throw new InvalidOperationException("judgement pool count mismatch");
                _judgementSnapshots = new JudgementPrewarmSnapshot[_judgements.Length];
                for (var i = 0; i < _judgements.Length; i++)
                {
                    if (!((patch_ANM_PLY_Judgment_00)(object)_judgements[i])
                        .captureJudgementPrewarmSnapshot(out _judgementSnapshots[i]))
                        throw new InvalidOperationException("judgement pool is already active");
                }

                var mainCamera = Camera.main;
                if (mainCamera == null)
                    throw new InvalidOperationException("main camera unavailable");
                createRenderTarget(mainCamera);
                captureJudgementCanvases();

                owner.playJudge(
                    _warmAnchor.position,
                    Vector2.zero,
                    Judge.Perfect,
                    Timing.Just,
                    1,
                    1f,
                    false);

                for (var i = 0; i < (int)ScoreType.Max; i++)
                    owner.setScore((ScoreType)i, engine.counters.getScore((ScoreType)i));

                if (!JudgementPrewarmEffectSnapshot.TryCapture(
                    engine.effectManager,
                    AssetAssign.NoteEffect.Type.normalCBreak,
                    out _normalEffectSnapshot))
                    throw new InvalidOperationException("normal effect pool unavailable");
                _normalEffect = engine.notesManager.createNoteEffect(
                    AssetAssign.NoteEffect.Type.normalCBreak,
                    _warmAnchor.position);
                if (_normalEffect == null)
                    _normalEffect = _normalEffectSnapshot.ActivatedObject;
                if (_normalEffect == null)
                    throw new InvalidOperationException("normal effect activation failed");


                Canvas.ForceUpdateCanvases();
            }

            internal void Render()
            {
                if (_warmCamera == null)
                    throw new InvalidOperationException("warm camera unavailable");

                try
                {
                    for (var i = 0; i < _canvases.Length; i++)
                    {
                        var canvas = _canvases[i];
                        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                            canvas.worldCamera = _warmCamera;
                    }
                    Canvas.ForceUpdateCanvases();
                    _warmCamera.Render();
                }
                finally
                {
                    for (var i = 0; i < _canvases.Length; i++)
                    {
                        var canvas = _canvases[i];
                        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                            canvas.worldCamera = _canvasCameras[i];
                    }
                }
            }

            internal bool Restore()
            {
                if (_restored)
                    return false;
                _restored = true;

                var valid = true;
                if (_normalEffectSnapshot != null)
                    valid &= _normalEffectSnapshot.ReturnRestoreAndValidate(_normalEffect);

                if (_judgements != null && _judgementSnapshots != null)
                {
                    for (var i = 0; i < _judgements.Length; i++)
                    {
                        valid &= ((patch_ANM_PLY_Judgment_00)(object)_judgements[i])
                            .restoreJudgementPrewarmSnapshot(_judgementSnapshots[i]);
                    }
                }
                if (_poolCursor != null)
                    valid &= _poolCursor.RestoreAndValidate();

                Canvas.ForceUpdateCanvases();
                valid &= _gameplay != null && _gameplay.Matches();
                destroyRenderTarget();
                return valid;
            }

            private void createRenderTarget(Camera mainCamera)
            {
                _cameraObject = new GameObject("JudgementPrewarmCamera");
                _cameraObject.hideFlags = HideFlags.HideAndDontSave;
                _warmCamera = _cameraObject.AddComponent<Camera>();
                _warmCamera.CopyFrom(mainCamera);
                _warmCamera.enabled = false;
                _warmCamera.clearFlags = CameraClearFlags.SolidColor;
                _warmCamera.backgroundColor = Color.black;
                _warmCamera.transform.position = mainCamera.transform.position
                    + mainCamera.transform.right * 10000f;
                _warmCamera.transform.rotation = mainCamera.transform.rotation;

                _renderTexture = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
                _renderTexture.hideFlags = HideFlags.HideAndDontSave;
                _renderTexture.Create();
                _warmCamera.targetTexture = _renderTexture;

                var anchorObject = new GameObject("JudgementPrewarmAnchor");
                anchorObject.hideFlags = HideFlags.HideAndDontSave;
                anchorObject.transform.SetParent(_cameraObject.transform, false);
                anchorObject.transform.localPosition = Vector3.forward * 10f;
                _warmAnchor = anchorObject.transform;
            }

            private void captureJudgementCanvases()
            {
                var canvases = new Canvas[_judgements.Length];
                var cameras = new Camera[_judgements.Length];
                var count = 0;
                for (var i = 0; i < _judgements.Length; i++)
                {
                    var canvas = _judgements[i].GetComponentInParent<Canvas>();
                    if (canvas == null)
                        continue;

                    var exists = false;
                    for (var j = 0; j < count; j++)
                    {
                        if (!ReferenceEquals(canvases[j], canvas))
                            continue;
                        exists = true;
                        break;
                    }
                    if (exists)
                        continue;

                    canvases[count] = canvas;
                    cameras[count] = canvas.worldCamera;
                    count++;
                }

                _canvases = new Canvas[count];
                _canvasCameras = new Camera[count];
                Array.Copy(canvases, _canvases, count);
                Array.Copy(cameras, _canvasCameras, count);
            }

            private void destroyRenderTarget()
            {
                if (_warmCamera != null)
                    _warmCamera.targetTexture = null;
                if (_renderTexture != null)
                {
                    _renderTexture.Release();
                    Destroy(_renderTexture);
                }
                if (_cameraObject != null)
                    Destroy(_cameraObject);
                _renderTexture = null;
                _warmCamera = null;
                _cameraObject = null;
                _warmAnchor = null;
            }
        }
    }

    internal struct JudgementPrewarmSnapshot
    {
        internal bool ActiveSelf;
        internal bool IsAlive;
        internal Vector3 LocalPosition;
        internal Quaternion LocalRotation;
        internal Vector3 LocalScale;
        internal bool HasImageJudge;
        internal float ImageJudgePattern;
        internal bool HasImageTiming;
        internal float ImageTimingPattern;
        internal bool HasSpriteJudge;
        internal float SpriteJudgePattern;
        internal bool HasSpriteTiming;
        internal float SpriteTimingPattern;
        internal bool HasUICounter;
        internal double UICounter;
        internal bool HasMeshCounter;
        internal int MeshCounter;
        internal bool HasAnimator;
        internal bool AnimatorEnabled;
        internal float AnimatorSpeed;
        internal AnimatorUpdateMode AnimatorUpdateMode;
        internal AnimatorCullingMode AnimatorCullingMode;
        internal int AnimatorStateParameter;
        internal int AnimatorStateHash;
        internal float AnimatorNormalizedTime;
    }

    [MonoModIfFlag("JudgementPrewarm")]
    [MonoModPatch("global::ANM_PLY_Judgment_00")]
    public class patch_ANM_PLY_Judgment_00 : ANM_PLY_Judgment_00
    {
        [MonoModIgnore] private MU3UIImageChanger _imageChangeJudge;
        [MonoModIgnore] private MU3UIImageChanger _imageChangeTiming;
        [MonoModIgnore] private MU3UICounter _count;
        [MonoModIgnore] private MU3SpriteChanger _spriteChangeJudge;
        [MonoModIgnore] private MU3SpriteChanger _spriteChangeTiming;
        [MonoModIgnore] private MU3CounterInt _countMesh;
        [MonoModIgnore] private bool _isAlive;
        [MonoModIgnore] private Animator _animator;
        [MonoModIgnore] private AnimationChecker _animationChecker;

        internal bool captureJudgementPrewarmSnapshot(out JudgementPrewarmSnapshot snapshot)
        {
            snapshot = new JudgementPrewarmSnapshot
            {
                ActiveSelf = gameObject.activeSelf,
                IsAlive = _isAlive,
                LocalPosition = transform.localPosition,
                LocalRotation = transform.localRotation,
                LocalScale = transform.localScale,
                HasImageJudge = _imageChangeJudge != null,
                HasImageTiming = _imageChangeTiming != null,
                HasSpriteJudge = _spriteChangeJudge != null,
                HasSpriteTiming = _spriteChangeTiming != null,
                HasUICounter = _count != null,
                HasMeshCounter = _countMesh != null,
                HasAnimator = _animator != null,
            };
            if (snapshot.HasImageJudge)
                snapshot.ImageJudgePattern = _imageChangeJudge.patternNumber;
            if (snapshot.HasImageTiming)
                snapshot.ImageTimingPattern = _imageChangeTiming.patternNumber;
            if (snapshot.HasSpriteJudge)
                snapshot.SpriteJudgePattern = _spriteChangeJudge.patternNumber;
            if (snapshot.HasSpriteTiming)
                snapshot.SpriteTimingPattern = _spriteChangeTiming.patternNumber;
            if (snapshot.HasUICounter)
                snapshot.UICounter = _count.Counter;
            if (snapshot.HasMeshCounter)
                snapshot.MeshCounter = _countMesh.Counter;
            if (snapshot.HasAnimator)
            {
                snapshot.AnimatorEnabled = _animator.enabled;
                snapshot.AnimatorSpeed = _animator.speed;
                snapshot.AnimatorUpdateMode = _animator.updateMode;
                snapshot.AnimatorCullingMode = _animator.cullingMode;
                snapshot.AnimatorStateParameter = _animator.GetInteger(MU3.Sys.Const.AnimatorID_State);
                var state = _animator.GetCurrentAnimatorStateInfo(0);
                snapshot.AnimatorStateHash = state.fullPathHash;
                snapshot.AnimatorNormalizedTime = state.normalizedTime;
            }
            return !snapshot.ActiveSelf && !snapshot.IsAlive;
        }

        internal bool restoreJudgementPrewarmSnapshot(JudgementPrewarmSnapshot snapshot)
        {
            transform.localPosition = snapshot.LocalPosition;
            transform.localRotation = snapshot.LocalRotation;
            transform.localScale = snapshot.LocalScale;
            if (snapshot.HasImageJudge)
                _imageChangeJudge.patternNumber = snapshot.ImageJudgePattern;
            if (snapshot.HasImageTiming)
                _imageChangeTiming.patternNumber = snapshot.ImageTimingPattern;
            if (snapshot.HasSpriteJudge)
                _spriteChangeJudge.patternNumber = snapshot.SpriteJudgePattern;
            if (snapshot.HasSpriteTiming)
                _spriteChangeTiming.patternNumber = snapshot.SpriteTimingPattern;
            if (snapshot.HasUICounter)
                _count.Counter = snapshot.UICounter;
            if (snapshot.HasMeshCounter)
                _countMesh.Counter = snapshot.MeshCounter;

            if (snapshot.HasAnimator)
            {
                _animator.enabled = true;
                _animator.ResetTrigger(MU3.Sys.Const.AnimatorID_Start);
                _animator.ResetTrigger(MU3.Sys.Const.AnimatorID_StartNumOff);
                _animator.SetInteger(MU3.Sys.Const.AnimatorID_State, snapshot.AnimatorStateParameter);
                if (snapshot.AnimatorStateHash != 0)
                {
                    _animator.Play(
                        snapshot.AnimatorStateHash,
                        0,
                        snapshot.AnimatorNormalizedTime);
                    _animator.Update(0f);
                }
                _animator.speed = snapshot.AnimatorSpeed;
                _animator.updateMode = snapshot.AnimatorUpdateMode;
                _animator.cullingMode = snapshot.AnimatorCullingMode;
                _animator.enabled = snapshot.AnimatorEnabled;
            }
            if (_animationChecker != null)
                _animationChecker.reset();

            _isAlive = snapshot.IsAlive;
            gameObject.SetActive(snapshot.ActiveSelf);
            return gameObject.activeSelf == snapshot.ActiveSelf
                && _isAlive == snapshot.IsAlive
                && transform.localPosition == snapshot.LocalPosition
                && transform.localRotation == snapshot.LocalRotation
                && transform.localScale == snapshot.LocalScale;
        }
    }
}

namespace MU3.Game
{
    [MonoModIfFlag("JudgementPrewarm")]
    [MonoModPatch("global::MU3.Game.GameBGM")]
    public class patch_GameBGM : GameBGM
    {
        private extern void orig_playMusic(MusicData musicData, int selectorID);

        public new void playMusic(MusicData musicData, int selectorID)
        {
            if (JudgementPrewarmCoordinator.TryDeferMusic(this, musicData, selectorID))
            {
                return;
            }
            orig_playMusic(musicData, selectorID);
        }

        public void playMusicAfterJudgementPrewarm(MusicData musicData, int selectorID)
        {
            orig_playMusic(musicData, selectorID);
        }
    }
}
