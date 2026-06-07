/*
 * Bare-metal STM32G0 blink on PA5 (Nucleo-G071RB green LED).
 * Configures PA5 as output and toggles it forever via the GPIOA BSRR/ODR registers.
 */
#include <stdint.h>

#define RCC_BASE     0x40021000u
#define RCC_IOPENR   (*(volatile uint32_t *)(RCC_BASE + 0x34u)) /* GPIO clock enable */

#define GPIOA_BASE   0x50000000u
#define GPIOA_MODER  (*(volatile uint32_t *)(GPIOA_BASE + 0x00u))
#define GPIOA_ODR    (*(volatile uint32_t *)(GPIOA_BASE + 0x14u))
#define GPIOA_BSRR   (*(volatile uint32_t *)(GPIOA_BASE + 0x18u))

#define LED_PIN 5

extern uint32_t _estack;
void Reset_Handler(void);
void Default_Handler(void);

__attribute__((section(".isr_vector"), used))
void (*const g_vectors[])(void) = {
    (void (*)(void)) & _estack,
    Reset_Handler,
    Default_Handler, /* NMI */
    Default_Handler, /* HardFault */
};

void Reset_Handler(void)
{
    /* Enable GPIOA clock (IOPENR bit 0). */
    RCC_IOPENR |= (1u << 0);

    /* PA5 as general-purpose output: MODER[11:10] = 0b01. */
    GPIOA_MODER = (GPIOA_MODER & ~(0x3u << (LED_PIN * 2))) | (0x1u << (LED_PIN * 2));

    for (;;)
    {
        GPIOA_BSRR = (1u << LED_PIN);          /* set PA5 high   */
        for (volatile int i = 0; i < 50; i++) { }
        GPIOA_BSRR = (1u << (LED_PIN + 16));   /* set PA5 low    */
        for (volatile int i = 0; i < 50; i++) { }
    }
}

void Default_Handler(void)
{
    for (;;) { }
}
