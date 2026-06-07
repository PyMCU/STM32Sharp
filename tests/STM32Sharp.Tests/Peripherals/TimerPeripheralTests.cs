using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

public class TimerPeripheralTests
{
    private const uint TIM2 = 0x40000000;
    private const uint CR1 = TIM2 + 0x00;
    private const uint DIER = TIM2 + 0x0C;
    private const uint SR = TIM2 + 0x10;
    private const uint EGR = TIM2 + 0x14;
    private const uint CNT = TIM2 + 0x24;
    private const uint PSC = TIM2 + 0x28;
    private const uint ARR = TIM2 + 0x2C;

    [Fact]
    public void Counter_advances_while_enabled()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(PSC, 0);
        m.Bus.WriteWord(ARR, 100);
        m.Bus.WriteWord(CR1, 1); // CEN

        m.Tim2.Tick(5);
        m.Bus.ReadWord(CNT).Should().Be(5u);
    }

    [Fact]
    public void Counter_does_not_advance_while_disabled()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(ARR, 100);
        m.Tim2.Tick(50); // CEN not set
        m.Bus.ReadWord(CNT).Should().Be(0u);
    }

    [Fact]
    public void Prescaler_divides_the_count_rate()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(PSC, 3); // divide by 4
        m.Bus.WriteWord(ARR, 100);
        m.Bus.WriteWord(CR1, 1);

        m.Tim2.Tick(8);
        m.Bus.ReadWord(CNT).Should().Be(2u);
    }

    [Fact]
    public void Overflow_sets_uif_and_reloads()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(PSC, 0);
        m.Bus.WriteWord(ARR, 9);
        m.Bus.WriteWord(CR1, 1);

        m.Tim2.Tick(10); // 0..9 then wrap
        m.Bus.ReadWord(CNT).Should().Be(0u);
        (m.Bus.ReadWord(SR) & 1u).Should().NotBe(0); // UIF
    }

    [Fact]
    public void Update_interrupt_is_raised_when_enabled()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(PSC, 0);
        m.Bus.WriteWord(ARR, 4);
        m.Bus.WriteWord(DIER, 1); // UIE
        m.Bus.WriteWord(CR1, 1);

        m.Tim2.Tick(5); // wraps once
        (m.Cpu.Registers.PendingInterrupts & (1u << 15)).Should().NotBe(0); // TIM2 IRQ = 15

        // Clearing UIF deasserts the line.
        m.Bus.WriteWord(SR, 0);
        (m.Cpu.Registers.PendingInterrupts & (1u << 15)).Should().Be(0u);
    }

    [Fact]
    public void Ug_event_resets_counter()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(ARR, 100);
        m.Bus.WriteWord(CR1, 1);
        m.Tim2.Tick(20);
        m.Bus.WriteWord(EGR, 1); // UG
        m.Bus.ReadWord(CNT).Should().Be(0u);
    }
}
