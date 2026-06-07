using System.Text;
using STM32.Peripherals;

namespace STM32Sharp.Tests.Integration;

/// <summary>Phase 3: real blink firmware toggles PA5 through the GPIO peripheral.</summary>
public class BlinkTests
{
    private static byte[] Firmware() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Firmware", "blink.bin"));

    [Fact]
    public void Blink_toggles_pa5()
    {
        using var m = new STM32Machine();

        var transitions = 0;
        m.GpioA.OnPinChange += (pin, _) => { if (pin == 5) transitions++; };

        m.LoadFlash(Firmware());
        m.Reset();

        for (var i = 0; i < 200; i++)
            m.Run(10_000);

        m.Cpu.IsLockedUp.Should().BeFalse();
        transitions.Should().BeGreaterThan(2, "PA5 should toggle repeatedly");
    }
}

/// <summary>Phase 3: real USART echo firmware re-transmits injected bytes.</summary>
public class UartEchoTests
{
    private static byte[] Firmware() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Firmware", "uart_echo.bin"));

    [Fact]
    public void Greeting_is_transmitted_then_input_is_echoed()
    {
        using var m = new STM32Machine();

        var tx = new StringBuilder();
        m.Usart2.OnByteTransmit += b => tx.Append((char)b);

        m.LoadFlash(Firmware());
        m.Reset();

        // Let the firmware send its greeting and reach the echo loop.
        for (var i = 0; i < 50; i++) m.Run(10_000);
        tx.ToString().Should().Contain("READY");

        // Inject "Hi" and let it echo back.
        m.Usart2.InjectByte((byte)'H');
        m.Usart2.InjectByte((byte)'i');
        for (var i = 0; i < 50; i++) m.Run(10_000);

        tx.ToString().Should().EndWith("Hi");
        m.Cpu.IsLockedUp.Should().BeFalse();
    }
}
