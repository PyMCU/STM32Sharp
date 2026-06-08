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
[ -d "$CMSIS/cmsis_device_c0" ] || \
  git clone --depth 1 https://github.com/STMicroelectronics/cmsis_device_c0.git "$CMSIS/cmsis_device_c0"
[ -d "$CMSIS/CMSIS_6" ] || \
  git clone --depth 1 https://github.com/ARM-software/CMSIS_6.git "$CMSIS/CMSIS_6"

CORE_INC="-I$CMSIS/CMSIS_6/CMSIS/Core/Include"

# Reference STM32G071 build.
build feature_check \
  -I"$CMSIS/cmsis_device_g0/Include" $CORE_INC -DSTM32G071xx

# Same firmware retargeted at the STM32C031 (Wokwi part): official ST C0 header + C0 linker script,
# proving the G0 peripheral map and the emulator's C031 preset run real C0 firmware.
echo "Building feature_check_c031 ..."
arm-none-eabi-gcc $CFLAGS \
  -I"$CMSIS/cmsis_device_c0/Include" $CORE_INC \
  -DSTM32C031xx '-DDEVICE_HEADER="stm32c031xx.h"' \
  -T "$HERE/stm32c0_min.ld" "$HERE/feature_check/feature_check.c" \
  -o "$HERE/feature_check/feature_check_c031.elf"
arm-none-eabi-objcopy -O binary "$HERE/feature_check/feature_check_c031.elf" "$HERE/feature_check/feature_check_c031.bin"
cp "$HERE/feature_check/feature_check_c031.bin" "$OUT/feature_check_c031.bin"

echo "Done. Binaries in $OUT"
