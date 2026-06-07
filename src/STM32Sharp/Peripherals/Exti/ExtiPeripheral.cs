using STM32.Core.Memory;

namespace STM32.Peripherals.Exti;

/// <summary>
/// Extended interrupts and events controller (EXTI) for STM32G0. Base 0x4002_1800.
/// On STM32G0 the port selection (EXTICR) lives inside EXTI (RM0444 §16.4), unlike F0/F4 where it
/// is in SYSCFG. Register subset:
///   RTSR1 0x00, FTSR1 0x04, SWIER1 0x08, RPR1 0x0C, FPR1 0x10,
///   EXTICR[4] 0x60..0x6C (8 bits per line, 4 lines per word), IMR1 0x80, EMR1 0x84.
///
/// A GPIO edge on line N is delivered via <see cref="OnPortEdge"/>: if the line's EXTICR selects
/// that port and the matching rising/falling trigger is enabled, the pending bit (RPR/FPR) is set
/// and — when IMR unmasks the line — the corresponding NVIC IRQ is asserted
/// (lines 0–1 → IRQ5, 2–3 → IRQ6, 4–15 → IRQ7).
/// </summary>
public sealed class ExtiPeripheral : IMemoryMappedDevice
{
    private const uint RTSR1  = 0x00;
    private const uint FTSR1  = 0x04;
    private const uint SWIER1 = 0x08;
    private const uint RPR1   = 0x0C;
    private const uint FPR1   = 0x10;
    private const uint EXTICR0 = 0x60;
    private const uint EXTICR3 = 0x6C;
    private const uint IMR1   = 0x80;
    private const uint EMR1   = 0x84;

    /// <summary>Set by the machine to assert/deassert an NVIC IRQ line.</summary>
    public Action<int, bool>? RaiseIrq;

    private uint _rtsr;
    private uint _ftsr;
    private uint _rpr;
    private uint _fpr;
    private uint _imr;
    private uint _emr;
    private readonly uint[] _exticr = new uint[4];

    public uint Size => 0x400;

    /// <summary>
    /// Notify EXTI of a GPIO edge. <paramref name="portIndex"/> is A=0, B=1, … The edge is only
    /// acted on if EXTICR maps this line to that port.
    /// </summary>
    public void OnPortEdge(int portIndex, int line, bool rising)
    {
        if (line is < 0 or > 15) return;
        if (SelectedPort(line) != portIndex) return;

        var bit = 1u << line;
        var triggered = false;

        if (rising && (_rtsr & bit) != 0) { _rpr |= bit; triggered = true; }
        if (!rising && (_ftsr & bit) != 0) { _fpr |= bit; triggered = true; }

        if (triggered && (_imr & bit) != 0)
            RaiseIrq?.Invoke(IrqForLine(line), true);
    }

    private int SelectedPort(int line)
    {
        var reg = _exticr[line >> 2];
        return (int)((reg >> ((line & 3) * 8)) & 0xFF);
    }

    private static int IrqForLine(int line) => line switch
    {
        0 or 1 => 5,
        2 or 3 => 6,
        _ => 7,
    };

    private void UpdateIrqLines()
    {
        if (RaiseIrq == null) return;
        var pending = (_rpr | _fpr) & _imr;
        // Re-evaluate the three grouped IRQ lines.
        RaiseIrq(5, (pending & 0x0003) != 0);
        RaiseIrq(6, (pending & 0x000C) != 0);
        RaiseIrq(7, (pending & 0xFFF0) != 0);
    }

    public uint ReadWord(uint address)
    {
        var off = address & 0xFF;
        if (off is >= EXTICR0 and <= EXTICR3)
            return _exticr[(off - EXTICR0) >> 2];

        return off switch
        {
            RTSR1 => _rtsr,
            FTSR1 => _ftsr,
            RPR1 => _rpr,
            FPR1 => _fpr,
            IMR1 => _imr,
            EMR1 => _emr,
            _ => 0,
        };
    }

    public ushort ReadHalfWord(uint address) =>
        (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));

    public byte ReadByte(uint address) =>
        (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));

    public void WriteWord(uint address, uint value)
    {
        var off = address & 0xFF;
        if (off is >= EXTICR0 and <= EXTICR3)
        {
            _exticr[(off - EXTICR0) >> 2] = value;
            return;
        }

        switch (off)
        {
            case RTSR1: _rtsr = value; break;
            case FTSR1: _ftsr = value; break;
            case IMR1: _imr = value; UpdateIrqLines(); break;
            case EMR1: _emr = value; break;
            case RPR1: _rpr &= ~value; UpdateIrqLines(); break; // rc_w1: write 1 clears
            case FPR1: _fpr &= ~value; UpdateIrqLines(); break;
            case SWIER1:
                // Software trigger: behaves like a rising edge on the selected lines.
                var fire = value & 0xFFFF;
                _rpr |= fire;
                UpdateIrqLines();
                break;
        }
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
        var aligned = address & ~3u;
        var shift = (int)((address & 3) << 3);
        var current = ReadWord(aligned);
        WriteWord(aligned, (current & ~(0xFFu << shift)) | ((uint)value << shift));
    }
}
