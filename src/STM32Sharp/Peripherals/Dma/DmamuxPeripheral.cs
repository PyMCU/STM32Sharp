using STM32.Core.Memory;

namespace STM32.Peripherals.Dma;

/// <summary>
/// DMA request multiplexer (DMAMUX1) for STM32G0. Base 0x4002_0800. Register layout (RM0444 §12):
///   per DMA channel n (0..6): CxCR at 0x00 + n*0x04, with DMAREQ_ID[6:0] selecting which peripheral
///   request line drives that channel; per request generator g (0..3): RGxCR at 0x100 + g*0x04.
///
/// As a router the mux is a pure table: <see cref="ChannelForRequest"/> answers which DMA channel
/// (1..7) a given request line is wired to, which the <see cref="DmaPeripheral"/> uses to deliver
/// request-driven (DREQ) transfers.
///
/// Request generators synthesize DMA requests from a trigger event: when generator g is enabled
/// (RGxCR.GE) and its trigger fires (driven by the host/co-simulator through
/// <see cref="TriggerRequestGenerator"/>, modelling the selected SIG_ID input), it emits GNBREQ+1
/// requests on its output line. On STM32G0 the generator outputs map to request ids 1..4
/// (generator g → request id g+1), so a channel selecting that id is serviced by the generator.
/// </summary>
public sealed class DmamuxPeripheral : IMemoryMappedDevice, IDmaRequestRouter
{
    private const int ChannelCount = 7; // DMAMUX channels 0..6 → DMA1 channels 1..7
    private const uint CXCR_END = (uint)ChannelCount * 4; // CxCR registers occupy 0x00..0x1B
    private const uint DMAREQ_ID_MASK = 0x7F;

    private const int GeneratorCount = 4;      // STM32G0 has 4 request generators
    private const uint RGCR_BASE = 0x100;      // RGxCR at 0x100 + g*4
    private const uint RGCR_END = RGCR_BASE + (uint)GeneratorCount * 4;
    private const uint RG_GE = 1u << 16;       // generator enable
    private static uint GnbReq(uint rgcr) => (rgcr >> 19) & 0x1F; // requests-1 per trigger

    private readonly uint[] _ccr = new uint[ChannelCount];
    private readonly uint[] _rgcr = new uint[GeneratorCount];

    /// <summary>
    /// Set by the machine to push a synthesized request into the DMA engine (id → Dma.Request). Unlike
    /// the pull-based <see cref="ChannelForRequest"/>, generators actively drive transfers.
    /// </summary>
    public Action<int>? DeliverRequest;

    public uint Size => 0x400;

    /// <summary>
    /// Fire request generator <paramref name="generator"/> (0..3) as if its selected trigger input had
    /// an active edge. When the generator is enabled it pushes GNBREQ+1 requests on its output line
    /// (request id = generator+1). No-op if the generator index is out of range or disabled.
    /// </summary>
    public void TriggerRequestGenerator(int generator)
    {
        if (generator < 0 || generator >= GeneratorCount) return;
        var rgcr = _rgcr[generator];
        if ((rgcr & RG_GE) == 0) return;

        var count = GnbReq(rgcr) + 1;
        var outputId = generator + 1; // G0: req_gen g → DMAMUX request id g+1
        for (uint i = 0; i < count; i++)
            DeliverRequest?.Invoke(outputId);
    }

    /// <summary>
    /// DMA channel (1..7) whose DMAMUX request id equals <paramref name="reqId"/>, or -1 if none.
    /// Request id 0 (no request / memory-to-memory) never matches.
    /// </summary>
    public int ChannelForRequest(int reqId)
    {
        if (reqId <= 0) return -1;
        for (var i = 0; i < ChannelCount; i++)
            if ((int)(_ccr[i] & DMAREQ_ID_MASK) == reqId)
                return i + 1;
        return -1;
    }

    public uint ReadWord(uint address)
    {
        var off = address & 0x3FF;
        if (off < CXCR_END) return _ccr[off / 4];
        if (off >= RGCR_BASE && off < RGCR_END) return _rgcr[(off - RGCR_BASE) / 4];
        return 0;
    }

    public ushort ReadHalfWord(uint address) =>
        (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));

    public byte ReadByte(uint address) =>
        (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));

    public void WriteWord(uint address, uint value)
    {
        var off = address & 0x3FF;
        if (off < CXCR_END) _ccr[off / 4] = value;
        else if (off >= RGCR_BASE && off < RGCR_END) _rgcr[(off - RGCR_BASE) / 4] = value;
    }

    public void WriteHalfWord(uint address, ushort value)
    {
        var aligned = address & ~3u;
        var shift = (int)((address & 2) << 3);
        var current = ReadWord(aligned);
        WriteWord(aligned, (current & ~(0xFFFFu << shift)) | ((uint)value << shift));
    }

    public void WriteByte(uint address, byte value)
    {
        var aligned = address & ~3u;
        var shift = (int)((address & 3) << 3);
        var current = ReadWord(aligned);
        WriteWord(aligned, (current & ~(0xFFu << shift)) | ((uint)value << shift));
    }
}
