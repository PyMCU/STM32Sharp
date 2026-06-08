namespace STM32.Peripherals.Dma;

/// <summary>
/// Routes a peripheral DMA request line (DREQ) to a DMA channel. Implemented by the STM32G0's
/// DMAMUX (<see cref="DmamuxPeripheral"/>) and the STM32L0's in-DMA CSELR (<see cref="DmaCselrRouter"/>).
/// </summary>
public interface IDmaRequestRouter
{
    /// <summary>DMA channel (1-based) wired to <paramref name="requestId"/>, or -1 if none.</summary>
    int ChannelForRequest(int requestId);
}
