using System.Text;
using STM32.Peripherals.Usart;

namespace STM32.TestKit.Probes;

/// <summary>
/// Captures bytes transmitted by a USART and allows injecting bytes into its RX path.
/// Attach to a <see cref="UsartPeripheral"/> via <see cref="Attach"/>.
/// </summary>
public sealed class UartProbe
{
    private readonly List<byte> _bytes = [];
    private string? _textCache;
    private string[]? _linesCache;

    /// <summary>All bytes transmitted so far.</summary>
    public IReadOnlyList<byte> Bytes => _bytes;

    /// <summary>Number of bytes captured.</summary>
    public int ByteCount => _bytes.Count;

    /// <summary>Transmitted bytes decoded as Latin-1 text.</summary>
    public string Text => _textCache ??= Encoding.Latin1.GetString(_bytes.ToArray());

    /// <summary>Lines split on LF (CR stripped), cached until the next byte arrives.</summary>
    public IReadOnlyList<string> Lines
        => _linesCache ??= Text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

    private UsartPeripheral? _usart;

    /// <summary>Attach this probe to a USART peripheral.</summary>
    public UartProbe Attach(UsartPeripheral usart)
    {
        if (_usart != null)
            _usart.OnByteTransmit -= Capture;
        _usart = usart;
        _usart.OnByteTransmit += Capture;
        return this;
    }

    /// <summary>Inject a byte as if received from a remote device.</summary>
    public void InjectByte(byte value) => _usart?.InjectByte(value);

    /// <summary>Inject a string as Latin-1 bytes.</summary>
    public void InjectString(string text)
    {
        if (_usart == null) return;
        foreach (var b in Encoding.Latin1.GetBytes(text))
            _usart.InjectByte(b);
    }

    /// <summary>Clear captured data.</summary>
    public void Clear()
    {
        _bytes.Clear();
        _textCache = null;
        _linesCache = null;
    }

    private void Capture(byte b)
    {
        _bytes.Add(b);
        _textCache = null;
        _linesCache = null;
    }
}
