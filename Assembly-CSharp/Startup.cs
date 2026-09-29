using MonoMod;
using MU3.App;

namespace MU3.Mod;

// Startup shared by every switch. Features register their own startup work with
// [OnStateEnter(nameof(ApplicationMU3.EState.WaitAMDaemonReady))] on ApplicationMU3.
internal static class Startup
{
    [OnStateEnter(nameof(ApplicationMU3.EState.WaitAMDaemonReady))]
    [MonoModIfFlag("AnyPatchEnabled")]
    internal static void Loaded(ApplicationMU3 application)
    {
        UnityEngine.Debug.Log("[Steroid] loaded.");
    }
}
