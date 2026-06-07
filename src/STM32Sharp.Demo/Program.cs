using STM32.TestKit;

// Interactive STM32Sharp demo: boots two real bare-metal firmwares and shows the
// emulated peripherals working — a blinking PA5 LED and a USART2 echo.

var firmwareDir = Path.Combine(AppContext.BaseDirectory, "Firmware");

Console.WriteLine("=== STM32Sharp demo (Cortex-M0+, STM32G0) ===\n");

// ── Blink demo ────────────────────────────────────────────────────────────
var blinkPath = Path.Combine(firmwareDir, "blink.bin");
if (File.Exists(blinkPath))
{
    Console.WriteLine("[blink] Booting blink.bin — watching PA5 (Nucleo-G071RB LED)...");
    using var sim = STM32TestSimulation.Create()
        .WithBinary(File.ReadAllBytes(blinkPath))
        .AddGpio("A", out var gpio);

    var lastTransitions = 0;
    for (var step = 0; step < 8; step++)
    {
        sim.RunInstructions(500_000);
        var t = gpio.Transitions(5);
        if (t != lastTransitions)
        {
            Console.WriteLine($"  PA5 -> {(gpio.Level(5) ? "HIGH" : "low ")}  (transitions: {t})");
            lastTransitions = t;
        }
    }
    Console.WriteLine($"[blink] PA5 toggled {gpio.Transitions(5)} times. CPU locked up: {sim.Cpu.IsLockedUp}\n");
}
else
{
    Console.WriteLine("[blink] blink.bin not found — run firmware/build.sh first.\n");
}

// ── UART echo demo ──────────────────────────────────────────────────────────
var echoPath = Path.Combine(firmwareDir, "uart_echo.bin");
if (File.Exists(echoPath))
{
    Console.WriteLine("[uart] Booting uart_echo.bin on USART2...");
    using var sim = STM32TestSimulation.Create()
        .WithBinary(File.ReadAllBytes(echoPath))
        .AddUart(2, out var uart);

    sim.RunUntilHalt(uart, "READY", maxInstructions: 2_000_000);
    Console.WriteLine($"  device says: {uart.Text.TrimEnd()}");

    const string message = "Hello, STM32!";
    Console.WriteLine($"  sending: {message}");
    uart.InjectString(message);
    sim.RunUntilHalt(() => uart.Text.EndsWith(message), maxInstructions: 2_000_000);

    var echoed = uart.Text["READY\n".Length..];
    Console.WriteLine($"  echoed back: {echoed}\n");
}
else
{
    Console.WriteLine("[uart] uart_echo.bin not found — run firmware/build.sh first.\n");
}

Console.WriteLine("=== demo complete ===");
