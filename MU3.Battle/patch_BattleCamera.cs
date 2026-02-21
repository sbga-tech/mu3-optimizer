using MonoMod;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace MU3.Battle;

[MonoModIfFlag("NoEffectCamera")]
public class patch_BattleCamera : BattleCamera
{
    [MonoModIgnore]
    private Camera _mainCamera;

    [MonoModIgnore] private Camera _postStageCamera;
    [MonoModIgnore] private Camera _stageMergeCamera;

    
    [MonoModIgnore] private GameObject _postStageBillboard;
    [MonoModIgnore] private GameObject _stageMergeBillboard;
    
    [MonoModIgnore] private GameObject _stageBillboard;
    
    [MonoModIgnore] private Animator _postStageCameraAnimator;
    [MonoModIgnore] private AnimationEventHandler _postStageCameraAnimationEventHandler;


    [MonoModIgnore] private Rect _postStageCameraRect;

    [MonoModIgnore] private RenderTexture _preStageRenderTexture;
    
    [MonoModIgnore]
    private extern void checkRenderTextureSize(ref RenderTexture renderTexture, int width, int height, int depth,
        RenderTextureFormat format);
    
    public extern void orig_setupStageCamera(GameObject stageObject, float renderTragetScale);

    public new void setupStageCamera(GameObject stageObject, float renderTragetScale)
    {
        orig_setupStageCamera(stageObject, renderTragetScale);
        if (_stageBillboard != null)
        {
            _stageBillboard.layer = 0; //make it directly rendered in the default layer instead of going thru 2 passes
        }

        // No observable difference in performance with or without this
         
        // Camera componentInChildren = stageObject.GetComponentInChildren<Camera>();
        // if (componentInChildren == null)
        // {
        //     return;
        // }
        //
        // PostProcessingBehaviour ppb = componentInChildren.gameObject.GetComponent<PostProcessingBehaviour>();
        // if (ppb == null)
        // {
        //     return;
        // }
        //
        // Destroy(ppb);
    }
    
    [MonoModReplace]
    private void createPostStageCamera(Transform parent, Camera nextCamera)
    {
        if (!(_mainCamera == null))
        {
            _postStageBillboard = new GameObject("PostStageBillboardStub");
            _postStageBillboard.transform.SetParent(nextCamera.transform, false);
            GameObject camObj = new GameObject("PostStageCameraStub");
            _postStageCamera = camObj.AddComponent<Camera>();
            _postStageCameraAnimator = camObj.AddComponent<Animator>();
            _postStageCameraAnimationEventHandler = camObj.AddComponent<AnimationEventHandler>();
            _postStageCamera.transform.SetParent(parent, false);
            _postStageCamera.enabled = false;
        }
    }

    [MonoModIgnore]
    private extern void faceBillboardToCamera(Camera camera, GameObject billboard, Rect rect, Vector2 screenSize);
    
    [MonoModReplace]
    private void faceBillboardsToCamera()
    {
        faceBillboardToCamera(_mainCamera, _stageBillboard, _postStageCameraRect, MU3.Sys.Const.ScreenSize);
    }
    
    [MonoModReplace]
    private void createStageMergeCamera(Transform parent, Camera nextCamera)
    {
        if (!(_mainCamera == null))
        {
            _postStageBillboard = new GameObject("StageMergeBillboardStub");
            _postStageBillboard.transform.SetParent(nextCamera.transform, false);
            
            _stageMergeCamera = new GameObject("StageMergeCameraStub").AddComponent<Camera>();
            _stageMergeCamera.transform.SetParent(parent, false);
            _stageMergeCamera.enabled = false;
        }
    }
}