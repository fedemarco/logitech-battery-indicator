namespace LogitechBattery.Tests;

public class ReportedLinkDownsTests
{
    private static readonly Slot FirstSlot = new("receiver", 1);
    private static readonly Slot SecondSlot = new("receiver", 2);

    [Fact]
    public void A_recorded_link_down_is_taken_once()
    {
        var linkDowns = new ReportedLinkDowns();
        linkDowns.Record(FirstSlot);

        Assert.True(linkDowns.Take(FirstSlot));
        Assert.False(linkDowns.Take(FirstSlot));
    }

    [Fact]
    public void A_link_down_is_taken_only_for_its_own_slot()
    {
        var linkDowns = new ReportedLinkDowns();
        linkDowns.Record(FirstSlot);

        Assert.False(linkDowns.Take(SecondSlot));
    }

    [Fact]
    public void Link_downs_recorded_on_many_threads_at_once_are_all_taken()
    {
        const int slotCount = 200;
        var linkDowns = new ReportedLinkDowns();
        var slots = Enumerable.Range(0, slotCount).Select(index => new Slot("receiver", (byte)index)).ToList();

        Parallel.ForEach(slots, linkDowns.Record);

        Assert.All(slots, slot => Assert.True(linkDowns.Take(slot)));
    }

    [Fact]
    public void A_forgotten_link_down_is_not_taken()
    {
        var linkDowns = new ReportedLinkDowns();
        linkDowns.Record(FirstSlot);

        linkDowns.Forget(FirstSlot);

        Assert.False(linkDowns.Take(FirstSlot));
    }

    [Fact]
    public void Clearing_drops_every_recorded_link_down()
    {
        var linkDowns = new ReportedLinkDowns();
        linkDowns.Record(FirstSlot);
        linkDowns.Record(SecondSlot);

        linkDowns.Clear();

        Assert.False(linkDowns.Take(FirstSlot));
        Assert.False(linkDowns.Take(SecondSlot));
    }
}
