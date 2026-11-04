using System.Text;

namespace LogitechBattery.HidPp;

/// <summary>HID++ 2.0 feature 0x0005 (DeviceName).</summary>
internal static class DeviceName
{
    public const ushort FeatureId = 0x0005;
    public const int GetCountFunction = 0;
    public const int GetNameFunction = 1;

    private const int MaxLength = 64;
    private const int ChunkLength = 16;

    /// <summary>Caps a reported name length at what we are willing to read.</summary>
    public static int LimitLength(int reportedLength) => Math.Min(reportedLength, MaxLength);

    /// <summary>Reads the getName chunks covering <paramref name="length"/> chars; stops early when a chunk cannot be read.</summary>
    public static List<byte[]> ReadChunks(int length, Func<byte, byte[]?> readChunk)
    {
        var chunks = new List<byte[]>();
        for (var position = 0; position < length; position += ChunkLength)
        {
            if (readChunk((byte)position) is not { } chunk)
                break;
            chunks.Add(chunk);
        }
        return chunks;
    }

    /// <summary>Tells whether <paramref name="chunks"/> cover a name of <paramref name="length"/> chars.</summary>
    public static bool IsComplete(int length, List<byte[]> chunks) => chunks.Count * ChunkLength >= length;

    /// <summary>Joins getName chunks (the params of each response) into the name of <paramref name="expectedLength"/> chars, dropping NUL padding.</summary>
    public static string Assemble(int expectedLength, IEnumerable<byte[]> chunks)
    {
        var name = new StringBuilder();
        foreach (var chunk in chunks)
            name.Append(Encoding.ASCII.GetString(chunk));
        if (name.Length > expectedLength)
            name.Length = expectedLength;
        return name.ToString().TrimEnd('\0');
    }
}
