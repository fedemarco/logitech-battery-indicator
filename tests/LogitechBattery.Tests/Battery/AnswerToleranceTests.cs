using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class AnswerToleranceTests
{
    private static readonly DeviceId Mouse = DeviceId.FromRoute(0xC54D, 1);
    private static readonly DeviceId Keyboard = DeviceId.FromRoute(0xC54D, 2);
    private static readonly BatteryStatus MouseStatus = TestData.Status(percent: 80);
    private static readonly BatteryStatus KeyboardStatus = TestData.Status(percent: 40);

    [Fact]
    public void One_missed_read_keeps_the_last_status()
    {
        var tolerance = new AnswerTolerance();
        tolerance.Answered(Mouse, MouseStatus);

        Assert.Same(MouseStatus, tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false));
    }

    [Fact]
    public void Two_missed_reads_in_a_row_are_not_answering()
    {
        var tolerance = new AnswerTolerance();
        tolerance.Answered(Mouse, MouseStatus);
        tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false);
        tolerance.StartRead();

        Assert.Null(tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false));
    }

    [Fact]
    public void An_answer_between_two_missed_reads_resets_the_count()
    {
        var tolerance = new AnswerTolerance();
        var newerStatus = TestData.Status(percent: 79);
        tolerance.Answered(Mouse, MouseStatus);
        tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false);
        tolerance.Answered(Mouse, newerStatus);

        Assert.Same(newerStatus, tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false));
    }

    [Fact]
    public void A_link_down_makes_the_first_missed_read_not_answering()
    {
        var tolerance = new AnswerTolerance();
        tolerance.Answered(Mouse, MouseStatus);

        Assert.Null(tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: true));
    }

    [Fact]
    public void A_device_that_never_answered_is_not_answering_at_the_first_missed_read()
    {
        var tolerance = new AnswerTolerance();

        Assert.Null(tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false));
    }

    [Fact]
    public void A_failed_read_resets_the_count()
    {
        var tolerance = new AnswerTolerance();
        tolerance.Answered(Mouse, MouseStatus);
        tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false);
        tolerance.Failed(Mouse);

        Assert.Null(tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false)); // nothing to keep after a failure
    }

    [Fact]
    public void Devices_are_counted_separately()
    {
        var tolerance = new AnswerTolerance();
        tolerance.Answered(Mouse, MouseStatus);
        tolerance.Answered(Keyboard, KeyboardStatus);
        tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false);

        tolerance.StartRead();

        Assert.Same(KeyboardStatus, tolerance.StatusToKeepAfterMissedRead(Keyboard, linkWentDown: false));
        Assert.Null(tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false));
    }

    [Fact]
    public void Two_passes_of_one_read_count_as_one_missed_read()
    {
        var tolerance = new AnswerTolerance();
        tolerance.Answered(Mouse, MouseStatus);
        tolerance.StartRead();
        tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false);

        Assert.Same(MouseStatus, tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false));
    }

    [Fact]
    public void A_missed_read_in_each_of_two_reads_is_not_answering()
    {
        var tolerance = new AnswerTolerance();
        tolerance.Answered(Mouse, MouseStatus);
        tolerance.StartRead();
        tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false);
        tolerance.StartRead();

        Assert.Null(tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false));
    }

    [Fact]
    public void A_forgotten_device_keeps_no_old_status()
    {
        var tolerance = new AnswerTolerance();
        tolerance.Answered(Mouse, MouseStatus);
        tolerance.Forget(Mouse);

        Assert.Null(tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false));
    }

    [Fact]
    public void Forgetting_one_device_keeps_the_others()
    {
        var tolerance = new AnswerTolerance();
        tolerance.Answered(Mouse, MouseStatus);
        tolerance.Answered(Keyboard, KeyboardStatus);
        tolerance.Forget(Mouse);

        Assert.Same(KeyboardStatus, tolerance.StatusToKeepAfterMissedRead(Keyboard, linkWentDown: false));
    }

    [Fact]
    public void Clearing_forgets_every_device()
    {
        var tolerance = new AnswerTolerance();
        tolerance.Answered(Mouse, MouseStatus);
        tolerance.Answered(Keyboard, KeyboardStatus);
        tolerance.Clear();

        Assert.Null(tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false));
        Assert.Null(tolerance.StatusToKeepAfterMissedRead(Keyboard, linkWentDown: false));
    }

    [Fact]
    public void A_device_found_again_after_not_answering_gets_its_full_tolerance()
    {
        var tolerance = new AnswerTolerance();
        tolerance.Answered(Mouse, MouseStatus);
        tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: true);
        tolerance.Answered(Mouse, MouseStatus);

        Assert.Same(MouseStatus, tolerance.StatusToKeepAfterMissedRead(Mouse, linkWentDown: false));
    }
}
