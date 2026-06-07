/*
 * Minimal bare-metal STM32G0 firmware exercising the boot path the STM32Cube HAL
 * relies on: turn the PLL on and spin until PLLRDY, switch the system clock to the
 * PLL and spin until SWS matches, then run a SysTick interrupt handler.
 *
 * It writes observable markers to fixed SRAM addresses so the emulator test can
 * assert progress:
 *   0x2000_0000 = boot marker (0xABCD1234 once clock config succeeds)
 *   0x2000_0004 = SysTick interrupt counter
 *
 * There are no .data/.bss globals (all state is at absolute SRAM addresses or on
 * the stack), so the markers never collide with the linker-placed sections.
 */
#include <stdint.h>

#define RCC_BASE   0x40021000u
#define RCC_CR     (*(volatile uint32_t *)(RCC_BASE + 0x00u))
#define RCC_CFGR   (*(volatile uint32_t *)(RCC_BASE + 0x08u))
#define PLLON      (1u << 24)
#define PLLRDY     (1u << 25)

#define SYST_CSR   (*(volatile uint32_t *)0xE000E010u)
#define SYST_RVR   (*(volatile uint32_t *)0xE000E014u)
#define SYST_CVR   (*(volatile uint32_t *)0xE000E018u)

#define MARKER     (*(volatile uint32_t *)0x20000000u)
#define TICKS      (*(volatile uint32_t *)0x20000004u)

extern uint32_t _estack;

void Reset_Handler(void);
void SysTick_Handler(void);
void Default_Handler(void);

__attribute__((section(".isr_vector"), used))
void (*const g_vectors[])(void) = {
    (void (*)(void)) & _estack, /* [0]  initial SP            */
    Reset_Handler,              /* [1]  reset                 */
    Default_Handler,            /* [2]  NMI                   */
    Default_Handler,            /* [3]  HardFault             */
    0, 0, 0, 0, 0, 0, 0,        /* [4..10] reserved           */
    Default_Handler,            /* [11] SVCall                */
    0, 0,                       /* [12,13] reserved           */
    Default_Handler,            /* [14] PendSV                */
    SysTick_Handler,            /* [15] SysTick               */
};

void Reset_Handler(void)
{
    MARKER = 0;
    TICKS = 0;

    /* HAL pattern: enable PLL and wait for it to lock. */
    RCC_CR |= PLLON;
    while (!(RCC_CR & PLLRDY)) { }

    /* Switch system clock to PLL (SW = 0b010 on STM32G0) and wait for SWS. */
    RCC_CFGR = (RCC_CFGR & ~0x7u) | 0x2u;
    while (((RCC_CFGR >> 3) & 0x7u) != 0x2u) { }

    MARKER = 0xABCD1234u;

    /* Configure SysTick to fire interrupts. */
    SYST_RVR = 1000u;
    SYST_CVR = 0u;
    SYST_CSR = 0x3u; /* ENABLE | TICKINT */

    for (;;) { }
}

void SysTick_Handler(void) { TICKS++; }

void Default_Handler(void)
{
    for (;;) { }
}
