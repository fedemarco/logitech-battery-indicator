using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class StuckReadWatchTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);
    private const long LimitMs = 60_000;

    private static StuckReadWatch WatchWithReadStartedAt(long startedAtMs)
    {
        var watch = new StuckReadWatch(Limit);
        watch.ReadStarted(startedAtMs);
        return watch;
    }

    [Fact]
    public void Nothing_is_reported_while_no_read_is_running() =>
        Assert.False(new StuckReadWatch(Limit).ShouldReport(nowMs: 1_000_000));

    [Fact]
    public void A_read_inside_the_limit_is_not_reported() =>
        Assert.False(WatchWithReadStartedAt(1_000).ShouldReport(1_000 + LimitMs - 1));

    [Fact]
    public void A_read_that_reaches_the_limit_is_reported() =>
        Assert.True(WatchWithReadStartedAt(1_000).ShouldReport(1_000 + LimitMs));

    [Fact]
    public void A_stuck_read_is_reported_only_once()
    {
        var watch = WatchWithReadStartedAt(1_000);
        watch.ShouldReport(1_000 + LimitMs);

        Assert.False(watch.ShouldReport(1_000 + 2 * LimitMs));
    }

    [Fact]
    public void A_finished_read_is_not_reported()
    {
        var watch = WatchWithReadStartedAt(1_000);
        watch.ReadFinished();

        Assert.False(watch.ShouldReport(1_000 + 10 * LimitMs));
    }

    [Fact]
    public void The_next_read_is_watched_again_after_a_report()
    {
        var watch = WatchWithReadStartedAt(1_000);
        watch.ShouldReport(1_000 + LimitMs);
        watch.ReadFinished();

        watch.ReadStarted(500_000);

        Assert.True(watch.ShouldReport(500_000 + LimitMs));
    }
}
