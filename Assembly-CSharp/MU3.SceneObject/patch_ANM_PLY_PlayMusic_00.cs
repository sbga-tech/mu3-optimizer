using MonoMod;
using MU3.CustomUI;
using MU3.User;

namespace MU3.SceneObject;

[MonoModIfFlag("InactiveMirrorIndicator")]
public class patch_ANM_PLY_PlayMusic_00 : ANM_PLY_PlayMusic_00
{
    [MonoModIgnore] private MU3UIImageChanger _optionMirrorImageChanger;

    public extern void orig_set_userOption(UserOption.DataSet value);

    public new UserOption.DataSet userOption
    {
        set
        {
            orig_set_userOption(value);
            // Pattern 1 (mirror off) selects a blank sprite that still costs
            // a live Image + BaseMeshEffect in every canvas traversal. Hide
            // the indicator object instead; MU3UIImageChanger.OnEnable
            // re-applies the stored pattern when it is shown again.
            _optionMirrorImageChanger.gameObject.SetActive(
                value.Mirror == UserOptionValue.eMirror.ON);
        }
    }
}
