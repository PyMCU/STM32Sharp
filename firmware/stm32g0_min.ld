/* Minimal linker script for STM32G071 (128 KB Flash @ 0x08000000, 36 KB SRAM @ 0x20000000). */
MEMORY
{
  FLASH (rx) : ORIGIN = 0x08000000, LENGTH = 128K
  RAM  (rwx) : ORIGIN = 0x20000000, LENGTH = 36K
}

_estack = ORIGIN(RAM) + LENGTH(RAM);

ENTRY(Reset_Handler)

SECTIONS
{
  .isr_vector : { KEEP(*(.isr_vector)) } > FLASH
  .text   : { *(.text*) *(.rodata*) } > FLASH
  .data   : { *(.data*) } > RAM AT > FLASH
  .bss    : { *(.bss*)  } > RAM
}
