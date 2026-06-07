using STM32.Core.Memory;

namespace STM32.Peripherals.Gpio;

/// <summary>
/// A single STM32 GPIO port (16 pins). STM32G0 ports are 0x400 apart starting at 0x5000_0000
/// (GPIOA = 0x5000_0000, GPIOB = 0x5000_0400, ...). Register layout (RM0444 §6.4):
///   MODER 0x00, OTYPER 0x04, OSPEEDR 0x08, PUPDR 0x0C, IDR 0x10, ODR 0x14,
///   BSRR 0x18, LCKR 0x1C, AFRL 0x20, AFRH 0x24, BRR 0x28.
///
/// Output pins drive their level from ODR; IDR reflects ODR for pins configured as outputs
/// (and external input for others, settable via <see cref="SetInput"/>). Level changes raise
/// <see cref="OnPinChange"/> so test probes can observe e.g. a blinking LED.
/// </summary>
public sealed class GpioPortPeripheral : IMemoryMappedDevice
{
    private const uint MODER  = 0x00;
    private const uint OTYPER = 0x04;
    private const uint OSPEEDR = 0x08;
    private const uint PUPDR  = 0x0C;
    private const uint IDR    = 0x10;
    private const uint ODR    = 0x14;
    private const uint BSRR   = 0x18;
    private const uint LCKR   = 0x1C;
    private const uint AFRL   = 0x20;
    private const uint AFRH   = 0x24;
    private const uint BRR    = 0x28;

    /// <summary>Port name for diagnostics, e.g. "A".</summary>
    public string Name { get; }

    /// <summary>Raised when an output pin level changes: (pin 0–15, level high?).</summary>
    public Action<int, bool>? OnPinChange;

    private uint _moder;
    private uint _otyper;
    private uint _ospeedr;
    private uint _pupdr;
    private uint _odr;
    private uint _afrl;
    private uint _afrh;
    private uint _externalIdr; // external input levels for pins not driven as outputs

    public uint Size => 0x400;

    public GpioPortPeripheral(string name) => Name = name;

    /// <summary>Drive an external input level on a pin (for pins configured as input).</summary>
    public void SetInput(int pin, bool high)
    {
        var bit = 1u << pin;
        if (high) _externalIdr |= bit; else _externalIdr &= ~bit;
    }

    /// <summary>Current output level of a pin (reads ODR bit).</summary>
    public bool GetOutput(int pin) => (_odr & (1u << pin)) != 0;

    private uint BuildIdr()
    {
        // For pins configured as output (MODER == 0b01), IDR reads back ODR; otherwise external.
        uint idr = 0;
        for (var pin = 0; pin < 16; pin++)
        {
            var mode = (_moder >> (pin * 2)) & 0x3;
            var bit = 1u << pin;
            if (mode == 0x1) // general-purpose output
                idr |= (_odr & bit);
            else
                idr |= (_externalIdr & bit);
        }
        return idr;
    }

    private void SetOdr(uint newOdr)
    {
        var changed = newOdr ^ _odr;
        _odr = newOdr;
        if (changed != 0 && OnPinChange != null)
        {
            for (var pin = 0; pin < 16; pin++)
                if ((changed & (1u << pin)) != 0)
                    OnPinChange(pin, (newOdr & (1u << pin)) != 0);
        }
    }

    public uint ReadWord(uint address)
    {
        return (address & 0xFF) switch
        {
            MODER => _moder,
            OTYPER => _otyper,
            OSPEEDR => _ospeedr,
            PUPDR => _pupdr,
            IDR => BuildIdr(),
            ODR => _odr,
            AFRL => _afrl,
            AFRH => _afrh,
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
            case MODER: _moder = value; break;
            case OTYPER: _otyper = value; break;
            case OSPEEDR: _ospeedr = value; break;
            case PUPDR: _pupdr = value; break;
            case ODR: SetOdr(value & 0xFFFF); break;
            case AFRL: _afrl = value; break;
            case AFRH: _afrh = value; break;

            case BSRR:
                // bits[15:0] set ODR, bits[31:16] reset ODR (reset wins on conflict per RM).
                SetOdr((_odr | (value & 0xFFFF)) & ~(value >> 16));
                break;

            case BRR:
                SetOdr(_odr & ~(value & 0xFFFF));
                break;

            case LCKR:
                // Pin configuration lock is not modeled; accept and ignore.
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
