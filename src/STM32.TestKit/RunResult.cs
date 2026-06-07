namespace STM32.TestKit;

/// <summary>Why a <see cref="STM32TestSimulation.RunUntilHalt(System.Func{bool},long)"/> call stopped.</summary>
public enum RunOutcome
{
    /// <summary>The supplied predicate became true — the run did what the test wanted.</summary>
    PredicateMet,

    /// <summary>The CPU entered lockup (a HardFault escalated): the firmware crashed.</summary>
    LockedUp,

    /// <summary>The instruction budget was exhausted before the predicate was met.</summary>
    BudgetReached,
}

/// <summary>
/// Outcome of a bounded <see cref="STM32TestSimulation.RunUntilHalt(System.Func{bool},long)"/> run.
/// Designed for CI: the run always terminates, and the outcome distinguishes success from a
/// firmware crash or a timeout so failing firmware fails the test with a clear reason instead
/// of hanging the build.
/// </summary>
public readonly record struct RunResult(
    RunOutcome Outcome,
    long InstructionsExecuted,
    bool CpuWasWaiting)
{
    /// <summary>True when the predicate was met (the run succeeded).</summary>
    public bool Succeeded => Outcome == RunOutcome.PredicateMet;

    public override string ToString() =>
        $"{Outcome} after {InstructionsExecuted} instructions" +
        (CpuWasWaiting ? " (CPU was in WFI/WFE)" : "");
}
