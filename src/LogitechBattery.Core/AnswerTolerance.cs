using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>
/// Decides when a device that misses a read counts as asleep. A mouse in light sleep answers some reads and misses others
/// for minutes before the receiver reports the link down, so one miss keeps the last answer. Changed only under the provider's read lock.
/// </summary>
internal sealed class AnswerTolerance
{
    // Two misses in a row: one miss is common for a mouse in light sleep, two mean it is not coming back soon.
    internal const int MissedReadsInARowThatMeanNotAnswering = 2;

    private readonly Dictionary<DeviceId, BatteryStatus> _lastAnswers = [];
    private readonly Dictionary<DeviceId, int> _missedReadsInARow = [];
    private readonly HashSet<DeviceId> _missedInThisRead = [];

    /// <summary>
    /// Call once per read. A read may run a second pass after a lost channel; a device counts as having missed only once per read,
    /// however many passes it missed in.
    /// </summary>
    public void StartRead() => _missedInThisRead.Clear();

    public void Answered(DeviceId device, BatteryStatus status)
    {
        _lastAnswers[device] = status;
        _missedReadsInARow.Remove(device);
        _missedInThisRead.Remove(device);
    }

    /// <summary>The device gave an error or an unreadable answer: it is not asleep, and earlier misses no longer count.</summary>
    public void Failed(DeviceId device) => Forget(device);

    /// <summary>Forgets everything about the device, so a later status is never kept from before its channel was lost.</summary>
    public void Forget(DeviceId device)
    {
        _lastAnswers.Remove(device);
        _missedReadsInARow.Remove(device);
        _missedInThisRead.Remove(device);
    }

    public void Clear()
    {
        _lastAnswers.Clear();
        _missedReadsInARow.Clear();
        _missedInThisRead.Clear();
    }

    /// <summary>
    /// The status to keep reporting after a missed read, or null when the device is not answering (the caller drops its route).
    /// A device that is not answering is forgotten, so it gets its full tolerance when it is found again.
    /// </summary>
    public BatteryStatus? StatusToKeepAfterMissedRead(DeviceId device, bool linkWentDown)
    {
        var missedReads = CountMissedRead(device);
        if (linkWentDown || missedReads >= MissedReadsInARowThatMeanNotAnswering)
        {
            Forget(device);
            return null;
        }
        return _lastAnswers.GetValueOrDefault(device);
    }

    private int CountMissedRead(DeviceId device)
    {
        var missedReads = _missedReadsInARow.GetValueOrDefault(device);
        if (_missedInThisRead.Add(device))
            _missedReadsInARow[device] = ++missedReads;
        return missedReads;
    }
}
