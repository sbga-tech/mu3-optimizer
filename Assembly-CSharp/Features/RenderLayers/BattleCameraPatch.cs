using MonoMod;
using MU3.Battle;
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

    private extern void orig_Awake();

    private new void Awake()
    {
        // These fields are shared asset references. Give each battle its own
        // outputs before the original code binds them to cameras and billboards.
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
        _forceLayers = true;
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
        var force = _forceLayers || lostTarget || refreshing || _wasRefreshingLayers ||
            getCurrentState() != EState.Play;
        var passes = _layerSchedule.Next(Time.time, MonoMod.RenderLayersConfig.StageFPS,
            MonoMod.RenderLayersConfig.BGMergeFPS, MonoMod.RenderLayersConfig.FXFPS, force);
        _scheduledStageCamera.enabled = (passes & RenderLayerSchedule.Stage) != 0;
        _stageMergeCamera.enabled = (passes & RenderLayerSchedule.Merge) != 0;
        _postStageCamera.enabled = (passes & RenderLayerSchedule.FX) != 0;
        _forceLayers = false;
        _wasRefreshingLayers = refreshing;
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
}
