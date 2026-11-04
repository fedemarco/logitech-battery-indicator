using System.Text;

namespace LogitechBattery.HidPp;

/// <summary>HID++ 2.0 feature 0x0005 (DeviceName).</summary>
internal static class DeviceName
{
    public const ushort FeatureId = 0x0005;
    public const int GetCountFunction = 0;
    public const int GetNameFunction = 1;

    /// <summary>Joins getName chunks (the params of each response) into the name of <paramref name="length"/> chars, dropping NUL padding.</summary>
    public static string Assemble(int length, IEnumerable<byte[]> chunks)
    {
        var sb = new StringBuilder();
        foreach (var chunk in chunks)
            sb.Append(Encoding.ASCII.GetString(chunk));
        if (sb.Length > length)
            sb.Length = length;
        return sb.ToString().TrimEnd('\0');
    }
}
