using STM32.Core.Memory;

namespace STM32.Peripherals.Crc;

/// <summary>
/// CRC calculation unit (RM0444 §17). Present on STM32G0/C0/L0 at 0x4002_3000. Registers: DR 0x00,
/// IDR 0x04, CR 0x08, INIT 0x10, POL 0x14.
///
/// The unit is a configurable MSB-first (non-reflected by default) CRC engine. Writing DR feeds data
/// of the access width (byte/half-word/word) into the running CRC; reading DR returns the current
/// value. CR.RESET reloads DR from INIT. INIT and POL set the seed and polynomial; CR.POLYSIZE picks
/// the 7/8/16/32-bit width and CR.REV_IN / CR.REV_OUT enable bit reversal. The reset configuration
/// (POL = 0x04C11DB7, INIT = 0xFFFF_FFFF, no reversal) is the classic CRC-32/MPEG-2 used by ST's HAL.
/// </summary>
public sealed class CrcPeripheral : IMemoryMappedDevice
{
    private const uint DR   = 0x00;
    private const uint IDR  = 0x04;
    private const uint CR   = 0x08;
    private const uint INIT = 0x10;
    private const uint POL  = 0x14;

    private const uint CR_RESET = 1u << 0;
    // POLYSIZE[4:3]: 00=32, 01=16, 10=8, 11=7
    // REV_IN[6:5]:   00=none, 01=byte, 10=half-word, 11=word
    private const uint CR_REV_OUT = 1u << 7;

    private const uint DEFAULT_POL = 0x04C11DB7;
    private const uint DEFAULT_INIT = 0xFFFFFFFF;

    private uint _cr;
    private uint _init = DEFAULT_INIT;
    private uint _pol = DEFAULT_POL;
    private uint _crc = DEFAULT_INIT;
    private byte _idr;

    public uint Size => 0x400;

    private int Width => (int)((_cr >> 3) & 0x3) switch { 0 => 32, 1 => 16, 2 => 8, _ => 7 };
    private int RevIn => (int)((_cr >> 5) & 0x3);

    private static uint ReverseBits(uint v, int bits)
    {
        uint r = 0;
        for (var i = 0; i < bits; i++)
        {
            r = (r << 1) | (v & 1);
            v >>= 1;
        }
        return r;
    }

    private uint ApplyRevIn(uint data, int bits)
    {
        // Reverse each granularity-sized chunk within the access width (granularity capped by it).
        var gran = RevIn switch { 1 => 8, 2 => 16, 3 => 32, _ => 0 };
        if (gran == 0) return data;
        var chunk = Math.Min(gran, bits);
        uint result = 0;
        for (var off = 0; off < bits; off += chunk)
        {
            var piece = (data >> off) & (chunk >= 32 ? 0xFFFFFFFF : (1u << chunk) - 1);
            result |= ReverseBits(piece, chunk) << off;
        }
        return result;
    }

    private void Feed(uint data, int bits)
    {
        if (RevIn != 0) data = ApplyRevIn(data, bits);

        var w = Width;
        var mask = w >= 32 ? 0xFFFFFFFF : (1u << w) - 1;

        for (var i = bits - 1; i >= 0; i--)
        {
            var inBit = (data >> i) & 1;
            var top = (_crc >> (w - 1)) & 1;
            _crc = (_crc << 1) & mask;
            if ((top ^ inBit) != 0) _crc ^= _pol;
        }
        _crc &= mask;
    }

    private uint CurrentResult()
    {
        var w = Width;
        var value = _crc & (w >= 32 ? 0xFFFFFFFF : (1u << w) - 1);
        if ((_cr & CR_REV_OUT) != 0) value = ReverseBits(value, w);
        return value;
    }

    public uint ReadWord(uint address)
    {
        switch (address & 0xFF)
        {
            case DR: return CurrentResult();
            case IDR: return _idr;
            case CR: return _cr;
            case INIT: return _init;
            case POL: return _pol;
            default: return 0;
        }
    }

    public ushort ReadHalfWord(uint address) =>
        (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));

    public byte ReadByte(uint address) =>
        (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));

    public void WriteWord(uint address, uint value) => Write(address, value, 32);

    public void WriteHalfWord(uint address, ushort value)
    {
        if ((address & 0xFF) == DR) { Write(address, value, 16); return; }
        var aligned = address & ~3u;
        var shift = (int)((address & 2) << 3);
        var current = ReadWord(aligned);
        WriteWord(aligned, (current & ~(0xFFFFu << shift)) | ((uint)value << shift));
    }

    public void WriteByte(uint address, byte value)
    {
        if ((address & 0xFF) == DR) { Write(address, value, 8); return; }
        if ((address & 0xFF) == IDR) { _idr = value; return; }
        var aligned = address & ~3u;
        var shift = (int)((address & 3) << 3);
        var current = ReadWord(aligned);
        WriteWord(aligned, (current & ~(0xFFu << shift)) | ((uint)value << shift));
    }

    private void Write(uint address, uint value, int bits)
    {
        switch (address & 0xFF)
        {
            case DR:
                Feed(value, bits);
                break;
            case IDR:
                _idr = (byte)value;
                break;
            case CR:
                _cr = value;
                if ((value & CR_RESET) != 0)
                {
                    _crc = _init;        // RESET reloads the seed; the bit self-clears
                    _cr &= ~CR_RESET;
                }
                break;
            case INIT:
                _init = value;
                _crc = value;            // writing INIT also re-seeds the engine
                break;
            case POL:
                _pol = value;
                break;
        }
    }
}
