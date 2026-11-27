using MonoMod;
using mu3;
using UnityEngine;
using UnityEngine.Rendering;

namespace MU3.Mod.ImageBloom;

[MonoModIfFlag(nameof(PatchConfig.ImageBloom))]
[MonoModPatch("global::mu3.ImageEffect")]
public class ImageEffectPatch : ImageEffect
{
    private const string LogPrefix = "[Steroid][ImageBloom] ";

    [MonoModIgnore] private Camera camera_;
    [MonoModIgnore] private Camera renderCamera_;
    [MonoModIgnore] private CommandBuffer commandBuffer_;
    [MonoModIgnore] private RenderTexture renderTexture_;
    [MonoModIgnore] private RenderTexture halfRenderTexture_;

    private FrameRateLimiter _bloomSource;
    private static bool _invalidRateReported;

    [MonoModReplace]
    private void OnPostRender()
    {
        renderCamera_.fieldOfView = camera_.fieldOfView;
        if (ImageBloomConfig.NoGlow)
            return;
        if (WriteBack || !RateLimited() || _bloomSource.Tick(Time.time, ImageBloomConfig.BloomFPS))
            renderCamera_.RenderWithShader(bloomExtractShader_, "BloomType");
    }

    private static bool RateLimited()
    {
        if (ImageBloomConfig.BloomFPS > 0f)
            return true;
        if (!_invalidRateReported)
        {
            _invalidRateReported = true;
            Debug.LogWarning(LogPrefix + "BloomFPS must be above 0, got " + ImageBloomConfig.BloomFPS
                + "; the glow is redrawn every frame.");
        }
        return false;
    }

    private extern void orig_rebuildCommandBuffer();

    private void rebuildCommandBuffer()
    {
        if (!ImageBloomConfig.NoGlow)
        {
            orig_rebuildCommandBuffer();
            return;
        }

        camera_.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer_);
        commandBuffer_.Clear();
        if (halfRenderTexture_ != null)
            commandBuffer_.Blit(BuiltinRenderTextureType.CameraTarget, halfRenderTexture_);
        if (WriteBack && renderTexture_ != null)
            commandBuffer_.Blit(BuiltinRenderTextureType.CameraTarget, renderTexture_);
        camera_.AddCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer_);
    }
}
