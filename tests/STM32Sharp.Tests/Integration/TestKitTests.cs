using STM32.TestKit;

namespace STM32Sharp.Tests.Integration;

/// <summary>Phase 4: exercise the fluent TestKit API against real firmware.</summary>
public class TestKitTests
{
    private static byte[] Fw(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Firmware", name));

    [Fact]
    public void Fluent_blink_via_gpio_probe()
    {
        using var sim = STM32TestSimulation.Create()
            .WithBinary(Fw("blink.bin"))
            .AddGpio("A", out var gpio);

        sim.RunInstructions(2_000_000);

        gpio.Toggled(5, count: 4).Should().BeTrue();
        sim.Cpu.IsLockedUp.Should().BeFalse();
    }

    [Fact]
    public void Fluent_uart_echo_via_uart_probe()
    {
        using var sim = STM32TestSimulation.Create()
            .WithBinary(Fw("uart_echo.bin"))
            .AddUart(2, out var uart);

        // Run until the greeting appears (never hangs — bounded by instruction budget).
        var result = sim.RunUntilHalt(uart, "READY", maxInstructions: 2_000_000);
        result.Succeeded.Should().BeTrue(result.ToString());

        uart.InjectString("Ping");
        sim.RunUntilHalt(() => uart.Text.EndsWith("Ping"), maxInstructions: 2_000_000)
           .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void RunUntilHalt_reports_budget_reached_for_idle_firmware()
    {
        using var sim = STM32TestSimulation.Create()
            .WithBinary(Fw("blink.bin"));

        var result = sim.RunUntilHalt(() => false, maxInstructions: 100_000);
        result.Outcome.Should().Be(RunOutcome.BudgetReached);
    }
}
