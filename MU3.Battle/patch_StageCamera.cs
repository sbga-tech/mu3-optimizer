using MonoMod;
using UnityEngine;

namespace MU3.Battle;

[MonoModIfFlag("NoEffectCamera")]
public class patch_StageCamera : StageCamera
{
    [MonoModIgnore]
    private Rect _renderingRect;
    
    [MonoModIgnore]
    private bool _holdVanishingPointOnCenter;

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
        Vector2 zero = Vector2.zero;
        if (_holdVanishingPointOnCenter)
        {
            zero.x = (2f * _renderingRect.x) / 1080f;
            zero.y = (2f * _renderingRect.y) / 1920f;
        }
        return zero;
    }
}