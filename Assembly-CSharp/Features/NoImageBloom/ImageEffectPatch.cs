using MonoMod;
using mu3;
using UnityEngine;
using UnityEngine.Rendering;

namespace MU3.Mod.NoImageBloom;

[MonoModIfFlag(nameof(PatchConfig.NoImageBloom))]
[MonoModPatch("global::mu3.ImageEffect")]
public class ImageEffectPatch : ImageEffect
{
    [MonoModIgnore]
    private Camera camera_;

    [MonoModIgnore]
    private Camera renderCamera_;
    
    [MonoModIgnore]
    private CommandBuffer commandBuffer_;
    
    
    [MonoModIgnore]
    private RenderTexture renderTexture_;

    [MonoModIgnore]
    private RenderTexture halfRenderTexture_;
    
    [MonoModReplace]
    private void OnPostRender()
    {
        renderCamera_.fieldOfView = camera_.fieldOfView;
    }

    [MonoModReplace]
    private void rebuildCommandBuffer()
    {
        camera_.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer_);
        commandBuffer_.Clear();

        // Just copy the rendered frame to halfRenderTexture_
        if (halfRenderTexture_ != null)
            commandBuffer_.Blit(BuiltinRenderTextureType.CameraTarget, halfRenderTexture_);

        // copies final frame into renderTexture_
        if (WriteBack && renderTexture_ != null)
            commandBuffer_.Blit(BuiltinRenderTextureType.CameraTarget, renderTexture_);

        camera_.AddCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer_);
    }
}