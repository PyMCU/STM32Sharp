using STM32.Core.Memory;

namespace STM32.Peripherals.Watchdog;

/// <summary>
/// Window watchdog (WWDG) for STM32G0. Base 0x4000_2C00. Registers (RM0444 §26):
///   CR 0x00, CFR 0x04, SR 0x08.
///
/// CR holds a 7-bit down-counter T[6:0] and the activation bit WDGA. Once active, the counter
/// decrements on the WWDG clock (PCLK ÷4096 ÷2^WDGTB); a reset occurs when T6 clears (counter
/// rolls under 0x40) or when software refreshes while the counter is still above the window W[6:0].
/// <see cref="OnTimeout"/> fires on either condition. The counter is advanced in CPU cycles via
/// <see cref="ITickable"/> for deterministic tests.
/// </summary>
public sealed class WwdgPeripheral : IMemoryMappedDevice, ITickable
{
    private const uint CR  = 0x00;
    private const uint CFR = 0x04;
    private const uint SR  = 0x08;

    private const uint WDGA = 1u << 7;
    private const uint T6   = 1u << 6;

    /// <summary>Fires on a window violation or counter underflow (a system reset on real HW).</summary>
    public Action? OnTimeout;

    private uint _cr = 0x7F;   // counter reset value
    private uint _cfr = 0x7F;  // window reset value
    private uint _sr;
    private bool _active;
    private long _cycleAccum;

    public uint Size => 0x400;

    private long ClockDivider => 4096L * (1L << (int)((_cfr >> 11) & 0x3)); // WDGTB at bits [12:11]

    /// <summary>Cycles until the down-counter next decrements (it may reset the system then).</summary>
    public long NextEventInCycles() => _active ? ClockDivider - _cycleAccum : long.MaxValue;

    public void Tick(long deltaCycles)
    {
        if (!_active) return;

        _cycleAccum += deltaCycles;
        while (_cycleAccum >= ClockDivider)
        {
            _cycleAccum -= ClockDivider;
            var t = _cr & 0x7F;
            if (t == 0x3F) // about to clear T6 (0x40 → 0x3F already means underflow next step)
            {
                _cr = (_cr & ~0x7Fu) | 0x3F;
                Trigger();
                return;
            }
            _cr = (_cr & ~0x7Fu) | ((t - 1) & 0x7F);
        }
    }

    private void Trigger()
    {
        _active = false;
        OnTimeout?.Invoke();
    }

    public uint ReadWord(uint address)
    {
        return (address & 0xFF) switch
        {
            CR => _cr,
            CFR => _cfr,
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
        switch (address & 0xFF)
        {
            case CR:
                var newCount = value & 0x7F;
                if (_active)
                {
                    // Refreshing while the counter is still above the window is a reset condition.
                    if ((_cr & 0x7F) > (_cfr & 0x7F))
                    {
                        Trigger();
                        return;
                    }
                }
                if ((value & WDGA) != 0) _active = true;
                _cr = (value & WDGA) | newCount;
                break;

            case CFR:
                _cfr = value & 0x1FFF;
                break;

            case SR:
                _sr &= ~value; // write-1-to-clear EWIF
                break;
        }
    }

    public void WriteHalfWord(uint address, ushort value) => WriteWord(address, value);
    public void WriteByte(uint address, byte value) => WriteWord(address, value);
}
