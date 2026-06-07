using STM32.Peripherals.Gpio;

namespace STM32.TestKit.Probes;

/// <summary>
/// Records level changes on a GPIO port's pins and exposes simple counters/state for assertions
/// (e.g. verifying a blinking LED toggles). Attach to a <see cref="GpioPortPeripheral"/>.
/// </summary>
public sealed class GpioProbe
{
    private readonly int[] _transitions = new int[16];
    private readonly bool[] _level = new bool[16];
    private GpioPortPeripheral? _port;

    /// <summary>Attach this probe to a GPIO port.</summary>
    public GpioProbe Attach(GpioPortPeripheral port)
    {
        if (_port != null)
            _port.OnPinChange -= Capture;
        _port = port;
        _port.OnPinChange += Capture;
        return this;
    }

    /// <summary>Number of level changes observed on a pin.</summary>
    public int Transitions(int pin) => _transitions[pin];

    /// <summary>Last observed level of a pin.</summary>
    public bool Level(int pin) => _level[pin];

    /// <summary>True if the pin toggled at least <paramref name="count"/> times.</summary>
    public bool Toggled(int pin, int count = 1) => _transitions[pin] >= count;

    private void Capture(int pin, bool high)
    {
        _transitions[pin]++;
        _level[pin] = high;
    }
}
