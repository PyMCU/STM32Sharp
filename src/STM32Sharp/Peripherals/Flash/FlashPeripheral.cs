using STM32.Core.Memory;

namespace STM32.Peripherals.Flash;

/// <summary>
/// Embedded Flash interface registers (FLASH) for STM32G0. Base address 0x4002_2000.
///
/// Models the register block that configures wait states, unlocks the controller and drives
/// page erase / word programming of the Flash array (the array itself is the fast-path buffer in
/// <see cref="BusInterconnect"/>). Programming semantics follow RM0444 §3.7:
///   - FLASH_KEYR: write 0x45670123 then 0xCDEF89AB to clear CR.LOCK.
///   - CR.PER + CR.PNB + CR.STRT: erase the selected 2 KB page to 0xFF.
///   - CR.PG: subsequent writes to the Flash region are programmed (bits clear only).
///   - SR.BSY always reads 0 (operations are instantaneous); SR.EOP is set after each op.
/// </summary>
public sealed class FlashPeripheral : IMemoryMappedDevice
{
    private const uint FLASH_ACR  = 0x00; // Access control (latency, prefetch, caches)
    private const uint FLASH_KEYR = 0x08; // Key register (unlock)
    private const uint FLASH_SR   = 0x10; // Status (BSY, EOP, error flags)
    private const uint FLASH_CR   = 0x14; // Control (PG, PER, PNB, STRT, LOCK)

    private const uint KEY1 = 0x45670123;
    private const uint KEY2 = 0xCDEF89AB;

    // CR bits (STM32G0)
    private const uint CR_PG   = 1u << 0;
    private const uint CR_PER  = 1u << 1;
    private const uint CR_STRT = 1u << 16;
    private const uint CR_LOCK = 1u << 31;
    private const uint CR_PNB_MASK = 0x3Fu << 3; // page number, bits [8:3]

    // SR bits
    private const uint SR_EOP = 1u << 0;

    private const uint PAGE_SIZE = 2048; // 2 KB pages on STM32G0

    private readonly BusInterconnect _bus;

    private uint _acr;
    private uint _cr = CR_LOCK; // locked out of reset
    private uint _sr;
    private int _keyState; // 0 = expect KEY1, 1 = expect KEY2

    public uint Size => 0x400;

    public FlashPeripheral(BusInterconnect bus) => _bus = bus;

    public uint ReadWord(uint address)
    {
        return (address & 0xFF) switch
        {
            FLASH_ACR => _acr,
            FLASH_SR => _sr,           // BSY (bit 16) always 0
            FLASH_CR => _cr,
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
            case FLASH_ACR:
                _acr = value;
                break;

            case FLASH_KEYR:
                if (_keyState == 0 && value == KEY1) _keyState = 1;
                else if (_keyState == 1 && value == KEY2) { _cr &= ~CR_LOCK; _keyState = 0; }
                else _keyState = 0;
                break;

            case FLASH_SR:
                _sr &= ~value; // write-1-to-clear (EOP, error flags)
                break;

            case FLASH_CR:
                if ((_cr & CR_LOCK) != 0)
                    return; // controller locked: ignore (real HW would fault)

                _cr = value;
                // Enable/disable Flash-array programming on the bus while PG is set.
                _bus.FlashWriteEnabled = (value & CR_PG) != 0;

                // Page erase: PER + STRT.
                if ((value & CR_PER) != 0 && (value & CR_STRT) != 0)
                {
                    var page = (value & CR_PNB_MASK) >> 3;
                    _bus.EraseFlash(page * PAGE_SIZE, PAGE_SIZE);
                    _cr &= ~CR_STRT;
                    _sr |= SR_EOP;
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
