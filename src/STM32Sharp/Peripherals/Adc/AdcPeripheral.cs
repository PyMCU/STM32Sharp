using STM32.Core.Memory;

namespace STM32.Peripherals.Adc;

/// <summary>
/// Analog-to-digital converter (ADC) for STM32G0. Base 0x4001_2400. Register subset (RM0444 §14):
///   ISR 0x00, IER 0x04, CR 0x08, CFGR1 0x0C, CHSELR 0x28, DR 0x40.
///
/// Conversions are instantaneous. Channel input values are supplied by the host via
/// <see cref="SetChannel"/>. Enabling the ADC (CR.ADEN) sets ADRDY; CR.ADSTART converts the
/// channels selected in CHSELR (bit mode) in ascending order — each DR read yields the current
/// channel's value, sets/clears EOC and advances the sequence (wrapping when CFGR1.CONT is set).
/// </summary>
public sealed class AdcPeripheral : IMemoryMappedDevice
{
    private const uint ISR    = 0x00;
    private const uint IER    = 0x04;
    private const uint CR     = 0x08;
    private const uint CFGR1  = 0x0C;
    private const uint CHSELR = 0x28;
    private const uint DR     = 0x40;

    // CR bits
    private const uint CR_ADEN    = 1u << 0;
    private const uint CR_ADSTART = 1u << 2;
    private const uint CR_ADSTP   = 1u << 4;
    private const uint CR_ADCAL   = 1u << 31;

    // CFGR1 bits
    private const uint CFGR1_CONT = 1u << 13;

    // ISR flags
    private const uint ISR_ADRDY = 1u << 0;
    private const uint ISR_EOC   = 1u << 2;
    private const uint ISR_EOS   = 1u << 3;

    private const int ChannelCount = 19; // ch0..18 (incl. temp sensor, vref, vbat)

    /// <summary>Raised when a conversion completes (EOC), signalling a DMA request (DREQ).</summary>
    public Action? OnDmaRequest;

    private readonly ushort[] _channels = new ushort[ChannelCount];

    private uint _isr;
    private uint _cr;
    private uint _cfgr1;
    private uint _chselr;
    private ushort _dr;

    private int[] _sequence = [];
    private int _seqIndex;

    public uint Size => 0x400;

    /// <summary>Set the conversion value for an analog channel (0..18), 12-bit (0..4095).</summary>
    public void SetChannel(int channel, ushort value)
    {
        if ((uint)channel < ChannelCount) _channels[channel] = (ushort)(value & 0x0FFF);
    }

    public uint ReadWord(uint address)
    {
        switch (address & 0xFF)
        {
            case ISR: return _isr;
            case IER: return 0;
            case CR: return _cr;
            case CFGR1: return _cfgr1;
            case CHSELR: return _chselr;
            case DR: return ReadDr();
            default: return 0;
        }
    }

    public ushort ReadHalfWord(uint address) =>
        (ushort)(ReadWord(address & ~3u) >> (int)((address & 2) << 3));

    public byte ReadByte(uint address) =>
        (byte)(ReadWord(address & ~3u) >> (int)((address & 3) << 3));

    private void BuildSequence()
    {
        var seq = new List<int>();
        for (var ch = 0; ch < ChannelCount; ch++)
            if ((_chselr & (1u << ch)) != 0)
                seq.Add(ch);
        _sequence = [.. seq];
        _seqIndex = 0;
    }

    private void ConvertCurrent()
    {
        if (_sequence.Length == 0) return;
        _dr = _channels[_sequence[_seqIndex]];
        _isr |= ISR_EOC;
        if (_seqIndex == _sequence.Length - 1) _isr |= ISR_EOS;
        OnDmaRequest?.Invoke();
    }

    private ushort ReadDr()
    {
        var val = _dr;
        _isr &= ~ISR_EOC;
        if (_sequence.Length == 0) return val;

        _seqIndex++;
        if (_seqIndex < _sequence.Length)
        {
            ConvertCurrent();
        }
        else if ((_cfgr1 & CFGR1_CONT) != 0)
        {
            _seqIndex = 0;
            _isr &= ~ISR_EOS;
            ConvertCurrent();
        }
        return val;
    }

    public void WriteWord(uint address, uint value)
    {
        switch (address & 0xFF)
        {
            case ISR: _isr &= ~value; break; // write-1-to-clear
            case CFGR1: _cfgr1 = value; break;
            case CHSELR: _chselr = value; break;

            case CR:
                if ((value & CR_ADCAL) != 0)
                {
                    // Calibration completes immediately; ADCAL self-clears.
                    value &= ~CR_ADCAL;
                }
                if ((value & CR_ADEN) != 0)
                    _isr |= ISR_ADRDY;
                if ((value & CR_ADSTART) != 0 && (_cr & CR_ADEN | value & CR_ADEN) != 0)
                {
                    BuildSequence();
                    _isr &= ~ISR_EOS;
                    ConvertCurrent();
                }
                if ((value & CR_ADSTP) != 0)
                    value &= ~CR_ADSTART; // stop clears ADSTART
                _cr = value & ~(CR_ADSTP);
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
