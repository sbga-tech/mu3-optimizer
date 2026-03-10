using MonoMod;
using UnityEngine;

namespace MU3.Battle;

[MonoModIfFlag("BetterRendering")]
public class patch_StageCamera : StageCamera
{
    [MonoModIgnore]
    private Rect _renderingRect;

    [MonoModIgnore]
    private bool _holdVanishingPointOnCenter;

    private Camera _cam;

    [MonoModReplace]
    public Rect get_renderingRect()
    {
        var original = _renderingRect;

        return new Rect(
            original.position,
            new Vector2(1080f, 1920f)
        );
    }

    [MonoModReplace]
    public new Vector2 calcProjectionM02M12()
    {
        var zero = Vector2.zero;
        if (_holdVanishingPointOnCenter)
        {
            zero.x = (2f * _renderingRect.x) / 1080f;
            zero.y = (2f * _renderingRect.y) / 1920f;
        }
        return zero;
    }


    // OnPreCull runs just before this camera's culling pass — the last
    // chance to skip rendering for this frame.
    // Delegates timing decisions to StageCompositor
    private void OnPreCull()
    {
        if (_cam == null)
            _cam = GetComponent<Camera>();

        var compositor = StageCompositor.Instance;
        if (compositor == null)
            return; // No compositor yet — render normally.

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