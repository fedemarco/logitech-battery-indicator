namespace LogitechBattery.Battery;

/// <summary>
/// Notices a battery read that never finishes: while it runs, every later refresh is skipped and the tray freezes without an error.
/// Detection only. Times are monotonic milliseconds (<see cref="Environment.TickCount64"/>), so a clock change cannot trigger it.
/// </summary>
public sealed class StuckReadWatch(TimeSpan limit)
{
    private long? _startedAtMs;
    private bool _reported;

    public void ReadStarted(long nowMs)
    {
        _startedAtMs = nowMs;
        _reported = false;
    }

    public void ReadFinished() => _startedAtMs = null;

    /// <summary>True once per read, the first time it is found running longer than the limit.</summary>
    public bool ShouldReport(long nowMs)
    {
        if (_startedAtMs is not { } startedAtMs || _reported || nowMs - startedAtMs < limit.TotalMilliseconds)
            return false;
        _reported = true;
        return true;
    }
}
