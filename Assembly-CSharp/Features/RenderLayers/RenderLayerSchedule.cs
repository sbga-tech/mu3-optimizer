namespace MU3.Mod.RenderLayers;

// Each output is a texture consumed by the next pass. An upstream refresh
// must reach the screen in the same frame even when downstream rates are lower.
internal struct RenderLayerSchedule
{
    internal const int Stage = 1;
    internal const int Merge = 2;
    internal const int FX = 4;

    private bool _initialized;
    private float _lastStage;
    private float _lastMerge;
    private float _lastFX;

    internal int Next(float now, float stageFPS, float mergeFPS, float fxFPS, bool force)
    {
        force |= !_initialized || now < _lastStage || now < _lastMerge || now < _lastFX;
        var stage = force || IsDue(now, _lastStage, stageFPS);
        var merge = stage || force || IsDue(now, _lastMerge, mergeFPS);
        var fx = merge || force || IsDue(now, _lastFX, fxFPS);
        if (stage) _lastStage = now;
        if (merge) _lastMerge = now;
        if (fx) _lastFX = now;
        _initialized = true;
        return (stage ? Stage : 0) | (merge ? Merge : 0) | (fx ? FX : 0);
    }

    private static bool IsDue(float now, float last, float fps)
    {
        return fps < 0f || (fps > 0f && now - last >= 1f / fps);
    }
}
