namespace STM32.Peripherals;

/// <summary>ARM core variant of an STM32 part (only ARMv6-M is emulated today).</summary>
public enum CortexCore
{
    /// <summary>Cortex-M0+ (ARMv6-M, Thumb-1) — the only core STM32Sharp emulates.</summary>
    CortexM0Plus,

    /// <summary>Cortex-M3 (ARMv7-M, Thumb-2) — NOT emulated; requires a Thumb-2 core.</summary>
    CortexM3,
}

/// <summary>
/// STM32 sub-family, which selects the system-peripheral variants (RCC/Flash and DMA request routing)
/// the machine wires up. G0 and C0 share the same map; L0 has its own RCC/Flash and uses DMA CSELR
/// instead of a DMAMUX.
/// </summary>
public enum StFamily
{
    G0,
    C0,
    L0,
    F1,
}

/// <summary>
/// A chip preset: the per-part facts the emulator needs (memory sizes, default clock, core) plus
/// metadata for tooling. The peripheral map and register semantics are the STM32G0 family's, which
/// the STM32C0 line shares verbatim; presets only specialise memory sizes and the reset clock.
///
/// Use with <see cref="STM32Machine(Stm32ChipPreset)"/> or the TestKit's <c>Create(preset)</c>.
/// </summary>
public readonly record struct Stm32ChipPreset(
    string Name,
    CortexCore Core,
    StFamily Family,
    uint FlashSize,
    uint SramSize,
    uint DefaultClockHz,
    bool HasDmamux,
    bool FullFidelity,
    string Notes)
{
    /// <summary>True when the emulated core can actually execute this part's instruction set.</summary>
    public bool IsEmulable => Core == CortexCore.CortexM0Plus;

    /// <summary>Build an ad-hoc preset from raw memory sizes (G0-compatible, full fidelity).</summary>
    public static Stm32ChipPreset Custom(uint flashSize, uint sramSize, uint clockHz = 16_000_000) =>
        new("STM32 (custom)", CortexCore.CortexM0Plus, StFamily.G0, flashSize, sramSize, clockHz,
            HasDmamux: true, FullFidelity: true, Notes: "Custom memory sizes on the STM32G0 map.");

    // ── STM32G0 (reference family; full fidelity) ────────────────────────

    /// <summary>STM32G071 — reference target (Nucleo-G071RB). 128 KB Flash / 36 KB SRAM.</summary>
    public static readonly Stm32ChipPreset G071 = new(
        "STM32G071", CortexCore.CortexM0Plus, StFamily.G0, 128 * 1024, 36 * 1024, 16_000_000,
        HasDmamux: true, FullFidelity: true,
        Notes: "Reference part. HSI 16 MHz at reset; full peripheral fidelity.");

    /// <summary>STM32G031 — value line. 64 KB Flash / 8 KB SRAM.</summary>
    public static readonly Stm32ChipPreset G031 = new(
        "STM32G031", CortexCore.CortexM0Plus, StFamily.G0, 64 * 1024, 8 * 1024, 16_000_000,
        HasDmamux: true, FullFidelity: true,
        Notes: "Same peripheral map as G071, smaller memories.");

    // ── STM32C0 (Wokwi; shares the G0 map verbatim) ──────────────────────

    /// <summary>STM32C031 — Wokwi's Nucleo-C031C6. 32 KB Flash / 12 KB SRAM, Cortex-M0+.</summary>
    public static readonly Stm32ChipPreset C031 = new(
        "STM32C031", CortexCore.CortexM0Plus, StFamily.C0, 32 * 1024, 12 * 1024, 12_000_000,
        HasDmamux: true, FullFidelity: true,
        Notes: "Peripheral map identical to STM32G0 (verified vs official CMSIS). No PLL: boots on " +
               "HSISYS = HSI48/4 ≈ 12 MHz; the emulator's synchronous clock-ready flags cover it.");

    // ── STM32L0 (Wokwi; full fidelity with L0-specific RCC/Flash/CSELR) ───

    /// <summary>
    /// STM32L031 — Wokwi's Nucleo-L031K6. 32 KB Flash / 8 KB SRAM, Cortex-M0+. Uses the L0-specific
    /// RCC (MSI/HSI/PLL clock tree), Flash (PECR controller) and DMA CSELR routing; the APB peripheral
    /// map (GPIO/USART/TIM/SPI/I2C/RTC) matches the G0.
    /// </summary>
    public static readonly Stm32ChipPreset L031 = new(
        "STM32L031", CortexCore.CortexM0Plus, StFamily.L0, 32 * 1024, 8 * 1024, 2_097_000,
        HasDmamux: false, FullFidelity: true,
        Notes: "L0 RCC (boots on MSI), L0 Flash (PECR unlock) and DMA CSELR routing modeled; APB " +
               "peripherals share the G0 map. Default clock MSI ≈ 2.1 MHz.");

    // ── STM32F1 (Wokwi; NOT emulable — Cortex-M3) ────────────────────────

    /// <summary>
    /// STM32F103C8 — Wokwi's "BluePill". 64 KB Flash / 20 KB SRAM. Cortex-M3 (Thumb-2): NOT emulable
    /// by the current ARMv6-M core, and exposed only so tooling can report it explicitly.
    /// </summary>
    public static readonly Stm32ChipPreset F103C8 = new(
        "STM32F103C8", CortexCore.CortexM3, StFamily.F1, 64 * 1024, 20 * 1024, 8_000_000,
        HasDmamux: false, FullFidelity: false,
        Notes: "Cortex-M3 (Thumb-2) with a different APB1/APB2 bus map. Requires a Thumb-2 core — out " +
               "of scope for the M0+ emulator.");

    /// <summary>All presets known to the emulator (including the non-emulable F103 for reporting).</summary>
    public static readonly IReadOnlyList<Stm32ChipPreset> All = [G071, G031, C031, L031, F103C8];
}
