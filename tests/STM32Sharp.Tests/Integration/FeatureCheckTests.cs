using STM32.Peripherals;

namespace STM32Sharp.Tests.Integration;

/// <summary>
/// End-to-end validation of the advanced peripherals (timers, SPI/I2C interrupts, RTC, DMA +
/// DMAMUX) against REAL firmware compiled with arm-none-eabi-gcc using the official
/// STMicroelectronics CMSIS device header (stm32g071xx.h) and ARM CMSIS-Core. The firmware drives
/// every peripheral exclusively through ST's authoritative register structs and bitfield masks, so
/// a green result proves the emulator's base addresses, register offsets and bit semantics agree
/// with silicon. Each subtest sets one bit in the result word at 0x2000_0000; all eight must pass.
/// </summary>
public class FeatureCheckTests
{
    private const uint Result = 0x20000000;

    private static byte[] LoadFirmware(string name = "feature_check.bin")
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Firmware", name);
        return File.ReadAllBytes(path);
    }

    private static STM32Machine Run(STM32Machine m, string firmware)
    {
        m.LoadFlash(LoadFirmware(firmware));
        m.Reset();
        // Small batches so time-aware peripherals (TIM3) tick while the firmware spins.
        for (var i = 0; i < 200; i++)
            m.Run(10_000);
        return m;
    }

    private static STM32Machine Run() => Run(new STM32Machine(), "feature_check.bin");

    [Fact]
    public void All_advanced_peripheral_subtests_pass()
    {
        using var m = Run();
        m.Cpu.IsLockedUp.Should().BeFalse("the firmware must not fault");
        m.Bus.ReadWord(Result).Should().Be(0xFFu, "every subtest bit must be set");
    }

    [Fact]
    public void C031_firmware_runs_on_the_c031_preset()
    {
        // feature_check_c031.bin is the SAME firmware compiled against the official ST CMSIS C0
        // header (stm32c031xx.h) and a 32K/12K linker script. Running it on the C031 preset proves
        // the STM32C0 peripheral map and a Wokwi-supported part work end-to-end.
        using var m = new STM32Machine(Stm32ChipPreset.C031);
        Run(m, "feature_check_c031.bin");
        m.Cpu.IsLockedUp.Should().BeFalse("the C031 firmware must not fault");
        m.Bus.ReadWord(Result).Should().Be(0xFFu, "every subtest must pass on the C031 too");
    }

    [Theory]
    [InlineData(0, "TIM3 counts and raises UIF + CC1IF")]
    [InlineData(1, "SPI1 full-duplex receives the idle MISO byte")]
    [InlineData(2, "SPI1 RXNEIE asserts the SPI1 NVIC line")]
    [InlineData(3, "I2C1 START to an absent slave raises NACKF")]
    [InlineData(4, "I2C1 NACKIE asserts the I2C1 NVIC line")]
    [InlineData(5, "RTC calendar write/read-back via WPR+INIT")]
    [InlineData(6, "DMA1 memory-to-memory block copy")]
    [InlineData(7, "DMA1 request-driven copy via SPI1_RX DREQ + DMAMUX")]
    public void Subtest_passes(int bit, string because)
    {
        using var m = Run();
        (m.Bus.ReadWord(Result) & (1u << bit)).Should().NotBe(0u, because);
    }
}
