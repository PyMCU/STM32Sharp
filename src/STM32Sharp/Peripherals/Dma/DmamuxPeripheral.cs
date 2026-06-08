using STM32.Core.Memory;

namespace STM32.Peripherals.Dma;

/// <summary>
/// DMA request multiplexer (DMAMUX1) for STM32G0. Base 0x4002_0800. Register layout (RM0444 §12):
///   per DMA channel n (0..6): CxCR at 0x00 + n*0x04, with DMAREQ_ID[6:0] selecting which peripheral
///   request line drives that channel. (Request generator and status registers are not modeled.)
///
/// The mux is a pure routing table: <see cref="ChannelForRequest"/> answers which DMA channel (1..7)
/// a given peripheral request line is wired to, which the <see cref="DmaPeripheral"/> uses to deliver
/// request-driven (DREQ) transfers.
/// </summary>
public sealed class DmamuxPeripheral : IMemoryMappedDevice, IDmaRequestRouter
{
    private const int ChannelCount = 7; // DMAMUX channels 0..6 → DMA1 channels 1..7
    private const uint CXCR_END = (uint)ChannelCount * 4; // CxCR registers occupy 0x00..0x1B
    private const uint DMAREQ_ID_MASK = 0x7F;

    private readonly uint[] _ccr = new uint[ChannelCount];

    public uint Size => 0x400;

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
