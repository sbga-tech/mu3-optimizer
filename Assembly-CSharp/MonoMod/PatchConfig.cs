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
    static MonoModRules()
    {
        ApplyFlags();
    }
}

