#!/usr/bin/env bash
# Builds the Arduino (STM32duino) sketch into a .bin and copies it into the test project.
# Unlike build.sh (bare-metal GCC), this needs arduino-cli with the STM32 core installed:
#
#   brew install arduino-cli   # or see https://arduino.github.io/arduino-cli
#   arduino-cli config add board_manager.additional_urls \
#     https://github.com/stm32duino/BoardManagerFiles/raw/main/package_stmicroelectronics_index.json
#   arduino-cli core update-index
#   arduino-cli core install STMicroelectronics:stm32
#
# The sketch is built for the Nucleo-G071RB, whose Serial is LPUART1 and LED is PA5. STM32duino runs
# on ST's HAL, so the resulting image boots through SystemClock_Config / HAL exactly like real firmware.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$HERE/../tests/STM32Sharp.Tests/Firmware"
FQBN="STMicroelectronics:stm32:Nucleo_64:pnum=NUCLEO_G071RB"

echo "Building arduino_blink for $FQBN ..."
arduino-cli compile --fqbn "$FQBN" --export-binaries "$HERE/arduino_blink"
cp "$HERE/arduino_blink/build/STMicroelectronics.stm32.Nucleo_64/arduino_blink.ino.bin" "$OUT/arduino_blink.bin"
echo "Done. arduino_blink.bin in $OUT"
