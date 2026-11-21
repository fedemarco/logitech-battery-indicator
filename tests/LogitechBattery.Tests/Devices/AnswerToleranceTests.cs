using LogitechBattery.Battery;
using LogitechBattery.Devices;

namespace LogitechBattery.Tests;

public class AnswerToleranceTests
{
    private static readonly DeviceId Mouse = DeviceId.FromRoute(0xC54D, 1);
    private static readonly DeviceId Keyboard = DeviceId.FromRoute(0xC54D, 2);
    private static readonly BatteryStatus MouseStatus = TestData.Status(percent: 80);
    private static readonly BatteryStatus KeyboardStatus = TestData.Status(percent: 40);

    private const long AnsweredAtMs = 1_000_000;
    private const long JustWithinCapMs = AnsweredAtMs + AnswerTolerance.MaxAnswerAgeMs - 1;
    private const long AtCapMs = AnsweredAtMs + AnswerTolerance.MaxAnswerAgeMs;
    private const long ReadIntervalMs = 60_000; // the app reads every minute
    private const long OneReadLaterMs = AnsweredAtMs + ReadIntervalMs;
    private const long TwoReadsLaterMs = AnsweredAtMs + 2 * ReadIntervalMs;
    private const long ThreeReadsLaterMs = AnsweredAtMs + 3 * ReadIntervalMs;

    private static AnswerTolerance ToleranceWithMouseAnswered()
    {
        var tolerance = new AnswerTolerance();
        tolerance.Answered(Mouse, MouseStatus, AnsweredAtMs);
        return tolerance;
    }

    private static BatteryStatus? MissedRead(AnswerTolerance tolerance, DeviceId device, long nowMs, bool linkWentDown = false) =>
        tolerance.KeepOrForgetAfterMissedRead(device, linkWentDown, nowMs);

    [Fact]
    public void Any_number_of_missed_reads_within_the_cap_keep_the_last_status()
    {
        var tolerance = ToleranceWithMouseAnswered();

        Assert.Same(MouseStatus, MissedRead(tolerance, Mouse, OneReadLaterMs));
        Assert.Same(MouseStatus, MissedRead(tolerance, Mouse, TwoReadsLaterMs));
        Assert.Same(MouseStatus, MissedRead(tolerance, Mouse, ThreeReadsLaterMs));
        Assert.Same(MouseStatus, MissedRead(tolerance, Mouse, JustWithinCapMs));
    }

    [Fact]
    public void A_link_down_is_not_answering_at_once()
    {
        var tolerance = ToleranceWithMouseAnswered();

        Assert.Null(MissedRead(tolerance, Mouse, OneReadLaterMs, linkWentDown: true));
    }

    [Fact]
    public void A_missed_read_at_the_cap_is_not_answering()
    {
        var tolerance = ToleranceWithMouseAnswered();

        Assert.Null(MissedRead(tolerance, Mouse, AtCapMs));
    }

    [Fact]
    public void A_missed_read_with_no_earlier_answer_is_not_answering()
    {
        var tolerance = new AnswerTolerance();

        Assert.Null(MissedRead(tolerance, Mouse, AnsweredAtMs));
    }

    [Fact]
    public void A_new_answer_restarts_the_cap()
    {
        var tolerance = ToleranceWithMouseAnswered();
        var newerStatus = TestData.Status(percent: 79);
        tolerance.Answered(Mouse, newerStatus, JustWithinCapMs);

        Assert.Same(newerStatus, MissedRead(tolerance, Mouse, AtCapMs));
    }

    [Fact]
    public void A_failed_read_drops_the_kept_answer()
    {
        var tolerance = ToleranceWithMouseAnswered();
        tolerance.Failed(Mouse);

        Assert.Null(MissedRead(tolerance, Mouse, OneReadLaterMs));
    }

    [Fact]
    public void A_forgotten_device_keeps_no_old_status()
    {
        var tolerance = ToleranceWithMouseAnswered();
        tolerance.Forget(Mouse);

        Assert.Null(MissedRead(tolerance, Mouse, OneReadLaterMs));
    }

    [Fact]
    public void Forgetting_one_device_keeps_the_others()
    {
        var tolerance = ToleranceWithMouseAnswered();
        tolerance.Answered(Keyboard, KeyboardStatus, AnsweredAtMs);
        tolerance.Forget(Mouse);

        Assert.Same(KeyboardStatus, MissedRead(tolerance, Keyboard, OneReadLaterMs));
    }

    [Fact]
    public void A_link_down_forgets_the_device_so_a_later_missed_read_keeps_nothing()
    {
        var tolerance = ToleranceWithMouseAnswered();
        MissedRead(tolerance, Mouse, OneReadLaterMs, linkWentDown: true);

        Assert.Null(MissedRead(tolerance, Mouse, TwoReadsLaterMs));
    }

    [Fact]
    public void Clearing_forgets_every_device()
    {
        var tolerance = ToleranceWithMouseAnswered();
        tolerance.Answered(Keyboard, KeyboardStatus, AnsweredAtMs);
        tolerance.Clear();

        Assert.Null(MissedRead(tolerance, Mouse, OneReadLaterMs));
        Assert.Null(MissedRead(tolerance, Keyboard, OneReadLaterMs));
    }

    [Fact]
    public void Devices_have_their_own_cap()
    {
        var tolerance = ToleranceWithMouseAnswered();
        tolerance.Answered(Keyboard, KeyboardStatus, AtCapMs);

        Assert.Null(MissedRead(tolerance, Mouse, AtCapMs));
        Assert.Same(KeyboardStatus, MissedRead(tolerance, Keyboard, AtCapMs));
    }
}
