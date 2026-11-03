namespace LogitechBattery.Battery;

public enum MockMode
{
    Auto,
    Discharging85,
    Discharging25,
    Discharging10,
    Charging60,
    Full100,
    Asleep,
    NotFound,
}

/// <summary>Fake battery source for development. Used from the UI thread only.</summary>
public sealed class MockBatteryProvider : IBatteryProvider
{
    // Auto cycle: discharge 100 -> 5 at 1%/s, asleep 5 s, charge 5 -> 100 at 3%/s, full 10 s.
    private const double DischargeSeconds = 95;
    private const double AsleepSeconds = 5;
    private const double ChargeSeconds = 95.0 / 3;
    private const double FullSeconds = 10;
    private const double CycleSeconds = DischargeSeconds + AsleepSeconds + ChargeSeconds + FullSeconds;

    private readonly TimeProvider _time;
    private MockMode _mode = MockMode.Auto;
    private long _cycleStart;

    public MockBatteryProvider(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _cycleStart = _time.GetTimestamp();
    }

    public MockMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            if (value == MockMode.Auto)
                _cycleStart = _time.GetTimestamp(); // restart the cycle
        }
    }

    public Task<BatteryStatus> ReadAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var (connection, percent, charging) = _mode switch
        {
            MockMode.Auto => AutoState(),
            MockMode.Discharging85 => (ConnectionState.Connected, (int?)85, ChargingState.Discharging),
            MockMode.Discharging25 => (ConnectionState.Connected, 25, ChargingState.Discharging),
            MockMode.Discharging10 => (ConnectionState.Connected, 10, ChargingState.Discharging),
            MockMode.Charging60 => (ConnectionState.Connected, 60, ChargingState.Charging),
            MockMode.Full100 => (ConnectionState.Connected, 100, ChargingState.Full),
            MockMode.Asleep => (ConnectionState.Asleep, null, ChargingState.Unknown),
            _ => (ConnectionState.NotFound, null, ChargingState.Unknown),
        };
        return Task.FromResult(new BatteryStatus(connection, percent, charging, now));
    }

    private (ConnectionState, int?, ChargingState) AutoState()
    {
        var t = _time.GetElapsedTime(_cycleStart).TotalSeconds % CycleSeconds;

        if (t < DischargeSeconds)
            return (ConnectionState.Connected, 100 - (int)t, ChargingState.Discharging);
        t -= DischargeSeconds;

        if (t < AsleepSeconds)
            return (ConnectionState.Asleep, null, ChargingState.Unknown);
        t -= AsleepSeconds;

        if (t < ChargeSeconds)
            return (ConnectionState.Connected, Math.Min(99, 5 + (int)(t * 3)), ChargingState.Charging);

        return (ConnectionState.Connected, 100, ChargingState.Full);
    }
}
