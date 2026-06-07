using STM32.Core.Cpu;
using STM32.Core.Memory;
using STM32.Peripherals.Adc;
using STM32.Peripherals.Dma;
using STM32.Peripherals.Exti;
using STM32.Peripherals.Flash;
using STM32.Peripherals.Gpio;
using STM32.Peripherals.I2c;
using STM32.Peripherals.Ppb;
using STM32.Peripherals.Rcc;
using STM32.Peripherals.Spi;
using STM32.Peripherals.SysCfg;
using STM32.Peripherals.Timer;
using STM32.Peripherals.Usart;

namespace STM32.Peripherals;

/// <summary>
/// Root class that wires the STM32 CPU, system bus and peripherals together.
/// Single-core Cortex-M0+ (STM32C0/F0/G0/L0 families).
///
/// Typical usage:
/// <code>
/// var machine = new STM32Machine();
/// machine.LoadFlash(firmwareBytes);
/// machine.Reset();
/// machine.Run(1_000_000);
/// </code>
/// </summary>
public sealed class STM32Machine : IDisposable
{
    /// <summary>Default core clock (HSI 16 MHz at reset on STM32G0).</summary>
    public const uint DEFAULT_CLK_HZ = 16_000_000;

    // STM32G0 peripheral base addresses (RM0444).
    private const uint PPB_BASE   = 0xE000E000; // NVIC / SysTick / SCB
    private const uint RCC_BASE   = 0x40021000; // Reset & Clock Control
    private const uint FLASH_BASE = 0x40022000; // Embedded Flash interface registers
    private const uint EXTI_BASE  = 0x40021800; // Extended interrupts controller
    private const uint SYSCFG_BASE = 0x40010000; // System configuration
    private const uint GPIO_BASE  = 0x50000000; // GPIOA; ports are 0x400 apart
    private const uint USART1_BASE = 0x40013800;
    private const uint USART2_BASE = 0x40004400;
    private const uint TIM2_BASE = 0x40000000;
    private const uint TIM3_BASE = 0x40000400;
    private const uint SPI1_BASE = 0x40013000;
    private const uint SPI2_BASE = 0x40003800;
    private const uint I2C1_BASE = 0x40005400;
    private const uint I2C2_BASE = 0x40005800;
    private const uint ADC_BASE = 0x40012400;
    private const uint DMA1_BASE = 0x40020000;

    // STM32G0 NVIC IRQ numbers (RM0444 §11.3).
    private const int IRQ_TIM2 = 15;
    private const int IRQ_TIM3 = 16;
    private const int IRQ_USART1 = 27;
    private const int IRQ_USART2 = 28;

    public BusInterconnect Bus { get; }
    public CortexM0Plus Cpu { get; }

    // ── System peripherals ──────────────────────────────────────────────
    public PpbPeripheral Ppb { get; }
    public RccPeripheral Rcc { get; }
    public FlashPeripheral Flash { get; }
    public ExtiPeripheral Exti { get; }
    public SysCfgPeripheral SysCfg { get; }

    // ── I/O peripherals ─────────────────────────────────────────────────
    /// <summary>GPIO ports indexed by name: "A","B","C","D","F".</summary>
    public IReadOnlyDictionary<string, GpioPortPeripheral> Gpio { get; }
    public GpioPortPeripheral GpioA { get; }
    public GpioPortPeripheral GpioB { get; }
    public GpioPortPeripheral GpioC { get; }
    public UsartPeripheral Usart1 { get; }
    public UsartPeripheral Usart2 { get; }
    public TimerPeripheral Tim2 { get; }
    public TimerPeripheral Tim3 { get; }
    public SpiPeripheral Spi1 { get; }
    public SpiPeripheral Spi2 { get; }
    public I2cPeripheral I2c1 { get; }
    public I2cPeripheral I2c2 { get; }
    public AdcPeripheral Adc { get; }
    public DmaPeripheral Dma { get; }

    /// <summary>Cycles elapsed in the most recent <see cref="Run"/> batch.</summary>
    public long LastElapsedCycles { get; private set; }

    /// <summary>Total instructions executed since reset (the emulator's deterministic clock).</summary>
    public long InstructionCount => Cpu.Cycles;

    private ITickable[] _tickables;

