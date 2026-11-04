using System.Collections.Concurrent;
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

    // Other battery features, probed only to find out what a device offers.
    private const ushort BatteryStatusFeatureId = 0x1000;
    private const int BatteryStatusGetStatusFunction = 0;
    private const ushort BatteryVoltageFeatureId = 0x1001;
    private const int BatteryVoltageGetVoltageFunction = 0;

    /// <summary>A device index on the writer stream, plus the reports every collection of the device delivers.</summary>
    private readonly record struct DeviceLink(HidStream Writer, BlockingCollection<byte[]> Reports, byte DeviceIndex);

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
            try { ProbeDevice(device.Collections, device.LongCollection); }
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
        try { return string.Join(", ", HidPpDevices.ReadUsages(device).Select(u => $"{HidPpDevices.UsagePage(u):X4}:{u & 0xFFFF:X4}")); }
        catch (Exception ex) { return $"<error: {ex.Message}>"; }
    }

    private void ProbeDevice(List<HidDevice> collections, HidDevice longCollection)
    {
        var streams = new List<HidStream>();
        var reports = new BlockingCollection<byte[]>();
        try
        {
            HidStream? writer = null;
            foreach (var collection in collections)
            {
                if (!collection.TryOpen(out var stream))
                {
                    Log($"cannot open {collection.DevicePath}");
                    continue;
                }
                streams.Add(stream);
                if (collection == longCollection)
                    writer = stream;
                new Thread(() => ReadLoop(stream, reports)) { IsBackground = true }.Start();
            }
            if (writer is null)
                return;

            foreach (var deviceIndex in HidPpMessage.CableDeviceIndexes.Concat(HidPpMessage.ReceiverDeviceIndexes))
            {
                Log("");
                Log($"-- devIdx {deviceIndex:X2}");
                try { ProbeIndex(new DeviceLink(writer, reports, deviceIndex)); }
                catch (Exception ex) { Log($"ERROR devIdx {deviceIndex:X2}: {ex.Message}"); }
            }
        }
        finally
        {
            foreach (var stream in streams)
                try { stream.Dispose(); } catch { }
        }
    }

    private static void ReadLoop(HidStream stream, BlockingCollection<byte[]> sink)
    {
        try
        {
            var buffer = new byte[stream.Device.GetMaxInputReportLength()];
            while (true)
            {
                try
                {
                    var length = stream.Read(buffer, 0, buffer.Length);
                    if (length > 0)
                        sink.Add(buffer[..length]);
                }
                catch (TimeoutException) { } // no input for a while (HidSharp's ReadTimeout): keep listening
            }
        }
        catch { /* stream disposed */ }
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
        if (DescribeFeature(link, UnifiedBattery.FeatureId) is { } unifiedFeatureIndex)
            ReportUnifiedBattery(link, unifiedFeatureIndex);
        if (DescribeFeature(link, BatteryStatusFeatureId) is { } statusFeatureIndex
            && Call(link, statusFeatureIndex, BatteryStatusGetStatusFunction) is { } status)
        {
            var p = HidPpMessage.ParamsOf(status);
            Log($"  BatteryStatus: {p[0]}% next {p[1]}% status {p[2]}");
        }
        if (DescribeFeature(link, BatteryVoltageFeatureId) is { } voltageFeatureIndex
            && Call(link, voltageFeatureIndex, BatteryVoltageGetVoltageFunction) is { } voltage)
        {
            var p = HidPpMessage.ParamsOf(voltage);
            Log($"  BatteryVoltage: {(p[0] << 8) | p[1]} mV flags {p[2]:X2}");
        }
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
        var p = HidPpMessage.ParamsOf(response);
        if (p[0] == 0)
        {
            Log($"  feature {featureId:X4}: not supported");
            return null;
        }
        Log($"  feature {featureId:X4}: index {p[0]:X2} type {p[1]:X2} version {p[2]}");
        return p[0];
    }

    private void ReportDeviceName(DeviceLink link, byte featureIndex)
    {
        var count = Call(link, featureIndex, DeviceName.GetCountFunction) is { } response ? HidPpMessage.ParamsOf(response)[0] : 0;
        if (count == 0)
            return;

        var chunks = DeviceName.ReadChunks(DeviceName.LimitLength(count), position =>
            Call(link, featureIndex, DeviceName.GetNameFunction, position) is { } chunk ? HidPpMessage.ParamsOf(chunk) : null);
        var name = string.Concat(chunks.Select(chunk => Encoding.ASCII.GetString(chunk).TrimEnd('\0')));
        Log($"  device name: \"{name}\"");
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

    /// <summary>Sends a read-only request; returns the matching response report or null (error/timeout, logged).</summary>
    private byte[]? Request(DeviceLink link, byte[] request)
    {
        while (link.Reports.TryTake(out _)) { } // drop stale reports from earlier requests
        Log($"  → {HidPpMessage.ToHex(request)}");
        link.Writer.Write(request);

        var deadline = DateTime.UtcNow + ResponseTimeout;
        while (link.Reports.TryTake(out var report, Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds)))
        {
            var reply = HidPpMessage.Classify(request, report);
            switch (reply.Kind)
            {
                case HidPpReplyKind.Response:
                    Log($"  ← {HidPpMessage.ToHex(report)}");
                    return report;
                case HidPpReplyKind.Error20:
                    Log($"  ← {HidPpMessage.ToHex(report)}   HID++ 2.0 error 0x{reply.ErrorCode:X2}");
                    return null;
                case HidPpReplyKind.Error10:
                    Log($"  ← {HidPpMessage.ToHex(report)}   HID++ 1.0 error 0x{reply.ErrorCode:X2}");
                    return null;
                case HidPpReplyKind.None:
                    Log($"  ← {HidPpMessage.ToHex(report)}   (unsolicited, skipped)");
                    break;
            }
        }
        Log("  ← timeout");
        return null;
    }
}
