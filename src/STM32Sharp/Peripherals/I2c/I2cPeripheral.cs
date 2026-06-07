using STM32.Core.Memory;

namespace STM32.Peripherals.I2c;

/// <summary>A simulated I2C slave device addressed by its 7-bit address.</summary>
public interface II2cSlave
{
    /// <summary>7-bit device address.</summary>
    int Address { get; }

    /// <summary>Master starts a write transaction (called on (re)START with RD_WRN = 0).</summary>
    void StartWrite() { }

    /// <summary>Master starts a read transaction (called on (re)START with RD_WRN = 1).</summary>
    void StartRead() { }

    /// <summary>Master transmits a byte to the slave.</summary>
    void Write(byte value);

    /// <summary>Master reads a byte from the slave.</summary>
    byte Read();

    /// <summary>STOP condition.</summary>
    void Stop() { }
}

/// <summary>
/// I2C controller (RM0444 §28, "I2C2" / V2 IP) in master mode. STM32G0 instances:
/// I2C1 @ 0x4000_5400, I2C2 @ 0x4000_5800. Register subset: CR1 0x00, CR2 0x04, ISR 0x18,
/// ICR 0x1C, RXDR 0x24, TXDR 0x28.
///
/// Drives the V2 transfer model: software programs CR2 (slave address, NBYTES, RD_WRN, START),
/// then writes TXDR / reads RXDR NBYTES times. Connected <see cref="II2cSlave"/> devices service
/// the bytes. Flags TXIS/RXNE/TC/STOPF/NACKF mirror the reference manual closely enough for the
/// HAL's polling routines to complete.
/// </summary>
public sealed class I2cPeripheral : IMemoryMappedDevice
{
    private const uint CR1  = 0x00;
    private const uint CR2  = 0x04;
    private const uint ISR  = 0x18;
    private const uint ICR  = 0x1C;
    private const uint RXDR = 0x24;
    private const uint TXDR = 0x28;

    // CR2 fields
    private const uint CR2_RD_WRN = 1u << 10;
    private const uint CR2_START  = 1u << 13;
    private const uint CR2_STOP   = 1u << 14;
    private const uint CR2_AUTOEND = 1u << 25;

    // ISR flags
    private const uint ISR_TXE   = 1u << 0;
    private const uint ISR_TXIS  = 1u << 1;
    private const uint ISR_RXNE  = 1u << 2;
    private const uint ISR_NACKF = 1u << 4;
    private const uint ISR_STOPF = 1u << 5;
    private const uint ISR_TC    = 1u << 6;
    private const uint ISR_BUSY  = 1u << 15;

    public string Name { get; }

    private readonly Dictionary<int, II2cSlave> _slaves = new();

    private uint _cr1;
    private uint _isr = ISR_TXE;
    private int _nbytes;
    private bool _autoend;
    private II2cSlave? _active;

    public uint Size => 0x400;

    public I2cPeripheral(string name) => Name = name;

    /// <summary>Attach a slave device on this bus.</summary>
    public void AddSlave(II2cSlave slave) => _slaves[slave.Address] = slave;

    public uint ReadWord(uint address)
    {
        switch (address & 0xFF)
        {
            case CR1: return _cr1;
            case ISR: return _isr;
            case RXDR: return ReadRxdr();
            default: return 0;
        }
    }

    public ushort ReadHalfWord(uint address) =>
        (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));

    public byte ReadByte(uint address) =>
        (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));

    private byte ReadRxdr()
    {
        if ((_isr & ISR_RXNE) == 0 || _active == null) return 0;
        var b = _active.Read();
        _isr &= ~ISR_RXNE;
        _nbytes--;
        if (_nbytes > 0) _isr |= ISR_RXNE;   // next byte ready
        else CompleteTransfer();
        return b;
    }

    private void CompleteTransfer()
    {
        if (_autoend)
        {
            _active?.Stop();
            _isr |= ISR_STOPF;
            _isr &= ~ISR_BUSY;
        }
        else
        {
            _isr |= ISR_TC;
        }
    }

    private void StartTransfer(uint cr2)
    {
        var addr = (int)((cr2 >> 1) & 0x7F); // 7-bit address in SADD[7:1]
        _nbytes = (int)((cr2 >> 16) & 0xFF);
        _autoend = (cr2 & CR2_AUTOEND) != 0;
        var read = (cr2 & CR2_RD_WRN) != 0;

        _isr &= ~(ISR_NACKF | ISR_STOPF | ISR_TC);
        _isr |= ISR_BUSY;

        if (!_slaves.TryGetValue(addr, out _active))
        {
            _isr |= ISR_NACKF;          // no device acknowledged
            _isr &= ~ISR_BUSY;
            return;
        }

        if (read)
        {
            _active.StartRead();
            if (_nbytes > 0) _isr |= ISR_RXNE;
            else CompleteTransfer();
        }
        else
        {
            _active.StartWrite();
            if (_nbytes > 0) _isr |= ISR_TXIS | ISR_TXE;
            else CompleteTransfer();
        }
    }

    public void WriteWord(uint address, uint value)
    {
        switch (address & 0xFF)
        {
            case CR1: _cr1 = value; break;

            case CR2:
                if ((value & CR2_START) != 0) StartTransfer(value);
                if ((value & CR2_STOP) != 0)
                {
                    _active?.Stop();
                    _isr |= ISR_STOPF;
                    _isr &= ~ISR_BUSY;
                }
                break;

            case ICR:
                _isr &= ~value; // write-1-to-clear (STOPF, NACKF, ...)
                break;

            case TXDR:
                WriteTxdr((byte)value);
                break;
        }
    }

    private void WriteTxdr(byte value)
    {
        if (_active == null) return;
        _active.Write(value);
        _isr &= ~(ISR_TXIS);
        _nbytes--;
        if (_nbytes > 0) _isr |= ISR_TXIS | ISR_TXE;
        else CompleteTransfer();
    }

    public void WriteHalfWord(uint address, ushort value)
    {
        var aligned = address & ~3u;
        var shift = (int)((address & 2) << 3);
        var current = ReadWord(aligned);
        WriteWord(aligned, (current & ~(0xFFFFu << shift)) | ((uint)value << shift));
    }

    public void WriteByte(uint address, byte value)
    {
        if ((address & 0xFF) == TXDR) { WriteTxdr(value); return; }
        var aligned = address & ~3u;
        var shift = (int)((address & 3) << 3);
        var current = ReadWord(aligned);
        WriteWord(aligned, (current & ~(0xFFu << shift)) | ((uint)value << shift));
    }
}
