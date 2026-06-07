using STM32.Core.Memory;

namespace STM32.Peripherals.Rtc;

/// <summary>
/// Real-time clock (RTC) for STM32G0. Base 0x4000_2800. Register subset (RM0444 §29):
///   TR 0x00, DR 0x04, SSR 0x08, ICSR 0x0C, PRER 0x10, CR 0x18, WPR 0x24,
///   ALRMAR 0x40, SR 0x50, SCR 0x5C.
///
/// The calendar is held in BCD in TR (time) and DR (date). Software unlocks the registers by
/// writing 0xCA then 0x53 to WPR, enters initialization (ICSR.INIT → INITF) to set the calendar,
/// then leaves it to let time advance. Time advances one second per (PREDIV_A+1)·(PREDIV_S+1)
/// RTC-clock ticks; <see cref="AdvanceSeconds"/> is provided for deterministic tests. Alarm A
/// (ALRMAR, with field masks) sets SR.ALRAF and, when enabled, raises the RTC IRQ.
/// </summary>
public sealed class RtcPeripheral : IMemoryMappedDevice, ITickable
{
    private const uint TR     = 0x00;
    private const uint DR     = 0x04;
    private const uint SSR    = 0x08;
    private const uint ICSR   = 0x0C;
    private const uint PRER   = 0x10;
    private const uint CR     = 0x18;
    private const uint WPR    = 0x24;
    private const uint ALRMAR = 0x40;
    private const uint SR     = 0x50;
    private const uint SCR    = 0x5C;

    // ICSR bits
    private const uint ICSR_INITF = 1u << 6;
    private const uint ICSR_INIT  = 1u << 7;
    private const uint ICSR_RSF   = 1u << 5;

    // CR bits
    private const uint CR_ALRAE  = 1u << 8;  // alarm A enable
    private const uint CR_ALRAIE = 1u << 12; // alarm A interrupt enable

    // SR / alarm
    private const uint SR_ALRAF = 1u << 0;

    // ALRMAR field-mask bits
    private const uint MSK1 = 1u << 7;   // seconds masked
    private const uint MSK2 = 1u << 15;  // minutes masked
    private const uint MSK3 = 1u << 23;  // hours masked
    private const uint MSK4 = 1u << 31;  // date masked

    /// <summary>STM32G0 RTC_TAMP IRQ line.</summary>
    public int Irq { get; } = 2;

    /// <summary>Set by the machine to assert/deassert the RTC IRQ.</summary>
    public Action<int, bool>? RaiseIrq;

    /// <summary>RTC clock ticks per second = (PREDIV_A+1)·(PREDIV_S+1) by default 32768.</summary>
    public long TicksPerSecond { get; set; } = 32768;

    private uint _tr; // BCD time
    private uint _dr = 0x2101_0000 | 0x01 | (0x01 << 8); // 2021-01-01 default (year 21, month 1, day 1)
    private uint _prer = (0x7Fu << 16) | 0xFF; // reset value
    private uint _cr;
    private uint _icsr = ICSR_RSF;
    private uint _alrmar;
    private uint _sr;
    private bool _unlocked;
    private int _wprState;
    private long _subSecondAccum;

    public uint Size => 0x400;

    private static uint ToBcd(int v) => (uint)(((v / 10) << 4) | (v % 10));
    private static int FromBcd(uint v) => (int)(((v >> 4) & 0xF) * 10 + (v & 0xF));

    /// <summary>Advance the calendar by whole seconds (deterministic test helper).</summary>
    public void AdvanceSeconds(int seconds)
    {
        for (var i = 0; i < seconds; i++)
            IncrementOneSecond();
    }

    public void Tick(long deltaCycles)
    {
        if ((_icsr & ICSR_INIT) != 0) return; // calendar frozen during init

        _subSecondAccum += deltaCycles;
        while (_subSecondAccum >= TicksPerSecond)
        {
            _subSecondAccum -= TicksPerSecond;
            IncrementOneSecond();
        }
    }

