using STM32.Peripherals;
using STM32.TestKit;
using STM32.TestKit.Probes;

namespace STM32Sharp.Tests.Integration;

/// <summary>
/// Parity test against Wokwi: the firmware is the official <c>wokwi/stm32-hello-wokwi</c> project,
/// a STM32CubeMX/HAL application for the Nucleo-C031C6 (the exact part Wokwi simulates). It is built
/// unmodified from ST's HAL — booting through <c>HAL_Init()</c> / <c>SystemClock_Config()</c> and
/// printing "Hello, Wokwi!" over USART2 (PA2/PA3) with blocking <c>HAL_UART_Transmit</c>. Running it
/// on the <see cref="Stm32ChipPreset.C031"/> preset and observing the same banner proves the emulator
/// is bit-compatible with what Wokwi runs.
/// </summary>
public class WokwiParityTests
{
    private static byte[] Firmware() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Firmware", "wokwi_hello.bin"));

    [Fact]
    public void Wokwi_hello_firmware_prints_its_banner_over_usart2()
    {
        using var sim = STM32TestSimulation.Create(Stm32ChipPreset.C031)
            .WithBinary(Firmware());
        var uart = new UartProbe().Attach(sim.Stm32.Usart2); // Nucleo-C031C6 Serial = USART2 (PA2/PA3)

        var result = sim.RunUntilHalt(() => uart.Text.Contains("Hello, Wokwi!"), 50_000_000);

        result.Outcome.Should().Be(RunOutcome.PredicateMet,
            "the real Wokwi HAL firmware must emit its banner over USART2 on the C031 preset");
    }
}
