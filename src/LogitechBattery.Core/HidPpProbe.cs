using System.Reflection;
using System.Text;
using HidSharp;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>
/// One-off diagnostic (<c>--probe</c>): lists Logitech HID collections and sends read-only HID++ 2.0 queries,
/// logging every report as hex. Writes %LOCALAPPDATA%\LogitechBattery\probe.txt.
/// </summary>
public sealed class HidPpProbe
{
    private const string ReportFileName = "probe.txt";
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(1);

    /// <summary>A device index on an open channel.</summary>
    private readonly record struct DeviceLink(HidPpChannel Channel, byte DeviceIndex);

    private readonly StringBuilder _log = new();

    /// <summary>Runs the probe and returns the path of the report file.</summary>
    public static string Run()
    {
        var probe = new HidPpProbe();
        try { probe.Execute(); }
        catch (Exception ex) { probe.Log($"FATAL: {ex}"); }

        Directory.CreateDirectory(AppPaths.DataDirectory);
        var file = Path.Combine(AppPaths.DataDirectory, ReportFileName);
        File.WriteAllText(file, probe._log.ToString());
        return file;
    }

    private void Log(string line) => _log.AppendLine(line);

    private void Execute()
    {
        Log($"LogitechBattery probe {AppVersion.InformationalVersionOf(Assembly.GetExecutingAssembly())}");
        Log($"{DateTimeOffset.Now:O}  {Environment.OSVersion}");
        Log("");

        var devices = DeviceList.Local.GetHidDevices(HidPpDevices.LogitechVid).ToList();
        Log($"== HID collections (VID 046D): {devices.Count}");
        var collections = devices.Select(DescribeCollection).ToList();

        foreach (var device in HidPpDevices.Group(collections))
        {
            Log("");
            Log($"== Physical device {device.Key}");
            try { ProbeDevice(device); }
            catch (Exception ex) { Log($"ERROR probing device: {ex.Message}"); }
        }
    }

    /// <summary>Logs one collection and returns it with its first usage and max output length.</summary>
    private HidCollection DescribeCollection(HidDevice device)
    {
        var collection = HidPpDevices.Describe(device);
        var maxInputLength = 0;
        try { maxInputLength = device.GetMaxInputReportLength(); } catch { }
        Log($"PID {device.ProductID:X4}  \"{ProductNameOrError(device)}\"  usage(page:usage) {UsagesOrError(device)}  maxIn {maxInputLength}  maxOut {collection.OutputReportLength}");
        Log($"    {device.DevicePath}");
        return collection;
    }

    private static string ProductNameOrError(HidDevice device)
    {
        try { return device.GetProductName(); }
        catch (Exception ex) { return $"<error: {ex.Message}>"; }
    }

    private static string UsagesOrError(HidDevice device)
    {
        try { return string.Join(", ", HidPpDevices.ReadUsages(device).Select(usage => $"{HidPpDevices.UsagePage(usage):X4}:{usage & 0xFFFF:X4}")); }
        catch (Exception ex) { return $"<error: {ex.Message}>"; }
    }

    private void ProbeDevice(HidPpDevice device)
    {
        if (HidPpChannel.Open(device, onReport: (_, _) => { }) is not { } channel)
        {
            Log($"cannot open {device.LongCollection.DevicePath}");
            return;
        }

        using (channel)
        {
            foreach (var deviceIndex in HidPpMessage.CableDeviceIndexes.Concat(HidPpMessage.ReceiverDeviceIndexes))
            {
                Log("");
                Log($"-- devIdx {deviceIndex:X2}");
                try { ProbeIndex(new DeviceLink(channel, deviceIndex)); }
                catch (Exception ex) { Log($"ERROR devIdx {deviceIndex:X2}: {ex.Message}"); }
            }
        }
    }

    private void ProbeIndex(DeviceLink link)
    {
        var ping = Request(link, HidPpMessage.BuildPing(link.DeviceIndex));
        if (ping is null)
        {
            Log("  => no HID++ 2.0 answer");
            return;
        }
        var pingParams = HidPpMessage.ParamsOf(ping);
        Log($"  protocol {pingParams[0]}.{pingParams[1]} (ping echo {(pingParams[2] == HidPpMessage.PingEcho ? "ok" : "mismatch")})");

        if (DescribeFeature(link, DeviceName.FeatureId) is { } nameFeatureIndex)
            ReportDeviceName(link, nameFeatureIndex);
        if (DescribeFeature(link, UnifiedBattery.FeatureId) is { } batteryFeatureIndex)
            ReportUnifiedBattery(link, batteryFeatureIndex);
    }

    /// <summary>Logs whether the device supports <paramref name="featureId"/>; returns its feature index if so.</summary>
    private byte? DescribeFeature(DeviceLink link, ushort featureId)
    {
        var response = Request(link, HidPpMessage.BuildGetFeature(link.DeviceIndex, featureId));
        if (response is null)
        {
            Log($"  feature {featureId:X4}: no answer");
            return null;
        }
        var featureParams = HidPpMessage.ParamsOf(response);
        var (featureIndex, featureType, featureVersion) = (featureParams[0], featureParams[1], featureParams[2]);
        if (featureIndex == 0)
        {
            Log($"  feature {featureId:X4}: not supported");
            return null;
        }
        Log($"  feature {featureId:X4}: index {featureIndex:X2} type {featureType:X2} version {featureVersion}");
        return featureIndex;
    }

    private void ReportDeviceName(DeviceLink link, byte featureIndex)
    {
        var count = Call(link, featureIndex, DeviceName.GetCountFunction) is { } response ? HidPpMessage.ParamsOf(response)[0] : 0;
        if (count == 0)
            return;

        var length = DeviceName.LimitLength(count);
        var chunks = DeviceName.ReadChunks(length, position =>
            Call(link, featureIndex, DeviceName.GetNameFunction, position) is { } chunk ? HidPpMessage.ParamsOf(chunk) : null);
        Log($"  device name: \"{DeviceName.Assemble(length, chunks)}\"");
    }

    private void ReportUnifiedBattery(DeviceLink link, byte featureIndex)
    {
        if (Call(link, featureIndex, UnifiedBattery.GetCapabilitiesFunction) is { } capabilities)
            Log($"  UnifiedBattery capabilities params: {HidPpMessage.ToHex(HidPpMessage.ParamsOf(capabilities)[..6])}");
        if (Call(link, featureIndex, UnifiedBattery.GetStatusFunction) is { } status)
        {
            var p = HidPpMessage.ParamsOf(status);
            Log($"  UnifiedBattery status: {p[0]}% levelFlags {p[1]:X2} charging {p[2]} externalPower {p[3]}");
        }
    }

    private byte[]? Call(DeviceLink link, byte featureIndex, int function, params byte[] parameters) =>
        Request(link, HidPpMessage.BuildRequest(link.DeviceIndex, featureIndex, function, HidPpMessage.SoftwareId, parameters));

    /// <summary>Sends a read-only request; returns the response report or null (error or timeout, logged).</summary>
    private byte[]? Request(DeviceLink link, byte[] request)
    {
        Log($"  → {HidPpMessage.ToHex(request)}");
        var answer = link.Channel.Exchange(request, ResponseTimeout, CancellationToken.None);
        if (answer.Response is { } response)
        {
            Log($"  ← {HidPpMessage.ToHex(response)}");
            return response;
        }
        Log(answer.ErrorCode is { } errorCode ? $"  ← HID++ 2.0 error 0x{errorCode:X2}" : "  ← no answer (timeout or HID++ 1.0 error)");
        return null;
    }
}
