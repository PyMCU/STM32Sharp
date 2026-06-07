using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

public class GpioPortTests
{
    private const uint GPIOA_ODR = 0x50000014;
    private const uint GPIOA_BSRR = 0x50000018;
    private const uint GPIOA_BRR = 0x50000028;
    private const uint GPIOA_IDR = 0x50000010;
    private const uint GPIOA_MODER = 0x50000000;

    [Fact]
    public void Bsrr_sets_and_resets_odr_bits()
    {
        using var m = new STM32Machine();

        m.Bus.WriteWord(GPIOA_BSRR, 1u << 5);          // set PA5
        (m.Bus.ReadWord(GPIOA_ODR) & (1u << 5)).Should().NotBe(0);
        m.GpioA.GetOutput(5).Should().BeTrue();

        m.Bus.WriteWord(GPIOA_BSRR, 1u << (5 + 16));   // reset PA5
        m.GpioA.GetOutput(5).Should().BeFalse();
    }

    [Fact]
    public void Brr_resets_odr_bits()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(GPIOA_ODR, 0xFFFF);
        m.Bus.WriteWord(GPIOA_BRR, 1u << 3);
        m.GpioA.GetOutput(3).Should().BeFalse();
        m.GpioA.GetOutput(4).Should().BeTrue();
    }

    [Fact]
    public void OnPinChange_fires_on_level_change()
    {
        using var m = new STM32Machine();
        var events = new List<(int pin, bool high)>();
        m.GpioA.OnPinChange += (p, h) => events.Add((p, h));

        m.Bus.WriteWord(GPIOA_BSRR, 1u << 2);
        m.Bus.WriteWord(GPIOA_BSRR, 1u << (2 + 16));

        events.Should().ContainInOrder((2, true), (2, false));
    }

    [Fact]
    public void Idr_reflects_odr_for_output_pins()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(GPIOA_MODER, 0x1u << (5 * 2)); // PA5 output
        m.Bus.WriteWord(GPIOA_BSRR, 1u << 5);
        (m.Bus.ReadWord(GPIOA_IDR) & (1u << 5)).Should().NotBe(0);
    }

    [Fact]
    public void Idr_reflects_external_input_for_input_pins()
    {
        using var m = new STM32Machine();
        // PA0 left as input (MODER reset = 0). Drive external high.
        m.GpioA.SetInput(0, true);
        (m.Bus.ReadWord(GPIOA_IDR) & 1u).Should().NotBe(0);
    }
}

public class UsartPeripheralTests
{
    private const uint USART2_CR1 = 0x40004400;
    private const uint USART2_ISR = 0x40004400 + 0x1C;
    private const uint USART2_RDR = 0x40004400 + 0x24;
    private const uint USART2_TDR = 0x40004400 + 0x28;

    [Fact]
    public void Tdr_write_emits_byte_when_enabled()
    {
        using var m = new STM32Machine();
        byte? sent = null;
        m.Usart2.OnByteTransmit += b => sent = b;

        m.Bus.WriteWord(USART2_CR1, 0x1 | 0x8); // UE | TE
        m.Bus.WriteWord(USART2_TDR, 0x41);

        sent.Should().Be((byte)0x41);
    }

    [Fact]
    public void Injected_byte_sets_rxne_and_is_read_via_rdr()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(USART2_CR1, 0x1 | 0x4); // UE | RE

        m.Usart2.InjectByte(0x37);
        (m.Bus.ReadWord(USART2_ISR) & (1u << 5)).Should().NotBe(0); // RXNE
        m.Bus.ReadWord(USART2_RDR).Should().Be(0x37u);
        (m.Bus.ReadWord(USART2_ISR) & (1u << 5)).Should().Be(0); // cleared
    }

    [Fact]
    public void Txe_and_tc_are_always_ready()
    {
        using var m = new STM32Machine();
        var isr = m.Bus.ReadWord(USART2_ISR);
        (isr & (1u << 7)).Should().NotBe(0); // TXE
        (isr & (1u << 6)).Should().NotBe(0); // TC
    }
}
