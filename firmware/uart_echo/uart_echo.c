/*
 * Bare-metal STM32G0 USART2 echo. Enables USART2, then polls RXNE and echoes each
 * received byte back through TDR. Also transmits a greeting on startup so the test can
 * assert TX works without injecting input.
 */
#include <stdint.h>

#define RCC_BASE      0x40021000u
#define RCC_APBENR1   (*(volatile uint32_t *)(RCC_BASE + 0x3Cu)) /* USART2 clock enable */

#define USART2_BASE   0x40004400u
#define USART2_CR1    (*(volatile uint32_t *)(USART2_BASE + 0x00u))
#define USART2_BRR    (*(volatile uint32_t *)(USART2_BASE + 0x0Cu))
#define USART2_ISR    (*(volatile uint32_t *)(USART2_BASE + 0x1Cu))
#define USART2_RDR    (*(volatile uint32_t *)(USART2_BASE + 0x24u))
#define USART2_TDR    (*(volatile uint32_t *)(USART2_BASE + 0x28u))

#define CR1_UE  (1u << 0)
#define CR1_RE  (1u << 2)
#define CR1_TE  (1u << 3)
#define ISR_RXNE (1u << 5)
#define ISR_TXE  (1u << 7)

extern uint32_t _estack;
void Reset_Handler(void);
void Default_Handler(void);

__attribute__((section(".isr_vector"), used))
void (*const g_vectors[])(void) = {
    (void (*)(void)) & _estack,
    Reset_Handler,
    Default_Handler,
    Default_Handler,
};

static void uart_putc(char c)
{
    while (!(USART2_ISR & ISR_TXE)) { }
    USART2_TDR = (uint32_t)(uint8_t)c;
}

void Reset_Handler(void)
{
    RCC_APBENR1 |= (1u << 17); /* USART2EN */

    USART2_BRR = 0x0010;       /* arbitrary; baud is not modeled */
    USART2_CR1 = CR1_UE | CR1_TE | CR1_RE;

    const char *msg = "READY\n";
    for (const char *p = msg; *p; ++p)
        uart_putc(*p);

    for (;;)
    {
        if (USART2_ISR & ISR_RXNE)
        {
            uint8_t b = (uint8_t)USART2_RDR;
            uart_putc((char)b);
        }
    }
}

void Default_Handler(void)
{
    for (;;) { }
}
