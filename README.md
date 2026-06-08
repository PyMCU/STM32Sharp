# STM32Sharp

Emulador de microcontroladores **STM32 (núcleo ARM Cortex-M0+)** en C#, parte de la familia de
emuladores "Sharp". Apunta a las series **STM32C0 / F0 / G0 / L0** (target de referencia:
**STM32G071**, Nucleo-G071RB). AOT-compatible, sin reflexión ni generación dinámica de código.

Comparte el núcleo Cortex-M0+ (ISA Thumb-1 de ARMv6-M) con
[RP2040Sharp](https://github.com/PyMCU/RP2040Sharp): el CPU, el decodificador de instrucciones, el
banco de registros y el NVIC/SysTick son comunes; lo específico de STM32 es el mapa de memoria y los
periféricos.

## Chips (presets)

`Stm32ChipPreset` fija memoria/reloj por pieza; el mapa de periféricos es el de la familia STM32G0,
que la línea STM32C0 comparte verbatim (verificado contra los headers CMSIS oficiales). Se usa con
`new STM32Machine(Stm32ChipPreset.C031)` o `STM32TestSimulation.Create(Stm32ChipPreset.C031)`.

| Preset | Núcleo | Flash / SRAM | Fidelidad | Notas |
|--------|--------|--------------|-----------|-------|
| `G071` | Cortex-M0+ | 128 KB / 36 KB | ✅ completa | Target de referencia |
| `G031` | Cortex-M0+ | 64 KB / 8 KB | ✅ completa | Mismo mapa que G071 |
| `C031` | Cortex-M0+ | 32 KB / 12 KB | ✅ completa | **Nucleo-C031C6 (Wokwi)**; mapa idéntico al G0 |
| `L031` | Cortex-M0+ | 32 KB / 8 KB | ✅ completa | **Nucleo-L031K6 (Wokwi)**; RCC/Flash propios del L0 + DMA CSELR |
| `F103C8` | Cortex-M3 | 64 KB / 20 KB | ❌ no emulable | **BluePill (Wokwi)**; requiere núcleo Thumb-2 |

> C031 y L031 están validados end-to-end con `feature_check` recompilado contra los headers oficiales
> `stm32c031xx.h` / `stm32l031xx.h` (ver más abajo). En L0 el RCC (boot MSI/HSI/PLL), el Flash (unlock
> PECR) y el enrutado DMA por CSELR son específicos de la familia. El F103 es Cortex-M3 (fuera del M0+).

## Arquitectura

```
src/
  STM32Sharp/                 librería del emulador
    Core/Cpu/                 CortexM0Plus, InstructionDecoder (LUT O(1)), Registers, Instructions/
    Core/Memory/              BusInterconnect, PeripheralBus, Ram, interfaces
    Peripherals/              STM32Machine + Ppb (NVIC/SysTick/SCB), Rcc, Flash, SysCfg, Exti,
                              Gpio, Usart, Timer, Spi, I2c, Adc, Dma, Dmamux, Rtc, Iwdg, Wwdg
  STM32.TestKit/              arnés de pruebas fluido (STM32TestSimulation + probes UART/GPIO)
  STM32Sharp.Runner/          CLI headless para CI (stm32 <bin> --expect-text ...)
  STM32Sharp.Demo/            demo interactiva (blink + UART echo)
tests/STM32Sharp.Tests/       376 tests (ISA Thumb-1 + periféricos + integración)
firmware/                     firmware bare-metal de ejemplo (compilado con arm-none-eabi-gcc)
```

### Mapa de memoria (STM32G0)

| Región | Dirección | Contenido |
|--------|-----------|-----------|
| `0x0` | `0x0000_0000` | Boot alias → espejo de Flash (BOOT0 = 0) |
| `0x0` | `0x0800_0000` | Flash (fast-path por puntero) |
| `0x2` | `0x2000_0000` | SRAM (fast-path por puntero) |
| `0x4` | `0x4000_0000` | Periféricos APB/AHB (RCC, FLASH, SYSCFG, EXTI, TIM, USART, SPI, I2C, ADC, DMA…) |
| `0x5` | `0x5000_0000` | GPIO (IOPORT) |
| `0xE` | `0xE000_0000` | PPB: NVIC, SysTick, SCB |

Flash y SRAM se sirven por aritmética de punteros; el resto se enruta por dirección absoluta en
`PeripheralBus`.

## Uso (TestKit)

```csharp
using var sim = STM32TestSimulation.Create()
    .WithBinary(File.ReadAllBytes("uart_echo.bin"))
    .AddUart(2, out var uart)
    .AddGpio("A", out var gpio);

sim.RunUntilHalt(uart, "READY");      // nunca cuelga: acotado por presupuesto de instrucciones
uart.InjectString("Hola");
sim.RunUntilHalt(() => uart.Text.EndsWith("Hola"));
```

## Co-simulación (scheduler de eventos de reloj)

Como avr8js / rp2040js, el emulador expone una **cola de eventos por ciclo** (`ClockEventQueue`) para
acoplarlo a un simulador externo con su propio solver. El motor solo avanza el CPU hasta el próximo
evento antes de tickear los periféricos, así que interrupciones, timeouts y eventos programados por el
host disparan en el ciclo exacto, sin depender del tamaño del lote. Sin nada temporal pendiente, corre
todo el presupuesto a máxima velocidad de una vez.

```csharp
using var m = new STM32Machine(Stm32ChipPreset.G071);
m.LoadFlash(firmware); m.Reset();

// El solver programa estímulos en ciclos exactos:
m.Scheduler.Schedule(m.Cpu.Cycles + 48_000, () => m.GpioC.SetInput(13, true)); // pulsa un pin
m.RunUntilCycle(m.Cpu.Cycles + 100_000);                                        // avanza con precisión de ciclo
```

`Scheduler.Schedule(atCycle, cb)` / `Cancel`, `Scheduler.NextCycle` (para saber hasta dónde avanzar) y
`RunUntilCycle(target)` cubren el bucle de co-simulación. Los periféricos temporales (SysTick, TIM, RTC,
watchdogs) declaran su próximo evento, de modo que sus IRQ ya ocurren en el instante correcto.

## Runner (CI)

```bash
dotnet run --project src/STM32Sharp.Runner -- firmware.bin --expect-text "PASS" --uart 2
# exit 0 = texto encontrado · 1 = no encontrado · 2 = CPU en lockup
```

## Firmware de ejemplo

Requiere el [Arm GNU Toolchain](https://developer.arm.com/downloads/-/arm-gnu-toolchain-downloads)
(`arm-none-eabi-gcc`):

```bash
./firmware/build.sh    # compila boot_clock, blink, uart_echo y feature_check (G071 + C031);
                       # clona los headers CMSIS oficiales y copia los .bin a los tests
```

- **boot_clock** — replica la secuencia del HAL: enciende PLL, espera `PLLRDY`, conmuta el reloj,
  configura SysTick con interrupción.
- **blink** — alterna PA5 (LED de la Nucleo-G071RB) vía GPIOA BSRR.
- **uart_echo** — emite un saludo y hace eco de los bytes recibidos por USART2.
- **feature_check** — compilado contra los **headers CMSIS oficiales de STMicroelectronics**
  (`stm32g071xx.h`) y ARM CMSIS-Core, clonados de GitHub por `build.sh`. No usa ninguna dirección
  escrita a mano: accede a TIM/SPI/I2C/RTC/DMA/DMAMUX vía las estructuras y máscaras de bits
  autoritativas de ST. Un resultado en verde (0xFF en `0x2000_0000`) demuestra que el mapa de
  memoria, los offsets de registros y la semántica de bits del emulador coinciden con el silicio.
- **feature_check_c031** — el mismo firmware recompilado contra `stm32c031xx.h` (header oficial del
  STM32C0) y un linker script de 32 KB/12 KB. Corre sobre el preset `C031` y pasa los 8 subtests,
  probando que una pieza soportada por Wokwi funciona end-to-end.
- **feature_check_l0** — firmware del **STM32L031** contra `stm32l031xx.h`, que ejercita el RCC del L0
  (boot HSI→PLL), el Flash del L0 (unlock PECR de dos etapas) y el enrutado DMA por CSELR, además de
  TIM2/SPI1/I2C1/RTC. Corre sobre el preset `L031` y pasa los 8 subtests.

### Firmware Arduino (STM32duino)

`arduino_blink` es un sketch real compilado con el **core Arduino oficial de STMicroelectronics**
(STM32duino), que se apoya en el HAL de ST — el binario arranca por `SystemClock_Config()`, HAL GPIO
y HAL UART por interrupción, igual que el firmware que un usuario subiría a la placa. En la
Nucleo-G071RB, `Serial` es **LPUART1** (PA2/PA3) y `LED_BUILTIN` es **PA5**. El emulador ejecuta el
boot completo del HAL, emite el banner por LPUART1 y parpadea PA5 (verificado en `ArduinoTests`).

```bash
./firmware/build_arduino.sh   # requiere arduino-cli + el core STMicroelectronics:stm32
```

## Estado

- ✅ Núcleo Cortex-M0+ (Thumb-1 completo, NVIC, SysTick, excepciones) — ISA validado con 267 tests.
- ✅ Boot de firmware real compilado con GCC (RCC/PLL, Flash, GPIO, USART).
- ✅ Periféricos avanzados validados con firmware compilado contra los **headers CMSIS oficiales de
  ST** (`feature_check`): TIM, SPI/I2C con IRQ, RTC, DMA mem-to-mem y request-driven vía DMAMUX.
- ✅ Periféricos: NVIC/SysTick/SCB, RCC, FLASH (unlock/erase/program), PWR, SYSCFG, EXTI, GPIO,
  USART1/2 + LPUART1, TIM2/TIM3 (PWM/captura/comparación), SPI1/SPI2 (con IRQ), I2C1/I2C2 (con IRQ),
  ADC, DMA1 + DMAMUX (mem-to-mem y request-driven/DREQ), RTC (calendario + alarma), IWDG/WWDG.
- ✅ Firmware **Arduino (STM32duino)** real: boot del HAL completo, Serial por LPUART1 e IRQ, blink PA5.
- ✅ **Scheduler de eventos por ciclo** (`ClockEventQueue`) para co-simulación cycle-accurate, al estilo
  avr8js / rp2040js (`Scheduler.Schedule` + `RunUntilCycle`).
- ✅ TestKit + Runner + Demo.
- ✅ 376 tests en verde.
- ⏳ Pendiente: DMA TX request-driven dirigido por reloj, request generators del DMAMUX,
  periféricos restantes (LPTIM, CRC, RNG) según el firmware objetivo.

## Licencia

MIT.
