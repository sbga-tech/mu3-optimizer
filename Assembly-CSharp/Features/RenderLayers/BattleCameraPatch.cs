using System.Collections.Generic;
using MonoMod;
using MU3.Battle;
using MU3.Util;
using UnityEngine;

namespace MU3.Mod.RenderLayers;

[MonoModIfFlag(nameof(PatchConfig.RenderLayers))]
[MonoModPatch("global::MU3.Battle.BattleCamera")]
public class BattleCameraPatch : BattleCamera
{
    [MonoModIgnore] private Camera _mainCamera;
    [MonoModIgnore] private Camera _stageMergeCamera;
    [MonoModIgnore] private Camera _postStageCamera;
    [MonoModIgnore] private RenderTexture _preStageRenderTexture;
    [MonoModIgnore] private RenderTexture _stageRenderTexutre;
    [MonoModIgnore] private RenderTexture _postStageRenderTexture;

    private Camera _scheduledStageCamera;
    private RenderLayerSchedule _layerSchedule;
    private bool _forceLayers = true;
    private float _refreshLayersUntil = float.NegativeInfinity;
    private bool _wasRefreshingLayers;
    // Cameras that draw the FX gate texture, which only FX-layer (post-stage) effects sample.
    // Null when there are none: MonoMod does not run patch-class field initializers.
    private Camera[] _gateCameras;
    // Camera matrices at the previous event-window frame (EventFXOnly).
    private Matrix4x4 _lastMainView;
    private Matrix4x4 _lastMainProjection;
    private Matrix4x4 _lastStageView;
    private Matrix4x4 _lastStageProjection;

    private RenderTexture _stageAsset;
    private RenderTexture _postStageAsset;

    private extern void orig_Awake();

    private new void Awake()
    {

        // These fields are shared asset references. Give each battle its own
        // outputs before the original code binds them to cameras and billboards.
        // but the originals also need to be kept around as a couple of the shaders 
        // use the original texture, causing the wave transition to break

        _stageAsset = _stageRenderTexutre;
        _postStageAsset = _postStageRenderTexture;
        _stageRenderTexutre = CreateLayerTarget(_stageRenderTexutre);
        _postStageRenderTexture = CreateLayerTarget(_postStageRenderTexture);
        orig_Awake();
    }

