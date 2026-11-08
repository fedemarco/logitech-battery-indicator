using System.Text;
using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class DeviceNameTests
{
    private const int ChunkLength = 16;

    private static byte[] Ascii(string text, int paddedTo = ChunkLength)
    {
        var chunk = new byte[paddedTo];
        Encoding.ASCII.GetBytes(text).CopyTo(chunk, 0);
        return chunk;
    }

    [Fact]
    public void Chunks_are_joined_into_the_device_name() =>
        Assert.Equal("PRO X2 SUPERSTRIKE", DeviceName.Assemble(18, [Ascii("PRO X2 SUPERSTRI"), Ascii("KE")]));

    [Fact]
    public void Name_is_cut_to_the_expected_length() =>
        Assert.Equal("AB", DeviceName.Assemble(2, [Ascii("ABC")]));

    [Fact]
    public void Nul_padding_after_the_name_is_removed() =>
        Assert.Equal("MX", DeviceName.Assemble(ChunkLength, [Ascii("MX")]));

    [Fact]
    public void Reading_chunks_asks_for_each_16_char_position_until_the_length_is_covered()
    {
        var positions = new List<byte>();

        var chunks = DeviceName.ReadChunks(33, position => { positions.Add(position); return Ascii("x"); });

        Assert.Equal([0, 16, 32], positions);
        Assert.Equal(3, chunks.Count);
        Assert.True(DeviceName.IsComplete(33, chunks));
    }

    [Fact]
    public void Reading_chunks_stops_at_the_first_unreadable_one()
    {
        var chunks = DeviceName.ReadChunks(40, position => position == 0 ? Ascii("x") : null);

        Assert.Single(chunks);
        Assert.False(DeviceName.IsComplete(40, chunks));
    }

    [Fact]
    public void Reported_length_is_capped_at_64() =>
        Assert.Equal([64, 10], [DeviceName.LimitLength(200), DeviceName.LimitLength(10)]);
}
