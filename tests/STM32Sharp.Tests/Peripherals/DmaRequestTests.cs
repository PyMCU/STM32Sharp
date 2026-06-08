using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

/// <summary>Request-driven (DREQ) DMA transfers routed through the DMAMUX.</summary>
public class DmaRequestTests
{
    private const uint DMA = 0x40020000;
    private const uint ISR = DMA + 0x00;

    // Channel 1 registers.
    private const uint CCR1 = DMA + 0x08;
    private const uint CNDTR1 = DMA + 0x0C;
    private const uint CPAR1 = DMA + 0x10;
    private const uint CMAR1 = DMA + 0x14;

    // DMAMUX channel-config registers (C0CR drives DMA channel 1).
    private const uint DMAMUX = 0x40020800;
    private const uint C0CR = DMAMUX + 0x00;

    private const uint EN = 1u << 0;
    private const uint TCIE = 1u << 1;
    private const uint CIRC = 1u << 5;
    private const uint MINC = 1u << 7;

    private const uint TCIF1 = 1u << 1;

    // Request line ids (RM0444).
    private const uint REQ_USART1_RX = 50;
    private const uint REQ_SPI1_RX = 16;

    private const uint USART1 = 0x40013800;
    private const uint USART1_CR1 = USART1 + 0x00;
    private const uint USART1_RDR = USART1 + 0x24;
    private const uint UE = 1u << 0;
    private const uint RE = 1u << 2;

    private const uint SPI1 = 0x40013000;
    private const uint SPI1_CR1 = SPI1 + 0x00;
    private const uint SPI1_DR = SPI1 + 0x0C;
    private const uint SPE = 1u << 6;

    [Fact]
    public void Usart_rx_dreq_moves_bytes_into_memory_buffer()
    {
        using var m = new STM32Machine();
        uint buf = 0x20000200;

        m.Bus.WriteWord(C0CR, REQ_USART1_RX);   // map USART1_RX → DMA channel 1
        m.Bus.WriteWord(CPAR1, USART1_RDR);
        m.Bus.WriteWord(CMAR1, buf);
        m.Bus.WriteWord(CNDTR1, 3);
        m.Bus.WriteWord(CCR1, MINC | EN);        // byte-size, mem increments, periph→mem

        m.Bus.WriteWord(USART1_CR1, UE | RE);
        m.Usart1.InjectByte(0x11);
        m.Usart1.InjectByte(0x22);
        m.Usart1.InjectByte(0x33);

        m.Bus.ReadByte(buf + 0).Should().Be((byte)0x11);
        m.Bus.ReadByte(buf + 1).Should().Be((byte)0x22);
        m.Bus.ReadByte(buf + 2).Should().Be((byte)0x33);
        (m.Bus.ReadWord(ISR) & TCIF1).Should().NotBe(0);
    }

    [Fact]
    public void Transfer_complete_raises_channel_irq()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(C0CR, REQ_USART1_RX);
        m.Bus.WriteWord(CPAR1, USART1_RDR);
        m.Bus.WriteWord(CMAR1, 0x20000200);
        m.Bus.WriteWord(CNDTR1, 1);
        m.Bus.WriteWord(CCR1, MINC | TCIE | EN);

        m.Usart1.InjectByte(0xAB);

        // Channel 1 → IRQ9 on STM32G0.
        (m.Cpu.Registers.PendingInterrupts & (1u << 9)).Should().NotBe(0);
    }

    [Fact]
    public void Circular_channel_reloads_and_keeps_running()
    {
        using var m = new STM32Machine();
        uint buf = 0x20000300;

        m.Bus.WriteWord(C0CR, REQ_USART1_RX);
        m.Bus.WriteWord(CPAR1, USART1_RDR);
        m.Bus.WriteWord(CMAR1, buf);
        m.Bus.WriteWord(CNDTR1, 2);
        m.Bus.WriteWord(CCR1, MINC | CIRC | EN);

        // Two bytes complete the buffer once; two more wrap and overwrite from the start.
        m.Usart1.InjectByte(0x01);
        m.Usart1.InjectByte(0x02);
        m.Usart1.InjectByte(0x03);
        m.Usart1.InjectByte(0x04);

        m.Bus.ReadByte(buf + 0).Should().Be((byte)0x03);
        m.Bus.ReadByte(buf + 1).Should().Be((byte)0x04);
        (m.Bus.ReadWord(CCR1) & EN).Should().NotBe(0); // still enabled (circular)
    }

    [Fact]
    public void Spi_rx_dreq_captures_received_byte()
    {
        using var m = new STM32Machine();
        uint buf = 0x20000400;
        m.Spi1.OnTransfer = _ => 0x5A;

        m.Bus.WriteWord(C0CR, REQ_SPI1_RX);
        m.Bus.WriteWord(CPAR1, SPI1_DR);
        m.Bus.WriteWord(CMAR1, buf);
        m.Bus.WriteWord(CNDTR1, 1);
        m.Bus.WriteWord(CCR1, EN); // single byte, no increment

        m.Bus.WriteWord(SPI1_CR1, SPE);
        m.Bus.WriteByte(SPI1_DR, 0x00); // clock a frame → MISO byte arrives → DREQ

        m.Bus.ReadByte(buf).Should().Be((byte)0x5A);
        (m.Bus.ReadWord(ISR) & TCIF1).Should().NotBe(0);
    }

    [Fact]
    public void Unmapped_request_does_not_transfer()
    {
        using var m = new STM32Machine();
        uint buf = 0x20000500;
        m.Bus.WriteWord(buf, 0xDEADBEEF);

        // DMAMUX left at request id 0 (no mapping); channel armed nonetheless.
        m.Bus.WriteWord(CPAR1, USART1_RDR);
        m.Bus.WriteWord(CMAR1, buf);
        m.Bus.WriteWord(CNDTR1, 1);
        m.Bus.WriteWord(CCR1, MINC | EN);

        m.Usart1.InjectByte(0x99);

        m.Bus.ReadWord(buf).Should().Be(0xDEADBEEFu); // untouched
        (m.Bus.ReadWord(ISR) & TCIF1).Should().Be(0u);
    }

    [Fact]
    public void Dmamux_maps_request_to_expected_channel()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(C0CR, REQ_USART1_RX);
        m.Dmamux.ChannelForRequest((int)REQ_USART1_RX).Should().Be(1);
        m.Dmamux.ChannelForRequest((int)REQ_SPI1_RX).Should().Be(-1);
        m.Dmamux.ChannelForRequest(0).Should().Be(-1);
    }
}
