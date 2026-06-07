using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

public class TimerAdvancedTests
{
    private const uint TIM2 = 0x40000000;
    private const uint CR1 = TIM2 + 0x00;
    private const uint DIER = TIM2 + 0x0C;
    private const uint SR = TIM2 + 0x10;
    private const uint CCMR1 = TIM2 + 0x18;
    private const uint CCER = TIM2 + 0x20;
    private const uint PSC = TIM2 + 0x28;
    private const uint ARR = TIM2 + 0x2C;
    private const uint CCR1 = TIM2 + 0x34;
    private const uint CCR2 = TIM2 + 0x38;

    [Fact]
    public void Pwm_mode1_output_tracks_cnt_vs_ccr()
    {
        using var m = new STM32Machine();
        var levels = new List<bool>();
        m.Tim2.OnChannelOutput += (ch, active) => { if (ch == 1) levels.Add(active); };

        m.Bus.WriteWord(PSC, 0);
        m.Bus.WriteWord(ARR, 9);
        m.Bus.WriteWord(CCR1, 5);
        m.Bus.WriteWord(CCMR1, 0x6u << 4); // OC1M = 110 (PWM mode 1)
        m.Bus.WriteWord(CR1, 1);           // CEN

        m.Tim2.Tick(20); // two full periods

        // PWM should go active (CNT<5) then inactive (CNT>=5) and back.
        levels.Should().Contain(true);
        levels.Should().Contain(false);
        // Within a period, duty high portion is CNT 0..4 (5 ticks of 10).
    }

    [Fact]
    public void Compare_match_sets_ccif()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(PSC, 0);
        m.Bus.WriteWord(ARR, 100);
        m.Bus.WriteWord(CCR1, 7);
        // OC1M output compare (frozen, non-PWM) — CC1S = 0 (compare).
        m.Bus.WriteWord(CR1, 1);

        m.Tim2.Tick(7);
        (m.Bus.ReadWord(SR) & (1u << 1)).Should().NotBe(0); // CC1IF
    }

    [Fact]
    public void Compare_interrupt_raises_irq()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(PSC, 0);
        m.Bus.WriteWord(ARR, 100);
        m.Bus.WriteWord(CCR1, 3);
        m.Bus.WriteWord(DIER, 1u << 1); // CC1IE
        m.Bus.WriteWord(CR1, 1);

        m.Tim2.Tick(3);
        (m.Cpu.Registers.PendingInterrupts & (1u << 15)).Should().NotBe(0); // TIM2 IRQ
    }

    [Fact]
    public void Input_capture_latches_counter()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(PSC, 0);
        m.Bus.WriteWord(ARR, 1000);
        m.Bus.WriteWord(CCMR1, 0x1u << 8); // CC2S = 01 (channel 2 as input)
        m.Bus.WriteWord(CR1, 1);

        m.Tim2.Tick(42);
        m.Tim2.CaptureInput(2);

        m.Bus.ReadWord(CCR2).Should().Be(42u);
        (m.Bus.ReadWord(SR) & (1u << 2)).Should().NotBe(0); // CC2IF
    }
}
