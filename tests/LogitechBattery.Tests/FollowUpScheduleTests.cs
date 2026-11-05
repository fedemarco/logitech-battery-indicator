using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class FollowUpScheduleTests
{
    private const int FirstDelayMs = 1000;
    private const int SecondDelayMs = 5000;
    private const int StepCount = 25;

    private static BatteryStatus Status(ConnectionState connection) =>
        new(connection, connection == ConnectionState.Connected ? 80 : null, ChargingState.Unknown, DateTimeOffset.Now);

    private static FollowUpSchedule StartedSchedule(out int generation)
    {
        var schedule = new FollowUpSchedule();
        schedule.Restart();
        generation = schedule.Generation;
        return schedule;
    }

    [Fact]
    public void Restart_returns_the_first_delay() =>
        Assert.Equal(FirstDelayMs, new FollowUpSchedule().Restart());

    [Fact]
    public void A_connected_read_stops_the_follow_up()
    {
        var schedule = StartedSchedule(out var generation);

        Assert.Null(schedule.Finish(generation, ran: true, Status(ConnectionState.Connected)));
    }

    [Fact]
    public void A_skipped_read_retries_the_same_step()
    {
        var schedule = StartedSchedule(out var generation);

        Assert.Equal(FirstDelayMs, schedule.Finish(generation, ran: false, status: null));
    }

    [Theory]
    [InlineData(ConnectionState.Asleep)]
    [InlineData(ConnectionState.NotFound)]
    public void A_read_without_a_connected_mouse_advances_to_the_next_step(ConnectionState connection)
    {
        var schedule = StartedSchedule(out var generation);

        Assert.Equal(SecondDelayMs, schedule.Finish(generation, ran: true, Status(connection)));
    }

    [Fact]
    public void A_failed_read_advances_to_the_next_step()
    {
        var schedule = StartedSchedule(out var generation);

        Assert.Equal(SecondDelayMs, schedule.Finish(generation, ran: true, status: null));
    }

    [Fact]
    public void Past_the_last_step_the_follow_up_stops()
    {
        var schedule = StartedSchedule(out var generation);

        for (var step = 1; step < StepCount; step++)
            Assert.NotNull(schedule.Finish(generation, ran: true, status: null));

        Assert.Null(schedule.Finish(generation, ran: true, status: null));
    }

    [Fact]
    public void A_restart_during_a_read_wins_over_the_outcome_of_that_read()
    {
        var schedule = StartedSchedule(out var generationBeforeRead);
        schedule.Restart();

        Assert.Null(schedule.Finish(generationBeforeRead, ran: true, Status(ConnectionState.NotFound)));
        Assert.Equal(SecondDelayMs, schedule.Finish(schedule.Generation, ran: true, status: null));
    }
}
