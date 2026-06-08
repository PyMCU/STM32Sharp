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
using STM32.Peripherals.Rtc;
using STM32.Peripherals.Spi;
using STM32.Peripherals.SysCfg;
using STM32.Peripherals.Timer;
using STM32.Peripherals.Usart;
using STM32.Peripherals.Watchdog;

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
    private const uint DMAMUX_BASE = 0x40020800;
    private const uint RTC_BASE = 0x40002800;
    private const uint WWDG_BASE = 0x40002C00;
    private const uint IWDG_BASE = 0x40003000;

    // STM32G0 NVIC IRQ numbers (RM0444 §11.3).
    private const int IRQ_TIM2 = 15;
    private const int IRQ_TIM3 = 16;
    private const int IRQ_I2C1 = 23;
    private const int IRQ_I2C2 = 24;
    private const int IRQ_SPI1 = 25;
    private const int IRQ_SPI2 = 26;
    private const int IRQ_USART1 = 27;
    private const int IRQ_USART2 = 28;

    // STM32G0 DMAMUX request line ids (RM0444 §12.3, mirrors HAL DMA_REQUEST_*).
    private const int REQ_ADC1 = 5;
    private const int REQ_SPI1_RX = 16;
    private const int REQ_SPI2_RX = 18;
    private const int REQ_USART1_RX = 50;
    private const int REQ_USART2_RX = 52;

    public BusInterconnect Bus { get; }
    public CortexM0Plus Cpu { get; }

    /// <summary>The chip preset this machine was built from (memory sizes, default clock, core).</summary>
    public Stm32ChipPreset Chip { get; }

    // ── System peripherals ──────────────────────────────────────────────
    public PpbPeripheral Ppb { get; }
    /// <summary>RCC device (RccPeripheral on G0/C0, RccL0Peripheral on L0).</summary>
    public IMemoryMappedDevice Rcc { get; }
    /// <summary>FLASH device (FlashPeripheral on G0/C0, FlashL0Peripheral on L0).</summary>
    public IMemoryMappedDevice Flash { get; }
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
    /// <summary>DMAMUX request router (G0/C0 only; null on L0, which routes via DMA CSELR).</summary>
    public DmamuxPeripheral? Dmamux { get; }
    /// <summary>DMA CSELR request router (L0 only; null on G0/C0).</summary>
    public DmaCselrRouter? DmaCselr { get; }
    public RtcPeripheral Rtc { get; }
    public IwdgPeripheral Iwdg { get; }
    public WwdgPeripheral Wwdg { get; }

    /// <summary>Number of watchdog-triggered system resets since construction.</summary>
    public int WatchdogResetCount { get; private set; }

    /// <summary>Host hook invoked when a watchdog (IWDG/WWDG) times out and resets the system.</summary>
    public Action? OnWatchdogReset;

    /// <summary>Cycles elapsed in the most recent <see cref="Run"/> batch.</summary>
    public long LastElapsedCycles { get; private set; }

    /// <summary>Total instructions executed since reset (the emulator's deterministic clock).</summary>
    public long InstructionCount => Cpu.Cycles;

    private ITickable[] _tickables;

    public STM32Machine(uint flashSize = 128 * 1024, uint sramSize = 64 * 1024)
        : this(Stm32ChipPreset.Custom(flashSize, sramSize)) { }

    /// <summary>
    /// Build a machine for a specific chip preset (see <see cref="Stm32ChipPreset"/>). Throws if the
    /// preset's core is not emulable (e.g. a Cortex-M3 part such as the STM32F103).
    /// </summary>
    public STM32Machine(Stm32ChipPreset chip)
    {
        if (!chip.IsEmulable)
            throw new NotSupportedException(
                $"{chip.Name} uses {chip.Core}, which the ARMv6-M (Cortex-M0+) core cannot execute. {chip.Notes}");

        Chip = chip;
        // The bus addresses memory with power-of-two masks, so round each region up to the next
        // power of two that fully contains the part's real size (e.g. G071's 36 KB → 64 KB,
        // C031's 12 KB → 16 KB). The real size is kept on Chip for reporting.
        Bus = new BusInterconnect(RoundUpPow2(chip.FlashSize), RoundUpPow2(chip.SramSize));
        Cpu = new CortexM0Plus(Bus);

        // ── System peripherals ──────────────────────────────────────────
        Ppb = new PpbPeripheral(Cpu);
        Bus.RegisterPeripheral(PPB_BASE, Ppb);

        // RCC and Flash controllers differ on the L0 (clock tree and PECR-based programming).
        Rcc = chip.Family == StFamily.L0 ? new RccL0Peripheral() : new RccPeripheral();
        Bus.RegisterPeripheral(RCC_BASE, Rcc);

        Flash = chip.Family == StFamily.L0 ? new FlashL0Peripheral(Bus) : new FlashPeripheral(Bus);
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
        Spi1 = new SpiPeripheral("SPI1", IRQ_SPI1) { RaiseIrq = Cpu.SetInterrupt };
        Spi2 = new SpiPeripheral("SPI2", IRQ_SPI2) { RaiseIrq = Cpu.SetInterrupt };
        Bus.RegisterPeripheral(SPI1_BASE, Spi1);
        Bus.RegisterPeripheral(SPI2_BASE, Spi2);

        // ── I2C ─────────────────────────────────────────────────────────
        I2c1 = new I2cPeripheral("I2C1", IRQ_I2C1) { RaiseIrq = Cpu.SetInterrupt };
        I2c2 = new I2cPeripheral("I2C2", IRQ_I2C2) { RaiseIrq = Cpu.SetInterrupt };
        Bus.RegisterPeripheral(I2C1_BASE, I2c1);
        Bus.RegisterPeripheral(I2C2_BASE, I2c2);

        // ── ADC ─────────────────────────────────────────────────────────
        Adc = new AdcPeripheral();
        Bus.RegisterPeripheral(ADC_BASE, Adc);

        // ── DMA + request routing (DMAMUX on G0/C0, CSELR on L0) ──────────
        Dma = new DmaPeripheral(Bus) { RaiseIrq = Cpu.SetInterrupt };
        if (chip.Family == StFamily.L0)
        {
            DmaCselr = new DmaCselrRouter();
            Dma.Cselr = DmaCselr;
            Dma.RequestRouter = DmaCselr; // CSELR lives inside the DMA block at offset 0xA8
        }
        else
        {
            Dmamux = new DmamuxPeripheral();
            Bus.RegisterPeripheral(DMAMUX_BASE, Dmamux);
            Dma.RequestRouter = Dmamux;
        }
        Bus.RegisterPeripheral(DMA1_BASE, Dma);

        // Route peripheral DREQs through the DMAMUX to the DMA engine.
        Usart1.OnRxDmaRequest = () => Dma.Request(REQ_USART1_RX);
        Usart2.OnRxDmaRequest = () => Dma.Request(REQ_USART2_RX);
        Spi1.OnRxDmaRequest = () => Dma.Request(REQ_SPI1_RX);
        Spi2.OnRxDmaRequest = () => Dma.Request(REQ_SPI2_RX);
        Adc.OnDmaRequest = () => Dma.Request(REQ_ADC1);

        // ── RTC ─────────────────────────────────────────────────────────
        Rtc = new RtcPeripheral { RaiseIrq = Cpu.SetInterrupt };
        Bus.RegisterPeripheral(RTC_BASE, Rtc);

        // ── Watchdogs ───────────────────────────────────────────────────
        Iwdg = new IwdgPeripheral { OnTimeout = HandleWatchdogReset };
        Wwdg = new WwdgPeripheral { OnTimeout = HandleWatchdogReset };
        Bus.RegisterPeripheral(IWDG_BASE, Iwdg);
        Bus.RegisterPeripheral(WWDG_BASE, Wwdg);

        _tickables = [Ppb, Tim2, Tim3, Rtc, Iwdg, Wwdg];
    }

    private static uint RoundUpPow2(uint v)
    {
        if (v != 0 && (v & (v - 1)) == 0) return v; // already a power of two
        uint p = 1;
        while (p < v) p <<= 1;
        return p;
    }

    private void HandleWatchdogReset()
    {
        WatchdogResetCount++;
        OnWatchdogReset?.Invoke();
        // Model the real chip: a watchdog timeout resets the core (re-reads SP/PC from the vector
        // table). Tick runs at the end of a Run batch, so resetting here is safe for the next batch.
        Cpu.Reset();
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
