using STM32.Core.Memory;
using STM32.Peripherals;

namespace STM32Sharp.Tests.Core;

/// <summary>
/// Phase 1 smoke tests: the ported Cortex-M0+ core must boot from the STM32 vector table
/// (SP @ 0x0800_0000, reset PC @ 0x0800_0004), execute Thumb-1 code from Flash, and read/write SRAM.
/// </summary>
public class CpuBootSmokeTests
{
    /// <summary>
    /// Build a minimal firmware image:
    ///   vector[0] = initial SP   (top of SRAM)
    ///   vector[1] = reset PC      (Thumb bit set)
    ///   code:  MOVS r0,#42 ; MOVS r1,#0x20 ; LSLS r1,r1,#24 ; STR r0,[r1] ; B .
    /// which writes 42 to SRAM base 0x2000_0000.
    /// </summary>
    private static byte[] BuildWriteToSramFirmware()
    {
        const uint codeStart = 0x08000008;
        ushort[] code =
        [
            0x202A, // movs r0, #42
            0x2120, // movs r1, #0x20
            0x0609, // lsls r1, r1, #24   -> r1 = 0x2000_0000
            0x6008, // str  r0, [r1, #0]
            0xE7FE, // b .  (spin)
        ];

        var image = new byte[8 + code.Length * 2];
        BitConverter.GetBytes(0x20001000u).CopyTo(image, 0);            // initial SP
        BitConverter.GetBytes(codeStart | 1u).CopyTo(image, 4);         // reset PC (Thumb)
        for (var i = 0; i < code.Length; i++)
            BitConverter.GetBytes(code[i]).CopyTo(image, 8 + i * 2);
        return image;
    }

    [Fact]
    public void Boot_reads_sp_and_pc_from_vector_table()
    {
        using var machine = new STM32Machine();
        machine.LoadFlash(BuildWriteToSramFirmware());
        machine.Reset();

        machine.Cpu.Registers.SP.Should().Be(0x20001000u);
        machine.Cpu.Registers.PC.Should().Be(0x08000008u); // Thumb bit stripped
    }

    [Fact]
    public void Executes_thumb_code_from_flash_and_writes_to_sram()
    {
        using var machine = new STM32Machine();
        machine.LoadFlash(BuildWriteToSramFirmware());
        machine.Reset();

        machine.Run(10); // a handful of instructions is enough to reach the STR

        machine.Cpu.Registers.R0.Should().Be(42u);
        machine.Cpu.Registers.R1.Should().Be(0x20000000u);
        machine.Bus.ReadWord(0x20000000).Should().Be(42u);
        machine.Cpu.IsLockedUp.Should().BeFalse();
    }

    [Fact]
    public void Flash_is_read_only_on_the_code_bus()
    {
        using var machine = new STM32Machine();
        machine.LoadFlash(BuildWriteToSramFirmware());

        machine.Bus.WriteWord(0x08000000, 0xDEADBEEF);
        machine.Bus.ReadWord(0x08000000).Should().Be(0x20001000u); // unchanged
    }

    [Fact]
    public void Boot_alias_at_zero_mirrors_flash()
    {
        using var machine = new STM32Machine();
        machine.LoadFlash(BuildWriteToSramFirmware());

        // 0x0000_0000 (boot alias) must read the same as 0x0800_0000 (Flash).
        machine.Bus.ReadWord(0x00000000).Should().Be(machine.Bus.ReadWord(0x08000000));
        machine.Bus.ReadWord(0x00000004).Should().Be(machine.Bus.ReadWord(0x08000004));
    }
}
