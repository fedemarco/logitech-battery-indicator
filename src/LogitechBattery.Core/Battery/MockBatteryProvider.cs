namespace LogitechBattery.Battery;

using Reading = (ConnectionState Connection, int? Percent, ChargingState Charging);

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
    // Auto cycle: discharge from full to the floor, asleep, charge back up, stay full.
    private const int FloorPercent = 5;
    private const int ChargePercentPerSecond = 3;

    // Percent changes by 1 per second; +1 keeps the floor on screen for its own second.
    internal const double DischargeSeconds = BatteryStatus.FullPercent - FloorPercent + 1;
    internal const double AsleepSeconds = 5;
    internal const double ChargeSeconds = (double)(BatteryStatus.FullPercent - FloorPercent) / ChargePercentPerSecond;
    internal const double FullSeconds = 10;
    internal const double CycleSeconds = DischargeSeconds + AsleepSeconds + ChargeSeconds + FullSeconds;

    // 100 % is reported as Full, so charging never shows it.
    private const int HighestChargingPercent = BatteryStatus.FullPercent - 1;

    private static readonly DeviceInfo MockDevice = new(DeviceId.Parse("00000001"), "Mock mouse");

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

    public Task<IReadOnlyList<DeviceReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var (connection, percent, charging) = _mode switch
        {
            MockMode.Auto => AutoReading(),
            MockMode.Discharging85 => Connected(85, ChargingState.Discharging),
            MockMode.Discharging25 => Connected(25, ChargingState.Discharging),
            MockMode.Discharging10 => Connected(10, ChargingState.Discharging),
            MockMode.Charging60 => Connected(60, ChargingState.Charging),
            MockMode.Full100 => Connected(BatteryStatus.FullPercent, ChargingState.Full),
            MockMode.Asleep => NoReading(ConnectionState.Asleep),
            _ => NoReading(ConnectionState.NotFound),
        };
        var status = new BatteryStatus(connection, percent, charging, _time.GetUtcNow());
        return Task.FromResult<IReadOnlyList<DeviceReading>>([new DeviceReading(MockDevice, status)]);
    }

    private Reading AutoReading()
    {
        const double asleepStart = DischargeSeconds;
        const double chargeStart = asleepStart + AsleepSeconds;
        const double fullStart = chargeStart + ChargeSeconds;

        var secondsIntoCycle = _time.GetElapsedTime(_cycleStart).TotalSeconds % CycleSeconds;

        if (secondsIntoCycle < asleepStart)
            return Connected(BatteryStatus.FullPercent - (int)secondsIntoCycle, ChargingState.Discharging);
        if (secondsIntoCycle < chargeStart)
            return NoReading(ConnectionState.Asleep);
        if (secondsIntoCycle < fullStart)
        {
            var charged = (int)((secondsIntoCycle - chargeStart) * ChargePercentPerSecond);
            return Connected(Math.Min(HighestChargingPercent, FloorPercent + charged), ChargingState.Charging);
        }
        return Connected(BatteryStatus.FullPercent, ChargingState.Full);
    }

    private static Reading Connected(int percent, ChargingState charging) => (ConnectionState.Connected, percent, charging);

    private static Reading NoReading(ConnectionState state) => (state, null, ChargingState.Unknown);
}
