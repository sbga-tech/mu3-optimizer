using System;
using MonoMod.InlineRT;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MU3.Battle
{
    public class StageCompositor : MonoBehaviour
    {
        private Camera _camera;
        private Camera _helper;
        private RenderTexture _stageRT;

        private RenderTexture _refPreFXStageRT;
        private RenderTexture _refPostStageRT;

        private const int RTWidth = 1080;
        private const int RTHeight = 1920;
        
        // Configurable frame rates (-1 = uncapped, 0 = freeze, >0 = rate-limited)
        
        //Recommended setting:
        //FXFPS>=BGMergeFPS>=StageFPS
        private float _stageFPS = 0f;
        private float _bgMergeFPS = 0f;
        private float _fxFPS = 30f;

        private float _lastStageTime = float.NegativeInfinity;
        private float _lastBGMergeTime = float.NegativeInfinity;
        private float _lastFXTime = float.NegativeInfinity;
        private bool _forceRenderNextFrame;
        private bool _hasStageRenderedThisFrame;
        private bool _hasFXRenderedSinceActive;

        // On-demand L30 rendering
        private float _fxLayerActiveUntil;
        //private bool _hadFXRequest;


        private bool _initialized;

        public static StageCompositor Instance { get; private set; }

        public static void RequestFXLayer(float duration)
        {
            if (Instance != null)
            {
                var until = Time.time + duration;
                if (until > Instance._fxLayerActiveUntil)
                    Instance._fxLayerActiveUntil = until;
                //Instance._hadFXRequest = true;
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
            //_hadFXRequest = false;
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
            
            _stageFPS = MonoMod.RenderingConfig.StageFPS;
            _bgMergeFPS = MonoMod.RenderingConfig.BGMergeFPS;
            _fxFPS = MonoMod.RenderingConfig.FXFPS;
            
        }

        public void Initialize(Camera mainCamera, RenderTexture preFXStageRT, RenderTexture postStageRT)
        {
            if (mainCamera == null || _helper == null)
            {
                return;
            }

            _camera = mainCamera;
            _helper.transform.SetParent(mainCamera.transform, false);

            if (preFXStageRT == null || postStageRT == null)
            {
                UnityEngine.Debug.LogError("[StageCompositor] Missing reference RTs. WTF??");
                EnsureRT(ref preFXStageRT);
                EnsureRT(ref postStageRT);
            }
            _refPreFXStageRT = preFXStageRT;
            _refPostStageRT = postStageRT;

            _initialized = true;
            Instance = this;
        }

        public void SetStageTexture(RenderTexture stageRT)
        {
            _stageRT = stageRT;
            ForceRenderNextFrame();
        }

        public void ClearStageTexture()
        {
            _stageRT = null;
            _forceRenderNextFrame = true;
            _hasFXRenderedSinceActive = false;
        }

        public void SetLegacyRenderTextures(RenderTexture stageRT, RenderTexture postStageRT)
        {
            if (stageRT == null || postStageRT == null)
            {
                throw new ArgumentException("[StageCompositor] Legacy RTs cannot be null. WHAT HAPPENED");
            }
            _refPreFXStageRT = stageRT;
            _refPostStageRT = postStageRT;
        }




        private static bool IsDue(float fps, float lastTime, float now)
        {
            if (fps == 0) return false;
            if (fps < 0) return true;
            return now - lastTime >= 1f / fps;
        }
        
        private static void EnsureRT(ref RenderTexture rt, int w = RTWidth, int h = RTHeight)
        {
            if (rt != null && rt.width == w && rt.height == h) return;
            if (rt != null) rt.Release();
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
        }


        private void OnPreRender()
        {
            if (!_initialized || _stageRT == null)
                return;

            var now = Time.time;
            var fxActive = IsFXLayerActive;

            // if (FreezeBGAfterBattleStart && _hadFXRequest && !fxActive)
            //     StageFPS = 0;

            var stageNew = _hasStageRenderedThisFrame;
            _hasStageRenderedThisFrame = false;
            
            var bgMergeDue = IsDue(_bgMergeFPS, _lastBGMergeTime, now);
            var fxDue = fxActive && IsDue(_fxFPS, _lastFXTime, now);

            var doRenderBg =  stageNew || bgMergeDue;
            var doRenderFX = _hasFXRenderedSinceActive != fxActive || fxDue;

            if (doRenderBg || doRenderFX || _forceRenderNextFrame)
            {
                _helper.fieldOfView = _camera.fieldOfView;
                _helper.nearClipPlane = _camera.nearClipPlane;
                _helper.farClipPlane = _camera.farClipPlane;
                _helper.rect = _camera.rect;
            }

            if (doRenderBg || _forceRenderNextFrame)
            {
                // Path 1: stage + L27 changed
                // stageRT -> postStageRT, L27 -> postStageRT,
                // postStageRT -> preFXStageRT, FX -> postStageRT, postStageRT -> out

                UnityEngine.Graphics.Blit(_stageRT, _refPostStageRT);

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
                // Path 2: only FX changed
                // preFXStageRT -> postStageRT, FX -> postStageRT, postStageRT -> out

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
                // Path 3: nothing changed
                // postStageRT -> out
                UnityEngine.Graphics.Blit(_refPostStageRT, _camera.targetTexture);
            }
            
            _forceRenderNextFrame = false;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (_helper != null)
                Destroy(_helper.gameObject);
        }
    }
}