using HidSharp;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>A HID collection with its first top-level usage (page&lt;&lt;16|usage, 0 if unknown) and max output report length.</summary>
internal readonly record struct HidCollection(HidDevice Device, uint Usage, int OutputReportLength);

/// <summary>A physical Logitech device exposing HID++: its 20-byte long collection and its 7-byte short one (if any).</summary>
internal sealed record HidPpDevice(string Key, HidDevice LongCollection, HidDevice? ShortCollection);

/// <summary>Finds HID++ capable devices; shared by the probe and the battery provider.</summary>
internal static class HidPpDevices
{
    public const int LogitechVid = 0x046D;

    /// <summary>HID++ lives on a vendor-defined usage page: usually 0xFF00, newer devices may use a higher one.</summary>
    private const uint VendorPageMinimum = 0xFF00;

    public static uint UsagePage(uint usage) => usage >> 16;

    /// <summary>All top-level usages (page&lt;&lt;16|usage) of a collection. Throws if the report descriptor cannot be read.</summary>
    public static List<uint> ReadUsages(HidDevice device) =>
        device.GetReportDescriptor().DeviceItems.SelectMany(item => item.Usages.GetAllValues()).ToList();

    /// <summary>Reads usage and output length of a collection; what cannot be read yields 0.</summary>
    public static HidCollection Describe(HidDevice device)
    {
        var firstUsage = OrDefault(() => ReadUsages(device).FirstOrDefault());
        var outputReportLength = OrDefault(device.GetMaxOutputReportLength);
        return new HidCollection(device, firstUsage, outputReportLength);
    }

    // Everything before "&col" identifies the physical device.
    public static string GroupKey(string path)
    {
        var collectionSuffixStart = path.IndexOf("&col", StringComparison.OrdinalIgnoreCase);
        return collectionSuffixStart > 0 ? path[..collectionSuffixStart] : path;
    }

    /// <summary>Groups collections per physical device; long collection = 20-byte reports, short = 7-byte.</summary>
    public static IEnumerable<HidPpDevice> Group(IEnumerable<HidCollection> collections)
    {
        foreach (var group in collections.GroupBy(collection => GroupKey(collection.Device.DevicePath)))
        {
            var vendor = group.Where(collection => UsagePage(collection.Usage) >= VendorPageMinimum).ToList();
            var longDevice = FirstWithOutputLength(vendor, HidPpMessage.LongLength);
            if (longDevice is not null)
                yield return new HidPpDevice(group.Key, longDevice, FirstWithOutputLength(vendor, HidPpMessage.ShortLength));
        }
    }

    private static HidDevice? FirstWithOutputLength(IEnumerable<HidCollection> collections, int outputReportLength) =>
        collections.Where(collection => collection.OutputReportLength == outputReportLength).Select(collection => collection.Device).FirstOrDefault();

    public static List<HidPpDevice> Find() =>
        Group(DeviceList.Local.GetHidDevices(LogitechVid).Select(Describe)).ToList();

    // A collection that cannot be queried is treated as unknown (0), not as an error.
    private static T? OrDefault<T>(Func<T> read)
    {
        try { return read(); }
        catch { return default; }
    }
}
