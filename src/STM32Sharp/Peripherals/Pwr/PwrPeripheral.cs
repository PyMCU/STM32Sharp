using STM32.Core.Memory;

namespace STM32.Peripherals.Pwr;

/// <summary>
/// Power control (PWR) for STM32C0/G0/L0. Base address 0x4000_7000.
///
/// The HAL touches PWR during clock setup — e.g. it sets CR1.DBP to unlock the backup domain and
/// spins until the read-back confirms it, and it polls SR for voltage-scaling / regulator "ready"
/// flags. We model the control registers as plain read/write storage (so written bits read back) and
/// report the status register as all-ready (0), which is the value the HAL's wait loops expect.
/// </summary>
public sealed class PwrPeripheral : IMemoryMappedDevice
{
    // STM32G0 status register PWR_SR2 (0x34): its voltage-scaling/regulator "ready" flags are all 0
    // when steady, which is exactly what the HAL's wait loops expect. (On the L0 the CSR is at 0x04
    // and its ready bits are likewise 0 when unwritten, so plain read/write storage already suffices.)
    private const uint PWR_SR2 = 0x34;

    private readonly uint[] _regs = new uint[0x40 / 4];

    public uint Size => 0x400;

    public uint ReadWord(uint address)
    {
        var off = address & 0x3F;
        if (off == PWR_SR2) return 0; // all ready, no pending transitions
        return _regs[off >> 2];
    }

    public ushort ReadHalfWord(uint address) =>
        (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));

    public byte ReadByte(uint address) =>
        (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));

    public void WriteWord(uint address, uint value) => _regs[(address & 0x3F) >> 2] = value;

    public void WriteHalfWord(uint address, ushort value)
    {
        var aligned = address & ~3u;
        var shift = (int)((address & 2) << 3);
        var current = ReadWord(aligned);
        WriteWord(aligned, (current & ~(0xFFFFu << shift)) | ((uint)value << shift));
    }

    public void WriteByte(uint address, byte value)
    {
        var aligned = address & ~3u;
        var shift = (int)((address & 3) << 3);
        var current = ReadWord(aligned);
        WriteWord(aligned, (current & ~(0xFFu << shift)) | ((uint)value << shift));
    }
}
