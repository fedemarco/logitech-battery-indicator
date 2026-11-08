using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class StuckReadWatchTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);
    private static readonly long LimitMs = (long)Limit.TotalMilliseconds;
    private const long ReadStartedAtMs = 1_000;
    private const long LaterReadStartedAtMs = 500_000;

    private static StuckReadWatch WatchWithReadStartedAt(long startedAtMs)
    {
        var watch = new StuckReadWatch(Limit);
        watch.ReadStarted(startedAtMs);
        return watch;
    }

    [Fact]
    public void Nothing_is_reported_while_no_read_is_running() =>
        Assert.False(new StuckReadWatch(Limit).ShouldReport(nowMs: ReadStartedAtMs + 10 * LimitMs));

    [Fact]
    public void A_read_inside_the_limit_is_not_reported() =>
        Assert.False(WatchWithReadStartedAt(ReadStartedAtMs).ShouldReport(ReadStartedAtMs + LimitMs - 1));

    [Fact]
    public void A_read_that_reaches_the_limit_is_reported() =>
        Assert.True(WatchWithReadStartedAt(ReadStartedAtMs).ShouldReport(ReadStartedAtMs + LimitMs));

    [Fact]
    public void A_stuck_read_is_reported_only_once()
    {
        var watch = WatchWithReadStartedAt(ReadStartedAtMs);
        watch.ShouldReport(ReadStartedAtMs + LimitMs);

        Assert.False(watch.ShouldReport(ReadStartedAtMs + 2 * LimitMs));
    }

    [Fact]
    public void A_finished_read_is_not_reported()
    {
        var watch = WatchWithReadStartedAt(ReadStartedAtMs);
        watch.ReadFinished();

        Assert.False(watch.ShouldReport(ReadStartedAtMs + 10 * LimitMs));
    }

    [Fact]
    public void The_next_read_is_watched_again_after_a_report()
    {
        var watch = WatchWithReadStartedAt(ReadStartedAtMs);
        watch.ShouldReport(ReadStartedAtMs + LimitMs);
        watch.ReadFinished();

        watch.ReadStarted(LaterReadStartedAtMs);

        Assert.True(watch.ShouldReport(LaterReadStartedAtMs + LimitMs));
    }
}
