using STM32.Core.Memory;

namespace STM32.Peripherals.Lptim;

/// <summary>
/// Low-power timer (LPTIM, RM0444 §29). STM32G0 has LPTIM1 @ 0x4000_7C00 and LPTIM2 @ 0x4000_9400;
/// the STM32L0 has LPTIM1. Register layout: ISR 0x00, ICR 0x04, IER 0x08, CFGR 0x0C, CR 0x10,
/// CMP 0x14, ARR 0x18, CNT 0x1C.
///
/// Modelled as a time-aware up-counter: while enabled (CR.ENABLE) and started (CR.CNTSTRT continuous
/// or CR.SNGSTRT single-shot), the counter advances one tick every 2^PRESC CPU cycles. Reaching CMP
/// sets ISR.CMPM, reaching ARR sets ISR.ARRM and reloads to 0 (single-shot stops there). Either flag
/// raises the shared LPTIM NVIC line when its IER bit is set. ICR clears flags (write-1-to-clear).
/// </summary>
public sealed class LptimPeripheral : IMemoryMappedDevice, ITickable
{
    private const uint ISR  = 0x00;
    private const uint ICR  = 0x04;
    private const uint IER  = 0x08;
    private const uint CFGR = 0x0C;
    private const uint CR   = 0x10;
    private const uint CMP  = 0x14;
    private const uint ARR  = 0x18;
    private const uint CNT  = 0x1C;

    private const uint ISR_CMPM = 1u << 0;
    private const uint ISR_ARRM = 1u << 1;
    private const uint IER_CMPMIE = 1u << 0;
    private const uint IER_ARRMIE = 1u << 1;

    private const uint CR_ENABLE  = 1u << 0;
    private const uint CR_SNGSTRT = 1u << 1;
    private const uint CR_CNTSTRT = 1u << 2;

    private const uint CFGR_PRESC = 0x7u << 9;

    public string Name { get; }
    public int Irq { get; }

    /// <summary>Set by the machine to assert/deassert this timer's (shared) NVIC IRQ.</summary>
    public Action<int, bool>? RaiseIrq;

    private uint _isr;
    private uint _ier;
    private uint _cfgr;
    private uint _cr;
    private uint _cmp;
    private uint _arr = 0x1; // reset value per RM
    private uint _cnt;
    private bool _running;
    private bool _continuous;
    private long _accum;

    public uint Size => 0x400;

    public LptimPeripheral(string name, int irq = -1)
    {
        Name = name;
        Irq = irq;
    }

    private int PrescaleShift => (int)((_cfgr & CFGR_PRESC) >> 9); // 2^PRESC
    private long CyclesPerTick => 1L << PrescaleShift;

    private void EvaluateIrq()
    {
        if (Irq < 0 || RaiseIrq == null) return;
        var pending =
            ((_ier & IER_CMPMIE) != 0 && (_isr & ISR_CMPM) != 0) ||
            ((_ier & IER_ARRMIE) != 0 && (_isr & ISR_ARRM) != 0);
        RaiseIrq(Irq, pending);
    }

    public void Tick(long deltaCycles)
    {
        if (!_running) return;
        _accum += deltaCycles;
        var per = CyclesPerTick;
        while (_accum >= per)
        {
            _accum -= per;
            _cnt = (_cnt + 1) & 0xFFFF;

            if (_cnt == (_cmp & 0xFFFF)) _isr |= ISR_CMPM;

            if (_cnt >= (_arr & 0xFFFF))
            {
                _isr |= ISR_ARRM;
                _cnt = 0;
                if (!_continuous) { _running = false; EvaluateIrq(); return; }
            }
        }
        EvaluateIrq();
    }

    public long NextEventInCycles()
    {
        if (!_running) return long.MaxValue;
        // Only worth stopping for if an enabled interrupt can fire.
        var watchCmp = (_ier & IER_CMPMIE) != 0;
        var watchArr = (_ier & IER_ARRMIE) != 0;
        if (!watchCmp && !watchArr) return long.MaxValue;

        var per = CyclesPerTick;
        long best = long.MaxValue;
        if (watchArr)
        {
            var ticks = ((_arr & 0xFFFF) - _cnt) & 0xFFFF;
            if (ticks == 0) ticks = (_arr & 0xFFFF);
            best = Math.Min(best, ticks * per - _accum);
        }
        if (watchCmp)
        {
            var target = _cmp & 0xFFFF;
            var ticks = (long)((target - _cnt) & 0xFFFF);
            if (ticks > 0) best = Math.Min(best, ticks * per - _accum);
        }
        return best < 1 ? 1 : best;
    }

    public uint ReadWord(uint address)
    {
        switch (address & 0xFF)
        {
            case ISR: return _isr;
            case IER: return _ier;
            case CFGR: return _cfgr;
            case CR: return _cr;
            case CMP: return _cmp;
            case ARR: return _arr;
            case CNT: return _cnt;
            default: return 0;
        }
    }

    public ushort ReadHalfWord(uint address) =>
        (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));

    public byte ReadByte(uint address) =>
        (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));

    public void WriteWord(uint address, uint value)
    {
        switch (address & 0xFF)
        {
            case ICR:
                _isr &= ~value; // write-1-to-clear
                EvaluateIrq();
                break;
            case IER:
                _ier = value;
                EvaluateIrq();
                break;
            case CFGR:
                _cfgr = value;
                break;
            case CR:
                _cr = value;
                if ((value & CR_ENABLE) == 0)
                {
                    _running = false;
                }
                else if ((value & (CR_SNGSTRT | CR_CNTSTRT)) != 0)
                {
                    _running = true;
                    _continuous = (value & CR_CNTSTRT) != 0;
                    _cnt = 0;
                    _accum = 0;
                }
                break;
            case CMP: _cmp = value & 0xFFFF; break;
            case ARR: _arr = value & 0xFFFF; break;
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
