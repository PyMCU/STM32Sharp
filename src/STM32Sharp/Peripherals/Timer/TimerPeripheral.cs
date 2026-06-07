using STM32.Core.Memory;

namespace STM32.Peripherals.Timer;

/// <summary>
/// General-purpose timer (TIMx) for STM32G0, e.g. TIM2 @ 0x4000_0000, TIM3 @ 0x4000_0400.
/// Register subset (RM0444 §27): CR1 0x00, DIER 0x0C, SR 0x10, EGR 0x14, CNT 0x24,
/// PSC 0x28, ARR 0x2C.
///
/// The counter advances one tick every (PSC+1) CPU cycles while CEN is set; on reaching ARR it
/// wraps to 0 and raises the update flag (SR.UIF). When DIER.UIE is set, the update event is
/// reported to the NVIC via <see cref="RaiseIrq"/> (level-style: asserted while UIF&amp;UIE hold).
/// </summary>
public sealed class TimerPeripheral : IMemoryMappedDevice, ITickable
{
    private const uint CR1  = 0x00;
    private const uint DIER = 0x0C;
    private const uint SR   = 0x10;
    private const uint EGR  = 0x14;
    private const uint CNT  = 0x24;
    private const uint PSC  = 0x28;
    private const uint ARR  = 0x2C;

    private const uint CR1_CEN = 1u << 0;
    private const uint DIER_UIE = 1u << 0;
    private const uint SR_UIF = 1u << 0;
    private const uint EGR_UG = 1u << 0;

    public string Name { get; }
    public int Irq { get; }

    /// <summary>Set by the machine to assert/deassert this timer's NVIC IRQ.</summary>
    public Action<int, bool>? RaiseIrq;

    private uint _cr1;
    private uint _dier;
    private uint _sr;
    private uint _cnt;
    private uint _psc;
    private uint _arr = 0xFFFF; // reset value
    private long _prescalerAccum;

    public uint Size => 0x400;

    public TimerPeripheral(string name, int irq = -1)
    {
        Name = name;
        Irq = irq;
    }

    public void Tick(long deltaCycles)
    {
        if ((_cr1 & CR1_CEN) == 0) return;

        _prescalerAccum += deltaCycles;
        var divisor = (long)_psc + 1;

        while (_prescalerAccum >= divisor)
        {
            _prescalerAccum -= divisor;
            if (_cnt >= _arr)
            {
                _cnt = 0;
                _sr |= SR_UIF;
                EvaluateIrq();
            }
            else
            {
                _cnt++;
            }
        }
    }

    private void EvaluateIrq()
    {
        if (Irq < 0 || RaiseIrq == null) return;
        var pending = (_sr & SR_UIF) != 0 && (_dier & DIER_UIE) != 0;
        RaiseIrq(Irq, pending);
    }

    public uint ReadWord(uint address)
    {
        return (address & 0xFF) switch
        {
            CR1 => _cr1,
            DIER => _dier,
            SR => _sr,
            CNT => _cnt,
            PSC => _psc,
            ARR => _arr,
            _ => 0,
        };
    }

    public ushort ReadHalfWord(uint address) =>
        (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));

    public byte ReadByte(uint address) =>
        (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));

    public void WriteWord(uint address, uint value)
    {
        switch (address & 0xFF)
        {
            case CR1: _cr1 = value; break;
            case DIER: _dier = value; EvaluateIrq(); break;
            case SR: _sr &= value; EvaluateIrq(); break; // rc_w0: write 0 clears flags
            case CNT: _cnt = value & 0xFFFF; break;
            case PSC: _psc = value & 0xFFFF; break;
            case ARR: _arr = value & 0xFFFF; break;
            case EGR:
                if ((value & EGR_UG) != 0)
                {
                    // Update generation: reset counter and prescaler, latch registers.
                    _cnt = 0;
                    _prescalerAccum = 0;
                }
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
