namespace STM32.Peripherals;

/// <summary>
/// A time-aware peripheral. <see cref="STM32Machine"/> advances it as the CPU runs, but only ever in
/// steps that stop at the peripheral's next event of interest (an interrupt, a flag change, a timeout):
/// <see cref="NextEventInCycles"/> reports how far away that is, and <see cref="Tick"/> advances the
/// state by an exact number of cycles. A peripheral with nothing observable pending returns
/// <see cref="long.MaxValue"/>, letting the engine run the CPU at full speed until something else needs
/// attention.
/// </summary>
public interface ITickable
{
    /// <summary>Advance the peripheral's state by <paramref name="deltaCycles"/> CPU cycles.</summary>
    void Tick(long deltaCycles);

    /// <summary>
    /// Cycles until this peripheral next does something observable (raises an IRQ, sets a flag, resets
    /// the system). <see cref="long.MaxValue"/> means "nothing pending" — the engine need not stop for it.
    /// </summary>
    long NextEventInCycles() => long.MaxValue;
}
