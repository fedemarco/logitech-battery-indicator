namespace LogitechBattery.Battery;

public enum ConnectionState
{
    Connected,

    /// <summary>Receiver present but the mouse is not responding.</summary>
    Asleep,

    NotFound,
}
