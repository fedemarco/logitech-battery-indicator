using System.Collections.Concurrent;

namespace LogitechBattery.Devices;

/// <summary>Slots whose device reported "link down" since they were last read. Recorded on a reader thread, taken by the read.</summary>
internal sealed class ReportedLinkDowns
{
    private readonly ConcurrentDictionary<Slot, byte> _slots = new();

    public void Record(Slot slot) => _slots[slot] = 0;

    /// <summary>True once per recorded link down.</summary>
    public bool Take(Slot slot) => _slots.TryRemove(slot, out byte unusedMarker);

    /// <summary>The device is linked again (or its channel is gone): an older link down no longer counts.</summary>
    public void Forget(Slot slot) => Take(slot);

    public void Clear() => _slots.Clear();
}
