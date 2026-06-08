using STM32.Peripherals;

namespace STM32Sharp.Tests.Peripherals;

public class ChipPresetTests
{
    [Fact]
    public void Preset_drives_memory_sizes()
    {
        using var m = new STM32Machine(Stm32ChipPreset.C031);
        m.Chip.Name.Should().Be("STM32C031");
        m.Chip.SramSize.Should().Be(12u * 1024);
        m.Chip.FlashSize.Should().Be(32u * 1024);

        // The part's real 12 KB of SRAM is addressable (bus rounds up to a power-of-two mask).
        m.Bus.WriteWord(0x20000000 + 12 * 1024 - 4, 0xCAFEBABE);
        m.Bus.ReadWord(0x20000000 + 12 * 1024 - 4).Should().Be(0xCAFEBABEu);

        // 32 KB Flash: a full-size image loads within range.
        m.LoadFlash(new byte[32 * 1024]); // must not throw
    }

    [Fact]
    public void Default_constructor_is_a_custom_full_fidelity_preset()
    {
        using var m = new STM32Machine();
        m.Chip.Core.Should().Be(CortexCore.CortexM0Plus);
        m.Chip.FullFidelity.Should().BeTrue();
    }

    [Theory]
    [InlineData("STM32G071", true)]
    [InlineData("STM32C031", true)]
    [InlineData("STM32L031", true)]   // emulable core, partial peripheral fidelity
    [InlineData("STM32F103C8", false)]
    public void Emulability_matches_core(string name, bool emulable)
    {
        var preset = Stm32ChipPreset.All.Single(p => p.Name == name);
        preset.IsEmulable.Should().Be(emulable);
    }

    [Fact]
    public void Cortex_m3_part_is_rejected_with_a_clear_message()
    {
        var act = () => new STM32Machine(Stm32ChipPreset.F103C8);
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Cortex-M3*Thumb-2*");
    }

    [Fact]
    public void L031_preset_reports_partial_fidelity_and_no_dmamux()
    {
        Stm32ChipPreset.L031.FullFidelity.Should().BeFalse();
        Stm32ChipPreset.L031.HasDmamux.Should().BeFalse();
        Stm32ChipPreset.C031.HasDmamux.Should().BeTrue();
    }
}
