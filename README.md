# STM32Sharp

Emulador de microcontroladores **STM32 (núcleo ARM Cortex-M0+)** en C#, parte de la familia de
emuladores "Sharp". Apunta a las series **STM32C0 / F0 / G0 / L0** (target de referencia:
**STM32G071**, Nucleo-G071RB). AOT-compatible, sin reflexión ni generación dinámica de código.

Comparte el núcleo Cortex-M0+ (ISA Thumb-1 de ARMv6-M) con
[RP2040Sharp](https://github.com/PyMCU/RP2040Sharp): el CPU, el decodificador de instrucciones, el
banco de registros y el NVIC/SysTick son comunes; lo específico de STM32 es el mapa de memoria y los
periféricos.

## Arquitectura

```
src/
  STM32Sharp/                 librería del emulador
    Core/Cpu/                 CortexM0Plus, InstructionDecoder (LUT O(1)), Registers, Instructions/
    Core/Memory/              BusInterconnect, PeripheralBus, Ram, interfaces
    Peripherals/              STM32Machine + Ppb (NVIC/SysTick/SCB), Rcc, Flash, Gpio, Usart
  STM32.TestKit/              arnés de pruebas fluido (STM32TestSimulation + probes UART/GPIO)
  STM32Sharp.Runner/          CLI headless para CI (stm32 <bin> --expect-text ...)
  STM32Sharp.Demo/            demo interactiva (blink + UART echo)
tests/STM32Sharp.Tests/       289 tests (ISA Thumb-1 + periféricos + integración)
firmware/                     firmware bare-metal de ejemplo (compilado con arm-none-eabi-gcc)
```

### Mapa de memoria (STM32G0)

| Región | Dirección | Contenido |
|--------|-----------|-----------|
| `0x0` | `0x0000_0000` | Boot alias → espejo de Flash (BOOT0 = 0) |
| `0x0` | `0x0800_0000` | Flash (fast-path por puntero) |
| `0x2` | `0x2000_0000` | SRAM (fast-path por puntero) |
| `0x4` | `0x4000_0000` | Periféricos APB/AHB (RCC, FLASH, USART…) |
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

## Runner (CI)

```bash
dotnet run --project src/STM32Sharp.Runner -- firmware.bin --expect-text "PASS" --uart 2
# exit 0 = texto encontrado · 1 = no encontrado · 2 = CPU en lockup
```

## Firmware de ejemplo

Requiere el [Arm GNU Toolchain](https://developer.arm.com/downloads/-/arm-gnu-toolchain-downloads)
(`arm-none-eabi-gcc`):

```bash
./firmware/build.sh    # compila boot_clock, blink y uart_echo, copia los .bin a los tests
```

- **boot_clock** — replica la secuencia del HAL: enciende PLL, espera `PLLRDY`, conmuta el reloj,
  configura SysTick con interrupción.
- **blink** — alterna PA5 (LED de la Nucleo-G071RB) vía GPIOA BSRR.
- **uart_echo** — emite un saludo y hace eco de los bytes recibidos por USART2.

## Estado

- ✅ Núcleo Cortex-M0+ (Thumb-1 completo, NVIC, SysTick, excepciones) — ISA validado con 267 tests.
- ✅ Boot de firmware real compilado con GCC (RCC/PLL, Flash, GPIO, USART).
- ✅ TestKit + Runner + Demo.
- ⏳ Pendiente: EXTI, TIM avanzados, SPI, I2C, ADC, DMA, programación/borrado de Flash.

## Licencia

MIT.
