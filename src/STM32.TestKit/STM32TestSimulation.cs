using STM32.Core.Cpu;
using STM32.Peripherals;
using STM32.Peripherals.Gpio;
using STM32.TestKit.Probes;

namespace STM32.TestKit;

/// <summary>
/// Fluent, deterministic test harness for the STM32 emulator.
/// <example>
/// <code>
/// using var sim = STM32TestSimulation.Create()
///     .WithFrequency(64_000_000)
///     .WithBinary(flashBytes)
///     .AddUart(2, out var uart)
///     .AddGpio("A", out var gpio);
///
/// sim.RunMilliseconds(10);
/// uart.Text.Should().Contain("READY");
/// gpio.Toggled(5).Should().BeTrue();
/// </code>
/// </example>
/// </summary>
public class STM32TestSimulation : IDisposable
{
    protected readonly STM32Machine Machine;

    /// <summary>Direct CPU access for low-level assertions.</summary>
    public CortexM0Plus Cpu => Machine.Cpu;

    /// <summary>Direct access to the machine for advanced scenarios.</summary>
    public STM32Machine Stm32 => Machine;

    private uint _clkHz = STM32Machine.DEFAULT_CLK_HZ;

    /// <summary>BKPT immediate values recorded during execution (captured, not escalated).</summary>
    public IReadOnlyList<byte> BreakpointHits => _breakpointHits;
    private readonly List<byte> _breakpointHits = [];

    protected STM32TestSimulation(uint flashSize, uint sramSize)
    {
        Machine = new STM32Machine(flashSize, sramSize);
        // Capture BKPT so firmware asserts/panics are observable without halting the sim.
        Machine.Cpu.OnBreakpoint = imm8 => _breakpointHits.Add(imm8);
    }

    /// <summary>Create a new simulation (defaults: 128 KB Flash, 64 KB SRAM).</summary>
    public static STM32TestSimulation Create(uint flashSize = 128 * 1024, uint sramSize = 64 * 1024)
        => new(flashSize, sramSize);

    // ── Configuration ────────────────────────────────────────────────

    /// <summary>Override the simulated CPU frequency used by RunMilliseconds/RunMicroseconds.</summary>
    public STM32TestSimulation WithFrequency(uint hz)
    {
        _clkHz = hz;
        return this;
    }

    /// <summary>Load a firmware image into Flash (0x0800_0000) and reset the CPU.</summary>
    public STM32TestSimulation WithBinary(ReadOnlySpan<byte> bytes)
    {
        Machine.LoadFlash(bytes);
        Machine.Reset();
        return this;
    }

    /// <summary>Attach a <see cref="UartProbe"/> to USART1 or USART2.</summary>
    public STM32TestSimulation AddUart(int index, out UartProbe probe)
    {
        var usart = index switch
        {
            1 => Machine.Usart1,
            2 => Machine.Usart2,
            _ => throw new ArgumentOutOfRangeException(nameof(index), "Use 1 or 2"),
        };
        probe = new UartProbe().Attach(usart);
        return this;
    }

    /// <summary>Attach a <see cref="GpioProbe"/> to a GPIO port ("A".."F").</summary>
    public STM32TestSimulation AddGpio(string port, out GpioProbe probe)
    {
        probe = new GpioProbe().Attach(Machine.Gpio[port]);
        return this;
    }

    /// <summary>Get the underlying GPIO port for direct manipulation (e.g. driving inputs).</summary>
    public GpioPortPeripheral Port(string port) => Machine.Gpio[port];

    // ── Execution ────────────────────────────────────────────────────

    /// <summary>Execute approximately <paramref name="instructions"/> instructions.</summary>
    public STM32TestSimulation RunInstructions(int instructions)
    {
        Machine.Run(instructions);
        return this;
    }

    /// <summary>Execute for approximately <paramref name="cycles"/> CPU cycles, in batches.</summary>
    public STM32TestSimulation RunCycles(long cycles)
    {
        const int BatchSize = 200_000;
        while (cycles > 0)
        {
            var batch = (int)Math.Min(cycles, BatchSize);
            Machine.Run(batch);
            cycles -= batch;
        }
        return this;
    }

    /// <summary>Execute for <paramref name="microseconds"/> simulated microseconds.</summary>
    public STM32TestSimulation RunMicroseconds(double microseconds)
        => RunCycles((long)(microseconds * _clkHz / 1_000_000.0));

    /// <summary>Execute for <paramref name="milliseconds"/> simulated milliseconds.</summary>
    public STM32TestSimulation RunMilliseconds(double milliseconds)
        => RunMicroseconds(milliseconds * 1000.0);

    /// <summary>Execute a single instruction.</summary>
    public STM32TestSimulation Step()
    {
        Machine.Cpu.Step();
        return this;
    }

    /// <summary>Reset the CPU to its initial state.</summary>
    public STM32TestSimulation Reset()
    {
        _breakpointHits.Clear();
        Machine.Reset();
        return this;
    }

    /// <summary>Total instructions executed since reset (deterministic).</summary>
    public long InstructionCount => Machine.InstructionCount;

    /// <summary>
    /// Run in bounded batches until <paramref name="until"/> returns true, the CPU locks up,
    /// or <paramref name="maxInstructions"/> is reached — whichever comes first. Never hangs:
    /// wedged or crashed firmware terminates with a diagnostic <see cref="RunResult"/>.
    /// </summary>
    public RunResult RunUntilHalt(Func<bool> until, long maxInstructions = 50_000_000)
    {
        var batch = (int)Math.Min(100_000, Math.Max(1, maxInstructions));
        var start = Machine.InstructionCount;

        while (true)
        {
            if (until())
                return new RunResult(RunOutcome.PredicateMet, Machine.InstructionCount - start, Cpu.Registers.Waiting);
            if (Cpu.IsLockedUp)
                return new RunResult(RunOutcome.LockedUp, Machine.InstructionCount - start, Cpu.Registers.Waiting);
            if (Machine.InstructionCount - start >= maxInstructions)
                return new RunResult(RunOutcome.BudgetReached, Machine.InstructionCount - start, Cpu.Registers.Waiting);

            Machine.Run(batch);
        }
    }

    /// <summary>Convenience overload: run until <paramref name="probe"/> captures <paramref name="expectedText"/>.</summary>
    public RunResult RunUntilHalt(UartProbe probe, string expectedText, long maxInstructions = 50_000_000)
        => RunUntilHalt(() => probe.Text.Contains(expectedText, StringComparison.Ordinal), maxInstructions);

    public void Dispose() => Machine.Dispose();
}
