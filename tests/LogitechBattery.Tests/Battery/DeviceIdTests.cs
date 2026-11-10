using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class DeviceIdTests
{
    [Theory]
    [InlineData("62C31634")]
    [InlineData("00000001")]
    [InlineData("RC54DS01")]
    public void Valid_ids_are_accepted_and_keep_their_text(string text)
    {
        Assert.True(DeviceId.TryParse(text, out var id));
        Assert.Equal(text, id.ToString());
    }

    [Theory]
    [InlineData("62c31634")] // lowercase is rejected, not normalized: the id must be the same text everywhere
    [InlineData("62C3163")]
    [InlineData("62C316345")]
    [InlineData("62C3163G")]
    [InlineData("00000000")]
    [InlineData("FFFFFFFF")]
    [InlineData("RC54D01")]
    [InlineData("rC54DS01")]
    [InlineData("RC54DS0G")]
    [InlineData("..\\evil")]
    [InlineData("62C31634\n")] // `$` would match before a trailing newline
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_ids_are_rejected(string? text)
    {
        Assert.False(DeviceId.TryParse(text, out var id));
        Assert.Null(id);
    }

    [Fact]
    public void Route_fallback_from_product_and_slot_round_trips_through_parse()
    {
        var fallback = DeviceId.FromRoute(0xC54D, 0x01);

        Assert.Equal("RC54DS01", fallback.ToString());
        Assert.True(DeviceId.TryParse(fallback.ToString(), out var parsed));
        Assert.Equal(fallback, parsed);
    }

    [Fact]
    public void Only_a_route_id_is_a_route_fallback()
    {
        Assert.True(DeviceId.FromRoute(0xC54D, 1).IsRouteFallback);
        Assert.False(DeviceId.Parse("62C31634").IsRouteFallback);
    }
}
