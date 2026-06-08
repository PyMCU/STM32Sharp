using STM32.Core.Memory;

namespace STM32.Peripherals.Rcc;

/// <summary>
/// Reset and Clock Control (RCC) for STM32G0. Base address 0x4002_1000.
///
/// This is the single most important peripheral for booting real STM32Cube/HAL firmware:
/// the HAL spins waiting for clock-ready flags (HSIRDY/HSERDY/PLLRDY) and for the system
/// clock switch status (SWS) to match the requested source (SW). We model those handshakes
/// synchronously — the moment firmware turns an oscillator on, we report it ready — so the
/// boot sequence never deadlocks. All other registers (peripheral clock enables, etc.) are
/// stored and read back verbatim.
/// </summary>
public sealed class RccPeripheral : IMemoryMappedDevice
{
    // Register offsets (STM32G0 reference manual RM0444 §5.4)
    private const uint RCC_CR   = 0x00; // Clock control
    private const uint RCC_CFGR = 0x08; // Clock configuration
    private const uint RCC_BDCR = 0x5C; // Backup domain control (LSE)
    private const uint RCC_CSR  = 0x60; // Control/status (LSI)

    // RCC_CR bits
    private const uint HSION  = 1u << 8;
    private const uint HSIRDY = 1u << 10;
    private const uint HSEON  = 1u << 16;
    private const uint HSERDY = 1u << 17;
    private const uint PLLON  = 1u << 24;
    private const uint PLLRDY = 1u << 25;

    // BDCR/CSR low-speed oscillator bits (LSEON→LSERDY in BDCR, LSION→LSIRDY in CSR)
    private const uint LSEON  = 1u << 0;
    private const uint LSERDY = 1u << 1;
    private const uint LSION  = 1u << 0;
    private const uint LSIRDY = 1u << 1;

    private readonly uint[] _regs = new uint[0x100 / 4];

    public uint Size => 0x400;

    public RccPeripheral()
    {
        // Reset state: HSI16 on and ready (RCC_CR reset value 0x0000_0500 on STM32G0).
        _regs[RCC_CR >> 2] = HSION | HSIRDY;
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
                // Each *RDY flag tracks its *ON bit: set when the oscillator is enabled, cleared when
                // turned off. The HAL not only waits for ready after enabling, it also disables an
                // oscillator (e.g. the PLL) and spins until its *RDY clears before reconfiguring — so
                // the flag must drop too, otherwise that second wait loop never completes.
                value = (value & HSION) != 0 ? value | HSIRDY : value & ~HSIRDY;
                value = (value & HSEON) != 0 ? value | HSERDY : value & ~HSERDY;
                value = (value & PLLON) != 0 ? value | PLLRDY : value & ~PLLRDY;
                _regs[RCC_CR >> 2] = value;
                break;

            case RCC_CFGR:
                // Reflect the selected system clock (SW bits [2:0]) into the status
                // field (SWS bits [5:3]) so HAL_RCC_ClockConfig()'s wait loop completes.
                var sw = value & 0x7u;
                value = (value & ~0x38u) | (sw << 3);
                _regs[RCC_CFGR >> 2] = value;
                break;

            case RCC_BDCR:
                value = (value & LSEON) != 0 ? value | LSERDY : value & ~LSERDY;
                _regs[RCC_BDCR >> 2] = value;
                break;

            case RCC_CSR:
                value = (value & LSION) != 0 ? value | LSIRDY : value & ~LSIRDY;
                _regs[RCC_CSR >> 2] = value;
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
