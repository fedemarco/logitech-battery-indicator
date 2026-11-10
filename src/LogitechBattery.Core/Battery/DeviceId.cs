using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace LogitechBattery.Battery;

/// <summary>
/// Identity of a device, safe to use in file names and settings keys. Either the HID++ unit ID (8 uppercase hex characters)
/// or, when the device has none, the route fallback <c>R&lt;PID 4 hex&gt;S&lt;slot 2 hex&gt;</c>.
/// </summary>
public sealed partial record DeviceId
{
    private const string AllZeroUnitId = "00000000";
    private const string AllOnesUnitId = "FFFFFFFF";

    [GeneratedRegex("^([0-9A-F]{8}|R[0-9A-F]{4}S[0-9A-F]{2})$")]
    private static partial Regex ValidForm();

    private readonly string _text;

    private DeviceId(string text) => _text = text;

    /// <summary>Accepts only the two valid forms, in uppercase; an all-zero or all-F unit ID is not a real one.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out DeviceId? id)
    {
        id = null;
        if (text is null || !ValidForm().IsMatch(text) || text is AllZeroUnitId or AllOnesUnitId)
            return false;

        id = new DeviceId(text);
        return true;
    }

    public static DeviceId Parse(string text) =>
        TryParse(text, out var id) ? id : throw new FormatException($"Not a device ID: \"{text}\"");

    public static DeviceId FromRoute(int productId, byte deviceIndex) => new($"R{productId:X4}S{deviceIndex:X2}");

    public override string ToString() => _text;
}
