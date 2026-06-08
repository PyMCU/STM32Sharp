namespace STM32.Peripherals.Dma;

/// <summary>
/// STM32L0 DMA request routing via the channel-selection register (CSELR, at DMA base + 0xA8). Unlike
/// the G0's DMAMUX — where each channel names a global request id — the L0 packs a 4-bit selector per
/// channel (C1S in bits [3:0], C2S in [7:4], …), whose meaning depends on the channel (RM0451 Table 51).
///
/// This router holds the CSELR value and maps the emulator's canonical request ids to the (channel,
/// selector) pairs the L0 defines. It is not memory-mapped itself: <see cref="DmaPeripheral"/> exposes
/// CSELR at offset 0xA8 and forwards reads/writes to <see cref="Value"/>.
/// </summary>
public sealed class DmaCselrRouter : IDmaRequestRouter
{
    /// <summary>Raw CSELR register value (4 bits per channel).</summary>
    public uint Value;

    // Canonical request id → (channel 1-based, expected 4-bit selector) per RM0451 Table 51 (L031).
    private static readonly (int Req, int Channel, uint Sel)[] Map =
    [
        (DmaRequestIds.Adc1,     1, 0),
        (DmaRequestIds.Spi1Rx,   2, 1),
        (DmaRequestIds.Spi2Rx,   4, 2),
        (DmaRequestIds.Usart1Rx, 3, 3),
        (DmaRequestIds.Usart2Rx, 5, 4),
    ];

    public int ChannelForRequest(int requestId)
    {
        foreach (var (req, ch, sel) in Map)
        {
            if (req != requestId) continue;
            var actual = (Value >> (4 * (ch - 1))) & 0xF;
            if (actual == sel) return ch;
        }
        return -1;
    }
}

/// <summary>Canonical DMA request line ids shared by the DMAMUX (G0) and CSELR (L0) routers.</summary>
public static class DmaRequestIds
{
    public const int Adc1     = 5;
    public const int Spi1Rx   = 16;
    public const int Spi2Rx   = 18;
    public const int Usart1Rx = 50;
    public const int Usart2Rx = 52;
}
