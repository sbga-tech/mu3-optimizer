using MonoMod;

[assembly: MonoModTargetModule("AMDaemon.NET")]

namespace MonoMod;

static partial class MonoModRules
{
    static MonoModRules()
    {
        InitializePatch();
        InlineAMDaemonCalls();
    }
}
