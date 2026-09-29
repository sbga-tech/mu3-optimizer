namespace MU3.Mod;

/// <summary>
/// Lets work run at most <c>fps</c> times per second while never skipping a frame when the frame
/// rate is at or below the cap. Works from its default value, as patch-class fields never run
/// initializers.
/// </summary>
internal struct FrameRateLimiter
{
    // A frame may run this fraction of an interval early. Frame-time jitter at the cap then never
    // skips a frame; the carried schedule still bounds the average rate by the cap.
    private const double Tolerance = 0.25;

    // Unity clocks are floats. Keeping the schedule in double stops rounding from drifting the rate
    // once a long session has made the clock coarse.
    private double _next;
    private bool _started;

    /// <summary>Returns whether work runs this frame. <paramref name="fps"/> must be positive.</summary>
    internal bool Tick(float now, float fps)
    {
        var interval = 1.0 / fps;
        // Too early for the next slot, unless the clock moved backwards past any legal early run.
        if (_started && now < _next - Tolerance * interval && _next - now <= 2.0 * interval)
            return false;
        Ran(now, interval);
        return true;
    }

    /// <summary>Records work that ran at <paramref name="now"/> regardless of the schedule.</summary>
    internal void Force(float now, float fps)
    {
        Ran(now, 1.0 / fps);
    }

    private void Ran(double now, double interval)
    {
        if (_started && now >= _next - Tolerance * interval)
        {
            // The run took its slot: carry the schedule so frame quantization does not erode the
            // average rate, and resync after a stall instead of running every frame to catch up.
            _next += interval;
            if (_next <= now)
                _next = now + interval;
        }
        else
        {
            // First run, a forced run before its slot, or a clock that moved backwards.
            _next = now + interval;
        }

        _started = true;
    }
}
