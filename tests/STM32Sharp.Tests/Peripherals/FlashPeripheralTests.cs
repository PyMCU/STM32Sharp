using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

public class FlashPeripheralTests
{
    private const uint FLASH_KEYR = 0x40022008;
    private const uint FLASH_SR = 0x40022010;
    private const uint FLASH_CR = 0x40022014;

    private const uint KEY1 = 0x45670123;
    private const uint KEY2 = 0xCDEF89AB;

    private const uint CR_PG = 1u << 0;
    private const uint CR_PER = 1u << 1;
    private const uint CR_STRT = 1u << 16;
    private const uint CR_LOCK = 1u << 31;

    private static void Unlock(STM32Machine m)
    {
        m.Bus.WriteWord(FLASH_KEYR, KEY1);
        m.Bus.WriteWord(FLASH_KEYR, KEY2);
    }

    [Fact]
    public void Controller_is_locked_out_of_reset()
    {
        using var m = new STM32Machine();
        (m.Bus.ReadWord(FLASH_CR) & CR_LOCK).Should().NotBe(0);
        // Writes to CR are ignored while locked.
        m.Bus.WriteWord(FLASH_CR, CR_PG);
        m.Bus.FlashWriteEnabled.Should().BeFalse();
    }

    [Fact]
    public void Unlock_sequence_clears_lock()
    {
        using var m = new STM32Machine();
        Unlock(m);
        (m.Bus.ReadWord(FLASH_CR) & CR_LOCK).Should().Be(0);
    }

    [Fact]
    public void Page_erase_sets_region_to_0xFF()
    {
        using var m = new STM32Machine();
        // Put some data in flash page 1 (0x0800_0800).
        var data = new byte[2048];
        for (var i = 0; i < data.Length; i++) data[i] = 0x5A;
        m.LoadFlash(data, offset: 2048);
        m.Bus.ReadWord(0x08000800).Should().Be(0x5A5A5A5Au);

        Unlock(m);
        m.Bus.WriteWord(FLASH_CR, CR_PER | (1u << 3) | CR_STRT); // PER, PNB=1, STRT

        m.Bus.ReadWord(0x08000800).Should().Be(0xFFFFFFFFu);
        (m.Bus.ReadWord(FLASH_SR) & 1u).Should().NotBe(0); // EOP
    }

    [Fact]
    public void Programming_writes_words_to_flash()
    {
        using var m = new STM32Machine();
        Unlock(m);

        // Erase page 2 then program a word.
        m.Bus.WriteWord(FLASH_CR, CR_PER | (2u << 3) | CR_STRT);
        m.Bus.WriteWord(FLASH_CR, CR_PG);          // enable programming
        m.Bus.WriteWord(0x08001000, 0x12345678);   // program into page 2
        m.Bus.WriteWord(FLASH_CR, 0);              // disable PG

        m.Bus.ReadWord(0x08001000).Should().Be(0x12345678u);
        m.Bus.FlashWriteEnabled.Should().BeFalse();
    }

    [Fact]
    public void Programming_can_only_clear_bits()
    {
        using var m = new STM32Machine();
        Unlock(m);
        m.Bus.WriteWord(FLASH_CR, CR_PER | (3u << 3) | CR_STRT); // erase page 3 -> 0xFF
        m.Bus.WriteWord(FLASH_CR, CR_PG);
        m.Bus.WriteWord(0x08001800, 0x0F0F0F0F);
        m.Bus.WriteWord(0x08001800, 0xFFFFFFFF); // cannot set bits back to 1
        m.Bus.WriteWord(FLASH_CR, 0);

        m.Bus.ReadWord(0x08001800).Should().Be(0x0F0F0F0Fu);
    }
}
