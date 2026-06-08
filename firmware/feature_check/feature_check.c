/*
 * Feature-validation firmware for STM32Sharp, compiled against the OFFICIAL
 * STMicroelectronics CMSIS device header (stm32g071xx.h) and the ARM CMSIS-Core.
 *
 * It does NOT use any hand-written register addresses: every access goes through
 * the authoritative ST peripheral structs and bitfield masks (TIM3->CCMR1,
 * SPI1->CR2, I2C1->ISR, RTC->WPR, DMA1_Channel1->CCR, DMAMUX1_Channel0->CCR, ...).
 * If the emulator's memory map, register offsets and bit semantics match silicon,
 * every subtest sets its bit in the result word; the test asserts 0xFF.
 *
 * Result marker: 0x2000_0000 (bitmask of passing subtests).
 *   bit0 TIM3 counts and raises UIF + CC1IF (PWM-mode compare)
 *   bit1 SPI1 full-duplex (RXNE after a frame, MISO idle = 0xFF)
 *   bit2 SPI1 RXNEIE asserts the SPI1 NVIC line (IRQ25 pending)
 *   bit3 I2C1 START to an absent slave raises NACKF
 *   bit4 I2C1 NACKIE asserts the I2C1 NVIC line (IRQ23 pending)
 *   bit5 RTC calendar write/read-back through the WPR-unlocked, INIT path
 *   bit6 DMA1 memory-to-memory block copy + TCIF
 *   bit7 DMA1 request-driven copy fed by the SPI1_RX DREQ via the DMAMUX
 */
#include "stm32g071xx.h"

#define RESULT (*(volatile uint32_t *)0x20000000u)
#define PHASE  (*(volatile uint32_t *)0x20000004u)

extern uint32_t _estack;
void Reset_Handler(void);
void Default_Handler(void);

__attribute__((section(".isr_vector"), used))
void (*const g_vectors[])(void) = {
    (void (*)(void)) & _estack,
    Reset_Handler,
    Default_Handler, /* NMI       */
    Default_Handler, /* HardFault */
};

static void spin(volatile uint32_t n) { while (n--) { __asm__ volatile("nop"); } }

