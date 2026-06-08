using STM32.TestKit;
using STM32.TestKit.Probes;

namespace STM32Sharp.Tests.Integration;

/// <summary>
/// End-to-end test of real Arduino firmware. The sketch is built with the official STMicroelectronics
/// Arduino core (STM32duino), which runs on ST's HAL, so the image boots through SystemClock_Config(),
/// HAL GPIO and interrupt-driven HAL UART exactly like firmware a user would flash. On the
/// Nucleo-G071RB, Serial is LPUART1 (PA2/PA3) and LED_BUILTIN is PA5.
/// </summary>
public class ArduinoTests
{
    private static byte[] Firmware() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Firmware", "arduino_blink.bin"));

    private static STM32TestSimulation Boot(out UartProbe uart, out GpioProbe led)
    {
        var sim = STM32TestSimulation.Create()
            .WithFrequency(64_000_000)
            .WithBinary(Firmware())
            .AddGpio("A", out led);
        uart = new UartProbe().Attach(sim.Stm32.Lpuart1); // STM32duino Serial = LPUART1 on this board
        return sim;
    }

    [Fact]
    public void Serial_prints_the_setup_banner()
    {
        using var sim = Boot(out var uart, out _);
        var result = sim.RunUntilHalt(() => uart.Text.Contains("STM32DUINO-OK"), 80_000_000);
        result.Outcome.Should().Be(RunOutcome.PredicateMet, "the Arduino setup() banner must arrive over Serial");
    }

    [Fact]
    public void Loop_blinks_the_led_and_keeps_printing()
    {
        using var sim = Boot(out var uart, out var led);
        // Run long enough (simulated) for several loop() iterations.
        sim.RunUntilHalt(() => led.Transitions(5) >= 4 && uart.Text.Contains("tick"), 120_000_000);

        led.Transitions(5).Should().BeGreaterThanOrEqualTo(4, "digitalWrite must toggle PA5 (LED_BUILTIN)");
        uart.Text.Should().Contain("tick", "loop() prints over Serial each iteration");
    }
}
