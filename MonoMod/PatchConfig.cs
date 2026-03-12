using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.InlineRT;

namespace MonoMod;
public static class RenderingConfig
{
    [IniField("Optimization.Rendering", "StageFPS")]
    public static float StageFPS;

    [IniField("Optimization.Rendering", "BGMergeFPS")]
    public static float BGMergeFPS;

    [IniField("Optimization.Rendering", "FXFPS", 30)]
    public static float FXFPS;

    [IniField("Optimization.Rendering", "DisableShadows", 1)]
    public static bool DisableShadows;

    [PatchIniConfig] static RenderingConfig() { }
}

static partial class MonoModRules
{
    static string IniPath => Environment.GetEnvironmentVariable("MU3_MODS_CONFIG_PATH") ?? "mu3.ini";

    static MonoModRules()
    {
        //Disable comm to AM could cause unexpected side effects, only have single digit perf gain,
        //and requires a freshly implemented JvsButton, so we just discard it for now.
        MonoModRule.Flag.Set("NoAMDuringPlay", false);

        using var ini = new IniFile(IniPath);
        var noImageBloom = ini.getIntValue("Optimization", "NoImageBloom", 1) != 0;
        MonoModRule.Flag.Set("NoImageBloom", noImageBloom);
        var betterRendering = ini.getIntValue("Optimization", "BetterRendering", 1) != 0;
        MonoModRule.Flag.Set("BetterRendering", betterRendering);
        var boostLoginRequests = ini.getIntValue("Optimization", "BoostLoginRequests", 1) != 0;
        MonoModRule.Flag.Set("BoostLoginRequests", boostLoginRequests);
        var noUiCameraDuringPlay = ini.getIntValue("Optimization", "NoUICameraDuringPlay", 0) != 0;
        MonoModRule.Flag.Set("NoUICameraDuringPlay", noUiCameraDuringPlay);
        var betterNotes = ini.getIntValue("Optimization", "BetterNotes", 1) != 0;
        MonoModRule.Flag.Set("BetterNotes", betterNotes);
    }
}