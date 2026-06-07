using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

public class ExtiPeripheralTests
{
    private const uint EXTI = 0x40021800;
    private const uint RTSR = EXTI + 0x00;
    private const uint FTSR = EXTI + 0x04;
    private const uint SWIER = EXTI + 0x08;
    private const uint RPR = EXTI + 0x0C;
    private const uint FPR = EXTI + 0x10;
    private const uint EXTICR0 = EXTI + 0x60;
    private const uint IMR = EXTI + 0x80;

    // EXTI line 0–1 → IRQ5, 2–3 → IRQ6, 4–15 → IRQ7
    private static uint IrqBit(int irq) => 1u << irq;

    [Fact]
    public void Rising_edge_on_selected_port_sets_pending_and_irq()
    {
        using var m = new STM32Machine();
        // Line 0 mapped to port A (EXTICR0 line0 = 0), rising trigger, unmasked.
        m.Bus.WriteWord(EXTICR0, 0x00);
        m.Bus.WriteWord(RTSR, 1u << 0);
        m.Bus.WriteWord(IMR, 1u << 0);

        m.GpioA.SetInput(0, true); // rising edge on PA0

        (m.Bus.ReadWord(RPR) & 1u).Should().NotBe(0);
        (m.Cpu.Registers.PendingInterrupts & IrqBit(5)).Should().NotBe(0);
    }

    [Fact]
    public void Falling_edge_respects_ftsr()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(EXTICR0, 0x00);
        m.Bus.WriteWord(FTSR, 1u << 1);
        m.Bus.WriteWord(IMR, 1u << 1);

        m.GpioA.SetInput(1, true);  // rising — no falling trigger configured
        (m.Bus.ReadWord(FPR) & (1u << 1)).Should().Be(0u);

        m.GpioA.SetInput(1, false); // falling
        (m.Bus.ReadWord(FPR) & (1u << 1)).Should().NotBe(0);
        (m.Cpu.Registers.PendingInterrupts & IrqBit(5)).Should().NotBe(0);
    }

    [Fact]
    public void Edge_on_unselected_port_is_ignored()
    {
        using var m = new STM32Machine();
        // Line 2 mapped to port B (value 1 in EXTICR0 line-2 byte).
        m.Bus.WriteWord(EXTICR0, 1u << (2 * 8));
        m.Bus.WriteWord(RTSR, 1u << 2);
        m.Bus.WriteWord(IMR, 1u << 2);

        m.GpioA.SetInput(2, true); // wrong port
        (m.Bus.ReadWord(RPR) & (1u << 2)).Should().Be(0u);

        m.GpioB.SetInput(2, true); // correct port
        (m.Bus.ReadWord(RPR) & (1u << 2)).Should().NotBe(0);
        (m.Cpu.Registers.PendingInterrupts & IrqBit(6)).Should().NotBe(0); // line 2 → IRQ6
    }

    [Fact]
    public void Masked_line_sets_pending_but_no_irq()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(EXTICR0, 0x00);
        m.Bus.WriteWord(RTSR, 1u << 0);
        // IMR not set → masked

        m.GpioA.SetInput(0, true);
        (m.Bus.ReadWord(RPR) & 1u).Should().NotBe(0);
        (m.Cpu.Registers.PendingInterrupts & IrqBit(5)).Should().Be(0u);
    }

    [Fact]
    public void Writing_pending_register_clears_it_and_deasserts_irq()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(EXTICR0, 0x00);
        m.Bus.WriteWord(RTSR, 1u << 0);
        m.Bus.WriteWord(IMR, 1u << 0);
        m.GpioA.SetInput(0, true);

        m.Bus.WriteWord(RPR, 1u << 0); // rc_w1
        (m.Bus.ReadWord(RPR) & 1u).Should().Be(0u);
        (m.Cpu.Registers.PendingInterrupts & IrqBit(5)).Should().Be(0u);
    }

    [Fact]
    public void Software_interrupt_triggers_line()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(IMR, 1u << 4);
        m.Bus.WriteWord(SWIER, 1u << 4);

        (m.Bus.ReadWord(RPR) & (1u << 4)).Should().NotBe(0);
        (m.Cpu.Registers.PendingInterrupts & IrqBit(7)).Should().NotBe(0); // line 4 → IRQ7
    }
}
