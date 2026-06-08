using STM32.Core.Memory;

namespace STM32.Peripherals.Rcc;

/// <summary>
/// Reset and Clock Control (RCC) for STM32L0. Base address 0x4002_1000.
///
/// The L0 clock tree differs from the G0: it boots on the MSI oscillator, the ready/enable bits sit
/// at different positions (MSI at CR[9:8], HSI at CR[2:0], HSE at CR[17:16], PLL at CR[25:24]), the
/// system-clock switch field SW/SWS is two bits wide (CFGR[1:0] / CFGR[3:2]), and there is a separate
/// HSI48 recovery clock in CRRCR. As with the G0 RCC, we mirror every oscillator-enable into its
/// ready flag synchronously so HAL clock setup never deadlocks; all other registers read back verbatim.
/// </summary>
public sealed class RccL0Peripheral : IMemoryMappedDevice
{
    // Register offsets (RM0451 §7.3)
    private const uint RCC_CR    = 0x00; // Clock control
    private const uint RCC_CRRCR = 0x08; // Clock recovery RC (HSI48)
    private const uint RCC_CFGR  = 0x0C; // Clock configuration

    // RCC_CR bits (L0)
    private const uint HSION  = 1u << 0;
    private const uint HSIRDY = 1u << 2;
    private const uint MSION  = 1u << 8;
    private const uint MSIRDY = 1u << 9;
    private const uint HSEON  = 1u << 16;
    private const uint HSERDY = 1u << 17;
    private const uint PLLON  = 1u << 24;
    private const uint PLLRDY = 1u << 25;

    // RCC_CRRCR bits (HSI48)
    private const uint HSI48ON  = 1u << 0;
    private const uint HSI48RDY = 1u << 1;

    private readonly uint[] _regs = new uint[0x100 / 4];

    public uint Size => 0x400;

    public RccL0Peripheral()
    {
        // Reset state: MSI on and ready (RCC_CR reset value 0x0000_0300 on STM32L0; SWS = MSI = 00).
        _regs[RCC_CR >> 2] = MSION | MSIRDY;
    }

    public uint ReadWord(uint address) => _regs[(address & 0xFF) >> 2];

    public ushort ReadHalfWord(uint address) =>
        (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));

    public byte ReadByte(uint address) =>
        (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));

    public void WriteWord(uint address, uint value)
    {
        var offset = address & 0xFF;
        switch (offset)
        {
            case RCC_CR:
                // Each *RDY flag tracks its *ON bit (set on enable, cleared on disable) so the HAL's
                // "turn oscillator off and wait until *RDY clears" loops complete (see RccPeripheral).
                value = (value & HSION) != 0 ? value | HSIRDY : value & ~HSIRDY;
                value = (value & MSION) != 0 ? value | MSIRDY : value & ~MSIRDY;
                value = (value & HSEON) != 0 ? value | HSERDY : value & ~HSERDY;
                value = (value & PLLON) != 0 ? value | PLLRDY : value & ~PLLRDY;
                _regs[RCC_CR >> 2] = value;
                break;

            case RCC_CRRCR:
                value = (value & HSI48ON) != 0 ? value | HSI48RDY : value & ~HSI48RDY;
                _regs[RCC_CRRCR >> 2] = value;
                break;

            case RCC_CFGR:
                // Reflect SW[1:0] (system clock switch) into SWS[3:2] so HAL's wait loop completes.
                var sw = value & 0x3u;
                value = (value & ~0xCu) | (sw << 2);
                _regs[RCC_CFGR >> 2] = value;
                break;

            default:
                _regs[offset >> 2] = value;
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
