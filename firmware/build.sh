#!/usr/bin/env bash
# Builds all sample firmwares to .bin and copies them into the test project.
# Requires the Arm GNU toolchain (arm-none-eabi-gcc) on PATH.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LD="$HERE/stm32g0_min.ld"
OUT="$HERE/../tests/STM32Sharp.Tests/Firmware"
mkdir -p "$OUT"

CFLAGS="-mcpu=cortex-m0plus -mthumb -Os -ffreestanding -nostdlib -Wl,--gc-sections"

build() {
  local name="$1"
  shift
  local src="$HERE/$name/$name.c"
  echo "Building $name ..."
  arm-none-eabi-gcc $CFLAGS "$@" -T "$LD" "$src" -o "$HERE/$name/$name.elf"
  arm-none-eabi-objcopy -O binary "$HERE/$name/$name.elf" "$HERE/$name/$name.bin"
  cp "$HERE/$name/$name.bin" "$OUT/$name.bin"
}

build boot_clock
build blink
build uart_echo

# feature_check is compiled against the OFFICIAL STMicroelectronics CMSIS device header and the
# ARM CMSIS-Core (cloned from GitHub), proving the emulator matches ST's authoritative register
# definitions. The headers are cached under firmware/.cmsis (git-ignored).
CMSIS="$HERE/.cmsis"
mkdir -p "$CMSIS"
[ -d "$CMSIS/cmsis_device_g0" ] || \
  git clone --depth 1 https://github.com/STMicroelectronics/cmsis_device_g0.git "$CMSIS/cmsis_device_g0"
[ -d "$CMSIS/CMSIS_6" ] || \
  git clone --depth 1 https://github.com/ARM-software/CMSIS_6.git "$CMSIS/CMSIS_6"

build feature_check \
  -I"$CMSIS/cmsis_device_g0/Include" \
  -I"$CMSIS/CMSIS_6/CMSIS/Core/Include" \
  -DSTM32G071xx

echo "Done. Binaries in $OUT"
