using System.Runtime.CompilerServices;

namespace STM32.Core.Memory;

/// <summary>
/// STM32 system bus. Memory map (Cortex-M0+ families: STM32C0/F0/G0/L0):
///   0x0000_0000  Boot alias  -> mirrors Flash by default (BOOT0 = 0)
///   0x0800_0000  Flash       (code, fast pointer path)
///   0x1FFF_xxxx  System mem / option bytes (peripheral bus, optional)
///   0x2000_0000  SRAM        (fast pointer path)
///   0x4000_0000  APB/AHB peripherals (peripheral bus)
///   0x5000_0000  IOPORT/GPIO (peripheral bus)
///   0xE000_0000  PPB: NVIC, SysTick, SCB (peripheral bus)
///
/// Flash (0x0800_0000) and the boot alias (0x0000_0000) both fall in region 0x0 and are
/// served by the same backing buffer through <see cref="MaskFlash"/>. SRAM is region 0x2.
/// Everything else is dispatched to the <see cref="PeripheralBus"/> by full address.
/// </summary>
public sealed unsafe class BusInterconnect : IMemoryBus, IDisposable
{
    public const uint REGION_FLASH = 0x0; // covers boot alias 0x0000_0000 and Flash 0x0800_0000
    public const uint REGION_SRAM = 0x2;  // 0x2000_0000

    public const uint FLASH_START_ADDRESS = 0x08000000;
    public const uint SRAM_START_ADDRESS = 0x20000000;

    public uint FlashSize { get; }
    public uint MaskFlash { get; }
    public uint SramSize { get; }
    public uint MaskSram { get; }

    public readonly byte* PtrFlash;
    public readonly byte* PtrSram;

    private readonly RandomAccessMemory _flash;
    private readonly RandomAccessMemory _sram;
    private readonly PeripheralBus _peripherals = new();

    private bool _disposed;

    /// <param name="flashSizeBytes">Flash size — must be a power of two (default 128 KB, STM32G071).</param>
    /// <param name="sramSizeBytes">SRAM size — must be a power of two (default 32 KB usable mask for G071's 36 KB).</param>
    public BusInterconnect(uint flashSizeBytes = 128 * 1024, uint sramSizeBytes = 64 * 1024)
    {
        if (!IsPowerOfTwo(flashSizeBytes))
            throw new ArgumentException("Flash size must be a power of two", nameof(flashSizeBytes));
        if (!IsPowerOfTwo(sramSizeBytes))
            throw new ArgumentException("SRAM size must be a power of two", nameof(sramSizeBytes));

        FlashSize = flashSizeBytes;
        MaskFlash = flashSizeBytes - 1;
        SramSize = sramSizeBytes;
        MaskSram = sramSizeBytes - 1;

        _flash = new RandomAccessMemory((int)flashSizeBytes);
        _sram = new RandomAccessMemory((int)sramSizeBytes);

        PtrFlash = _flash.BasePtr;
        PtrSram = _sram.BasePtr;
    }

    private static bool IsPowerOfTwo(uint v) => v != 0 && (v & (v - 1)) == 0;

    /// <summary>Register a memory-mapped peripheral at its absolute base address.</summary>
    public void RegisterPeripheral(uint baseAddress, IMemoryMappedDevice device) =>
        _peripherals.Register(baseAddress, device);

    /// <summary>Load a firmware image into Flash starting at offset 0 (i.e. 0x0800_0000).</summary>
    public void LoadFlash(ReadOnlySpan<byte> image, uint offset = 0)
    {
        if (offset + (uint)image.Length > FlashSize)
            throw new ArgumentException("Image does not fit in Flash");
        var dst = new Span<byte>(PtrFlash + offset, (int)(FlashSize - offset));
        image.CopyTo(dst);
    }

    // --- READ ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte(uint address)
    {
        var region = address >> 28;
        if (region == REGION_SRAM) return PtrSram[address & MaskSram];
        if (region == REGION_FLASH) return PtrFlash[address & MaskFlash];
        return _peripherals.ReadByte(address);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadHalfWord(uint address)
    {
        var region = address >> 28;
        if (region == REGION_SRAM) return Unsafe.ReadUnaligned<ushort>(PtrSram + (address & MaskSram));
        if (region == REGION_FLASH) return Unsafe.ReadUnaligned<ushort>(PtrFlash + (address & MaskFlash));
        return _peripherals.ReadHalfWord(address);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadWord(uint address)
    {
        var region = address >> 28;
        if (region == REGION_SRAM) return Unsafe.ReadUnaligned<uint>(PtrSram + (address & MaskSram));
        if (region == REGION_FLASH) return Unsafe.ReadUnaligned<uint>(PtrFlash + (address & MaskFlash));
        return _peripherals.ReadWord(address);
    }

    // --- WRITE ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(uint address, byte value)
    {
        var region = address >> 28;
        if (region == REGION_SRAM) { PtrSram[address & MaskSram] = value; return; }
        if (region == REGION_FLASH) return; // Flash is read-only on the code bus (program via FLASH peripheral)
        _peripherals.WriteByte(address, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteHalfWord(uint address, ushort value)
    {
        var region = address >> 28;
        if (region == REGION_SRAM) { Unsafe.WriteUnaligned(PtrSram + (address & MaskSram), value); return; }
        if (region == REGION_FLASH) return;
        _peripherals.WriteHalfWord(address, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteWord(uint address, uint value)
    {
        var region = address >> 28;
        if (region == REGION_SRAM) { Unsafe.WriteUnaligned(PtrSram + (address & MaskSram), value); return; }
        if (region == REGION_FLASH) return;
        _peripherals.WriteWord(address, value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _flash.Dispose();
        _sram.Dispose();
        _disposed = true;
    }
}
