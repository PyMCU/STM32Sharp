namespace STM32.Core.Memory;

/// <summary>
/// Routes peripheral-space accesses to the matching <see cref="IMemoryMappedDevice"/>
/// by absolute address range. Unlike the RP2040 APB/AHB bridges (which split the map by
/// the top nibble of the address), STM32 peripherals are scattered across several buses
/// (APB at 0x4000_0000, AHB at 0x4002_0000, IOPORT/GPIO at 0x5000_0000, PPB at 0xE000_0000),
/// so we route on the full address and hand each device an offset relative to its base.
/// </summary>
public sealed class PeripheralBus
{
    private readonly struct Entry(uint start, uint end, IMemoryMappedDevice device)
    {
        public readonly uint Start = start;
        public readonly uint End = end; // exclusive
        public readonly IMemoryMappedDevice Device = device;
    }

    private readonly List<Entry> _entries = [];

    /// <summary>Register a device at <paramref name="baseAddress"/>; its window is [base, base+Size).</summary>
    public void Register(uint baseAddress, IMemoryMappedDevice device)
    {
        var end = baseAddress + device.Size;
        foreach (var e in _entries)
            if (baseAddress < e.End && end > e.Start)
                throw new InvalidOperationException(
                    $"Peripheral window 0x{baseAddress:X8}..0x{end:X8} overlaps an existing device " +
                    $"(0x{e.Start:X8}..0x{e.End:X8}).");
        _entries.Add(new Entry(baseAddress, end, device));
    }

    private bool TryFind(uint address, out IMemoryMappedDevice device, out uint offset)
    {
        // Linear scan: the device count is small (tens) and this is the slow path.
        foreach (var e in _entries)
        {
            if (address >= e.Start && address < e.End)
            {
                device = e.Device;
                offset = address - e.Start;
                return true;
            }
        }
        device = null!;
        offset = 0;
        return false;
    }

    public byte ReadByte(uint address) => TryFind(address, out var d, out var o) ? d.ReadByte(o) : (byte)0;
    public ushort ReadHalfWord(uint address) => TryFind(address, out var d, out var o) ? d.ReadHalfWord(o) : (ushort)0;
    public uint ReadWord(uint address) => TryFind(address, out var d, out var o) ? d.ReadWord(o) : 0u;

    public void WriteByte(uint address, byte value) { if (TryFind(address, out var d, out var o)) d.WriteByte(o, value); }
    public void WriteHalfWord(uint address, ushort value) { if (TryFind(address, out var d, out var o)) d.WriteHalfWord(o, value); }
    public void WriteWord(uint address, uint value) { if (TryFind(address, out var d, out var o)) d.WriteWord(o, value); }
}
