using STM32.Core.Memory;

namespace STM32.Peripherals.Flash;

/// <summary>
/// Embedded Flash interface registers (FLASH) for STM32G0. Base address 0x4002_2000.
///
/// This models the *register block* that configures wait states and reports status — not the
/// Flash array itself (the array is the fast-path buffer in <see cref="BusInterconnect"/>).
/// For boot it is enough to: accept FLASH_ACR (latency/prefetch) and read it back, and report
/// FLASH_SR with BSY cleared so HAL_FLASH wait loops complete immediately. Program/erase is a
/// stub for now (Phase 6).
/// </summary>
public sealed class FlashPeripheral : IMemoryMappedDevice
{
    private const uint FLASH_ACR = 0x00; // Access control (latency, prefetch, caches)
    private const uint FLASH_SR  = 0x10; // Status (BSY, EOP, error flags)

    private readonly uint[] _regs = new uint[0x100 / 4];

    public uint Size => 0x400;

    public uint ReadWord(uint address)
    {
        var offset = address & 0xFF;
        // Status register always reads "not busy" (BSY = 0, no errors).
        if (offset == FLASH_SR) return 0;
        return _regs[offset >> 2];
    }

    public ushort ReadHalfWord(uint address) =>
        (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));

    public byte ReadByte(uint address) =>
        (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));

    public void WriteWord(uint address, uint value) => _regs[(address & 0xFF) >> 2] = value;

    public void WriteHalfWord(uint address, ushort value)
    {
        var aligned = address & ~3u;
        var shift = (int)((address & 2) << 3);
        var current = _regs[(aligned & 0xFF) >> 2];
        _regs[(aligned & 0xFF) >> 2] = (current & ~(0xFFFFu << shift)) | ((uint)value << shift);
    }

    public void WriteByte(uint address, byte value)
    {
        var aligned = address & ~3u;
        var shift = (int)((address & 3) << 3);
        var current = _regs[(aligned & 0xFF) >> 2];
        _regs[(aligned & 0xFF) >> 2] = (current & ~(0xFFu << shift)) | ((uint)value << shift);
    }
}
