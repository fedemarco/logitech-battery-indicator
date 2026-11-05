namespace LogitechBattery.Battery;

/// <summary>
/// When to read again after a change (USB, mouse link, battery event): the cable shows up at once, but the
/// mouse can take a while to reconnect wirelessly. 1 s, then every 5 s for a minute, every 10 s, 15 s and 30 s
/// for a minute each; then the regular 60 s poll takes over. Stops as soon as the mouse is connected.
/// The caller owns the timer and asks this class for the next delay.
/// </summary>
public sealed class FollowUpSchedule
{
    private static readonly int[] DelaysMs =
    [
        1000,
        .. Enumerable.Repeat(5000, 12),
        .. Enumerable.Repeat(10_000, 6),
        .. Enumerable.Repeat(15_000, 4),
        .. Enumerable.Repeat(30_000, 2),
    ];

    private int _step;

    /// <summary>Counts restarts, so a read can tell that a change arrived while it was running.</summary>
    public int Generation { get; private set; }

    /// <summary>Starts over after a change; returns the delay before the first follow-up read.</summary>
    public int Restart()
    {
        Generation++;
        _step = 0;
        return DelaysMs[0];
    }

    /// <summary>
    /// Handles the end of a follow-up read that began at <paramref name="generation"/>.
    /// Returns the delay before the next read, or null if no further read is to be scheduled.
    /// A read that was skipped (<paramref name="ran"/> false) is tried again at the same step.
    /// </summary>
    public int? Finish(int generation, bool ran, BatteryStatus? status)
    {
        if (generation != Generation)
            return null; // a restart during the read already scheduled the next one

        if (status?.Connection == ConnectionState.Connected)
            return null;

        if (ran)
            _step++;
        return _step < DelaysMs.Length ? DelaysMs[_step] : null;
    }
}
