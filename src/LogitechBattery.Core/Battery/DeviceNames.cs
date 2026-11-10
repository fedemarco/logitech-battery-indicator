namespace LogitechBattery.Battery;

/// <summary>The names the tray and the notifications show for devices: clean, short and distinct from each other.</summary>
public static class DeviceNames
{
    public const int MaxLength = 32;

    private const int IdSuffixLength = 4;
    private const string UnnamedDevice = "Logitech device";

    public static IReadOnlyDictionary<DeviceId, string> Display(IReadOnlyList<DeviceInfo> devices)
    {
        var shortNames = devices.ToDictionary(device => device.Id, device => ShortName(device.Name));
        var nameCounts = shortNames.Values.Where(name => name.Length > 0).GroupBy(name => name).ToDictionary(group => group.Key, group => group.Count());

        return shortNames.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Length == 0 ? $"{UnnamedDevice} ({IdSuffix(entry.Key)})"
                : nameCounts[entry.Value] > 1 ? $"{entry.Value} ({IdSuffix(entry.Key)})"
                : entry.Value);
    }

    // The device sends the name; control characters (a stray NUL, a newline) must not reach the tooltip or a notification.
    private static string ShortName(string name)
    {
        var printable = new string(name.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return printable.Length > MaxLength ? printable[..MaxLength] : printable;
    }

    private static string IdSuffix(DeviceId id)
    {
        var text = id.ToString();
        return text[^IdSuffixLength..];
    }
}
