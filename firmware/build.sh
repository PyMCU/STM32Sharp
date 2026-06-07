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
  local src="$HERE/$name/$name.c"
  echo "Building $name ..."
  arm-none-eabi-gcc $CFLAGS -T "$LD" "$src" -o "$HERE/$name/$name.elf"
  arm-none-eabi-objcopy -O binary "$HERE/$name/$name.elf" "$HERE/$name/$name.bin"
  cp "$HERE/$name/$name.bin" "$OUT/$name.bin"
}

build boot_clock
build blink
build uart_echo

echo "Done. Binaries in $OUT"
