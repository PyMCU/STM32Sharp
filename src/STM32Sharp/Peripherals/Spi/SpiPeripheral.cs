using STM32.Core.Memory;

namespace STM32.Peripherals.Spi;

/// <summary>
/// SPI controller (RM0444 §32) in master mode. STM32G0 instances: SPI1 @ 0x4001_3000,
/// SPI2 @ 0x4000_3800. Register subset: CR1 0x00, CR2 0x04, SR 0x08, DR 0x0C.
///
/// Transfers are instantaneous and full-duplex: writing DR clocks out a byte and immediately
/// clocks in the byte returned by <see cref="OnTransfer"/> (a connected slave; defaults to 0xFF,
/// i.e. MISO idle-high / no device). TXE stays asserted; RXNE is set when a received byte is
/// available and cleared by reading DR.
/// </summary>
public sealed class SpiPeripheral : IMemoryMappedDevice
{
    private const uint CR1 = 0x00;
    private const uint CR2 = 0x04;
    private const uint SR  = 0x08;
    private const uint DR  = 0x0C;

    private const uint CR1_SPE = 1u << 6; // SPI enable

    // SR bits
    private const uint SR_RXNE = 1u << 0;
    private const uint SR_TXE  = 1u << 1;

    public string Name { get; }

    /// <summary>
    /// Connected slave: receives the transmitted byte, returns the byte shifted back on MISO.
    /// When null, reads return 0xFF.
    /// </summary>
    public Func<byte, byte>? OnTransfer;

    private uint _cr1;
    private uint _cr2;
    private byte _rx;
    private bool _rxFull;

    public uint Size => 0x400;

    public SpiPeripheral(string name) => Name = name;

    private uint BuildSr()
    {
        uint sr = SR_TXE; // always ready to transmit
        if (_rxFull) sr |= SR_RXNE;
        return sr;
    }

    public uint ReadWord(uint address)
    {
        switch (address & 0xFF)
        {
            case CR1: return _cr1;
            case CR2: return _cr2;
            case SR: return BuildSr();
            case DR:
                _rxFull = false;
                return _rx;
            default: return 0;
        }
    }

    public ushort ReadHalfWord(uint address)
    {
        if ((address & 0xFF) == DR)
        {
            _rxFull = false;
            return _rx;
        }
        return (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));
    }

    public byte ReadByte(uint address)
    {
        if ((address & 0xFF) == DR)
        {
            _rxFull = false;
            return _rx;
        }
        return (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));
    }

    private void Transmit(byte value)
    {
        if ((_cr1 & CR1_SPE) == 0) return; // peripheral disabled
        _rx = OnTransfer?.Invoke(value) ?? 0xFF;
        _rxFull = true;
    }

    public void WriteWord(uint address, uint value)
    {
        switch (address & 0xFF)
        {
            case CR1: _cr1 = value; break;
            case CR2: _cr2 = value; break;
            case DR: Transmit((byte)value); break;
        }
    }

    public void WriteHalfWord(uint address, ushort value)
    {
        if ((address & 0xFF) == DR) { Transmit((byte)value); return; }
        var aligned = address & ~3u;
        var shift = (int)((address & 2) << 3);
        var current = ReadWord(aligned);
        WriteWord(aligned, (current & ~(0xFFFFu << shift)) | ((uint)value << shift));
    }

    public void WriteByte(uint address, byte value)
    {
        if ((address & 0xFF) == DR) { Transmit(value); return; }
        var aligned = address & ~3u;
        var shift = (int)((address & 3) << 3);
        var current = ReadWord(aligned);
        WriteWord(aligned, (current & ~(0xFFu << shift)) | ((uint)value << shift));
    }
}
