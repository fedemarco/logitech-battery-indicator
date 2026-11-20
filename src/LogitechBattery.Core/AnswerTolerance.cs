using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>
/// Decides when a device that misses a read counts as asleep. A mouse in light sleep answers some reads and misses others
/// for minutes before the receiver reports the link down, so after a missed read the last answer is kept until the link goes down
/// or the answer is too old. Times are monotonic milliseconds (<see cref="Environment.TickCount64"/>), so a clock change cannot matter.
/// Changed only under the provider's read lock.
/// </summary>
internal sealed class AnswerTolerance
{
    // The receiver reports a link down after about 5 to 7 minutes of idle. The cap covers a link down that never arrives (e.g. out of range).
    internal const long MaxAnswerAgeMs = 10 * 60 * 1000;

    private readonly record struct Answer(BatteryStatus Status, long AtMs);

    private readonly Dictionary<DeviceId, Answer> _lastAnswers = [];

    public void Answered(DeviceId device, BatteryStatus status, long nowMs) => _lastAnswers[device] = new Answer(status, nowMs);

    /// <summary>The device gave an error or an unreadable answer: it is not asleep, so nothing is kept.</summary>
    public void Failed(DeviceId device) => Forget(device);

    /// <summary>Forgets everything about the device, so a later status is never kept from before its channel was lost.</summary>
    public void Forget(DeviceId device) => _lastAnswers.Remove(device);

    public void Clear() => _lastAnswers.Clear();

    /// <summary>
    /// The status to keep reporting after a missed read, or null when the device is not answering (the caller drops its route).
    /// A device that is not answering is forgotten, so an old answer is never kept after it is found again.
    /// </summary>
    public BatteryStatus? KeepOrForgetAfterMissedRead(DeviceId device, bool linkWentDown, long nowMs)
    {
        var hasRecentAnswer = _lastAnswers.TryGetValue(device, out var lastAnswer) && IsRecent(lastAnswer, nowMs);
        if (linkWentDown || !hasRecentAnswer)
        {
            Forget(device);
            return null;
        }
        return lastAnswer.Status;
    }

    private static bool IsRecent(Answer answer, long nowMs) => nowMs - answer.AtMs < MaxAnswerAgeMs;
}
