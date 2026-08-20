using System;
using MonoMod.InlineRT;

namespace MonoMod;

static partial class MonoModRules
{
    static string IniPath => Environment.GetEnvironmentVariable("MU3_MODS_CONFIG_PATH") ?? "mu3.ini";

    static void ApplyFlags()
    {
        // Disable comm to AM could cause unexpected side effects, only have single digit perf gain,
        // and requires a freshly implemented JvsButton, so we just discard it for now.
        MonoModRule.Flag.Set("NoAMDuringPlay", false);

        using var ini = new IniFile(IniPath);
        MonoModRule.Flag.Set("NoImageBloom", ini.getIntValue("Optimization", "NoImageBloom", 1) != 0);
        MonoModRule.Flag.Set("BetterRendering", ini.getIntValue("Optimization", "BetterRendering", 1) != 0);
        MonoModRule.Flag.Set("BoostLoginRequests", ini.getIntValue("Optimization", "BoostLoginRequests", 1) != 0);
        MonoModRule.Flag.Set("NoUICameraDuringPlay", ini.getIntValue("Optimization", "NoUICameraDuringPlay", 0) != 0);
        MonoModRule.Flag.Set("BetterNotes", ini.getIntValue("Optimization", "BetterNotes", 1) != 0);
        MonoModRule.Flag.Set("NoAlloc", ini.getIntValue("Optimization", "NoAlloc", 1) != 0);
    }
}