void Reset_Handler(void)
{
    uint32_t result = 0;
    RESULT = 0;
    PHASE = 1;

    /* ── bit0: TIM3 general-purpose timer, PWM mode 1 on CH1 ──────────── */
    TIM3->PSC  = 0;
    TIM3->ARR  = 9;
    TIM3->CCR1 = 5;
    TIM3->CCMR1 = (0x6u << TIM_CCMR1_OC1M_Pos); /* OC1M = 110 (PWM mode 1) */
    TIM3->CCER  = TIM_CCER_CC1E;
    TIM3->DIER  = 0;
    TIM3->CR1   = TIM_CR1_CEN;
    spin(50000); /* let the emulator tick the timer across run batches */
    if ((TIM3->SR & TIM_SR_UIF) && (TIM3->SR & TIM_SR_CC1IF))
        result |= 1u << 0;
    RESULT = result; PHASE = 2;

    /* ── bit1/bit2: SPI1 master full-duplex + RX interrupt to NVIC ─────── */
    SPI1->CR1 = SPI_CR1_SPE;
    SPI1->CR2 = SPI_CR2_RXNEIE;          /* arm RX interrupt */
    *(volatile uint8_t *)&SPI1->DR = 0x3C;
    if (SPI1->SR & SPI_SR_RXNE)
    {
        if ((NVIC->ISPR[0] & (1u << SPI1_IRQn)) != 0)
            result |= 1u << 2;
        if (*(volatile uint8_t *)&SPI1->DR == 0xFF) /* MISO idle-high, no slave */
            result |= 1u << 1;
    }
    SPI1->CR2 = 0;                        /* drop the RX interrupt enable */
    RESULT = result; PHASE = 3;

    /* ── bit3/bit4: I2C1 addressing an absent slave → NACK + NVIC line ──── */
    I2C1->CR1 = I2C_CR1_PE | I2C_CR1_NACKIE;
    I2C1->CR2 = ((uint32_t)0x55u << 1)               /* SADD[7:1] = 0x55 */
              | (1u << I2C_CR2_NBYTES_Pos)
              | I2C_CR2_AUTOEND
              | I2C_CR2_START;
    if (I2C1->ISR & I2C_ISR_NACKF)
        result |= 1u << 3;
    if ((NVIC->ISPR[0] & (1u << I2C1_IRQn)) != 0)
        result |= 1u << 4;
    I2C1->ICR = I2C_ICR_NACKCF;
    RESULT = result; PHASE = 4;

    /* ── bit5: RTC calendar set/read-back (WPR unlock → INIT → write) ───── */
    RTC->WPR = 0xCA;
    RTC->WPR = 0x53;
    RTC->ICSR = RTC_ICSR_INIT;
    for (volatile uint32_t g = 0; g < 1000 && (RTC->ICSR & RTC_ICSR_INITF) == 0; g++) { }
    RTC->TR = 0x00123456u;               /* 12:34:56 in BCD */
    if ((RTC->TR & 0x007F7F7Fu) == 0x00123456u)
        result |= 1u << 5;
    RESULT = result; PHASE = 5;

    /* ── bit6: DMA1 memory-to-memory word copy ─────────────────────────── */
    {
        volatile uint32_t *src = (volatile uint32_t *)0x20000100u;
        volatile uint32_t *dst = (volatile uint32_t *)0x20000200u;
        for (uint32_t i = 0; i < 4; i++) src[i] = 0xC0DE0000u + i;

        DMA1_Channel1->CPAR  = (uint32_t)src;
        DMA1_Channel1->CMAR  = (uint32_t)dst;
        DMA1_Channel1->CNDTR = 4;
        DMA1_Channel1->CCR   = DMA_CCR_MEM2MEM | DMA_CCR_PINC | DMA_CCR_MINC
                             | DMA_CCR_PSIZE_1 | DMA_CCR_MSIZE_1 | DMA_CCR_EN;

        uint32_t ok = (DMA1->ISR & DMA_ISR_TCIF1) ? 1u : 0u;
        for (uint32_t i = 0; i < 4; i++)
            if (dst[i] != 0xC0DE0000u + i) ok = 0;
        if (ok) result |= 1u << 6;
        DMA1_Channel1->CCR = 0;
        DMA1->IFCR = DMA_IFCR_CGIF1;
    }
    RESULT = result; PHASE = 6;

    /* ── bit7: DMA1 request-driven, SPI1_RX DREQ routed via the DMAMUX ──── */
    {
        volatile uint8_t *buf = (volatile uint8_t *)0x20000300u;
        buf[0] = 0; buf[1] = 0;

        DMAMUX1_Channel0->CCR = (16u << DMAMUX_CxCR_DMAREQ_ID_Pos); /* SPI1_RX (RM0444) */
        DMA1_Channel1->CPAR  = (uint32_t)&SPI1->DR;
        DMA1_Channel1->CMAR  = (uint32_t)buf;
        DMA1_Channel1->CNDTR = 2;
        DMA1_Channel1->CCR   = DMA_CCR_MINC | DMA_CCR_EN; /* byte size, P->M */

        /* Two SPI frames → two RX DREQs → two bytes captured by the DMA. */
        *(volatile uint8_t *)&SPI1->DR = 0x00;
        *(volatile uint8_t *)&SPI1->DR = 0x00;

        if ((DMA1->ISR & DMA_ISR_TCIF1) && buf[0] == 0xFF && buf[1] == 0xFF)
            result |= 1u << 7;
        DMA1_Channel1->CCR = 0;
    }

    RESULT = result; PHASE = 7;
    for (;;) { __asm__ volatile("wfi"); }
}

void Default_Handler(void) { for (;;) { } }
