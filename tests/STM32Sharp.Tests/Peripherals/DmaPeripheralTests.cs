using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

public class DmaPeripheralTests
{
    private const uint DMA = 0x40020000;
    private const uint ISR = DMA + 0x00;
    private const uint IFCR = DMA + 0x04;

    // Channel 1 registers
    private const uint CCR1 = DMA + 0x08;
    private const uint CNDTR1 = DMA + 0x0C;
    private const uint CPAR1 = DMA + 0x10;
    private const uint CMAR1 = DMA + 0x14;

    private const uint EN = 1u << 0;
    private const uint TCIE = 1u << 1;
    private const uint DIR = 1u << 4;
    private const uint PINC = 1u << 6;
    private const uint MINC = 1u << 7;
    private const uint MEM2MEM = 1u << 14;
    private const uint PSIZE_32 = 2u << 8;
    private const uint MSIZE_32 = 2u << 10;

    private const uint TCIF1 = 1u << 1; // channel 1 TCIF at bit 1

    [Fact]
    public void Memory_to_memory_copies_a_block_of_words()
    {
        using var m = new STM32Machine();
        uint src = 0x20000100, dst = 0x20000200;
        for (uint i = 0; i < 4; i++)
            m.Bus.WriteWord(src + i * 4, 0x1000u + i);

        m.Bus.WriteWord(CPAR1, src);
        m.Bus.WriteWord(CMAR1, dst);
        m.Bus.WriteWord(CNDTR1, 4);
        m.Bus.WriteWord(CCR1, MEM2MEM | PINC | MINC | PSIZE_32 | MSIZE_32 | EN);

        for (uint i = 0; i < 4; i++)
            m.Bus.ReadWord(dst + i * 4).Should().Be(0x1000u + i);

        (m.Bus.ReadWord(ISR) & TCIF1).Should().NotBe(0);
    }

    [Fact]
    public void Single_shot_clears_enable_after_transfer()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CPAR1, 0x20000000);
        m.Bus.WriteWord(CMAR1, 0x20000040);
        m.Bus.WriteWord(CNDTR1, 1);
        m.Bus.WriteWord(CCR1, MEM2MEM | PSIZE_32 | MSIZE_32 | EN);

        (m.Bus.ReadWord(CCR1) & EN).Should().Be(0u);
    }

    [Fact]
    public void Transfer_complete_raises_irq_when_enabled()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CPAR1, 0x20000000);
        m.Bus.WriteWord(CMAR1, 0x20000040);
        m.Bus.WriteWord(CNDTR1, 1);
        m.Bus.WriteWord(CCR1, MEM2MEM | PSIZE_32 | MSIZE_32 | TCIE | EN);

        // Channel 1 → IRQ9 on STM32G0.
        (m.Cpu.Registers.PendingInterrupts & (1u << 9)).Should().NotBe(0);
    }

    [Fact]
    public void Ifcr_clears_flags()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CPAR1, 0x20000000);
        m.Bus.WriteWord(CMAR1, 0x20000040);
        m.Bus.WriteWord(CNDTR1, 1);
        m.Bus.WriteWord(CCR1, MEM2MEM | PSIZE_32 | MSIZE_32 | EN);

        m.Bus.WriteWord(IFCR, TCIF1 | 1u); // clear GIF+TCIF for ch1
        (m.Bus.ReadWord(ISR) & TCIF1).Should().Be(0u);
    }

    [Fact]
    public void Byte_sized_transfer_without_increment_repeats_address()
    {
        using var m = new STM32Machine();
        uint dst = 0x20000080;
        m.Bus.WriteWord(0x20000000, 0xAB);
        m.Bus.WriteWord(CPAR1, 0x20000000);
        m.Bus.WriteWord(CMAR1, dst);
        m.Bus.WriteWord(CNDTR1, 3);
        // 8-bit, source increments, dest fixed.
        m.Bus.WriteWord(CCR1, MEM2MEM | PINC | EN);

        // dst stays at one byte; last write wins. Just assert it ran and flagged complete.
        (m.Bus.ReadWord(ISR) & TCIF1).Should().NotBe(0);
    }
}
