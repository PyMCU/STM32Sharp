using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

public class IwdgTests
{
    private const uint IWDG = 0x40003000;
    private const uint KR = IWDG + 0x00;
    private const uint PR = IWDG + 0x04;
    private const uint RLR = IWDG + 0x08;

    private const uint START = 0xCCCC;
    private const uint ACCESS = 0x5555;
    private const uint REFRESH = 0xAAAA;

    [Fact]
    public void Times_out_when_not_refreshed()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(KR, ACCESS);
        m.Bus.WriteWord(PR, 0);     // ÷4
        m.Bus.WriteWord(RLR, 100);  // 404 cycles
        m.Bus.WriteWord(KR, START);

        m.Iwdg.Tick(500);
        m.WatchdogResetCount.Should().Be(1);
    }

    [Fact]
    public void Refresh_prevents_timeout()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(KR, ACCESS);
        m.Bus.WriteWord(PR, 0);
        m.Bus.WriteWord(RLR, 100);  // 404 cycles
        m.Bus.WriteWord(KR, START);

        for (var i = 0; i < 10; i++)
        {
            m.Iwdg.Tick(300);
            m.Bus.WriteWord(KR, REFRESH);
        }
        m.WatchdogResetCount.Should().Be(0);
    }

    [Fact]
    public void Inactive_watchdog_never_resets()
    {
        using var m = new STM32Machine();
        m.Iwdg.Tick(1_000_000);
        m.WatchdogResetCount.Should().Be(0);
    }
}

public class WwdgTests
{
    private const uint WWDG = 0x40002C00;
    private const uint CR = WWDG + 0x00;
    private const uint CFR = WWDG + 0x04;

    private const uint WDGA = 1u << 7;

    [Fact]
    public void Counter_underflow_triggers_reset()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CFR, 0x7F);            // window = max, WDGTB = 0 → ÷4096
        m.Bus.WriteWord(CR, WDGA | 0x41);      // active, counter just above 0x40

        m.Wwdg.Tick(4096L * 4); // a few decrements to cross 0x3F
        m.WatchdogResetCount.Should().Be(1);
    }

    [Fact]
    public void Refresh_above_window_triggers_reset()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CFR, 0x40);          // window = 0x40
        m.Bus.WriteWord(CR, WDGA | 0x7F);    // counter 0x7F > window
        m.Bus.WriteWord(CR, WDGA | 0x7F);    // refresh while above window → reset
        m.WatchdogResetCount.Should().Be(1);
    }

    [Fact]
    public void Inactive_window_watchdog_never_resets()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CR, 0x40); // WDGA not set
        m.Wwdg.Tick(1_000_000);
        m.WatchdogResetCount.Should().Be(0);
    }
}
