using MonoMod;
using MonoMod.InlineRT;
using UnityEngine;

namespace MU3.Battle;

[MonoModIfFlag("RenderLayers")]
public class patch_BattleCamera : BattleCamera
{

    [MonoModIgnore] private Camera _mainCamera;

    [MonoModIgnore] private Animator _postStageCameraAnimator;
    [MonoModIgnore] private AnimationEventHandler _postStageCameraAnimationEventHandler;

    [MonoModIgnore] private RenderTexture _preStageRenderTexture;

    [MonoModIgnore] private RenderTexture _stageRenderTexutre;
    [MonoModIgnore] private RenderTexture _postStageRenderTexture;
    [MonoModIgnore] private GameObject _effectCameraPrefab;
    [MonoModIgnore] private Camera _postStageCamera;
    [MonoModIgnore] private Rect _stageCameraRect;

    private CameraClearFlags _originalMainClearFlags;
    private Color _originalMainBackgroundColor;

    private StageCompositor _compositor;

    [MonoModReplace]
    private void createPostStageCamera(Transform parent, Camera nextCamera)
    {
        if (_mainCamera == null)
            return;

        // Stage pixels are composited directly into the reference post-stage
        // target. Keep the effect-camera asset for its animator and event
        // handler; the compositor owns the actual layer render.

        if (_effectCameraPrefab != null)
        {
            var effectObj = UnityEngine.Object.Instantiate(_effectCameraPrefab, parent, false);
            _postStageCamera = effectObj.GetComponentInChildren<Camera>();
            if (_postStageCamera != null)
            {
                _postStageCamera.enabled = false;
                _postStageCamera.targetTexture = null;
            }
            _postStageCameraAnimator = effectObj.GetComponentInChildren<Animator>();
            if (_postStageCameraAnimator != null)
            {
                _postStageCameraAnimationEventHandler =
                    _postStageCameraAnimator.GetComponent<AnimationEventHandler>();
            }
        }

        if (_postStageCameraAnimator == null)
        {
            var animatorStub = new GameObject("PostStageCameraAnimatorStub");
            _postStageCameraAnimator = animatorStub.AddComponent<Animator>();
            _postStageCameraAnimationEventHandler = animatorStub.AddComponent<AnimationEventHandler>();
            animatorStub.transform.SetParent(parent, false);
        }

        // Preserve the scene camera state for the transition path. The
        // compositor temporarily needs depth-only output while active.
        _originalMainClearFlags = _mainCamera.clearFlags;
        _originalMainBackgroundColor = _mainCamera.backgroundColor;
        _mainCamera.clearFlags = CameraClearFlags.Depth;
        
        _compositor = _mainCamera.gameObject.AddComponent<StageCompositor>();
        
        _compositor.Initialize(_mainCamera, _postStageCamera, _stageRenderTexutre,
            _postStageRenderTexture, _originalMainClearFlags, _originalMainBackgroundColor);
        
    }
    
    private void DisableShadows()
    {
        var lights = FindObjectsOfType<Light>();
        foreach (var light in lights)
        {
            light.shadows = LightShadows.None;
        }
            
        var renders = FindObjectsOfType<Renderer>();
        foreach (var renderer in renders)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
    
    [MonoModReplace]
    private GameObject makeBillboard(Camera camera, Texture texture, int layer, string name)
    {
        return null;
    }

    [MonoModReplace]
    private void createStageMergeCamera(Transform parent, Camera nextCamera)
    {
    }

    public extern void orig_setupStageCamera(GameObject stageObject, float renderTargetScale);

    public new void setupStageCamera(GameObject stageObject, float renderTargetScale)
    {
        var stageCamera = stageObject.GetComponentInChildren<Camera>();
        if (stageCamera != null)
            stageCamera.targetTexture = null;

        orig_setupStageCamera(stageObject, renderTargetScale);

        // Ensure _stageRenderTexutre is created (normally done by createStageMergeCamera)
        if (_stageRenderTexutre != null && !_stageRenderTexutre.IsCreated())
            _stageRenderTexutre.Create();

        // Feed the stage RT and the serialized scene viewport to the compositor.
        if (_compositor != null && _preStageRenderTexture != null)
            _compositor.SetStageTexture(_preStageRenderTexture, _stageCameraRect);

    }

    public extern void orig_destroyStageCamera();

    public new void destroyStageCamera()
    {
        orig_destroyStageCamera();

        if (_compositor != null)
            _compositor.ClearStageTexture();
    }

    public extern void orig_Execute_StartCutscene();

    private void Execute_StartCutscene()
    {
        orig_Execute_StartCutscene();

        if (_compositor != null)
            _compositor.ForceRenderNextFrame();
    }

    public extern void orig_Leave_StartCutscene();
    
    private void Leave_StartCutscene()
    {
        orig_Leave_StartCutscene();
        if (MonoMod.RenderLayersConfig.DisableShadows)
            DisableShadows();
    }

    [MonoModReplace]
    private void faceBillboardsToCamera()
    {
    }

    [MonoModReplace]
    private void syncEffectCamera()
    {
    }
}