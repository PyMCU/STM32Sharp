using STM32.Core.Memory;

namespace STM32.Peripherals.Flash;

/// <summary>
/// Embedded Flash interface registers (FLASH) for STM32L0. Base address 0x4002_2000.
///
/// The L0 controller is unlocked in two stages (RM0451 §3): write PEKEY1/PEKEY2 to PEKEYR to clear
/// PECR.PELOCK (enables PECR and data-EEPROM access), then PRGKEY1/PRGKEY2 to PRGKEYR to clear
/// PECR.PRGLOCK (enables program memory). Programming is driven through PECR (PROG/ERASE) rather than
/// the G0's CR/PNB/STRT scheme. We model the unlock handshake, ACR wait states and a ready status so
/// HAL boot/Flash routines complete; word programming gates <see cref="BusInterconnect.FlashWriteEnabled"/>.
/// </summary>
public sealed class FlashL0Peripheral : IMemoryMappedDevice
{
    // Register offsets (RM0451 §3.7)
    private const uint FLASH_ACR     = 0x00;
    private const uint FLASH_PECR    = 0x04;
    private const uint FLASH_PEKEYR  = 0x0C;
    private const uint FLASH_PRGKEYR = 0x10;
    private const uint FLASH_SR      = 0x18;

    // PECR bits
    private const uint PECR_PELOCK  = 1u << 0;
    private const uint PECR_PRGLOCK = 1u << 1;
    private const uint PECR_PROG    = 1u << 3;
    private const uint PECR_ERASE   = 1u << 9;

    // SR bits
    private const uint SR_EOP = 1u << 1; // end of operation (BSY = bit 0, always read 0)

    // Unlock keys (RM0451 §3.3.4)
    private const uint PEKEY1  = 0x89ABCDEF, PEKEY2  = 0x02030405;
    private const uint PRGKEY1 = 0x8C9DAEBF, PRGKEY2 = 0x13141516;

    private const uint PAGE_SIZE = 128; // 128-byte pages on STM32L0

    private readonly BusInterconnect _bus;

    private uint _acr;
    private uint _pecr = PECR_PELOCK | PECR_PRGLOCK; // fully locked out of reset
    private uint _sr;
    private int _peKeyState;  // 0 = expect PEKEY1, 1 = expect PEKEY2
    private int _prgKeyState; // 0 = expect PRGKEY1, 1 = expect PRGKEY2

    public uint Size => 0x400;

    public FlashL0Peripheral(BusInterconnect bus) => _bus = bus;

    private bool Unlocked => (_pecr & (PECR_PELOCK | PECR_PRGLOCK)) == 0;

    public uint ReadWord(uint address)
    {
        return (address & 0xFF) switch
        {
            FLASH_ACR => _acr,
            FLASH_PECR => _pecr,
            FLASH_SR => _sr, // BSY (bit 0) always 0: operations are instantaneous
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

            case FLASH_PEKEYR:
                if ((_pecr & PECR_PELOCK) == 0) break; // already unlocked
                if (_peKeyState == 0 && value == PEKEY1) _peKeyState = 1;
                else if (_peKeyState == 1 && value == PEKEY2) { _pecr &= ~PECR_PELOCK; _peKeyState = 0; }
                else _peKeyState = 0;
                break;

            case FLASH_PRGKEYR:
                if ((_pecr & PECR_PELOCK) != 0) break; // PELOCK must be cleared first
                if (_prgKeyState == 0 && value == PRGKEY1) _prgKeyState = 1;
                else if (_prgKeyState == 1 && value == PRGKEY2) { _pecr &= ~PECR_PRGLOCK; _prgKeyState = 0; }
                else _prgKeyState = 0;
                break;

            case FLASH_PECR:
                // PELOCK/PRGLOCK can only be cleared via the key sequences; software may re-lock by
                // writing 1, and sets the PROG/ERASE/DATA mode bits while unlocked.
                _pecr = (_pecr & (PECR_PELOCK | PECR_PRGLOCK)) | (value & ~(PECR_PELOCK | PECR_PRGLOCK));
                if ((value & PECR_PELOCK) != 0) _pecr |= PECR_PELOCK | PECR_PRGLOCK;
                if ((value & PECR_PRGLOCK) != 0) _pecr |= PECR_PRGLOCK;
                _bus.FlashWriteEnabled = Unlocked && (_pecr & PECR_PROG) != 0;
                break;

            case FLASH_SR:
                _sr &= ~value; // write-1-to-clear (EOP, error flags)
                break;
        }
    }

    /// <summary>Erase a Flash page to the array's blank state (used by L0 page-erase firmware).</summary>
    public void ErasePage(uint flashOffset)
    {
        if (!Unlocked || (_pecr & PECR_ERASE) == 0) return;
        _bus.EraseFlash(flashOffset & ~(PAGE_SIZE - 1), PAGE_SIZE);
        _sr |= SR_EOP;
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
