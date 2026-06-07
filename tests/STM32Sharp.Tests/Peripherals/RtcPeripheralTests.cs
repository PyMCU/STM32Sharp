using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

public class RtcPeripheralTests
{
    private const uint RTC = 0x40002800;
    private const uint TR = RTC + 0x00;
    private const uint DR = RTC + 0x04;
    private const uint ICSR = RTC + 0x0C;
    private const uint CR = RTC + 0x18;
    private const uint WPR = RTC + 0x24;
    private const uint ALRMAR = RTC + 0x40;
    private const uint SR = RTC + 0x50;
    private const uint SCR = RTC + 0x5C;

    private const uint INIT = 1u << 7;
    private const uint ALRAE = 1u << 8;
    private const uint ALRAIE = 1u << 12;
    private const uint ALRAF = 1u << 0;

    private static void Unlock(STM32Machine m)
    {
        m.Bus.WriteWord(WPR, 0xCA);
        m.Bus.WriteWord(WPR, 0x53);
    }

    [Fact]
    public void Registers_are_write_protected_until_unlocked()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(TR, 0x123456); // ignored, still locked
        m.Bus.ReadWord(TR).Should().Be(0u);

        Unlock(m);
        m.Bus.WriteWord(TR, 0x123456);
        m.Bus.ReadWord(TR).Should().Be(0x123456u);
    }

    [Fact]
    public void Init_mode_sets_initf()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(ICSR, INIT);
        (m.Bus.ReadWord(ICSR) & (1u << 6)).Should().NotBe(0); // INITF
    }

    [Fact]
    public void Time_advances_by_seconds_and_carries()
    {
        using var m = new STM32Machine();
        Unlock(m);
        m.Bus.WriteWord(TR, 0x00_00_59); // 00:00:59 BCD
        m.Rtc.AdvanceSeconds(1);
        m.Bus.ReadWord(TR).Should().Be(0x00_01_00u); // 00:01:00
    }

    [Fact]
    public void Hour_rolls_over_to_next_day()
    {
        using var m = new STM32Machine();
        Unlock(m);
        m.Bus.WriteWord(TR, 0x23_59_59); // 23:59:59
        var dayBefore = m.Bus.ReadWord(DR) & 0x3F;
        m.Rtc.AdvanceSeconds(1);
        m.Bus.ReadWord(TR).Should().Be(0x00_00_00u);
        (m.Bus.ReadWord(DR) & 0x3F).Should().NotBe(dayBefore);
    }

    [Fact]
    public void Alarm_fires_and_can_raise_irq()
    {
        using var m = new STM32Machine();
        Unlock(m);
        m.Bus.WriteWord(TR, 0x10_00_00);
        // Alarm at 10:00:05, mask date/hours/minutes off so only seconds compared.
        m.Bus.WriteWord(ALRMAR, (1u << 31) | (1u << 23) | (1u << 15) | 0x05);
        m.Bus.WriteWord(CR, ALRAE | ALRAIE);

        m.Rtc.AdvanceSeconds(5);

        (m.Bus.ReadWord(SR) & ALRAF).Should().NotBe(0);
        (m.Cpu.Registers.PendingInterrupts & (1u << 2)).Should().NotBe(0); // RTC IRQ = 2

        m.Bus.WriteWord(SCR, ALRAF); // clear
        (m.Bus.ReadWord(SR) & ALRAF).Should().Be(0u);
    }
}
