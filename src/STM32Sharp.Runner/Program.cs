using System.Text;
using STM32.Peripherals;

namespace STM32Sharp.Runner;

/// <summary>
/// Headless STM32 firmware runner for CI pipelines. Loads a raw flash image (.bin), runs it under
/// a bounded instruction budget — so it can never hang the build — and optionally checks the USART
/// output for an expected string.
///
///   stm32sharp &lt;image.bin&gt; [--expect-text "PASS"] [--uart 1|2] [--max-instructions N]
///
/// Serial output goes to stdout; the run summary goes to stderr. Exit codes:
///   0  expected text found (or no --expect-text given and the run did not crash)
///   1  expected text not found within the instruction budget
///   2  the CPU locked up (HardFault escalation — the firmware crashed)
///   64 usage error
///   66 image file not found
/// </summary>
internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitFailed = 1;
    private const int ExitCrashed = 2;
    private const int ExitUsage = 64;
    private const int ExitNoInput = 66;

    private static int Main(string[] args)
    {
        string? imagePath = null;
        string? expectText = null;
        var uartIndex = 2;
        long maxInstructions = 500_000_000;
        var quiet = false;
        uint flashSize = 128 * 1024;
        uint sramSize = 64 * 1024;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "-h" or "--help":
                    PrintUsage(Console.Out);
                    return ExitOk;
                case "--expect-text":
                    if (++i >= args.Length) return Usage("--expect-text requires a value");
                    expectText = args[i];
                    break;
                case "--uart":
                    if (++i >= args.Length || !int.TryParse(args[i], out uartIndex) || uartIndex is not (1 or 2))
                        return Usage("--uart requires 1 or 2");
                    break;
                case "--max-instructions":
                    if (++i >= args.Length || !long.TryParse(args[i], out maxInstructions) || maxInstructions <= 0)
                        return Usage("--max-instructions requires a positive integer");
                    break;
                case "--flash-kb":
                    if (++i >= args.Length || !uint.TryParse(args[i], out var fk) || fk == 0)
                        return Usage("--flash-kb requires a positive integer");
                    flashSize = fk * 1024;
                    break;
                case "--sram-kb":
                    if (++i >= args.Length || !uint.TryParse(args[i], out var sk) || sk == 0)
                        return Usage("--sram-kb requires a positive integer");
                    sramSize = sk * 1024;
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                default:
                    if (a.StartsWith('-')) return Usage($"unknown option '{a}'");
                    if (imagePath != null) return Usage("more than one image given");
                    imagePath = a;
                    break;
            }
        }

        if (imagePath is null) return Usage("no firmware image given");
        if (!File.Exists(imagePath))
        {
            Console.Error.WriteLine($"error: image not found: {imagePath}");
            return ExitNoInput;
        }

        var bytes = File.ReadAllBytes(imagePath);
        using var machine = new STM32Machine(flashSize, sramSize);
        machine.LoadFlash(bytes);
        machine.Reset();

        var output = new StringBuilder();
        void Emit(byte b)
        {
            output.Append((char)b);
            if (!quiet) { Console.Out.Write((char)b); Console.Out.Flush(); }
        }

        var usart = uartIndex == 1 ? machine.Usart1 : machine.Usart2;
        usart.OnByteTransmit += Emit;

        const int batch = 100_000;
        var start = machine.InstructionCount;
        var crashed = false;
        var found = false;

        while (machine.InstructionCount - start < maxInstructions)
        {
            if (expectText != null && output.ToString().Contains(expectText, StringComparison.Ordinal))
            {
                found = true;
                break;
            }
            if (machine.Cpu.IsLockedUp)
            {
                crashed = true;
                break;
            }
            machine.Run(batch);
        }

        if (!found && expectText != null && output.ToString().Contains(expectText, StringComparison.Ordinal))
            found = true;
        if (!crashed && machine.Cpu.IsLockedUp)
            crashed = true;

        var executed = machine.InstructionCount - start;

        if (expectText != null && found)
        {
            Console.Error.WriteLine($"OK: found \"{expectText}\" after {executed} instructions.");
            return ExitOk;
        }

        if (crashed)
        {
            Console.Error.WriteLine($"FAIL: CPU locked up after {executed} instructions " +
                                    $"(PC=0x{machine.Cpu.Registers.PC:X8}, IPSR={machine.Cpu.Registers.IPSR}).");
            return ExitCrashed;
        }

        if (expectText != null)
        {
            Console.Error.WriteLine($"FAIL: \"{expectText}\" not seen within {maxInstructions} instructions " +
                                    $"(executed {executed}" +
                                    (machine.Cpu.Registers.Waiting ? ", CPU was in WFI/WFE" : "") + ").");
            return ExitFailed;
        }

        Console.Error.WriteLine($"OK: ran {executed} instructions, no crash.");
        return ExitOk;
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        PrintUsage(Console.Error);
        return ExitUsage;
    }

    private static void PrintUsage(TextWriter w)
    {
        w.WriteLine("Usage: stm32sharp <image.bin> [options]");
        w.WriteLine();
        w.WriteLine("Options:");
        w.WriteLine("  --expect-text <text>     Pass (exit 0) only if <text> appears in serial output");
        w.WriteLine("  --uart 1|2               USART to watch (default: 2)");
        w.WriteLine("  --max-instructions <n>   Hard execution budget (default: 500000000)");
        w.WriteLine("  --flash-kb <n>           Flash size in KB, power of two (default: 128)");
        w.WriteLine("  --sram-kb <n>            SRAM size in KB, power of two (default: 64)");
        w.WriteLine("  --quiet                  Do not echo serial output to stdout");
        w.WriteLine("  -h, --help               Show this help");
        w.WriteLine();
        w.WriteLine("Exit codes: 0 ok · 1 text not found · 2 firmware crashed · 64 usage · 66 no input");
    }
}
