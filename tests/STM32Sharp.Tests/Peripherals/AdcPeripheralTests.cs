using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

public class AdcPeripheralTests
{
    private const uint ADC = 0x40012400;
    private const uint ISR = ADC + 0x00;
    private const uint CR = ADC + 0x08;
    private const uint CFGR1 = ADC + 0x0C;
    private const uint CHSELR = ADC + 0x28;
    private const uint DR = ADC + 0x40;

    private const uint ADEN = 1u << 0;
    private const uint ADSTART = 1u << 2;
    private const uint ISR_ADRDY = 1u << 0;
    private const uint ISR_EOC = 1u << 2;
    private const uint ISR_EOS = 1u << 3;
    private const uint CONT = 1u << 13;

    [Fact]
    public void Enabling_sets_adready()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CR, ADEN);
        (m.Bus.ReadWord(ISR) & ISR_ADRDY).Should().NotBe(0);
    }

    [Fact]
    public void Single_channel_conversion_returns_injected_value()
    {
        using var m = new STM32Machine();
        m.Adc.SetChannel(3, 0x0ABC);

        m.Bus.WriteWord(CR, ADEN);
        m.Bus.WriteWord(CHSELR, 1u << 3);
        m.Bus.WriteWord(CR, ADEN | ADSTART);

        (m.Bus.ReadWord(ISR) & ISR_EOC).Should().NotBe(0);
        m.Bus.ReadWord(DR).Should().Be(0x0ABCu);
        (m.Bus.ReadWord(ISR) & ISR_EOC).Should().Be(0u); // cleared by DR read
    }

    [Fact]
    public void Sequence_of_channels_converts_in_order()
    {
        using var m = new STM32Machine();
        m.Adc.SetChannel(1, 111);
        m.Adc.SetChannel(5, 555);

        m.Bus.WriteWord(CR, ADEN);
        m.Bus.WriteWord(CHSELR, (1u << 1) | (1u << 5));
        m.Bus.WriteWord(CR, ADEN | ADSTART);

        m.Bus.ReadWord(DR).Should().Be(111u);
        m.Bus.ReadWord(DR).Should().Be(555u);
        (m.Bus.ReadWord(ISR) & ISR_EOS).Should().NotBe(0);
    }

    [Fact]
    public void Continuous_mode_wraps_the_sequence()
    {
        using var m = new STM32Machine();
        m.Adc.SetChannel(2, 42);

        m.Bus.WriteWord(CR, ADEN);
        m.Bus.WriteWord(CFGR1, CONT);
        m.Bus.WriteWord(CHSELR, 1u << 2);
        m.Bus.WriteWord(CR, ADEN | ADSTART);

        m.Bus.ReadWord(DR).Should().Be(42u);
        m.Bus.ReadWord(DR).Should().Be(42u); // wrapped, converts again
    }
}
