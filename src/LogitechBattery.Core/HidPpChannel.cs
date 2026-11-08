using HidSharp;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>What a request got back: the response report, or the code of a HID++ 2.0 error, or neither (HID++ 1.0 error or timeout: the device is not reachable).</summary>
internal readonly record struct HidPpAnswer(byte[]? Response, byte? ErrorCode)
{
    public static readonly HidPpAnswer Nothing = new(null, null);
}

/// <summary>
/// A HID++ device kept open. Its long and short collections are read on background threads, so every report is seen:
/// answers on either collection (HID++ 1.0 errors arrive at once on the short one) and unsolicited notifications.
/// </summary>
internal sealed class HidPpChannel : IDisposable
{
    private const int StreamWriteTimeoutMs = 200;
    private const byte FirstRotatingSoftwareId = 0x1; // 0x0 is for device events
    private const byte LastRotatingSoftwareId = 0xE;  // 0xF is used by other Logitech software

    private sealed record PendingRequest(byte[] Request, TaskCompletionSource<byte[]> Reply);

    private readonly HidStream _longStream;
    private readonly HidStream? _shortStream;
    private readonly Action<HidPpChannel, byte[]> _onReport;
    private readonly object _exchangeLock = new();
    private volatile PendingRequest? _pending;
    private volatile bool _closed;
    private byte _softwareId = LastRotatingSoftwareId;

    public string Key { get; }
    public int ProductId { get; }

    /// <summary>True once a collection stopped reading (device unplugged) or the channel was disposed.</summary>
    public bool IsClosed => _closed;

    public bool HasShortCollection => _shortStream is not null;

    private HidPpChannel(HidPpDevice device, HidStream longStream, HidStream? shortStream, Action<HidPpChannel, byte[]> onReport)
    {
        Key = device.Key;
        ProductId = device.LongCollection.ProductID;
        _longStream = longStream;
        _shortStream = shortStream;
        _onReport = onReport;
    }

    /// <summary>Opens the device and starts reading; null if its long collection cannot be opened.</summary>
    /// <param name="onReport">Called on a reader thread for every report, answers included.</param>
    public static HidPpChannel? Open(HidPpDevice device, Action<HidPpChannel, byte[]> onReport)
    {
        if (!device.LongCollection.TryOpen(out var longStream))
            return null;
        HidStream? shortStream = null;
        if (device.ShortCollection is { } shortCollection)
        {
            if (shortCollection.TryOpen(out var opened))
                shortStream = opened;
            else
                AppLog.Write($"Cannot open the short collection of {device.Key}"); // the channel works without it, minus HID++ 1.0 requests
        }

        var channel = new HidPpChannel(device, longStream, shortStream, onReport);
        channel.StartReading(longStream);
        if (shortStream is not null)
            channel.StartReading(shortStream);
        return channel;
    }

    /// <summary>Sends a long (HID++ 2.0) or short (HID++ 1.0) request and waits for its response, an error or the timeout.</summary>
    public HidPpAnswer Exchange(byte[] request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (_exchangeLock)
        {
            // A fresh software id per request: a late reply to a timed-out request no longer matches the next one.
            request = HidPpMessage.WithSoftwareId(request, NextSoftwareId());
            var pending = new PendingRequest(request, new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously));
            _pending = pending;
            try
            {
                var stream = StreamFor(request);
                stream.WriteTimeout = StreamWriteTimeoutMs;
                stream.Write(request);
                var answered = pending.Reply.Task.Wait(timeout, cancellationToken);
                return answered ? ToAnswer(request, pending.Reply.Task.Result) : HidPpAnswer.Nothing;
            }
            finally
            {
                _pending = null;
            }
        }
    }

    private byte NextSoftwareId() =>
        _softwareId = _softwareId >= LastRotatingSoftwareId ? FirstRotatingSoftwareId : (byte)(_softwareId + 1);

    public void Dispose()
    {
        _closed = true;
        foreach (var stream in new[] { _longStream, _shortStream })
            try { stream?.Dispose(); } catch { }
    }

    private HidStream StreamFor(byte[] request) =>
        HidPpMessage.IsShort(request)
            ? _shortStream ?? throw new InvalidOperationException("The device has no short HID++ collection.")
            : _longStream;

    private static HidPpAnswer ToAnswer(byte[] request, byte[] report)
    {
        var reply = HidPpMessage.Classify(request, report);
        return reply.Kind switch
        {
            HidPpReplyKind.Response => new HidPpAnswer(report, null),
            HidPpReplyKind.Error20 => new HidPpAnswer(null, reply.ErrorCode),
            _ => HidPpAnswer.Nothing,
        };
    }

    private void StartReading(HidStream stream) =>
        new Thread(() => ReadLoop(stream)) { IsBackground = true, Name = "HID++ reader" }.Start();

    private void ReadLoop(HidStream stream)
    {
        try
        {
            var buffer = new byte[stream.Device.GetMaxInputReportLength()];
            while (true)
            {
                int length;
                try { length = stream.Read(buffer, 0, buffer.Length); }
                catch (TimeoutException) { continue; } // no input for a while (HidSharp's ReadTimeout): keep listening
                if (length > 0)
                    Deliver(buffer[..length]);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { } // unplugged or disposed
        catch (Exception ex) { AppLog.Write($"HID++ reader stopped: {ex}"); }
        finally
        {
            _closed = true;
        }
    }

    private void Deliver(byte[] report)
    {
        // The handler runs first, so a log written there precedes whatever the waiting request logs next.
        try { _onReport(this, report); }
        catch (Exception ex) { AppLog.Write($"HID++ report handler failed: {ex}"); } // a faulty handler must not stop the reader
        if (_pending is { } pending && HidPpMessage.Classify(pending.Request, report).Kind != HidPpReplyKind.None)
            pending.Reply.TrySetResult(report);
    }
}
