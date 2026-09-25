using MonoMod;
using UnityEngine;

namespace MU3.Battle;

[MonoModIfFlag("RenderLayers")]
public class patch_StageCamera : StageCamera
{
    [MonoModIgnore]
    private Camera _cam;

    // Scene assets use a 1080x1050 stage viewport at y=730. Keep the
    // serialized StageCamera rect and reference projection math unchanged;
    // BattleCamera uses that rect to place the stage below the header.

    // OnPreCull runs just before this camera's culling pass - the last
    // chance to skip rendering for this frame.
    // Delegates timing decisions to StageCompositor.
    private void OnPreCull()
    {
        if (_cam == null)
            _cam = GetComponent<Camera>();

        var compositor = StageCompositor.Instance;
        if (compositor == null)
            return;

        var now = Time.time;
        if (compositor.ShouldStageRender(now))
        {
            compositor.NotifyStageRendered(now);
        }
        else
        {
            _cam.enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (_cam != null && !_cam.enabled)
            _cam.enabled = true;
    }
}