    private static RenderTexture CreateLayerTarget(RenderTexture asset)
    {
        var target = asset != null ? Instantiate(asset)
            : new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32);
        target.hideFlags = HideFlags.DontSave;
        InitializeLayerTarget(target);
        return target;
    }

    private static void InitializeLayerTarget(RenderTexture target)
    {
        target.Create();
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    public extern void orig_setupStageCamera(GameObject stageObject, float renderTargetScale);

    public new void setupStageCamera(GameObject stageObject, float renderTargetScale)
    {
        // Reference setup can release/resize these targets. No camera may keep
        // a binding while that happens, including the previous stage camera.
        DetachLayerCamera(_scheduledStageCamera);
        DetachLayerCamera(_stageMergeCamera);
        DetachLayerCamera(_postStageCamera);
        _scheduledStageCamera = stageObject.GetComponentInChildren<Camera>();
        DetachLayerCamera(_scheduledStageCamera);
        try
        {
            orig_setupStageCamera(stageObject, renderTargetScale);
        }
        finally
        {
            if (_stageMergeCamera != null)
                _stageMergeCamera.targetTexture = _stageRenderTexutre;
            if (_postStageCamera != null)
                _postStageCamera.targetTexture = _postStageRenderTexture;
        }
        SetGateCameras(true);
        _gateCameras = FindGateCameras();
        _forceLayers = true;
    }

    // The battle stage's FX gate camera draws layer 29 into its own texture. Only layer-30
    // effects sample that texture, and only the post-stage (FX) camera draws layer 30, so the
    // gate output is used exactly when the FX layer redraws. Cameras disabled at setup stay off.
    private static Camera[] FindGateCameras()
    {
        if (!MonoMod.RenderLayersConfig.ScheduleGateCamera)
            return null;
        var engine = SingletonMonoBehaviour<GameEngine>.instance;
        var stage = engine != null ? engine.battleStage : null;
        if (stage == null)
            return null;
        var found = new List<Camera>();
        foreach (var camera in stage.GetComponentsInChildren<Camera>(true))
        {
            if (camera.enabled && camera.targetTexture != null &&
                camera.cullingMask == 1 << MU3.Sys.Const.Layer_FX_Gate)
                found.Add(camera);
        }
        return found.ToArray();
    }

    private void SetGateCameras(bool enabled)
    {
        if (_gateCameras == null)
            return;
        foreach (var camera in _gateCameras)
        {
            if (camera != null)
                camera.enabled = enabled;
        }
    }

    // Called on the owning BattleCamera, never on a process-global compositor.
    public void RequestLayerRefresh(float duration)
    {
        _refreshLayersUntil = Mathf.Max(_refreshLayersUntil, Time.time + duration);
        _forceLayers = true;
    }

    private void LateUpdate()
    {
        // Rendering still uses the original cameras, shader, billboards, depth
        // order and post-processing. Only decide which cached outputs refresh.
        if (_mainCamera == null || !_mainCamera.isActiveAndEnabled ||
            _scheduledStageCamera == null || !_scheduledStageCamera.gameObject.activeInHierarchy ||
            _stageMergeCamera == null || !_stageMergeCamera.gameObject.activeInHierarchy ||
            _postStageCamera == null || !_postStageCamera.gameObject.activeInHierarchy)
        {
            RestoreLayerCameras();
            _forceLayers = true;
            return;
        }

        var lostTarget = RestoreLayerTarget(_preStageRenderTexture);
        lostTarget |= RestoreLayerTarget(_stageRenderTexutre);
        lostTarget |= RestoreLayerTarget(_postStageRenderTexture);
        var refreshing = Time.time < _refreshLayersUntil;
        var force = _forceLayers || lostTarget || getCurrentState() != EState.Play;
        var forceFX = false;
        if (refreshing || _wasRefreshingLayers)
        {
            // Event effects draw on the FX layer. With EventFXOnly, a still camera leaves the
            // stage and merged background at their own rates; the frames that open and close
            // a window, and any camera motion inside it, still redraw every layer.
            var moved = !MonoMod.RenderLayersConfig.EventFXOnly || !refreshing || LayerCamerasMoved();
            force |= moved;
            forceFX = !moved;
        }
        var passes = _layerSchedule.Next(Time.time, MonoMod.RenderLayersConfig.StageFPS,
            MonoMod.RenderLayersConfig.BGMergeFPS, MonoMod.RenderLayersConfig.FXFPS, force, forceFX);
        _scheduledStageCamera.enabled = (passes & RenderLayerSchedule.Stage) != 0;
        _stageMergeCamera.enabled = (passes & RenderLayerSchedule.Merge) != 0;
        _postStageCamera.enabled = (passes & RenderLayerSchedule.FX) != 0;
        SetGateCameras((passes & RenderLayerSchedule.FX) != 0);
        _forceLayers = false;
        _wasRefreshingLayers = refreshing;
    }

    // True when the main or stage camera's view or projection differs from the previous call.
    private bool LayerCamerasMoved()
    {
        var mainView = _mainCamera.worldToCameraMatrix;
        var mainProjection = _mainCamera.projectionMatrix;
        var stageView = _scheduledStageCamera.worldToCameraMatrix;
        var stageProjection = _scheduledStageCamera.projectionMatrix;
        var moved = mainView != _lastMainView || mainProjection != _lastMainProjection ||
            stageView != _lastStageView || stageProjection != _lastStageProjection;
        _lastMainView = mainView;
        _lastMainProjection = mainProjection;
        _lastStageView = stageView;
        _lastStageProjection = stageProjection;
        return moved;
    }

    private static bool RestoreLayerTarget(RenderTexture target)
    {
        if (target == null || target.IsCreated())
            return false;
        InitializeLayerTarget(target);
        return true;
    }

    private void RestoreLayerCameras()
    {
        if (_scheduledStageCamera != null)
            _scheduledStageCamera.enabled = true;
        if (_stageMergeCamera != null)
            _stageMergeCamera.enabled = true;
        if (_postStageCamera != null)
            _postStageCamera.enabled = true;
        SetGateCameras(true);
    }

    private static void DetachLayerCamera(Camera camera)
    {
        if (camera == null)
            return;
        camera.enabled = false;
        camera.targetTexture = null;
    }

    public extern void orig_destroyStageCamera();

    public new void destroyStageCamera()
    {
        DetachLayerCamera(_scheduledStageCamera);
        _scheduledStageCamera = null;
        orig_destroyStageCamera();
        RestoreLayerCameras();
        _gateCameras = null;
        _forceLayers = true;
    }

    private void OnDisable()
    {
        RestoreLayerCameras();
        _forceLayers = true;
    }

    private extern void orig_OnDestroy();

    private void OnDestroy()
    {
        DetachLayerCamera(_scheduledStageCamera);
        DetachLayerCamera(_stageMergeCamera);
        DetachLayerCamera(_postStageCamera);
        orig_OnDestroy();
        SetGateCameras(true);
        _gateCameras = null;
        ReleaseLayerTarget(ref _preStageRenderTexture);
        ReleaseLayerTarget(ref _stageRenderTexutre);
        ReleaseLayerTarget(ref _postStageRenderTexture);
    }

    private static void ReleaseLayerTarget(ref RenderTexture target)
    {
        if (target == null)
            return;
        if (RenderTexture.active == target)
            RenderTexture.active = null;
        target.Release();
        Destroy(target);
        target = null;
    }

    public extern void orig_Leave_StartCutscene();

    private void Leave_StartCutscene()
    {
        orig_Leave_StartCutscene();
        if (MonoMod.RenderLayersConfig.DisableShadows)
            DisableShadows();
    }

    private void DisableShadows()
    {
        foreach (var light in FindObjectsOfType<Light>())
            light.shadows = LightShadows.None;
        foreach (var renderer in FindObjectsOfType<Renderer>())
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    public void RebindEffectTextures(GameObject root)
    {
        // Awake hasn't captured the original assets yet, so there is nothing to match against.
        if (_stageAsset == null && _postStageAsset == null) return;

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var m in r.materials)
            {
                if (!m.HasProperty("_RenderTexture")) continue;
                var tex = m.GetTexture("_RenderTexture") as RenderTexture;
                
                if (tex == _stageAsset)
                {
                    m.SetTexture("_RenderTexture", _stageRenderTexutre);
                }
                else if (tex == _postStageAsset)
                {
                    m.SetTexture("_RenderTexture", _postStageRenderTexture);
                }
            }
        }
    }
}
