using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

/// <summary>
/// CRC calculation unit. The reset configuration (POL = 0x04C11DB7, INIT = 0xFFFF_FFFF, no reversal)
/// is CRC-32/MPEG-2, so the well-known check value for the ASCII string "123456789" is 0x0376E6E7.
/// </summary>
public class CrcTests
{
    private const uint CRC = 0x40023000;
    private const uint DR = CRC + 0x00;
    private const uint CR = CRC + 0x08;
    private const uint INIT = CRC + 0x10;
    private const uint POL = CRC + 0x14;

    private const uint CR_RESET = 1u << 0;
    private const uint CR_REVOUT = 1u << 7;

    [Fact]
    public void Default_config_computes_crc32_mpeg2_check_value()
    {
        using var m = new STM32Machine();
        foreach (var b in "123456789"u8.ToArray())
            m.Bus.WriteByte(DR, b);

        m.Bus.ReadWord(DR).Should().Be(0x0376E6E7u);
    }

    [Fact]
    public void Reset_bit_reseeds_from_init()
    {
        using var m = new STM32Machine();
        m.Bus.WriteByte(DR, 0xAB);
        m.Bus.ReadWord(DR).Should().NotBe(0xFFFFFFFFu);

        m.Bus.WriteWord(CR, CR_RESET);                 // RESET reloads DR from INIT
        m.Bus.ReadWord(DR).Should().Be(0xFFFFFFFFu);
        (m.Bus.ReadWord(CR) & CR_RESET).Should().Be(0u, "RESET is self-clearing");
    }

    [Fact]
    public void Word_feed_matches_byte_feed_big_endian()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(DR, 0x12345678);
        var word = m.Bus.ReadWord(DR);

        m.Bus.WriteWord(CR, CR_RESET);
        m.Bus.WriteByte(DR, 0x12);
        m.Bus.WriteByte(DR, 0x34);
        m.Bus.WriteByte(DR, 0x56);
        m.Bus.WriteByte(DR, 0x78);

        m.Bus.ReadWord(DR).Should().Be(word, "a 32-bit feed is processed MSB-first, same as four bytes");
    }

    [Fact]
    public void Writing_init_reseeds_the_engine()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(INIT, 0x00000000);
        m.Bus.WriteWord(CR, CR_RESET);
        m.Bus.ReadWord(DR).Should().Be(0x00000000u);
        m.Bus.ReadWord(POL).Should().Be(0x04C11DB7u, "polynomial keeps its reset value");
    }

    [Fact]
    public void Reverse_output_reflects_the_result()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CR, CR_REVOUT);
        foreach (var b in "123456789"u8.ToArray())
            m.Bus.WriteByte(DR, b);

        // Bit-reflection of 0x0376E6E7.
        m.Bus.ReadWord(DR).Should().Be(0xE7676EC0u);
    }
}
