using STM32.Core.Memory;

namespace STM32.Peripherals.Usart;

/// <summary>
/// STM32 USART (RM0444 §34). STM32G0 instances: USART1 @ 0x4001_3800, USART2 @ 0x4000_4400.
/// Register layout: CR1 0x00, CR2 0x04, CR3 0x08, BRR 0x0C, GTPR 0x10, RTOR 0x14, RQR 0x18,
/// ISR 0x1C, ICR 0x20, RDR 0x24, TDR 0x28.
///
/// Transmission is instantaneous: writing TDR fires <see cref="OnByteTransmit"/> and TXE/TC stay
/// asserted (we are always ready to send). Received bytes are queued via <see cref="InjectByte"/>,
/// which raises RXNE. When the relevant CR1 interrupt-enable bit is set, the peripheral pulses its
/// NVIC IRQ through <see cref="RaiseIrq"/>.
/// </summary>
public sealed class UsartPeripheral : IMemoryMappedDevice
{
    private const uint CR1 = 0x00;
    private const uint ISR = 0x1C;
    private const uint ICR = 0x20;
    private const uint RDR = 0x24;
    private const uint TDR = 0x28;

    // CR1 bits
    private const uint UE = 1u << 0;
    private const uint RE = 1u << 2;
    private const uint TE = 1u << 3;
    private const uint RXNEIE = 1u << 5;
    private const uint TCIE = 1u << 6;
    private const uint TXEIE = 1u << 7;

    // ISR bits
    private const uint ISR_RXNE = 1u << 5;
    private const uint ISR_TC   = 1u << 6;
    private const uint ISR_TXE  = 1u << 7;

    /// <summary>USART name for diagnostics, e.g. "USART2".</summary>
    public string Name { get; }

    /// <summary>The NVIC IRQ line for this USART, or -1 if not wired.</summary>
    public int Irq { get; }

    /// <summary>Raised with each transmitted byte (TDR write).</summary>
    public Action<byte>? OnByteTransmit;

    /// <summary>Raised when a received byte is available, signalling a DMA request (RX DREQ).</summary>
    public Action? OnRxDmaRequest;

    /// <summary>Set by the machine to assert/deassert this USART's NVIC IRQ.</summary>
    public Action<int, bool>? RaiseIrq;

    private uint _cr1;
    private readonly Queue<byte> _rxFifo = new();

    public uint Size => 0x400;

    public UsartPeripheral(string name, int irq = -1)
    {
        Name = name;
        Irq = irq;
    }

    /// <summary>Feed a byte into the receive path as if it arrived on the wire.</summary>
    public void InjectByte(byte value)
    {
        _rxFifo.Enqueue(value);
        EvaluateIrq();
        OnRxDmaRequest?.Invoke();
    }

    private uint BuildIsr()
    {
        // TXE and TC are always set (instant transmit); RXNE set when data is queued.
        uint isr = ISR_TXE | ISR_TC;
        if (_rxFifo.Count > 0) isr |= ISR_RXNE;
        return isr;
    }

    private void EvaluateIrq()
    {
        if (Irq < 0 || RaiseIrq == null || (_cr1 & UE) == 0) return;

        var pending =
            (((_cr1 & RXNEIE) != 0) && _rxFifo.Count > 0) ||
            ((_cr1 & TXEIE) != 0) ||  // TXE always set
            ((_cr1 & TCIE) != 0);     // TC always set

        RaiseIrq(Irq, pending);
    }

    public uint ReadWord(uint address)
    {
        switch (address & 0xFF)
        {
            case CR1: return _cr1;
            case ISR: return BuildIsr();
            case RDR:
                if (_rxFifo.Count > 0)
                {
                    var b = _rxFifo.Dequeue();
                    EvaluateIrq();
                    return b;
                }
                return 0;
            default: return 0;
        }
    }

    public ushort ReadHalfWord(uint address) =>
        (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));

    public byte ReadByte(uint address) =>
        (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));

    public void WriteWord(uint address, uint value)
    {
        switch (address & 0xFF)
        {
            case CR1:
                _cr1 = value;
                EvaluateIrq();
                break;

            case TDR:
                if ((_cr1 & UE) != 0 && (_cr1 & TE) != 0)
                    OnByteTransmit?.Invoke((byte)(value & 0xFF));
                // TXE/TC remain asserted; if TXEIE/TCIE set, keep IRQ pending.
                EvaluateIrq();
                break;

            case ICR:
                // Write-1-to-clear flags (TC, IDLE, ...). We keep TC permanently set, so ignore.
                break;
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
