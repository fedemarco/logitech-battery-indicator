namespace LogitechBattery.Battery;

/// <summary>The names the tray and the notifications show for devices: clean, short and distinct from each other.</summary>
public static class DeviceNames
{
    public const int MaxLength = 32;

    private const int IdSuffixLength = 4;

    public static IReadOnlyDictionary<DeviceId, string> Display(IReadOnlyList<DeviceInfo> devices)
    {
        var shortNames = devices.ToDictionary(device => device.Id, device => Clean(device.Name, MaxLength));
        var nameCounts = shortNames.Values.GroupBy(name => name).ToDictionary(group => group.Key, group => group.Count());

        return shortNames.ToDictionary(
            entry => entry.Key,
            entry => DisplayNameOf(entry.Key, entry.Value, nameCounts[entry.Value]));
    }

    /// <summary>The device sends the name; control characters (a stray NUL, a newline) must not reach the tooltip, a notification or a settings line.</summary>
    internal static string Clean(string name, int maxLength)
    {
        var withoutControls = new string(name.Where(character => !char.IsControl(character)).ToArray()).Trim();
        var cut = withoutControls.Length <= maxLength ? withoutControls : withoutControls[..maxLength];
        // A cut inside a surrogate pair would leave an invalid character.
        return cut.Length > 0 && char.IsHighSurrogate(cut[^1]) ? cut[..^1] : cut;
    }

    private static string DisplayNameOf(DeviceId id, string shortName, int sameNameCount)
    {
        if (shortName.Length == 0)
            return $"{DeviceInfo.UnnamedDeviceName} ({IdSuffix(id)})";
        if (sameNameCount > 1)
            return $"{shortName} ({IdSuffix(id)})";
        return shortName;
    }

    private static string IdSuffix(DeviceId id)
    {
        var text = id.ToString();
        return text[^IdSuffixLength..];
    }
}
