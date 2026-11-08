using System.Reflection;
using System.Text;
using HidSharp;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>
/// Diagnostic (<c>--listen</c>): keeps every Logitech HID++ device open (its long and short collection) and logs each incoming report with a
/// timestamp until stopped, reopening collections after USB changes. The only request sent is a read of the
/// notification flags register. Writes %LOCALAPPDATA%\LogitechBattery\listen.txt.
/// </summary>
public sealed class HidPpListener
{
    private const string ReportFileName = "listen.txt";

    private readonly StringBuilder _log = new();
    private readonly Dictionary<string, HidPpChannel> _channels = new();

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
        OpenNewDevices();
    }

    private void Stop()
    {
        DeviceList.Local.Changed -= OnUsbDevicesChanged;
        lock (_channels)
        {
            foreach (var channel in _channels.Values)
                channel.Dispose();
            _channels.Clear();
        }
        Log("stopped");
    }

    private void OnUsbDevicesChanged(object? sender, DeviceListChangedEventArgs e)
    {
        Log("USB devices changed");
        OpenNewDevices();
    }

    private void OpenNewDevices()
    {
        lock (_channels)
        {
            ForgetClosedChannels();
            foreach (var device in HidPpDevices.Find().Where(device => !_channels.ContainsKey(device.Key)))
                Open(device);
        }
    }

    private void ForgetClosedChannels()
    {
        foreach (var (key, channel) in _channels.Where(entry => entry.Value.IsClosed).ToList())
        {
            Log($"{channel.ProductId:X4}  closed");
            channel.Dispose();
            _channels.Remove(key);
        }
    }

    private void Open(HidPpDevice device)
    {
        var productId = device.LongCollection.ProductID;
        if (HidPpChannel.Open(device, OnReport) is not { } channel)
        {
            Log($"{productId:X4}  cannot open");
            return;
        }
        _channels[device.Key] = channel;
        Log($"{productId:X4}  opened");

        if (channel.HasShortCollection)
            RequestNotificationFlags(channel);
    }

    /// <summary>Read-only: the answer (logged like every report) shows which notifications the receiver forwards.</summary>
    private void RequestNotificationFlags(HidPpChannel channel)
    {
        var request = HidPpMessage.BuildGetRegister(HidPpMessage.ReceiverIndex, ReceiverNotifications.FlagsRegister);
        Log($"{channel.ProductId:X4}  → {HidPpMessage.ToHex(request)}  (read notification flags)");
        try
        {
            channel.Exchange(request, DeviceDiscovery.Timeout, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException)
        {
            Log($"{channel.ProductId:X4}  write failed: {ex.Message}");
        }
    }

    // The report id tells the collection: 0x10 short, 0x11 long.
    private void OnReport(HidPpChannel channel, byte[] report) => Log($"{channel.ProductId:X4}  ← {HidPpMessage.ToHex(report)}");
}
