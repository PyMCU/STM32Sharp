namespace STM32.Peripherals;

/// <summary>
/// Peripheral that advances its simulation state by a given number of CPU cycles.
/// Called by <see cref="STM32Machine"/> at the end of each Run() batch.
/// </summary>
public interface ITickable
{
    void Tick(long deltaCycles);
}
