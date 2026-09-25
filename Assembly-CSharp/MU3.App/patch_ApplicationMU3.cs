using MonoMod;
using MU3.Mod;

namespace MU3.App;

public class patch_ApplicationMU3 : ApplicationMU3
{
    [MonoModIfFlag("AnyPatchEnabled")]
    private extern void orig_initializeFirst();
    [MonoModIfFlag("AnyPatchEnabled")]
    private void initializeFirst()
    {
        orig_initializeFirst();
        UnityPlayerHooks.TryStart(gameObject);
        UnityEngine.Debug.Log("[Steroid] loaded.");
    }

}
