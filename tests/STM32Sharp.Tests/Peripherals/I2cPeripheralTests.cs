using STM32.Peripherals;
using STM32.Peripherals.I2c;

namespace STM32Sharp.Tests.Peripherals;

public class I2cPeripheralTests
{
    private const uint I2C1 = 0x40005400;
    private const uint CR2 = I2C1 + 0x04;
    private const uint ISR = I2C1 + 0x18;
    private const uint ICR = I2C1 + 0x1C;
    private const uint RXDR = I2C1 + 0x24;
    private const uint TXDR = I2C1 + 0x28;

    private const uint RD_WRN = 1u << 10;
    private const uint START = 1u << 13;
    private const uint AUTOEND = 1u << 25;

    private const uint TXIS = 1u << 1;
    private const uint RXNE = 1u << 2;
    private const uint NACKF = 1u << 4;
    private const uint STOPF = 1u << 5;

    /// <summary>A trivial slave that echoes a fixed sequence on read and records writes.</summary>
    private sealed class FakeSlave(int address) : II2cSlave
    {
        public int Address => address;
        public readonly List<byte> Written = [];
        public Queue<byte> ToRead = new();
        public void Write(byte value) => Written.Add(value);
        public byte Read() => ToRead.Count > 0 ? ToRead.Dequeue() : (byte)0;
    }

    private static uint Cr2(int addr7, int nbytes, bool read) =>
        ((uint)addr7 << 1) | ((uint)nbytes << 16) | (read ? RD_WRN : 0) | AUTOEND | START;

    [Fact]
    public void Master_write_delivers_bytes_to_slave()
    {
        using var m = new STM32Machine();
        var slave = new FakeSlave(0x42);
        m.I2c1.AddSlave(slave);

        m.Bus.WriteWord(CR2, Cr2(0x42, 2, read: false));
        (m.Bus.ReadWord(ISR) & TXIS).Should().NotBe(0);
        m.Bus.WriteWord(TXDR, 0xAA);
        m.Bus.WriteWord(TXDR, 0xBB);

        slave.Written.Should().Equal((byte)0xAA, (byte)0xBB);
        (m.Bus.ReadWord(ISR) & STOPF).Should().NotBe(0); // AUTOEND
    }

    [Fact]
    public void Master_read_returns_slave_bytes()
    {
        using var m = new STM32Machine();
        var slave = new FakeSlave(0x42);
        slave.ToRead = new Queue<byte>([0x11, 0x22]);
        m.I2c1.AddSlave(slave);

        m.Bus.WriteWord(CR2, Cr2(0x42, 2, read: true));
        (m.Bus.ReadWord(ISR) & RXNE).Should().NotBe(0);
        var b0 = m.Bus.ReadByte(RXDR);
        var b1 = m.Bus.ReadByte(RXDR);

        b0.Should().Be((byte)0x11);
        b1.Should().Be((byte)0x22);
        (m.Bus.ReadWord(ISR) & STOPF).Should().NotBe(0);
    }

    [Fact]
    public void Addressing_absent_slave_sets_nack()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CR2, Cr2(0x55, 1, read: false));
        (m.Bus.ReadWord(ISR) & NACKF).Should().NotBe(0);
    }

    [Fact]
    public void Icr_clears_flags()
    {
        using var m = new STM32Machine();
        m.Bus.WriteWord(CR2, Cr2(0x55, 1, read: false)); // sets NACKF
        m.Bus.WriteWord(ICR, NACKF);
        (m.Bus.ReadWord(ISR) & NACKF).Should().Be(0u);
    }
}