    public STM32Machine(uint flashSize = 128 * 1024, uint sramSize = 64 * 1024)
    {
        Bus = new BusInterconnect(flashSize, sramSize);
        Cpu = new CortexM0Plus(Bus);

        // ── System peripherals ──────────────────────────────────────────
        Ppb = new PpbPeripheral(Cpu);
        Bus.RegisterPeripheral(PPB_BASE, Ppb);

        Rcc = new RccPeripheral();
        Bus.RegisterPeripheral(RCC_BASE, Rcc);

        Flash = new FlashPeripheral(Bus);
        Bus.RegisterPeripheral(FLASH_BASE, Flash);

        Exti = new ExtiPeripheral { RaiseIrq = Cpu.SetInterrupt };
        Bus.RegisterPeripheral(EXTI_BASE, Exti);

        SysCfg = new SysCfgPeripheral();
        Bus.RegisterPeripheral(SYSCFG_BASE, SysCfg);

        // ── GPIO ports (STM32G0 has A,B,C,D,F) ──────────────────────────
        var ports = new Dictionary<string, GpioPortPeripheral>();
        var portNames = new[] { ("A", 0u), ("B", 1u), ("C", 2u), ("D", 3u), ("F", 5u) };
        foreach (var (name, index) in portNames)
        {
            var port = new GpioPortPeripheral(name);
            Bus.RegisterPeripheral(GPIO_BASE + index * 0x400, port);
            // Route external input edges on this port to EXTI (it filters by EXTICR).
            var portIndex = (int)index;
            port.OnInputChange += (pin, high) => Exti.OnPortEdge(portIndex, pin, high);
            ports[name] = port;
        }
        Gpio = ports;
        GpioA = ports["A"];
        GpioB = ports["B"];
        GpioC = ports["C"];

        // ── USARTs ──────────────────────────────────────────────────────
        Usart1 = new UsartPeripheral("USART1", IRQ_USART1) { RaiseIrq = Cpu.SetInterrupt };
        Usart2 = new UsartPeripheral("USART2", IRQ_USART2) { RaiseIrq = Cpu.SetInterrupt };
        Bus.RegisterPeripheral(USART1_BASE, Usart1);
        Bus.RegisterPeripheral(USART2_BASE, Usart2);

        // ── Timers ──────────────────────────────────────────────────────
        Tim2 = new TimerPeripheral("TIM2", IRQ_TIM2) { RaiseIrq = Cpu.SetInterrupt };
        Tim3 = new TimerPeripheral("TIM3", IRQ_TIM3) { RaiseIrq = Cpu.SetInterrupt };
        Bus.RegisterPeripheral(TIM2_BASE, Tim2);
        Bus.RegisterPeripheral(TIM3_BASE, Tim3);

        // ── SPI ─────────────────────────────────────────────────────────
        Spi1 = new SpiPeripheral("SPI1");
        Spi2 = new SpiPeripheral("SPI2");
        Bus.RegisterPeripheral(SPI1_BASE, Spi1);
        Bus.RegisterPeripheral(SPI2_BASE, Spi2);

        // ── I2C ─────────────────────────────────────────────────────────
        I2c1 = new I2cPeripheral("I2C1");
        I2c2 = new I2cPeripheral("I2C2");
        Bus.RegisterPeripheral(I2C1_BASE, I2c1);
        Bus.RegisterPeripheral(I2C2_BASE, I2c2);

        // ── ADC ─────────────────────────────────────────────────────────
        Adc = new AdcPeripheral();
        Bus.RegisterPeripheral(ADC_BASE, Adc);

        // ── DMA ─────────────────────────────────────────────────────────
        Dma = new DmaPeripheral(Bus) { RaiseIrq = Cpu.SetInterrupt };
        Bus.RegisterPeripheral(DMA1_BASE, Dma);

        _tickables = [Ppb, Tim2, Tim3];
    }

    /// <summary>
    /// Replace the time-aware peripheral list that ticks after each <see cref="Run"/> batch.
    /// The PPB (SysTick) is always included; additional tickables (timers, etc.) are appended
    /// as later phases wire them in.
    /// </summary>
    public void SetTickables(params ITickable[] tickables) =>
        _tickables = [Ppb, .. tickables];

    /// <summary>Copy a raw firmware image into Flash (offset 0 = 0x0800_0000).</summary>
    public void LoadFlash(ReadOnlySpan<byte> image, uint offset = 0) => Bus.LoadFlash(image, offset);

    /// <summary>Re-read the initial SP/PC from the vector table and clear CPU state.</summary>
    public void Reset() => Cpu.Reset();

    /// <summary>
    /// Run approximately <paramref name="instructions"/> instructions, then advance all
    /// time-aware peripherals by the number of cycles actually consumed.
    /// </summary>
    public void Run(int instructions)
    {
        var before = Cpu.Cycles;
        Cpu.Run(instructions);
        var delta = Cpu.Cycles - before;
        LastElapsedCycles = delta;

        foreach (var t in _tickables)
            t.Tick(delta);
    }

    public void Dispose() => Bus.Dispose();
}
