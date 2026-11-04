using HidSharp;

namespace LogitechBattery;

/// <summary>A HID collection with its first top-level usage (page&lt;&lt;16|usage, 0 if unknown) and max output report length.</summary>
internal readonly record struct HidCollection(HidDevice Dev, uint Usage, int OutLen);

/// <summary>A physical Logitech device exposing HID++: its vendor collections and the 20-byte long one.</summary>
internal sealed record HidPpDevice(string Key, List<HidDevice> Collections, HidDevice Long);

/// <summary>Finds HID++ capable devices; shared by the probe and the battery provider.</summary>
internal static class HidPpDevices
{
    public const int LogitechVid = 0x046D;

    /// <summary>Reads usage and output length of a collection; failures yield 0.</summary>
    public static HidCollection Describe(HidDevice d)
    {
        uint first = 0;
        int outLen = 0;
        try { first = d.GetReportDescriptor().DeviceItems.SelectMany(i => i.Usages.GetAllValues()).FirstOrDefault(); } catch { }
        try { outLen = d.GetMaxOutputReportLength(); } catch { }
        return new HidCollection(d, first, outLen);
    }

    // Everything before "&col" identifies the physical device.
    public static string GroupKey(string path)
    {
        var i = path.IndexOf("&col", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? path[..i] : path;
    }

    /// <summary>
    /// HID++ lives on a vendor usage page (usually 0xFF00, newer devices may differ):
    /// long collection = 20-byte reports, short = 7-byte.
    /// </summary>
    public static IEnumerable<HidPpDevice> Group(IEnumerable<HidCollection> collections)
    {
        foreach (var group in collections.GroupBy(c => GroupKey(c.Dev.DevicePath)))
        {
            var vendor = group.Where(c => c.Usage >> 16 >= 0xFF00).ToList();
            var longs = vendor.Where(c => c.OutLen == 20).ToList();
            if (longs.Count > 0)
                yield return new HidPpDevice(group.Key, vendor.Select(c => c.Dev).ToList(), longs[0].Dev);
        }
    }

    public static List<HidPpDevice> Find() =>
        Group(DeviceList.Local.GetHidDevices(LogitechVid).Select(Describe)).ToList();
}
