using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

/// <summary>
/// DMAMUX request generators (RM0444 §12.4): a trigger event makes a generator emit GNBREQ+1 DMA
/// requests on its output line (request id = generator+1 on STM32G0), which the request mux routes to
/// whichever channel selected that id. The trigger itself is driven by the host/co-simulator through
/// <see cref="STM32.Peripherals.Dma.DmamuxPeripheral.TriggerRequestGenerator"/>, modelling SIG_ID.
/// </summary>
public class DmamuxGeneratorTests
{
    private const uint DMA = 0x40020000;
    private const uint ISR = DMA + 0x00;
    private const uint CCR1 = DMA + 0x08;
    private const uint CNDTR1 = DMA + 0x0C;
    private const uint CPAR1 = DMA + 0x10;
    private const uint CMAR1 = DMA + 0x14;

    private const uint DMAMUX = 0x40020800;
    private const uint C0CR = DMAMUX + 0x00;     // drives DMA channel 1
    private const uint RG0CR = DMAMUX + 0x100;   // request generator 0

    private const uint EN = 1u << 0;
    private const uint MINC = 1u << 7;
    private const uint TCIF1 = 1u << 1;

    private const uint RG_GE = 1u << 16;
    private static uint Gnbreq(uint n) => (n & 0x1F) << 19; // requests - 1

    // Generator 0's output is DMAMUX request id 1 on STM32G0.
    private const uint REQ_GEN0 = 1;

    private static void ArmChannelFromGenerator(STM32Machine m, uint src, uint dst, uint count)
    {
        m.Bus.WriteWord(C0CR, REQ_GEN0);     // channel 1 listens to generator-0 output
        m.Bus.WriteWord(CPAR1, src);         // fixed source (stands in for a peripheral DR)
        m.Bus.WriteWord(CMAR1, dst);
        m.Bus.WriteWord(CNDTR1, count);
        m.Bus.WriteWord(CCR1, MINC | EN);    // periph → mem, memory increments, byte size
    }

    [Fact]
    public void Trigger_emits_gnbreq_plus_one_requests()
    {
        using var m = new STM32Machine();
        uint src = 0x20000600, dst = 0x20000610;
        m.Bus.WriteByte(src, 0xAB);
        ArmChannelFromGenerator(m, src, dst, 3);

        m.Bus.WriteWord(RG0CR, RG_GE | Gnbreq(2)); // 3 requests per trigger
        m.Dmamux!.TriggerRequestGenerator(0);

        m.Bus.ReadByte(dst + 0).Should().Be((byte)0xAB);
        m.Bus.ReadByte(dst + 1).Should().Be((byte)0xAB);
        m.Bus.ReadByte(dst + 2).Should().Be((byte)0xAB);
        (m.Bus.ReadWord(ISR) & TCIF1).Should().NotBe(0u, "the 3rd request drains the channel");
    }

    [Fact]
    public void Disabled_generator_emits_nothing()
    {
        using var m = new STM32Machine();
        uint src = 0x20000700, dst = 0x20000710;
        m.Bus.WriteByte(src, 0x5A);
        m.Bus.WriteWord(dst, 0u);
        ArmChannelFromGenerator(m, src, dst, 3);

        m.Bus.WriteWord(RG0CR, Gnbreq(2));  // GE clear → disabled
        m.Dmamux!.TriggerRequestGenerator(0);

        m.Bus.ReadWord(dst).Should().Be(0u, "a disabled generator produces no requests");
        (m.Bus.ReadWord(ISR) & TCIF1).Should().Be(0u);
    }

    [Fact]
    public void Generator_stops_when_the_channel_is_drained()
    {
        using var m = new STM32Machine();
        uint src = 0x20000800, dst = 0x20000810;
        m.Bus.WriteByte(src, 0xC3);
        ArmChannelFromGenerator(m, src, dst, 2); // channel only wants 2 elements

        m.Bus.WriteWord(RG0CR, RG_GE | Gnbreq(4)); // generator would emit 5
        m.Dmamux!.TriggerRequestGenerator(0);

        // Only 2 land; the rest are ignored once the channel disables (single-shot).
        m.Bus.ReadByte(dst + 0).Should().Be((byte)0xC3);
        m.Bus.ReadByte(dst + 1).Should().Be((byte)0xC3);
        m.Bus.ReadByte(dst + 2).Should().Be((byte)0x00);
        (m.Bus.ReadWord(CCR1) & EN).Should().Be(0u);
    }
}