    private void IncrementOneSecond()
    {
        var s = FromBcd(_tr & 0x7F);
        var mn = FromBcd((_tr >> 8) & 0x7F);
        var h = FromBcd((_tr >> 16) & 0x3F);

        if (++s >= 60) { s = 0; if (++mn >= 60) { mn = 0; if (++h >= 24) { h = 0; IncrementDay(); } } }

        _tr = ToBcd(s) | (ToBcd(mn) << 8) | (ToBcd(h) << 16);
        CheckAlarm();
    }

    private void IncrementDay()
    {
        var d = FromBcd(_dr & 0x3F);
        var mo = FromBcd((_dr >> 8) & 0x1F);
        var y = FromBcd((_dr >> 16) & 0xFF);
        if (++d > 28) { d = 1; if (++mo > 12) { mo = 1; y = (y + 1) % 100; } }
        _dr = (_dr & 0xFFFF_0000 & ~0x1FFFu) | ToBcd(d) | (ToBcd(mo) << 8) | (ToBcd(y) << 16);
    }

    private void CheckAlarm()
    {
        if ((_cr & CR_ALRAE) == 0) return;

        var match = true;
        if ((_alrmar & MSK1) == 0 && (_alrmar & 0x7F) != (_tr & 0x7F)) match = false;
        if ((_alrmar & MSK2) == 0 && ((_alrmar >> 8) & 0x7F) != ((_tr >> 8) & 0x7F)) match = false;
        if ((_alrmar & MSK3) == 0 && ((_alrmar >> 16) & 0x3F) != ((_tr >> 16) & 0x3F)) match = false;
        // Date field (MSK4) compared against day-of-month only for simplicity.
        if ((_alrmar & MSK4) == 0 && ((_alrmar >> 24) & 0x3F) != (_dr & 0x3F)) match = false;

        if (match)
        {
            _sr |= SR_ALRAF;
            if ((_cr & CR_ALRAIE) != 0)
                RaiseIrq?.Invoke(Irq, true);
        }
    }

    public uint ReadWord(uint address)
    {
        return (address & 0xFF) switch
        {
            TR => _tr,
            DR => _dr,
            SSR => (uint)(TicksPerSecond - 1 - _subSecondAccum % TicksPerSecond),
            ICSR => _icsr,
            PRER => _prer,
            CR => _cr,
            ALRMAR => _alrmar,
            SR => _sr,
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

        if (off == WPR)
        {
            var key = value & 0xFF;
            if (_wprState == 0 && key == 0xCA) _wprState = 1;
            else if (_wprState == 1 && key == 0x53) { _unlocked = true; _wprState = 0; }
            else { _wprState = 0; _unlocked = false; }
            return;
        }

        // SCR / SR clear path is always available (write-1-to-clear of alarm flag).
        if (off == SCR)
        {
            if ((value & SR_ALRAF) != 0)
            {
                _sr &= ~SR_ALRAF;
                RaiseIrq?.Invoke(Irq, false);
            }
            return;
        }

        if (off == ICSR)
        {
            if ((value & ICSR_INIT) != 0) _icsr |= ICSR_INIT | ICSR_INITF;
            else _icsr = (_icsr & ~(ICSR_INIT | ICSR_INITF)) | ICSR_RSF;
            return;
        }

        if (!_unlocked) return; // calendar registers are write-protected

        switch (off)
        {
            case TR: _tr = value & 0x007F7F7F; break;
            case DR: _dr = value; break;
            case PRER:
                _prer = value;
                var preDivA = ((value >> 16) & 0x7F) + 1;
                var preDivS = (value & 0x7FFF) + 1;
                TicksPerSecond = preDivA * preDivS;
                break;
            case CR: _cr = value; break;
            case ALRMAR: _alrmar = value; break;
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
