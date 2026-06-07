using STM32.Peripherals;

namespace STM32Sharp.Tests.Integration;

/// <summary>
/// Phase 2 end-to-end boot test. Runs real bare-metal firmware (compiled with
/// arm-none-eabi-gcc for cortex-m0plus) that mirrors the STM32Cube HAL boot path:
/// enable PLL → wait PLLRDY → switch system clock → wait SWS → enable SysTick IRQ.
/// The firmware must complete clock configuration without locking up and the SysTick
/// handler must run.
/// </summary>
public class BootClockTests
{
    private static byte[] LoadFirmware()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Firmware", "boot_clock.bin");
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void Firmware_completes_clock_config_and_runs_systick()
    {
        using var m = new STM32Machine();
        m.LoadFlash(LoadFirmware());
        m.Reset();

        // ~1M instructions: plenty to pass clock config and accrue SysTick interrupts.
        for (var i = 0; i < 100; i++)
            m.Run(10_000);

        m.Cpu.IsLockedUp.Should().BeFalse("the boot path must not fault");
        m.Bus.ReadWord(0x20000000).Should().Be(0xABCD1234u, "clock config reached the success marker");
        m.Bus.ReadWord(0x20000004).Should().BeGreaterThan(0u, "the SysTick handler must have run");
    }
}
