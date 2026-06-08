#!/usr/bin/env bash
# Builds the official Wokwi STM32 example (wokwi/stm32-hello-wokwi) and copies the resulting .bin into
# the test project as wokwi_hello.bin. This is the very firmware Wokwi runs for the Nucleo-C031C6: a
# STM32CubeMX/HAL application that prints "Hello, Wokwi!" over USART2. Running it unmodified on the
# C031 preset (see WokwiParityTests) proves bit-level parity with Wokwi.
#
# Requires the Arm GNU toolchain (arm-none-eabi-gcc) and git on PATH.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$HERE/../tests/STM32Sharp.Tests/Firmware"
SRC="$HERE/.wokwi/stm32-hello-wokwi"

mkdir -p "$HERE/.wokwi"
if [ ! -d "$SRC" ]; then
  git clone --depth 1 https://github.com/wokwi/stm32-hello-wokwi.git "$SRC"
fi

echo "Building wokwi/stm32-hello-wokwi (HAL) ..."
make -C "$SRC" -j4 >/dev/null

cp "$SRC/build/debug/build/stm32-hello-wokwi.bin" "$OUT/wokwi_hello.bin"
echo "Done. Copied wokwi_hello.bin to $OUT"
