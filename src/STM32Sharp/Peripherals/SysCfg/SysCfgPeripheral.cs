using STM32.Core.Memory;

namespace STM32.Peripherals.SysCfg;

/// <summary>
/// System configuration controller (SYSCFG) for STM32G0. Base 0x4001_0000.
/// Holds memory-remap and miscellaneous configuration registers. The boot path writes a few of
/// these; modelling them as plain read/write storage is sufficient (on STM32G0 the EXTI port
/// selection lives in EXTI, not here, so SYSCFG has no interrupt-routing role to emulate).
/// </summary>
public sealed class SysCfgPeripheral : IMemoryMappedDevice
{
    private readonly uint[] _regs = new uint[0x100 / 4];

    public uint Size => 0x400;

    public uint ReadWord(uint address) => _regs[(address & 0xFF) >> 2];

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
