using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HidSharp;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>
/// Diagnostic (<c>--listen</c>): keeps every Logitech HID++ collection open and logs each incoming report with a
/// timestamp until stopped, reopening collections after USB changes. The only request sent is a read of the
/// notification flags register. Writes %LOCALAPPDATA%\LogitechBattery\listen.txt.
/// </summary>
public sealed class HidPpListener
{
    private const string ReportFileName = "listen.txt";
    private const byte NotificationFlagsRegister = 0x00;
    private const int WriteTimeoutMs = 200;

    private readonly StringBuilder _log = new();
    private readonly Dictionary<string, HidStream> _openStreams = new();

    /// <summary>Listens until <paramref name="waitUntilStopped"/> returns; returns the path of the report file.</summary>
    public static string Run(Action waitUntilStopped)
    {
        var listener = new HidPpListener();
        try
        {
            listener.Start();
            waitUntilStopped();
        }
        catch (Exception ex) { listener.Log($"FATAL: {ex}"); }
        finally { listener.Stop(); }

        Directory.CreateDirectory(AppPaths.DataDirectory);
        var file = Path.Combine(AppPaths.DataDirectory, ReportFileName);
        File.WriteAllText(file, listener.LogText());
        return file;
    }

    private void Log(string line)
    {
        lock (_log)
            _log.AppendLine($"{DateTime.Now:HH:mm:ss.fff}  {line}");
    }

    private string LogText()
    {
        lock (_log)
            return _log.ToString();
    }

    private void Start()
    {
        Log($"LogitechBattery listen {AppVersion.InformationalVersionOf(Assembly.GetExecutingAssembly())}  {Environment.OSVersion}");
        DeviceList.Local.Changed += OnUsbDevicesChanged;
        OpenNewCollections();
    }

    private void Stop()
    {
        DeviceList.Local.Changed -= OnUsbDevicesChanged;
        List<HidStream> streams;
        lock (_openStreams)
            streams = _openStreams.Values.ToList();
        foreach (var stream in streams)
            try { stream.Dispose(); } catch { }
        Log("stopped");
    }

    private void OnUsbDevicesChanged(object? sender, DeviceListChangedEventArgs e)
    {
        Log("USB devices changed");
        OpenNewCollections();
    }

    private void OpenNewCollections()
    {
        lock (_openStreams)
        {
            foreach (var device in HidPpDevices.Find())
                foreach (var collection in device.Collections.Where(c => !_openStreams.ContainsKey(c.DevicePath)))
                    Open(collection);
        }
    }

    private void Open(HidDevice collection)
    {
        var label = Label(collection);
        if (!collection.TryOpen(out var stream))
        {
            Log($"{label}  cannot open");
            return;
        }
        _openStreams[collection.DevicePath] = stream;
        Log($"{label}  opened");
        new Thread(() => ReadLoop(collection, stream, label)) { IsBackground = true }.Start();

        if (collection.GetMaxOutputReportLength() == HidPpMessage.ShortLength)
            RequestNotificationFlags(stream, label);
    }

    /// <summary>Read-only: the answer (logged by the read loop) shows which notifications the receiver forwards.</summary>
    private void RequestNotificationFlags(HidStream stream, string label)
    {
        var request = HidPpMessage.BuildGetRegister(HidPpMessage.ReceiverIndex, NotificationFlagsRegister);
        Log($"{label}  → {HidPpMessage.ToHex(request)}  (read notification flags)");
        try
        {
            stream.WriteTimeout = WriteTimeoutMs;
            stream.Write(request);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException) { Log($"{label}  write failed: {ex.Message}"); }
    }

    private void ReadLoop(HidDevice collection, HidStream stream, string label)
    {
        try
        {
            var buffer = new byte[collection.GetMaxInputReportLength()];
            while (true)
            {
                try
                {
                    var length = stream.Read(buffer, 0, buffer.Length);
                    if (length > 0)
                        Log($"{label}  ← {HidPpMessage.ToHex(buffer[..length])}");
                }
                catch (TimeoutException) { } // no input for a while (HidSharp's ReadTimeout): keep listening
            }
        }
        catch (Exception ex) { Log($"{label}  closed ({ex.GetType().Name})"); }
        finally
        {
            lock (_openStreams)
                _openStreams.Remove(collection.DevicePath);
            try { stream.Dispose(); } catch { }
        }
    }

    // e.g. "C54D col01": the product id tells receiver from cable, the collection number short from long reports.
    private static string Label(HidDevice collection) =>
        $"{collection.ProductID:X4} {Regex.Match(collection.DevicePath, @"col\d+", RegexOptions.IgnoreCase).Value.ToLowerInvariant()}";
}
