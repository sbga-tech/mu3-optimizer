using MonoMod;
using MonoMod.InlineRT;
using UnityEngine;

namespace MU3.Battle;

[MonoModIfFlag("BetterRendering")]
public class patch_BattleCamera : BattleCamera
{

    [MonoModIgnore] private Camera _mainCamera;

    [MonoModIgnore] private Animator _postStageCameraAnimator;
    [MonoModIgnore] private AnimationEventHandler _postStageCameraAnimationEventHandler;

    [MonoModIgnore] private RenderTexture _preStageRenderTexture;

    [MonoModIgnore] private RenderTexture _stageRenderTexutre;
    [MonoModIgnore] private RenderTexture _postStageRenderTexture;

    private StageCompositor _compositor;

    [MonoModReplace]
    private void createPostStageCamera(Transform parent, Camera nextCamera)
    {
        if (_mainCamera == null)
            return;

        // Stub billboard — stage content is drawn via CommandBuffer, not a quad

        // _postStageBillboard = new GameObject("PostStageBillboardStub");
        // _postStageBillboard.transform.SetParent(nextCamera.transform, false);

        // Instantiate the EffectCamera prefab purely for its Animator + AnimationEventHandler.
        // The Camera component is disabled (no rendering).
        // if (_effectCameraPrefab != null)
        // {
        //     GameObject effectObj = Object.Instantiate(_effectCameraPrefab, parent, false);
        //     _postStageCamera = effectObj.GetComponentInChildren<Camera>();
        //     if (_postStageCamera != null)
        //         _postStageCamera.enabled = false;
        //     _postStageCameraAnimator = effectObj.GetComponentInChildren<Animator>();
        //     if (_postStageCameraAnimator != null)
        //         _postStageCameraAnimationEventHandler =
        //             _postStageCameraAnimator.GetComponent<AnimationEventHandler>();
        // }

        if (_postStageCameraAnimator == null)
        {
            var animatorStub = new GameObject("PostStageCameraAnimatorStub");
            _postStageCameraAnimator = animatorStub.AddComponent<Animator>();
            _postStageCameraAnimationEventHandler = animatorStub.AddComponent<AnimationEventHandler>();
            animatorStub.transform.SetParent(parent, false);
        }

        // MainCamera keeps its original culling mask (excludes L27/L30).
        // The StageCompositor helper camera renders L27 and L30 separately.
        _mainCamera.clearFlags = CameraClearFlags.Depth;
        
        _compositor = _mainCamera.gameObject.AddComponent<StageCompositor>();
        
        _compositor.Initialize(_mainCamera, _stageRenderTexutre, _postStageRenderTexture);
        
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
        orig_setupStageCamera(stageObject, renderTargetScale);

        // Ensure _stageRenderTexutre is created (normally done by createStageMergeCamera)
        if (_stageRenderTexutre != null && !_stageRenderTexutre.IsCreated())
            _stageRenderTexutre.Create();

        // Feed stage RT to the CommandBuffer compositor
        if (_compositor != null && _preStageRenderTexture != null)
            _compositor.SetStageTexture(_preStageRenderTexture);

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
        if (RenderingConfig.DisableShadows)
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