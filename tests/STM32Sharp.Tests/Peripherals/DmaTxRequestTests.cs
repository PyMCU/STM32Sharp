using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

/// <summary>
/// Clock-driven, request-driven memory-to-peripheral (TX) DMA. Enabling a peripheral's transmit DMA
/// (USART CR3.DMAT / SPI CR2.TXDMAEN) makes the machine pump one element per frame period through the
/// cycle scheduler, so the buffer drains over time rather than in a single instantaneous burst. A tiny
/// spin firmware keeps the CPU advancing cycles while the DMA works in the background.
/// </summary>
public class DmaTxRequestTests
{
    private const uint DMA = 0x40020000;
    private const uint CCR1 = DMA + 0x08;
    private const uint CNDTR1 = DMA + 0x0C;
    private const uint CPAR1 = DMA + 0x10;
    private const uint CMAR1 = DMA + 0x14;

    private const uint DMAMUX = 0x40020800;
    private const uint C0CR = DMAMUX + 0x00;

    private const uint EN = 1u << 0;
    private const uint DIR = 1u << 4; // read from memory (TX)
    private const uint MINC = 1u << 7;

    private const uint REQ_USART2_TX = 53;
    private const uint REQ_SPI1_TX = 17;

    private const uint USART2 = 0x40004400;
    private const uint USART2_CR1 = USART2 + 0x00;
    private const uint USART2_CR3 = USART2 + 0x08;
    private const uint USART2_BRR = USART2 + 0x0C;
    private const uint USART2_TDR = USART2 + 0x28;
    private const uint UE = 1u << 0;
    private const uint TE = 1u << 3;
    private const uint DMAT = 1u << 7;

    private const uint SPI1 = 0x40013000;
    private const uint SPI1_CR1 = SPI1 + 0x00;
    private const uint SPI1_CR2 = SPI1 + 0x04;
    private const uint SPI1_DR = SPI1 + 0x0C;
    private const uint SPE = 1u << 6;
    private const uint TXDMAEN = 1u << 1;

    // Minimal Thumb image: vector table (SP, Reset) + a branch-to-self so the CPU spins, advancing the
    // cycle clock while background DMA runs. Reset points at 0x0800_0008 (Thumb bit set).
    private static byte[] SpinImage()
    {
        var img = new byte[0x0C];
        BitConverter.GetBytes(0x20002000u).CopyTo(img, 0); // initial SP
        BitConverter.GetBytes(0x08000009u).CopyTo(img, 4); // reset → 0x0800_0008 | thumb
        BitConverter.GetBytes((ushort)0xE7FE).CopyTo(img, 8); // b . (spin)
        return img;
    }

    private static STM32Machine Spinning()
    {
        var m = new STM32Machine();
        m.LoadFlash(SpinImage());
        m.Reset();
        return m;
    }

    private static void ArmTxChannel(STM32Machine m, uint reqId, uint periphReg, uint buf, byte[] data)
    {
        for (uint i = 0; i < data.Length; i++) m.Bus.WriteByte(buf + i, data[i]);
        m.Bus.WriteWord(C0CR, reqId);          // map request → DMA channel 1
        m.Bus.WriteWord(CPAR1, periphReg);
        m.Bus.WriteWord(CMAR1, buf);
        m.Bus.WriteWord(CNDTR1, (uint)data.Length);
        m.Bus.WriteWord(CCR1, DIR | MINC | EN); // byte size, memory increments, mem → peripheral
    }

    [Fact]
    public void Usart_tx_dma_drains_buffer_to_the_data_register_over_cycles()
    {
        using var m = Spinning();
        var sent = new List<byte>();
        m.Usart2.OnByteTransmit = b => sent.Add(b);

        byte[] data = [0xDE, 0xAD, 0xBE, 0xEF];
        ArmTxChannel(m, REQ_USART2_TX, USART2_TDR, 0x20000200, data);

        m.Bus.WriteWord(USART2_BRR, 16);            // frame period for pacing
        m.Bus.WriteWord(USART2_CR1, UE | TE);
        m.Bus.WriteWord(USART2_CR3, DMAT);          // enable TX DMA → starts the clock-paced pump

        m.RunUntilCycle(m.Cpu.Cycles + 2_000);

        sent.Should().Equal(data);
        (m.Bus.ReadWord(CCR1) & EN).Should().Be(0u, "single-shot channel disables once drained");
    }

    [Fact]
    public void Usart_tx_dma_is_paced_not_instantaneous()
    {
        using var m = Spinning();
        var sent = new List<byte>();
        m.Usart2.OnByteTransmit = b => sent.Add(b);

        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        ArmTxChannel(m, REQ_USART2_TX, USART2_TDR, 0x20000300, data);
        m.Bus.WriteWord(USART2_BRR, 100);           // 100 cycles per frame
        m.Bus.WriteWord(USART2_CR1, UE | TE);
        m.Bus.WriteWord(USART2_CR3, DMAT);

        m.RunUntilCycle(m.Cpu.Cycles + 250);        // room for ~2 frames only
        var partial = sent.Count;
        partial.Should().BeInRange(1, 5, "pacing means only a few frames have gone out so far");

        m.RunUntilCycle(m.Cpu.Cycles + 1_000);      // let the rest drain
        sent.Should().Equal(data);
        sent.Count.Should().BeGreaterThan(partial, "more frames transmit as cycles advance");
    }

    [Fact]
    public void Spi_tx_dma_clocks_each_byte_out_of_the_data_register()
    {
        using var m = Spinning();
        var clocked = new List<byte>();
        m.Spi1.OnTransfer = tx => { clocked.Add(tx); return 0x00; };

        byte[] data = [0x10, 0x20, 0x30];
        ArmTxChannel(m, REQ_SPI1_TX, SPI1_DR, 0x20000400, data);

        m.Bus.WriteWord(SPI1_CR1, SPE);             // baud prescaler 0 → small frame period
        m.Bus.WriteWord(SPI1_CR2, TXDMAEN);         // enable TX DMA

        m.RunUntilCycle(m.Cpu.Cycles + 2_000);

        clocked.Should().Equal(data, "each DMA element is written to DR and clocked out on MOSI");
    }
}
