using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HidSharp;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>
/// One-off diagnostic (<c>--probe</c>): lists Logitech HID collections and sends read-only HID++ 2.0 queries,
/// logging every report as hex. Writes %LOCALAPPDATA%\LogitechBattery\probe.txt and opens it in Notepad.
/// </summary>
internal sealed class HidPpProbe
{
    private const int LogitechVid = 0x046D;
    private const byte SwId = 0x0A;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private readonly StringBuilder _log = new();

    public static void Run()
    {
        var probe = new HidPpProbe();
        try { probe.Execute(); }
        catch (Exception ex) { probe.Log($"FATAL: {ex}"); }

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogitechBattery");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "probe.txt");
        File.WriteAllText(file, probe._log.ToString());
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
    }

    private void Log(string line) => _log.AppendLine(line);

    private void Execute()
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Log($"LogitechBattery probe {version}");
        Log($"{DateTimeOffset.Now:O}  {Environment.OSVersion}");
        Log("");

        var devices = DeviceList.Local.GetHidDevices(LogitechVid).ToList();
        Log($"== HID collections (VID 046D): {devices.Count}");
        var infos = new List<(HidDevice Dev, uint Usage, string Group)>();
        foreach (var d in devices)
        {
            var usage = DescribeCollection(d);
            infos.Add((d, usage, GroupKey(d.DevicePath)));
        }

