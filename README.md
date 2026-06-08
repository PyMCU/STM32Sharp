# STM32Sharp

![Build Status](https://github.com/PyMCU/STM32Sharp/actions/workflows/test.yml/badge.svg)
![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET Version](https://img.shields.io/badge/.NET-10.0-purple)

**STM32Sharp** is an emulator for **STM32 microcontrollers (ARM Cortex-M0+ core)**, written entirely
in modern **C# (.NET 10)**. It runs real, unmodified ST HAL, STM32duino (Arduino) and bare-metal
firmware — and is designed as a deterministic **firmware testkit** and a **cycle-accurate
co-simulation engine** for embedding in a circuit simulator with its own solver.

It is part of the "Sharp" family of emulators and shares its Cortex-M0+ core (the ARMv6-M Thumb-1 CPU,
instruction decoder, register bank and NVIC/SysTick) with
[RP2040Sharp](https://github.com/PyMCU/RP2040Sharp), which is itself a C# port of Uri Shaked's
[rp2040js](https://github.com/wokwi/rp2040js). What is STM32-specific here is the memory map and the
peripherals, rewritten and validated against the **official STMicroelectronics CMSIS headers**.

It targets the **STM32C0 / F0 / G0 / L0** series (reference target: **STM32G071**, Nucleo-G071RB).
These parts are small Cortex-M0+ MCUs, so — unlike RP2040Sharp — running MicroPython is out of scope;
the validation firmware is ST HAL / Arduino / CMSIS instead. The same set of chips is what
[Wokwi](https://wokwi.com/stm32) simulates, and STM32Sharp runs Wokwi's own example firmware unmodified
(see [Wokwi parity](#firmware) below).

## Features

- **ARM Cortex-M0+** full instruction set (Thumb-1), including exceptions, NVIC and SysTick.
- **Validated against the official ST CMSIS headers** — `feature_check` firmware accesses every
  peripheral through ST's authoritative structs and bit masks (no hand-written addresses), proving the
  memory map, register offsets and bit semantics match the silicon.
- **Runs real ST HAL & STM32duino firmware**, booting through `SystemClock_Config()`, HAL GPIO and
  interrupt-driven HAL UART exactly like firmware flashed onto the board.
- **Wokwi parity** — runs the official `wokwi/stm32-hello-wokwi` HAL project unmodified on the C031 preset.
- **Chip presets** for the parts Wokwi supports: G071, G031, C031 (Nucleo-C031C6), L031 (Nucleo-L031K6).
- **Cycle-accurate co-simulation** — a per-cycle `ClockEventQueue` (avr8js / rp2040js model) lets an
  external solver drive the emulator and inject stimuli at exact cycles.
- **Deterministic firmware testkit** — every run is bounded (wedged firmware fails with a reason
  instead of hanging) and the instruction count is reproducible across machines.
- **Peripherals:** NVIC/SysTick/SCB, RCC, FLASH (unlock/erase/program), PWR, SYSCFG, EXTI, GPIO,
  USART1/2 + LPUART1, TIM2/TIM3 (PWM/capture/compare), LPTIM1/2, SPI1/2 and I2C1/2 (with IRQ), ADC,
  DMA1 + DMAMUX (memory-to-memory, request-driven RX/TX, request generators), CRC, RTC, IWDG/WWDG.

## Chips (presets)

`Stm32ChipPreset` fixes memory/clock per part; the peripheral map is the STM32G0 one, which the
STM32C0 line shares verbatim (verified against the official CMSIS headers). Use it with
`new STM32Machine(Stm32ChipPreset.C031)` or `STM32TestSimulation.Create(Stm32ChipPreset.C031)`.

| Preset | Core | Flash / SRAM | Fidelity | Notes |
|--------|------|--------------|----------|-------|
| `G071` | Cortex-M0+ | 128 KB / 36 KB | ✅ full | Reference target |
| `G031` | Cortex-M0+ | 64 KB / 8 KB | ✅ full | Same map as G071 |
| `C031` | Cortex-M0+ | 32 KB / 12 KB | ✅ full | **Nucleo-C031C6 (Wokwi)**; map identical to G0 |
| `L031` | Cortex-M0+ | 32 KB / 8 KB | ✅ full | **Nucleo-L031K6 (Wokwi)**; L0-specific RCC/Flash + DMA CSELR |
| `F103C8` | Cortex-M3 | 64 KB / 20 KB | ❌ not emulable | **BluePill (Wokwi)**; requires a Thumb-2 core |

> C031 and L031 are validated end-to-end with `feature_check` recompiled against the official
> `stm32c031xx.h` / `stm32l031xx.h` headers. On L0 the RCC (MSI/HSI/PLL boot), the Flash (PECR unlock)
> and the CSELR-based DMA routing are family-specific. The F103 is a Cortex-M3 (outside the M0+ scope);
> its preset exists for completeness and throws `NotSupportedException` when constructed.

## Getting started

```bash
git clone https://github.com/PyMCU/STM32Sharp.git
cd STM32Sharp
dotnet build STM32.slnx
dotnet test  STM32.slnx
```

## Usage

### TestKit

```csharp
using STM32.TestKit;

using var sim = STM32TestSimulation.Create()
    .WithBinary(File.ReadAllBytes("uart_echo.bin"))
    .AddUart(2, out var uart)
    .AddGpio("A", out var gpio);

sim.RunUntilHalt(uart, "READY");      // never hangs: bounded by an instruction budget
uart.InjectString("Hello");
sim.RunUntilHalt(() => uart.Text.EndsWith("Hello"));
```

### Validating firmware in CI

Built for use as a compiler/firmware testkit (e.g. for [PyMCU](https://github.com/PyMCU/PyMCU))
without flaky or hanging builds. A run is always **bounded** — wedged firmware fails the test with a
reason instead of stalling — and the instruction count is **deterministic**.

```csharp
var result = sim.RunUntilHalt(() => uart.Text.Contains("PASS"), maxInstructions: 5_000_000);
result.Outcome.Should().Be(RunOutcome.PredicateMet);   // PredicateMet / LockedUp / BudgetReached
```

Or headless from a pipeline, with the `stm32` runner CLI (exit 0 = found · 1 = not found · 2 = lockup):

```bash
dotnet run --project src/STM32Sharp.Runner -- firmware.bin --expect-text "PASS" --uart 2
```

### Co-simulation (clock-event scheduler)

Like avr8js / rp2040js, the emulator exposes a **per-cycle event queue** (`ClockEventQueue`) so it can
be coupled to an external simulator with its own solver. The engine only advances the CPU up to the
next event before ticking the peripherals, so interrupts, timeouts and host-scheduled events fire at
the exact cycle, independent of the batch size. With nothing time-related pending, it runs the whole
budget at full speed in one go.

```csharp
using var m = new STM32Machine(Stm32ChipPreset.G071);
m.LoadFlash(firmware); m.Reset();

// The solver schedules stimuli at exact cycles:
m.Scheduler.Schedule(m.Cpu.Cycles + 48_000, () => m.GpioC.SetInput(13, true)); // pulse a pin
m.RunUntilCycle(m.Cpu.Cycles + 100_000);                                        // advance cycle-accurately
```

`Scheduler.Schedule(atCycle, cb)` / `Cancel`, `Scheduler.NextCycle` (how far to advance) and
`RunUntilCycle(target)` cover the co-simulation loop. The time-based peripherals (SysTick, TIM, LPTIM,
RTC, watchdogs) declare their next event, so their IRQs already fire at the right instant.

## Memory map (STM32G0)

| Region | Address | Contents |
|--------|---------|----------|
| `0x0` | `0x0000_0000` | Boot alias → Flash mirror (BOOT0 = 0) |
| `0x0` | `0x0800_0000` | Flash (pointer fast-path) |
| `0x2` | `0x2000_0000` | SRAM (pointer fast-path) |
| `0x4` | `0x4000_0000` | APB/AHB peripherals (RCC, FLASH, SYSCFG, EXTI, TIM, USART, SPI, I2C, ADC, DMA, CRC…) |
| `0x5` | `0x5000_0000` | GPIO (IOPORT) |
| `0xE` | `0xE000_0000` | PPB: NVIC, SysTick, SCB |

Flash and SRAM are served through pointer arithmetic; everything else is routed by absolute address
in `PeripheralBus`.

## Firmware

Requires the [Arm GNU Toolchain](https://developer.arm.com/downloads/-/arm-gnu-toolchain-downloads)
(`arm-none-eabi-gcc`). The committed `.bin` files let the tests run offline; the scripts rebuild them.

```bash
./firmware/build.sh          # boot_clock, blink, uart_echo, feature_check (G071 + C031 + L031)
./firmware/build_arduino.sh  # STM32duino sketch (requires arduino-cli + STMicroelectronics:stm32)
./firmware/build_wokwi.sh    # the official wokwi/stm32-hello-wokwi HAL project (requires git)
```

- **boot_clock / blink / uart_echo** — bare-metal HAL-style boot, PA5 LED toggle, USART2 echo.
- **feature_check** — compiled against the **official ST CMSIS headers** (`stm32g071xx.h` and ARM
  CMSIS-Core, cloned by `build.sh`). It exercises TIM/SPI/I2C/RTC/DMA/DMAMUX through ST's authoritative
  structs only; a green result (`0xFF` at `0x2000_0000`) proves bit-level register fidelity.
  Recompiled as **feature_check_c031** (`stm32c031xx.h`) and **feature_check_l0** (`stm32l031xx.h`,
  exercising the L0 RCC/Flash and CSELR DMA routing) for the Wokwi parts.
- **Arduino (STM32duino)** — a real sketch built with the official ST Arduino core; boots the full HAL
  and prints over Serial (LPUART1 on the Nucleo-G071RB) with interrupt-driven UART, blinking PA5.
- **Wokwi parity** — the official `wokwi/stm32-hello-wokwi` STM32CubeMX/HAL project (the exact firmware
  Wokwi runs for the Nucleo-C031C6) boots and prints "Hello, Wokwi!" over USART2 on the C031 preset,
  unmodified (see `WokwiParityTests`).

## Solution structure

| Project | Description |
|---------|-------------|
| `src/STM32Sharp` | Core library — CPU, bus, peripherals, `STM32Machine` |
| `src/STM32.TestKit` | Fluent test harness (`STM32TestSimulation` + UART/GPIO probes) |
| `src/STM32Sharp.Runner` | Headless `stm32` CLI: run firmware, `--expect-text`, CI exit codes |
| `src/STM32Sharp.Demo` | Interactive demo (blink + UART echo) |
| `tests/STM32Sharp.Tests` | 393 tests (Thumb-1 ISA + peripherals + integration) |
| `firmware/` | Bare-metal, Arduino and Wokwi sample firmware |

## Roadmap

### Core / CPU
- [x] Full Thumb-1 instruction set, exceptions, NVIC, SysTick
- [x] WFI / WFE sleep with correct event-driven wakeup
- [x] Per-cycle clock-event scheduler for cycle-accurate co-simulation
- [ ] Cortex-M3/M4 (Thumb-2) for the STM32F1/F4 families — out of scope for the M0+ target

### Peripherals
- [x] RCC, FLASH, PWR, SYSCFG, EXTI, GPIO
- [x] USART1/2, LPUART1
- [x] TIM2/TIM3 (PWM, capture, compare), LPTIM1/2
- [x] SPI1/2, I2C1/2 (with IRQ)
- [x] ADC
- [x] DMA1 + DMAMUX (memory-to-memory, request-driven RX/TX, request generators); L0 DMA CSELR
- [x] CRC, RTC (calendar + alarm), IWDG/WWDG
- [ ] Remaining peripherals (DAC, COMP, TSC, AES on parts that have them) as firmware requires

> Note: these STM32C0/L0/G0 parts have **no hardware RNG** (verified against the CMSIS headers — there
> is no `RNG_BASE`), so an RNG peripheral is intentionally not modelled.

### Ecosystem
- [x] Chip presets matching the Wokwi-supported boards
- [x] Validation against the official ST CMSIS headers
- [x] Real STM32duino and Wokwi HAL firmware running unmodified
- [ ] NuGet package and NativeAOT targets

## Contributing

1. Fork the repository.
2. Create a feature branch (`git checkout -b feature/my-feature`).
3. Ensure all tests pass (`dotnet test STM32.slnx`).
4. Open a Pull Request against `main`.

## License

MIT License — see [LICENSE](LICENSE).

Shares its Cortex-M0+ core with [RP2040Sharp](https://github.com/PyMCU/RP2040Sharp), based on the
original work from [rp2040js](https://github.com/wokwi/rp2040js) © 2021 Uri Shaked.
C# port © 2026 Iván Montiel Cardona.
