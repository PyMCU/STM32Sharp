using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

public class RccPeripheralTests
{
    private const uint RCC_CR = 0x40021000;
    private const uint RCC_CFGR = 0x40021008;

    private const uint HSEON = 1u << 16;
    private const uint HSERDY = 1u << 17;
    private const uint PLLON = 1u << 24;
    private const uint PLLRDY = 1u << 25;

    [Fact]
    public void Reset_state_has_hsi_on_and_ready()
    {
        using var m = new STM32Machine();
        var cr = m.Bus.ReadWord(RCC_CR);
        (cr & (1u << 8)).Should().NotBe(0); // HSION
        (cr & (1u << 10)).Should().NotBe(0); // HSIRDY
    }

    [Fact]
    public void Turning_hse_on_reflects_ready_immediately()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(RCC_CR, m.Bus.ReadWord(RCC_CR) | HSEON);
        (m.Bus.ReadWord(RCC_CR) & HSERDY).Should().NotBe(0);
    }

    [Fact]
    public void Turning_pll_on_reflects_ready_immediately()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(RCC_CR, m.Bus.ReadWord(RCC_CR) | PLLON);
        (m.Bus.ReadWord(RCC_CR) & PLLRDY).Should().NotBe(0);
    }

    [Fact]
    public void System_clock_switch_status_follows_request()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(RCC_CFGR, 0x2); // SW = PLL
        var sws = (m.Bus.ReadWord(RCC_CFGR) >> 3) & 0x7;
        sws.Should().Be(0x2u);
    }
}