        foreach (var group in infos.GroupBy(i => i.Group))
        {
            // HID++ long = page 0xFF00 usage 2, short = usage 1
            var longs = group.Where(i => i.Usage == 0xFF000002).ToList();
            if (longs.Count == 0)
                continue;
            var hidpp = group.Where(i => i.Usage is 0xFF000001 or 0xFF000002).Select(i => i.Dev).ToList();
            Log("");
            Log($"== Physical device {group.Key}");
            try { ProbeDevice(hidpp, longs[0].Dev); }
            catch (Exception ex) { Log($"ERROR probing device: {ex.Message}"); }
        }
    }

    // Everything before "&col" identifies the physical device.
    private static string GroupKey(string path)
    {
        var i = path.IndexOf("&col", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? path[..i] : path;
    }

    /// <summary>Logs one collection; returns page&lt;&lt;16|usage of its first top-level usage (0 if unknown).</summary>
    private uint DescribeCollection(HidDevice d)
    {
        string name;
        try { name = d.GetProductName(); } catch (Exception ex) { name = $"<error: {ex.Message}>"; }
        string usages;
        uint first = 0;
        try
        {
            var all = d.GetReportDescriptor().DeviceItems.SelectMany(i => i.Usages.GetAllValues()).ToList();
            usages = string.Join(", ", all.Select(u => $"{u >> 16:X4}:{u & 0xFFFF:X4}"));
            first = all.FirstOrDefault();
        }
        catch (Exception ex) { usages = $"<error: {ex.Message}>"; }
        int inLen = 0, outLen = 0;
        try { inLen = d.GetMaxInputReportLength(); outLen = d.GetMaxOutputReportLength(); } catch { }
        Log($"PID {d.ProductID:X4}  \"{name}\"  usage(page:usage) {usages}  maxIn {inLen}  maxOut {outLen}");
        Log($"    {d.DevicePath}");
        return first;
    }

    private void ProbeDevice(List<HidDevice> collections, HidDevice longDevice)
    {
        var streams = new List<HidStream>();
        var reports = new BlockingCollection<byte[]>();
        try
        {
            HidStream? writer = null;
            foreach (var c in collections)
            {
                if (!c.TryOpen(out var s))
                {
                    Log($"cannot open {c.DevicePath}");
                    continue;
                }
                streams.Add(s);
                if (c == longDevice) writer = s;
                var t = new Thread(() => ReadLoop(s, reports)) { IsBackground = true };
                t.Start();
            }
            if (writer is null)
                return;

            foreach (byte dev in new byte[] { 0xFF, 1, 2, 3, 4, 5, 6 })
            {
                Log("");
                Log($"-- devIdx {dev:X2}");
                try { ProbeIndex(writer, reports, dev); }
                catch (Exception ex) { Log($"ERROR devIdx {dev:X2}: {ex.Message}"); }
            }
        }
        finally
        {
            foreach (var s in streams)
                try { s.Dispose(); } catch { }
        }
    }

    private static void ReadLoop(HidStream stream, BlockingCollection<byte[]> sink)
    {
        try
        {
            var buf = new byte[stream.Device.GetMaxInputReportLength()];
            while (true)
            {
                var n = stream.Read(buf, 0, buf.Length);
                if (n > 0) sink.Add(buf[..n]);
            }
        }
        catch { /* stream disposed */ }
    }

    private void ProbeIndex(HidStream writer, BlockingCollection<byte[]> reports, byte dev)
    {
        var ping = Request(writer, reports, dev, 0x00, 1, 0, 0, 0x5A);
        if (ping is null)
        {
            Log("  => no HID++ 2.0 answer");
            return;
        }
        Log($"  protocol {ping[4]}.{ping[5]} (ping echo {(ping[6] == 0x5A ? "ok" : "mismatch")})");

        byte? Feature(ushort id)
        {
            var r = Request(writer, reports, dev, 0x00, 0, (byte)(id >> 8), (byte)id);
            var idx = r?[4];
            Log(r is null ? $"  feature {id:X4}: no answer" : idx == 0 ? $"  feature {id:X4}: not supported" : $"  feature {id:X4}: index {idx:X2} type {r[5]:X2} version {r[6]}");
            return idx is null or 0 ? null : idx;
        }

        if (Feature(0x0005) is { } nameIdx)
        {
            var count = Request(writer, reports, dev, nameIdx, 0)?[4];
            if (count is > 0)
            {
                var sb = new StringBuilder();
                for (int pos = 0; pos < Math.Min((int)count, 64) && Request(writer, reports, dev, nameIdx, 1, (byte)pos) is { } r; pos += 16)
                    sb.Append(Encoding.ASCII.GetString(r, 4, 16).TrimEnd('\0'));
                Log($"  device name: \"{sb}\"");
            }
        }
        if (Feature(0x1004) is { } uni)
        {
            if (Request(writer, reports, dev, uni, 0) is { } c)
                Log($"  UnifiedBattery capabilities params: {HidPpMessage.ToHex(c[4..10])}");
            if (Request(writer, reports, dev, uni, 1) is { } s)
                Log($"  UnifiedBattery status: {s[4]}% levelFlags {s[5]:X2} charging {s[6]} externalPower {s[7]}");
        }
        if (Feature(0x1000) is { } bs && Request(writer, reports, dev, bs, 0) is { } b)
            Log($"  BatteryStatus: {b[4]}% next {b[5]}% status {b[6]}");
        if (Feature(0x1001) is { } bv && Request(writer, reports, dev, bv, 0) is { } v)
            Log($"  BatteryVoltage: {(v[4] << 8) | v[5]} mV flags {v[6]:X2}");
    }

    /// <summary>Sends a read-only request; returns the matching response report or null (error/timeout, logged).</summary>
    private byte[]? Request(HidStream writer, BlockingCollection<byte[]> reports, byte dev, byte feature, int fn, params byte[] args)
    {
        var req = HidPpMessage.BuildRequest(dev, feature, fn, SwId, args);
        while (reports.TryTake(out _)) { } // drop stale reports from earlier requests
        Log($"  → {HidPpMessage.ToHex(req)}");
        writer.Write(req);

        var deadline = DateTime.UtcNow + Timeout;
        while (reports.TryTake(out var rep, Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds)))
        {
            var reply = HidPpMessage.Classify(req, rep);
            switch (reply.Kind)
            {
                case HidPpReplyKind.Response:
                    Log($"  ← {HidPpMessage.ToHex(rep)}");
                    return rep;
                case HidPpReplyKind.Error20:
                    Log($"  ← {HidPpMessage.ToHex(rep)}   HID++ 2.0 error 0x{reply.ErrorCode:X2}");
                    return null;
                case HidPpReplyKind.Error10:
                    Log($"  ← {HidPpMessage.ToHex(rep)}   HID++ 1.0 error 0x{reply.ErrorCode:X2}");
                    return null;
                default:
                    Log($"  ← {HidPpMessage.ToHex(rep)}   (unsolicited, skipped)");
                    break;
            }
        }
        Log("  ← timeout");
        return null;
    }
}
