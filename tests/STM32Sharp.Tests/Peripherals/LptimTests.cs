using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

/// <summary>
/// Low-power timer (LPTIM). Driven directly through its <c>ITickable</c> interface so the counter,
/// autoreload/compare flags and the shared NVIC line can be checked deterministically.
/// </summary>
public class LptimTests
{
    private const uint LPTIM1 = 0x40007C00;
    private const uint ISR = LPTIM1 + 0x00;
    private const uint ICR = LPTIM1 + 0x04;
    private const uint IER = LPTIM1 + 0x08;
    private const uint CFGR = LPTIM1 + 0x0C;
    private const uint CR = LPTIM1 + 0x10;
    private const uint CMP = LPTIM1 + 0x14;
    private const uint ARR = LPTIM1 + 0x18;
    private const uint CNT = LPTIM1 + 0x1C;

    private const uint ISR_CMPM = 1u << 0;
    private const uint ISR_ARRM = 1u << 1;
    private const uint IER_CMPMIE = 1u << 0;
    private const uint IER_ARRMIE = 1u << 1;
    private const uint CR_ENABLE = 1u << 0;
    private const uint CR_SNGSTRT = 1u << 1;
    private const uint CR_CNTSTRT = 1u << 2;

    private const int IRQ_LPTIM1_G0 = 17;

    [Fact]
    public void Autoreload_match_wraps_and_raises_the_irq()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(IER, IER_ARRMIE);
        m.Bus.WriteWord(ARR, 4);
        m.Bus.WriteWord(CR, CR_ENABLE | CR_CNTSTRT); // continuous

        m.Lptim1!.Tick(4); // CNT 1,2,3,4 → ARRM at 4, reload to 0

        (m.Bus.ReadWord(ISR) & ISR_ARRM).Should().NotBe(0u);
        m.Bus.ReadWord(CNT).Should().Be(0u, "continuous mode reloads to zero");
        (m.Cpu.Registers.PendingInterrupts & (1u << IRQ_LPTIM1_G0)).Should().NotBe(0u);
    }

    [Fact]
    public void Compare_match_sets_cmpm()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(IER, IER_CMPMIE);
        m.Bus.WriteWord(ARR, 10);
        m.Bus.WriteWord(CMP, 3);
        m.Bus.WriteWord(CR, CR_ENABLE | CR_CNTSTRT);

        m.Lptim1!.Tick(3); // CNT reaches 3 == CMP

        (m.Bus.ReadWord(ISR) & ISR_CMPM).Should().NotBe(0u);
        (m.Cpu.Registers.PendingInterrupts & (1u << IRQ_LPTIM1_G0)).Should().NotBe(0u);
    }

    [Fact]
    public void Single_shot_stops_after_one_period()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(IER, IER_ARRMIE);
        m.Bus.WriteWord(ARR, 3);
        m.Bus.WriteWord(CR, CR_ENABLE | CR_SNGSTRT);

        m.Lptim1!.Tick(50); // far beyond one period

        (m.Bus.ReadWord(ISR) & ISR_ARRM).Should().NotBe(0u);
        m.Bus.ReadWord(CNT).Should().Be(0u, "the counter stopped at the autoreload and did not keep running");

        m.Bus.WriteWord(ICR, ISR_ARRM); // write-1-to-clear
        (m.Bus.ReadWord(ISR) & ISR_ARRM).Should().Be(0u);
    }

    [Fact]
    public void Prescaler_divides_the_tick_rate()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CFGR, 0x3u << 9); // PRESC = 3 → /8
        m.Bus.WriteWord(ARR, 100);
        m.Bus.WriteWord(CR, CR_ENABLE | CR_CNTSTRT);

        m.Lptim1!.Tick(8); // 8 cycles / 8 = 1 count

        m.Bus.ReadWord(CNT).Should().Be(1u);
    }

    [Fact]
    public void Lptim_presence_matches_the_family()
    {
        using var g0 = new STM32Machine(Stm32ChipPreset.G071);
        g0.Lptim1.Should().NotBeNull();
        g0.Lptim2.Should().NotBeNull("the STM32G0 has two LPTIMs");

        using var c0 = new STM32Machine(Stm32ChipPreset.C031);
        c0.Lptim1.Should().BeNull("the STM32C0 has no LPTIM");

        using var l0 = new STM32Machine(Stm32ChipPreset.L031);
        l0.Lptim1.Should().NotBeNull("the STM32L0 has LPTIM1");
        l0.Lptim2.Should().BeNull("the STM32L0 has no LPTIM2");
    }
}
