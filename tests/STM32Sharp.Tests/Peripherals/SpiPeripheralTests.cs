using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

public class SpiPeripheralTests
{
    private const uint SPI1 = 0x40013000;
    private const uint CR1 = SPI1 + 0x00;
    private const uint CR2 = SPI1 + 0x04;
    private const uint SR = SPI1 + 0x08;
    private const uint DR = SPI1 + 0x0C;

    private const uint SPE = 1u << 6;
    private const uint SR_RXNE = 1u << 0;
    private const uint SR_TXE = 1u << 1;
    private const uint RXNEIE = 1u << 6;
    private const uint TXEIE = 1u << 7;

    private const uint IRQ_SPI1 = 1u << 25;

    [Fact]
    public void Txe_is_ready_after_enable()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CR1, SPE);
        (m.Bus.ReadWord(SR) & SR_TXE).Should().NotBe(0);
    }

    [Fact]
    public void Transfer_invokes_slave_and_returns_miso_byte()
    {
        using var m = new STM32Machine();
        byte? mosi = null;
        m.Spi1.OnTransfer = b => { mosi = b; return 0xA5; };

        m.Bus.WriteWord(CR1, SPE);
        m.Bus.WriteByte(DR, 0x3C);

        mosi.Should().Be((byte)0x3C);
        (m.Bus.ReadWord(SR) & SR_RXNE).Should().NotBe(0);
        m.Bus.ReadByte(DR).Should().Be((byte)0xA5);
        (m.Bus.ReadWord(SR) & SR_RXNE).Should().Be(0u); // cleared after read
    }

    [Fact]
    public void No_slave_returns_0xFF()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CR1, SPE);
        m.Bus.WriteByte(DR, 0x01);
        m.Bus.ReadByte(DR).Should().Be((byte)0xFF);
    }

    [Fact]
    public void Disabled_spi_does_not_transfer()
    {
        using var m = new STM32Machine();
        var called = false;
        m.Spi1.OnTransfer = b => { called = true; return 0; };
        m.Bus.WriteByte(DR, 0x10); // SPE not set
        called.Should().BeFalse();
    }

    [Fact]
    public void Txeie_raises_irq_immediately()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CR2, TXEIE); // TXE is always asserted
        (m.Cpu.Registers.PendingInterrupts & IRQ_SPI1).Should().NotBe(0);
    }

    [Fact]
    public void Rxneie_raises_irq_on_received_byte_and_clears_on_read()
    {
        using var m = new STM32Machine();
        m.Spi1.OnTransfer = _ => 0xA5;

        m.Bus.WriteWord(CR1, SPE);
        m.Bus.WriteWord(CR2, RXNEIE);
        (m.Cpu.Registers.PendingInterrupts & IRQ_SPI1).Should().Be(0u); // no byte yet

        m.Bus.WriteByte(DR, 0x3C); // clocks in a byte → RXNE
        (m.Cpu.Registers.PendingInterrupts & IRQ_SPI1).Should().NotBe(0);

        m.Bus.ReadByte(DR); // reading clears RXNE
        (m.Cpu.Registers.PendingInterrupts & IRQ_SPI1).Should().Be(0u);
    }
}
