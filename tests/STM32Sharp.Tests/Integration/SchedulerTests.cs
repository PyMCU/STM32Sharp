using STM32.TestKit;

namespace STM32Sharp.Tests.Integration;

/// <summary>
/// Cycle-accurate co-simulation through the machine's clock-event scheduler — the model an external
/// simulator (a circuit solver) uses to drive the emulator and inject stimuli at exact cycles. Real
/// firmware (blink) advances the CPU; the test schedules events against the cycle counter.
/// </summary>
public class SchedulerTests
{
    private static STM32TestSimulation Boot()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Firmware", "blink.bin");
        return STM32TestSimulation.Create().WithBinary(File.ReadAllBytes(path));
    }

    [Fact]
    public void Scheduled_event_fires_at_the_target_cycle()
    {
        using var sim = Boot();
        var target = sim.Cpu.Cycles + 10_000;
        long firedAt = -1;
        sim.Stm32.Scheduler.Schedule(target, () => firedAt = sim.Cpu.Cycles);

        sim.Stm32.RunUntilCycle(target + 1_000);

        // Fires the instant the clock reaches the target (within one instruction's worth of cycles).
        firedAt.Should().BeGreaterThanOrEqualTo(target);
        firedAt.Should().BeLessThan(target + 4);
    }

    [Fact]
    public void Solver_can_inject_input_at_a_precise_cycle()
    {
        using var sim = Boot();
        var port = sim.Port("C");
        var at = sim.Cpu.Cycles + 25_000;
        // Emulate a circuit solver driving PC13 high at a specific cycle.
        sim.Stm32.Scheduler.Schedule(at, () => port.SetInput(13, true));

        sim.Stm32.RunUntilCycle(at - 1);
        (port.ReadWord(0x10) & (1u << 13)).Should().Be(0u, "input not driven yet");

        sim.Stm32.RunUntilCycle(at + 1_000);
        (port.ReadWord(0x10) & (1u << 13)).Should().NotBe(0u, "solver drove the pin at the scheduled cycle");
    }

    [Fact]
    public void Run_advances_the_cycle_clock_monotonically()
    {
        using var sim = Boot();
        var c0 = sim.Cpu.Cycles;
        sim.Stm32.RunUntilCycle(c0 + 50_000);
        sim.Cpu.Cycles.Should().BeGreaterThanOrEqualTo(c0 + 50_000);
    }
}
