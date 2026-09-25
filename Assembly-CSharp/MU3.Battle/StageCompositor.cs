using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MU3.Battle
{
    public class StageCompositor : MonoBehaviour
    {
        private Camera _camera;
        private Camera _layerCamera;
        private Camera _helper;
        private RenderTexture _stageRT;
        private Rect _stageRect;

        private RenderTexture _refPreFXStageRT;
        private RenderTexture _refPostStageRT;

        private CameraClearFlags _fallbackClearFlags;
        private Color _fallbackBackgroundColor;
        private bool _hasFallbackCameraState;
        private bool _clearOutputNextFrame;

        private const int RTWidth = 1080;
        private const int RTHeight = 1920;

        // Configurable frame rates (-1 = uncapped, 0 = freeze, >0 = rate-limited)
        private float _stageFPS = 0f;
        private float _bgMergeFPS = 0f;
        private float _fxFPS = 30f;

        private float _lastStageTime = float.NegativeInfinity;
        private float _lastBGMergeTime = float.NegativeInfinity;
        private float _lastFXTime = float.NegativeInfinity;
        private bool _forceRenderNextFrame;
        private bool _hasStageRenderedThisFrame;
        private bool _hasFXRenderedSinceActive;

        private float _fxLayerActiveUntil;
        private bool _initialized;

        public static StageCompositor Instance { get; private set; }

        public static void RequestFXLayer(float duration)
        {
            if (Instance != null)
            {
                var until = Time.time + duration;
                if (until > Instance._fxLayerActiveUntil)
                    Instance._fxLayerActiveUntil = until;
            }
        }

        private bool IsFXLayerActive => Time.time < _fxLayerActiveUntil;

        public bool ShouldStageRender(float now)
        {
            return _forceRenderNextFrame || IsDue(_stageFPS, _lastStageTime, now);
        }

        public void NotifyStageRendered(float now)
        {
            _lastStageTime = now;
            _hasStageRenderedThisFrame = true;
        }

        public void ForceRenderNextFrame()
        {
            _lastStageTime = float.NegativeInfinity;
            _lastBGMergeTime = float.NegativeInfinity;
            _lastFXTime = float.NegativeInfinity;
            _forceRenderNextFrame = true;
            _hasFXRenderedSinceActive = false;
        }

        private void Awake()
        {
            var helperObj = new GameObject("BackgroundLayerCamera");

            _helper = helperObj.AddComponent<Camera>();
            _helper.enabled = false;
            _helper.tag = "Untagged";
            _helper.clearFlags = CameraClearFlags.Depth;
            _helper.renderingPath = RenderingPath.DeferredShading;
            _helper.useOcclusionCulling = false;
            _helper.allowHDR = true;
            _helper.allowMSAA = false;

            _stageFPS = MonoMod.RenderLayersConfig.StageFPS;
            _bgMergeFPS = MonoMod.RenderLayersConfig.BGMergeFPS;
            _fxFPS = MonoMod.RenderLayersConfig.FXFPS;
            _stageRect = new Rect(0f, 0f, RTWidth, RTHeight);
        }

        public void Initialize(Camera mainCamera, Camera layerCamera, RenderTexture preFXStageRT,
            RenderTexture postStageRT, CameraClearFlags fallbackClearFlags,
            Color fallbackBackgroundColor)
        {
            if (mainCamera == null || _helper == null)
                return;

            _camera = mainCamera;
            _layerCamera = layerCamera != null ? layerCamera : mainCamera;
            _helper.transform.SetParent(mainCamera.transform, false);
            _fallbackClearFlags = fallbackClearFlags;
            _fallbackBackgroundColor = fallbackBackgroundColor;
            _hasFallbackCameraState = true;

            if (preFXStageRT == null || postStageRT == null)
            {
                UnityEngine.Debug.LogWarning("[Steroid][StageCompositor] reference render textures missing; creating replacements.");
                EnsureRT(ref preFXStageRT);
                EnsureRT(ref postStageRT);
            }
            _refPreFXStageRT = preFXStageRT;
            _refPostStageRT = postStageRT;
            _clearOutputNextFrame = true;

            _initialized = true;
            Instance = this;
        }

        public void SetStageTexture(RenderTexture stageRT, Rect stageRect)
        {
            _stageRT = stageRT;
            _stageRect = stageRect;
            if (_stageRect.width <= 0f || _stageRect.height <= 0f)
                _stageRect = new Rect(0f, 0f, RTWidth, RTHeight);
            _clearOutputNextFrame = true;
            ForceRenderNextFrame();
        }

        public void ClearStageTexture()
        {
            _stageRT = null;
            _forceRenderNextFrame = true;
            _hasStageRenderedThisFrame = false;
            _hasFXRenderedSinceActive = false;
            DetachHelperTarget();
            RestoreFallbackCameraState();
        }

        public void SetLegacyRenderTextures(RenderTexture stageRT, RenderTexture postStageRT)
        {
            if (stageRT == null || postStageRT == null)
                throw new ArgumentException("[StageCompositor] Legacy RTs cannot be null. WHAT HAPPENED");

            DetachHelperTarget();
            _refPreFXStageRT = stageRT;
            _refPostStageRT = postStageRT;
            _clearOutputNextFrame = true;
        }

        private static bool IsDue(float fps, float lastTime, float now)
        {
            if (fps == 0) return false;
            if (fps < 0) return true;
            return now - lastTime >= 1f / fps;
        }

        private void EnsureRT(ref RenderTexture rt, int w = RTWidth, int h = RTHeight)
        {
            if (rt != null && rt.width == w && rt.height == h)
                return;

            if (rt != null)
            {
                if (_helper != null && _helper.targetTexture == rt)
                    _helper.targetTexture = null;
                if (_camera != null && _camera.targetTexture == rt)
                    _camera.targetTexture = null;
                rt.Release();
            }

            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
        }

        private void DrawStageTexture()
        {
            var previous = RenderTexture.active;
            var cleared = false;
            try
            {
                RenderTexture.active = _refPostStageRT;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0f, _refPostStageRT.width, 0f, _refPostStageRT.height);
                if (_clearOutputNextFrame)
                {
                    GL.Clear(true, true, Color.clear);
                    cleared = true;
                }
                UnityEngine.Graphics.DrawTexture(_stageRect, _stageRT);
                GL.PopMatrix();
            }
            finally
            {
                RenderTexture.active = previous;
            }

            if (cleared)
                _clearOutputNextFrame = false;
        }

        private void OnPreRender()
        {
            if (!_initialized || _stageRT == null || _refPreFXStageRT == null || _refPostStageRT == null)
                return;

            var now = Time.time;
            var fxActive = IsFXLayerActive;
            var stageNew = _hasStageRenderedThisFrame;
            _hasStageRenderedThisFrame = false;

            var bgMergeDue = IsDue(_bgMergeFPS, _lastBGMergeTime, now);
            var fxDue = fxActive && IsDue(_fxFPS, _lastFXTime, now);

            var doRenderBg = stageNew || bgMergeDue;
            var doRenderFX = _hasFXRenderedSinceActive != fxActive || fxDue;

            if (doRenderBg || doRenderFX || _forceRenderNextFrame)
            {
                var layerCamera = _layerCamera != null ? _layerCamera : _camera;
                _helper.transform.localPosition = layerCamera.transform.localPosition;
                _helper.transform.localRotation = layerCamera.transform.localRotation;
                _helper.transform.localScale = layerCamera.transform.localScale;
                _helper.fieldOfView = layerCamera.fieldOfView;
                _helper.nearClipPlane = layerCamera.nearClipPlane;
                _helper.farClipPlane = layerCamera.farClipPlane;
                _helper.rect = layerCamera.rect;
            }

            if (doRenderBg || _forceRenderNextFrame)
            {
                // Draw the serialized stage viewport into the full-size output,
                // then preserve the reference layer ordering for L27 and L30.
                DrawStageTexture();

                _helper.targetTexture = _refPostStageRT;
                _helper.cullingMask = 1 << MU3.Sys.Const.Layer_BackgroundMerge;
                _helper.Render();
                _lastBGMergeTime = now;

                UnityEngine.Graphics.Blit(_refPostStageRT, _refPreFXStageRT);

                if (fxActive)
                {
                    _helper.cullingMask = 1 << MU3.Sys.Const.Layer_FX_BackGround;
                    _helper.Render();
                    _lastFXTime = now;
                }

                _hasFXRenderedSinceActive = fxActive;
                UnityEngine.Graphics.Blit(_refPostStageRT, _camera.targetTexture);
            }
            else if (doRenderFX)
            {
                UnityEngine.Graphics.Blit(_refPreFXStageRT, _refPostStageRT);

                if (fxActive)
                {
                    _helper.targetTexture = _refPostStageRT;
                    _helper.cullingMask = 1 << MU3.Sys.Const.Layer_FX_BackGround;
                    _helper.Render();
                    _lastFXTime = now;
                }

                _hasFXRenderedSinceActive = fxActive;
                UnityEngine.Graphics.Blit(_refPostStageRT, _camera.targetTexture);
            }
            else
            {
                UnityEngine.Graphics.Blit(_refPostStageRT, _camera.targetTexture);
            }

            _forceRenderNextFrame = false;
        }

        private void DetachHelperTarget()
        {
            if (_helper != null)
                _helper.targetTexture = null;
        }

        private void RestoreFallbackCameraState()
        {
            if (_camera != null && _hasFallbackCameraState)
            {
                _camera.clearFlags = _fallbackClearFlags;
                _camera.backgroundColor = _fallbackBackgroundColor;
            }
        }

        private void OnDisable()
        {
            DetachHelperTarget();
        }

        private void OnDestroy()
        {
            DetachHelperTarget();
            RestoreFallbackCameraState();

            if (Instance == this)
                Instance = null;

            if (_helper != null)
                Destroy(_helper.gameObject);

            _initialized = false;
            _camera = null;
            _layerCamera = null;
            _refPostStageRT = null;
            _stageRT = null;
        }
    }
}
