using STM32.Core.Memory;

namespace STM32.Peripherals.Timer;

/// <summary>
/// General-purpose timer (TIMx) for STM32G0, e.g. TIM2 @ 0x4000_0000, TIM3 @ 0x4000_0400.
/// Register subset (RM0444 §27): CR1 0x00, DIER 0x0C, SR 0x10, EGR 0x14, CCMR1 0x18, CCMR2 0x1C,
/// CCER 0x20, CNT 0x24, PSC 0x28, ARR 0x2C, CCR1 0x34, CCR2 0x38, CCR3 0x3C, CCR4 0x40.
///
/// The counter advances one tick every (PSC+1) CPU cycles while CEN is set; on reaching ARR it
/// wraps to 0 and raises the update flag (SR.UIF). Each capture/compare channel (1–4) supports:
///   • Output compare / PWM: when CNT matches CCRx the compare flag CCxIF is set; in PWM mode 1/2
///     the channel output level (exposed via <see cref="OnChannelOutput"/>) tracks CNT vs CCRx.
///   • Input capture: <see cref="CaptureInput"/> latches CNT into CCRx and sets CCxIF.
/// Update and capture/compare events are reported to the NVIC via <see cref="RaiseIrq"/> when their
/// DIER enable bits are set.
/// </summary>
public sealed class TimerPeripheral : IMemoryMappedDevice, ITickable
{
    private const uint CR1   = 0x00;
    private const uint DIER  = 0x0C;
    private const uint SR    = 0x10;
    private const uint EGR   = 0x14;
    private const uint CCMR1 = 0x18;
    private const uint CCMR2 = 0x1C;
    private const uint CCER  = 0x20;
    private const uint CNT   = 0x24;
    private const uint PSC   = 0x28;
    private const uint ARR   = 0x2C;
    private const uint CCR1  = 0x34;
    private const uint CCR2  = 0x38;
    private const uint CCR3  = 0x3C;
    private const uint CCR4  = 0x40;

    private const uint CR1_CEN = 1u << 0;
    private const uint DIER_UIE = 1u << 0;          // update interrupt enable
    private const uint SR_UIF = 1u << 0;            // update flag
    private const uint EGR_UG = 1u << 0;

    public string Name { get; }
    public int Irq { get; }

    /// <summary>Set by the machine to assert/deassert this timer's NVIC IRQ.</summary>
    public Action<int, bool>? RaiseIrq;

    /// <summary>Raised when a PWM/output-compare channel level changes: (channel 1–4, active?).</summary>
    public Action<int, bool>? OnChannelOutput;

    private uint _cr1;
    private uint _dier;
    private uint _sr;
    private uint _cnt;
    private uint _psc;
    private uint _arr = 0xFFFF; // reset value
    private uint _ccmr1;
    private uint _ccmr2;
    private uint _ccer;
    private readonly uint[] _ccr = new uint[5]; // 1..4 used
    private readonly bool[] _chOut = new bool[5];
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
            if (_cnt >= _arr) _cnt = 0;
            else _cnt++;

            if (_cnt == 0)
            {
                _sr |= SR_UIF;
                EvaluateIrq();
            }
            CompareChannels();
        }
    }

    /// <summary>
    /// Cycles until the counter next advances — but only while something observes it (an interrupt is
    /// enabled in DIER, or a PWM/output-compare consumer is attached). Otherwise the counter just needs
    /// to be up to date on the next read, so we let the engine run freely and catch up in bulk.
    /// </summary>
    public long NextEventInCycles()
    {
        if ((_cr1 & CR1_CEN) == 0) return long.MaxValue;
        if (_dier == 0 && OnChannelOutput == null) return long.MaxValue;
        return (long)_psc + 1 - _prescalerAccum;
    }

    // ── Output compare / PWM ───────────────────────────────────────────

    private int OcMode(int ch)
    {
        // OCxM bits: CCMR1 has CH1 (bits 4-6) and CH2 (bits 12-14); CCMR2 has CH3/CH4.
        var ccmr = ch <= 2 ? _ccmr1 : _ccmr2;
        var shift = (ch is 1 or 3) ? 4 : 12;
        return (int)((ccmr >> shift) & 0x7);
    }

    private int CaptureSelect(int ch)
    {
        // CCxS bits select capture (non-zero) vs compare (0).
        var ccmr = ch <= 2 ? _ccmr1 : _ccmr2;
        var shift = (ch is 1 or 3) ? 0 : 8;
        return (int)((ccmr >> shift) & 0x3);
    }

    private void CompareChannels()
    {
        for (var ch = 1; ch <= 4; ch++)
        {
            if (CaptureSelect(ch) != 0) continue; // channel in input mode

            // Compare match sets the CCxIF flag (SR bit ch).
            if (_cnt == _ccr[ch])
            {
                _sr |= 1u << ch;
                EvaluateIrq();
            }

            // PWM mode 1 (110): active while CNT < CCRx. PWM mode 2 (111): inverse.
            var mode = OcMode(ch);
            if (mode is 6 or 7)
            {
                var active = _cnt < _ccr[ch];
                if (mode == 7) active = !active;
                if (active != _chOut[ch])
                {
                    _chOut[ch] = active;
                    OnChannelOutput?.Invoke(ch, active);
                }
            }
        }
    }

    /// <summary>Current PWM/output-compare level of a channel (1–4).</summary>
    public bool ChannelOutput(int channel) => _chOut[channel];

    // ── Input capture ──────────────────────────────────────────────────

    /// <summary>
    /// Simulate an active edge on a channel's input: latch CNT into CCRx and set CCxIF
    /// (and the IRQ when its capture/compare interrupt is enabled).
    /// </summary>
    public void CaptureInput(int channel)
    {
        if (channel is < 1 or > 4) return;
        if (CaptureSelect(channel) == 0) return; // not configured as input
        _ccr[channel] = _cnt;
        _sr |= 1u << channel; // CCxIF
        EvaluateIrq();
    }

    private void EvaluateIrq()
    {
        if (Irq < 0 || RaiseIrq == null) return;
        // Update IRQ or any enabled capture/compare flag.
        var pending = ((_sr & SR_UIF) != 0 && (_dier & DIER_UIE) != 0);
        for (var ch = 1; ch <= 4; ch++)
            if ((_sr & (1u << ch)) != 0 && (_dier & (1u << ch)) != 0)
                pending = true;
        RaiseIrq(Irq, pending);
    }

    public uint ReadWord(uint address)
    {
        return (address & 0xFF) switch
        {
            CR1 => _cr1,
            DIER => _dier,
            SR => _sr,
            CCMR1 => _ccmr1,
            CCMR2 => _ccmr2,
            CCER => _ccer,
            CNT => _cnt,
            PSC => _psc,
            ARR => _arr,
            CCR1 => _ccr[1],
            CCR2 => _ccr[2],
            CCR3 => _ccr[3],
            CCR4 => _ccr[4],
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
            case CCMR1: _ccmr1 = value; break;
            case CCMR2: _ccmr2 = value; break;
            case CCER: _ccer = value; break;
            case CNT: _cnt = value & 0xFFFF; break;
            case PSC: _psc = value & 0xFFFF; break;
            case ARR: _arr = value & 0xFFFF; break;
            case CCR1: _ccr[1] = value & 0xFFFF; break;
            case CCR2: _ccr[2] = value & 0xFFFF; break;
            case CCR3: _ccr[3] = value & 0xFFFF; break;
            case CCR4: _ccr[4] = value & 0xFFFF; break;
            case EGR:
                if ((value & EGR_UG) != 0)
                {
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
