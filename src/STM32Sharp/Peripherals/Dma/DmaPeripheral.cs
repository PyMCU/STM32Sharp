using STM32.Core.Memory;

namespace STM32.Peripherals.Dma;

/// <summary>
/// DMA controller (DMA1) for STM32G0. Base 0x4002_0000, 7 channels. Register layout (RM0444 §11):
///   ISR 0x00, IFCR 0x04, then per channel n (1..7) at 0x08 + (n-1)*0x14:
///   CCRn +0x00, CNDTRn +0x04, CPARn +0x08, CMARn +0x0C.
///
/// Modeled as an immediate block transfer: enabling a channel (CCR.EN) runs the whole transfer at
/// once over the system bus, honouring DIR / MEM2MEM, PINC / MINC and PSIZE / MSIZE, then raises
/// the transfer-complete flag (and IRQ when TCIE is set). This is a simplification of the real
/// request-driven engine but covers memory-to-memory and peripheral copies used by typical firmware.
/// </summary>
public sealed class DmaPeripheral : IMemoryMappedDevice
{
    private const uint ISR  = 0x00;
    private const uint IFCR = 0x04;
    private const uint CHANNEL_BASE = 0x08;
    private const uint CHANNEL_STRIDE = 0x14;
    private const int ChannelCount = 7;

    // CCR bits
    private const uint CCR_EN      = 1u << 0;
    private const uint CCR_TCIE    = 1u << 1;
    private const uint CCR_DIR     = 1u << 4;  // 1 = read from memory
    private const uint CCR_CIRC    = 1u << 5;
    private const uint CCR_PINC    = 1u << 6;
    private const uint CCR_MINC    = 1u << 7;
    private const uint CCR_MEM2MEM = 1u << 14;

    // Per-channel ISR flag positions: GIF, TCIF, HTIF, TEIF at 4*(n-1) + {0,1,2,3}
    private static uint GifBit(int ch) => 1u << (4 * (ch - 1) + 0);
    private static uint TcifBit(int ch) => 1u << (4 * (ch - 1) + 1);

    private readonly IMemoryBus _bus;

    /// <summary>Set by the machine to assert a channel's grouped NVIC IRQ.</summary>
    public Action<int, bool>? RaiseIrq;

    private uint _isr;
    private readonly uint[] _ccr = new uint[ChannelCount + 1];
    private readonly uint[] _cndtr = new uint[ChannelCount + 1];
    private readonly uint[] _cpar = new uint[ChannelCount + 1];
    private readonly uint[] _cmar = new uint[ChannelCount + 1];

    public uint Size => 0x400;

    public DmaPeripheral(IMemoryBus bus) => _bus = bus;

    // STM32G0: ch1 → IRQ9, ch2-3 → IRQ10, ch4-7 → IRQ11.
    private static int IrqForChannel(int ch) => ch switch
    {
        1 => 9,
        2 or 3 => 10,
        _ => 11,
    };

    private static int ChannelOf(uint offset)
    {
        if (offset < CHANNEL_BASE) return -1;
        var ch = (int)((offset - CHANNEL_BASE) / CHANNEL_STRIDE) + 1;
        return ch is >= 1 and <= ChannelCount ? ch : -1;
    }

    private static uint ChannelReg(uint offset) => (offset - CHANNEL_BASE) % CHANNEL_STRIDE;

    private static int SizeBytes(uint sizeCode) => sizeCode switch { 0 => 1, 1 => 2, _ => 4 };

    private uint ReadSized(uint addr, int bytes) => bytes switch
    {
        1 => _bus.ReadByte(addr),
        2 => _bus.ReadHalfWord(addr),
        _ => _bus.ReadWord(addr),
    };

    private void WriteSized(uint addr, uint value, int bytes)
    {
        switch (bytes)
        {
            case 1: _bus.WriteByte(addr, (byte)value); break;
            case 2: _bus.WriteHalfWord(addr, (ushort)value); break;
            default: _bus.WriteWord(addr, value); break;
        }
    }

    private void RunTransfer(int ch)
    {
        var ccr = _ccr[ch];
        var count = _cndtr[ch];
        var pAddr = _cpar[ch];
        var mAddr = _cmar[ch];

        var pSize = SizeBytes((ccr >> 8) & 0x3);
        var mSize = SizeBytes((ccr >> 10) & 0x3);
        var pInc = (ccr & CCR_PINC) != 0;
        var mInc = (ccr & CCR_MINC) != 0;

        uint srcAddr, dstAddr;
        int srcSize, dstSize;
        bool srcInc, dstInc;

        if ((ccr & CCR_MEM2MEM) != 0 || (ccr & CCR_DIR) == 0)
        {
            // Read from peripheral/CPAR into memory/CMAR (and mem2mem uses CPAR as source).
            srcAddr = pAddr; srcSize = pSize; srcInc = pInc;
            dstAddr = mAddr; dstSize = mSize; dstInc = mInc;
        }
        else
        {
            // DIR = 1: read from memory/CMAR into peripheral/CPAR.
            srcAddr = mAddr; srcSize = mSize; srcInc = mInc;
            dstAddr = pAddr; dstSize = pSize; dstInc = pInc;
        }

        for (uint i = 0; i < count; i++)
        {
            var val = ReadSized(srcAddr, srcSize);
            WriteSized(dstAddr, val, dstSize);
            if (srcInc) srcAddr += (uint)srcSize;
            if (dstInc) dstAddr += (uint)dstSize;
        }

        // Transfer complete.
        _isr |= GifBit(ch) | TcifBit(ch);
        if ((ccr & CCR_CIRC) == 0)
            _ccr[ch] &= ~CCR_EN; // single-shot: clear EN
        if ((ccr & CCR_TCIE) != 0)
            RaiseIrq?.Invoke(IrqForChannel(ch), true);
    }

    public uint ReadWord(uint address)
    {
        var off = address & 0x3FF;
        if (off == ISR) return _isr;
        if (off == IFCR) return 0;

        var ch = ChannelOf(off);
        if (ch < 0) return 0;
        return ChannelReg(off) switch
        {
            0x00 => _ccr[ch],
            0x04 => _cndtr[ch],
            0x08 => _cpar[ch],
            0x0C => _cmar[ch],
            _ => 0,
        };
    }

    public ushort ReadHalfWord(uint address) =>
        (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));

    public byte ReadByte(uint address) =>
        (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));

    public void WriteWord(uint address, uint value)
    {
        var off = address & 0x3FF;
        if (off == IFCR)
        {
            _isr &= ~value; // write-1-to-clear
            return;
        }
        if (off == ISR) return;

        var ch = ChannelOf(off);
        if (ch < 0) return;

        switch (ChannelReg(off))
        {
            case 0x00:
                _ccr[ch] = value;
                if ((value & CCR_EN) != 0)
                    RunTransfer(ch);
                break;
            case 0x04: _cndtr[ch] = value & 0xFFFF; break;
            case 0x08: _cpar[ch] = value; break;
            case 0x0C: _cmar[ch] = value; break;
        }
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
