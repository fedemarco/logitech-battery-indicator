using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class FollowUpScheduleTests
{
    private const int FirstDelayMs = 1000;
    private const int SecondDelayMs = 5000;

    private static FollowUpSchedule StartedSchedule(out int generation)
    {
        var schedule = new FollowUpSchedule();
        schedule.Restart();
        generation = schedule.Generation;
        return schedule;
    }

    [Fact]
    public void A_read_with_every_expected_device_connected_stops_the_follow_up()
    {
        var schedule = StartedSchedule(out var generation);

        Assert.Null(schedule.Finish(generation, ran: true, allExpectedDevicesConnected: true));
    }

    [Fact]
    public void A_skipped_read_retries_the_same_step()
    {
        var schedule = StartedSchedule(out var generation);

        Assert.Equal(FirstDelayMs, schedule.Finish(generation, ran: false, allExpectedDevicesConnected: false));
    }

    [Fact]
    public void A_read_with_a_device_not_connected_advances_to_the_next_step()
    {
        var schedule = StartedSchedule(out var generation);

        Assert.Equal(SecondDelayMs, schedule.Finish(generation, ran: true, allExpectedDevicesConnected: false));
    }

    [Fact]
    public void A_device_that_never_connects_is_followed_up_on_the_whole_schedule()
    {
        // 1 s, then one minute each of 5 s, 10 s, 15 s and 30 s delays
        int[] OneMinuteOf(int delayMs) => Enumerable.Repeat(delayMs, 60_000 / delayMs).ToArray();
        int[] expectedDelaysMs = [FirstDelayMs, .. OneMinuteOf(5000), .. OneMinuteOf(10_000), .. OneMinuteOf(15_000), .. OneMinuteOf(30_000)];

        Assert.Equal(expectedDelaysMs, DelaysUntilTheFollowUpStops());
    }

    private static List<int> DelaysUntilTheFollowUpStops()
    {
        var schedule = new FollowUpSchedule();
        var delays = new List<int> { schedule.Restart() };
        while (schedule.Finish(schedule.Generation, ran: true, allExpectedDevicesConnected: false) is { } delay)
            delays.Add(delay);
        return delays;
    }

    [Fact]
    public void A_restart_during_a_read_wins_over_the_outcome_of_that_read()
    {
        var schedule = StartedSchedule(out var generationBeforeRead);
        schedule.Restart();

        Assert.Null(schedule.Finish(generationBeforeRead, ran: true, allExpectedDevicesConnected: false));
        Assert.Equal(SecondDelayMs, schedule.Finish(schedule.Generation, ran: true, allExpectedDevicesConnected: false));
    }
}
