using STM32.Core.Memory;

namespace STM32.Peripherals.Watchdog;

/// <summary>
/// Independent watchdog (IWDG) for STM32G0. Base 0x4000_3000. Registers (RM0444 §25):
///   KR 0x00, PR 0x04, RLR 0x08, SR 0x0C, WINR 0x10.
///
/// Software starts it with KR = 0xCCCC, unlocks PR/RLR with KR = 0x5555 and refreshes ("kicks")
/// it with KR = 0xAAAA. The down-counter is reloaded from RLR scaled by the PR prescaler (÷4…÷256).
/// If it reaches zero without a refresh, <see cref="OnTimeout"/> fires (a system reset on real HW).
/// For determinism the counter is measured in CPU cycles and advanced via <see cref="ITickable"/>.
/// </summary>
public sealed class IwdgPeripheral : IMemoryMappedDevice, ITickable
{
    private const uint KR   = 0x00;
    private const uint PR   = 0x04;
    private const uint RLR  = 0x08;
    private const uint SR   = 0x0C;
    private const uint WINR = 0x10;

    private const uint KEY_REFRESH = 0xAAAA;
    private const uint KEY_ACCESS  = 0x5555;
    private const uint KEY_START   = 0xCCCC;

    /// <summary>Fires when the watchdog times out without being refreshed.</summary>
    public Action? OnTimeout;

    private uint _pr;
    private uint _rlr = 0xFFF;     // reset value
    private uint _winr = 0xFFF;
    private bool _running;
    private bool _accessEnabled;
    private long _counter;

    public uint Size => 0x400;

    private uint PrescalerDivider => 4u << (int)(_pr & 0x7); // PR=0→4, 1→8, … 6→256

    private long ReloadCycles => (_rlr + 1) * PrescalerDivider;

    private void Reload() => _counter = ReloadCycles;

    /// <summary>Cycles until the watchdog times out and resets the system (when running).</summary>
    public long NextEventInCycles() => _running ? (_counter > 0 ? _counter : 1) : long.MaxValue;

    public void Tick(long deltaCycles)
    {
        if (!_running) return;
        _counter -= deltaCycles;
        if (_counter <= 0)
        {
            _running = false;
            OnTimeout?.Invoke();
        }
    }

    public uint ReadWord(uint address)
    {
        return (address & 0xFF) switch
        {
            PR => _pr,
            RLR => _rlr,
            SR => 0, // PVU/RVU/WVU always ready
            WINR => _winr,
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
            case KR:
                switch (value & 0xFFFF)
                {
                    case KEY_START: _running = true; Reload(); break;
                    case KEY_ACCESS: _accessEnabled = true; break;
                    case KEY_REFRESH: Reload(); break;
                }
                break;
            case PR: if (_accessEnabled) _pr = value & 0x7; break;
            case RLR: if (_accessEnabled) _rlr = value & 0xFFF; break;
            case WINR:
                if (_accessEnabled)
                {
                    _winr = value & 0xFFF;
                    Reload(); // writing WINR also refreshes (RM0444 §25.4.6)
                }
                break;
        }
    }

    public void WriteHalfWord(uint address, ushort value) => WriteWord(address, value);
    public void WriteByte(uint address, byte value) => WriteWord(address, value);
}
