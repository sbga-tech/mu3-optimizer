namespace MU3.Mod.RenderLayers;

// Each output is a texture consumed by the next pass. An upstream refresh
// must reach the screen in the same frame even when downstream rates are lower.
internal struct RenderLayerSchedule
{
    internal const int Stage = 1;
    internal const int Merge = 2;
    internal const int FX = 4;

    private bool _initialized;
    private FrameRateLimiter _stage;
    private FrameRateLimiter _merge;
    private FrameRateLimiter _fx;

    internal int Next(float now, float stageFPS, float mergeFPS, float fxFPS, bool force)
    {
        force |= !_initialized;
        _initialized = true;
        var stage = IsDue(ref _stage, now, stageFPS, force);
        var merge = IsDue(ref _merge, now, mergeFPS, force || stage);
        var fx = IsDue(ref _fx, now, fxFPS, force || merge);
        return (stage ? Stage : 0) | (merge ? Merge : 0) | (fx ? FX : 0);
    }

    // Negative rates redraw every frame, 0 only when forced, and positive rates at most that many
    // times per second. A forced redraw counts against the layer's schedule.
    private static bool IsDue(ref FrameRateLimiter limiter, float now, float fps, bool force)
    {
        if (fps < 0f)
            return true;
        if (force)
        {
            if (fps > 0f)
                limiter.Force(now, fps);
            return true;
        }

        return fps > 0f && limiter.Tick(now, fps);
    }
}
