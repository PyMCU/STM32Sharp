# STM32Sharp

An emulator for **STM32 microcontrollers (ARM Cortex-M0+ core)** written in C#, part of the "Sharp"
family of emulators. It targets the **STM32C0 / F0 / G0 / L0** series (reference target:
**STM32G071**, Nucleo-G071RB). AOT-compatible, with no reflection or dynamic code generation.

It shares the Cortex-M0+ core (ARMv6-M Thumb-1 ISA) with
[RP2040Sharp](https://github.com/PyMCU/RP2040Sharp): the CPU, the instruction decoder, the register
bank and the NVIC/SysTick are common; what is STM32-specific is the memory map and the peripherals.

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
> `stm32c031xx.h` / `stm32l031xx.h` headers (see below). On L0 the RCC (MSI/HSI/PLL boot), the Flash
> (PECR unlock) and the CSELR-based DMA routing are family-specific. The F103 is a Cortex-M3 (outside
> the M0+ scope).

## Architecture

```
src/
  STM32Sharp/                 emulator library
    Core/Cpu/                 CortexM0Plus, InstructionDecoder (O(1) LUT), Registers, Instructions/
    Core/Memory/              BusInterconnect, PeripheralBus, Ram, interfaces
    Peripherals/              STM32Machine + Ppb (NVIC/SysTick/SCB), Rcc, Flash, SysCfg, Exti,
                              Gpio, Usart, Timer, Spi, I2c, Adc, Dma, Dmamux, Rtc, Iwdg, Wwdg
  STM32.TestKit/              fluent test harness (STM32TestSimulation + UART/GPIO probes)
  STM32Sharp.Runner/          headless CLI for CI (stm32 <bin> --expect-text ...)
  STM32Sharp.Demo/            interactive demo (blink + UART echo)
tests/STM32Sharp.Tests/       376 tests (Thumb-1 ISA + peripherals + integration)
firmware/                     bare-metal sample firmware (built with arm-none-eabi-gcc)
```

### Memory map (STM32G0)

| Region | Address | Contents |
|--------|---------|----------|
| `0x0` | `0x0000_0000` | Boot alias → Flash mirror (BOOT0 = 0) |
| `0x0` | `0x0800_0000` | Flash (pointer fast-path) |
| `0x2` | `0x2000_0000` | SRAM (pointer fast-path) |
| `0x4` | `0x4000_0000` | APB/AHB peripherals (RCC, FLASH, SYSCFG, EXTI, TIM, USART, SPI, I2C, ADC, DMA…) |
| `0x5` | `0x5000_0000` | GPIO (IOPORT) |
| `0xE` | `0xE000_0000` | PPB: NVIC, SysTick, SCB |

Flash and SRAM are served through pointer arithmetic; everything else is routed by absolute address
in `PeripheralBus`.

## Usage (TestKit)

```csharp
using var sim = STM32TestSimulation.Create()
    .WithBinary(File.ReadAllBytes("uart_echo.bin"))
    .AddUart(2, out var uart)
    .AddGpio("A", out var gpio);

sim.RunUntilHalt(uart, "READY");      // never hangs: bounded by an instruction budget
uart.InjectString("Hello");
sim.RunUntilHalt(() => uart.Text.EndsWith("Hello"));
```

## Co-simulation (clock-event scheduler)

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

`Scheduler.Schedule(atCycle, cb)` / `Cancel`, `Scheduler.NextCycle` (to know how far to advance) and
`RunUntilCycle(target)` cover the co-simulation loop. The time-based peripherals (SysTick, TIM, RTC,
watchdogs) declare their next event, so their IRQs already fire at the right instant.

## Runner (CI)

```bash
dotnet run --project src/STM32Sharp.Runner -- firmware.bin --expect-text "PASS" --uart 2
# exit 0 = text found · 1 = not found · 2 = CPU in lockup
```

## Sample firmware

Requires the [Arm GNU Toolchain](https://developer.arm.com/downloads/-/arm-gnu-toolchain-downloads)
(`arm-none-eabi-gcc`):

```bash
./firmware/build.sh    # builds boot_clock, blink, uart_echo and feature_check (G071 + C031);
                       # clones the official CMSIS headers and copies the .bin files to the tests
```

- **boot_clock** — replicates the HAL sequence: enables the PLL, waits for `PLLRDY`, switches the
  clock, configures SysTick with an interrupt.
- **blink** — toggles PA5 (the Nucleo-G071RB LED) via GPIOA BSRR.
- **uart_echo** — emits a greeting and echoes bytes received over USART2.
- **feature_check** — built against the **official STMicroelectronics CMSIS headers**
  (`stm32g071xx.h`) and ARM CMSIS-Core, cloned from GitHub by `build.sh`. It uses no hand-written
  addresses: it accesses TIM/SPI/I2C/RTC/DMA/DMAMUX through ST's authoritative structs and bit masks.
  A green result (`0xFF` at `0x2000_0000`) proves that the emulator's memory map, register offsets and
  bit semantics match the silicon.
- **feature_check_c031** — the same firmware recompiled against `stm32c031xx.h` (the official STM32C0
  header) and a 32 KB/12 KB linker script. It runs on the `C031` preset and passes the 8 subtests,
  proving a Wokwi-supported part works end-to-end.
- **feature_check_l0** — **STM32L031** firmware against `stm32l031xx.h`, which exercises the L0 RCC
  (HSI→PLL boot), the L0 Flash (two-stage PECR unlock) and the CSELR-based DMA routing, plus
  TIM2/SPI1/I2C1/RTC. It runs on the `L031` preset and passes the 8 subtests.

### Arduino firmware (STM32duino)

`arduino_blink` is a real sketch built with the **official STMicroelectronics Arduino core**
(STM32duino), which sits on top of ST's HAL — the binary boots through `SystemClock_Config()`, HAL
GPIO and interrupt-driven HAL UART, just like firmware a user would flash onto the board. On the
Nucleo-G071RB, `Serial` is **LPUART1** (PA2/PA3) and `LED_BUILTIN` is **PA5**. The emulator runs the
full HAL boot, emits the banner over LPUART1 and blinks PA5 (verified in `ArduinoTests`).

```bash
./firmware/build_arduino.sh   # requires arduino-cli + the STMicroelectronics:stm32 core
```

## Status

- ✅ Cortex-M0+ core (complete Thumb-1, NVIC, SysTick, exceptions) — ISA validated with 267 tests.
- ✅ Boot of real GCC-built firmware (RCC/PLL, Flash, GPIO, USART).
- ✅ Advanced peripherals validated with firmware built against the **official ST CMSIS headers**
  (`feature_check`): TIM, SPI/I2C with IRQ, RTC, DMA memory-to-memory and request-driven via DMAMUX.
- ✅ Peripherals: NVIC/SysTick/SCB, RCC, FLASH (unlock/erase/program), PWR, SYSCFG, EXTI, GPIO,
  USART1/2 + LPUART1, TIM2/TIM3 (PWM/capture/compare), SPI1/SPI2 (with IRQ), I2C1/I2C2 (with IRQ),
  ADC, DMA1 + DMAMUX (memory-to-memory and request-driven/DREQ), RTC (calendar + alarm), IWDG/WWDG.
- ✅ Real **Arduino (STM32duino)** firmware: full HAL boot, Serial over LPUART1 with IRQ, PA5 blink.
- ✅ **Per-cycle event scheduler** (`ClockEventQueue`) for cycle-accurate co-simulation, in the style
  of avr8js / rp2040js (`Scheduler.Schedule` + `RunUntilCycle`).
- ✅ TestKit + Runner + Demo.
- ✅ 376 tests passing.
- ⏳ Pending: clock-driven request-driven DMA TX, DMAMUX request generators, remaining peripherals
  (LPTIM, CRC, RNG) as the target firmware requires.

## License

MIT.
