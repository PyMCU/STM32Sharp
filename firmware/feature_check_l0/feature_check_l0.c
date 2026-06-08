/*
 * Feature-validation firmware for the STM32L031 (Wokwi's Nucleo-L031K6), compiled against the
 * OFFICIAL STMicroelectronics CMSIS device header (stm32l031xx.h) and the ARM CMSIS-Core.
 *
 * It exercises the L0-specific system peripherals the emulator now models — the L0 RCC clock tree
 * (boot on HSI → PLL), the L0 Flash PECR unlock, and DMA request routing via CSELR (no DMAMUX) —
 * alongside the shared APB peripherals (TIM2, SPI1, I2C1, RTC). Each subtest sets a bit in the
 * result word at 0x2000_0000; the test asserts 0xFF.
 *
 *   bit0 RCC L0: enable HSI + PLL, switch SYSCLK to PLL, SWS reports PLL
 *   bit1 Flash L0: ACR latency + two-stage PECR unlock (PEKEYR then PRGKEYR)
 *   bit2 TIM2 counts and raises UIF + CC1IF (PWM-mode compare)
 *   bit3 SPI1 full-duplex (RXNE, MISO idle 0xFF) + RXNEIE asserts the SPI1 NVIC line
 *   bit4 I2C1 START to an absent slave raises NACKF + NACKIE asserts the I2C1 NVIC line
 *   bit5 RTC calendar write/read-back through WPR + INIT
 *   bit6 DMA1 memory-to-memory block copy
 *   bit7 DMA1 request-driven copy via SPI1_RX, routed by CSELR (channel 2)
 */
#include "stm32l031xx.h"

#define RESULT (*(volatile uint32_t *)0x20000000u)
#define PHASE  (*(volatile uint32_t *)0x20000004u)

/* L0 Flash unlock keys (RM0451 §3.3.4; not exported by the CMSIS header). */
#define FLASH_PEKEY1  0x89ABCDEFu
#define FLASH_PEKEY2  0x02030405u
#define FLASH_PRGKEY1 0x8C9DAEBFu
#define FLASH_PRGKEY2 0x13141516u

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
    RESULT = 0; PHASE = 1;

    /* ── bit0: L0 RCC — boot on HSI, lock the PLL, switch SYSCLK to PLL ── */
    RCC->CR |= RCC_CR_HSION;
    while ((RCC->CR & RCC_CR_HSIRDY) == 0) { }
    RCC->CR |= RCC_CR_PLLON;
    while ((RCC->CR & RCC_CR_PLLRDY) == 0) { }
    RCC->CFGR = (RCC->CFGR & ~RCC_CFGR_SW) | RCC_CFGR_SW_PLL;
    while (((RCC->CFGR & RCC_CFGR_SWS) >> RCC_CFGR_SWS_Pos) != (RCC_CFGR_SWS_PLL >> RCC_CFGR_SWS_Pos)) { }
    result |= 1u << 0;
    RESULT = result; PHASE = 2;

    /* ── bit1: L0 Flash — wait states + two-stage PECR unlock ──────────── */
    FLASH->ACR |= FLASH_ACR_LATENCY;
    FLASH->PEKEYR = FLASH_PEKEY1;
    FLASH->PEKEYR = FLASH_PEKEY2;
    FLASH->PRGKEYR = FLASH_PRGKEY1;
    FLASH->PRGKEYR = FLASH_PRGKEY2;
    if ((FLASH->ACR & FLASH_ACR_LATENCY) && (FLASH->PECR & (FLASH_PECR_PELOCK | FLASH_PECR_PRGLOCK)) == 0)
        result |= 1u << 1;
    RESULT = result; PHASE = 3;

    /* ── bit2: TIM2 general-purpose timer, PWM mode 1 on CH1 ───────────── */
    TIM2->PSC  = 0;
    TIM2->ARR  = 9;
    TIM2->CCR1 = 5;
    TIM2->CCMR1 = (0x6u << TIM_CCMR1_OC1M_Pos);
    TIM2->CCER  = TIM_CCER_CC1E;
    TIM2->CR1   = TIM_CR1_CEN;
    spin(50000);
    if ((TIM2->SR & TIM_SR_UIF) && (TIM2->SR & TIM_SR_CC1IF))
        result |= 1u << 2;
    RESULT = result; PHASE = 4;

    /* ── bit3: SPI1 master full-duplex + RX interrupt to NVIC ──────────── */
    SPI1->CR1 = SPI_CR1_SPE;
    SPI1->CR2 = SPI_CR2_RXNEIE;
    *(volatile uint8_t *)&SPI1->DR = 0x3C;
    if ((SPI1->SR & SPI_SR_RXNE)
        && (NVIC->ISPR[0] & (1u << SPI1_IRQn))
        && *(volatile uint8_t *)&SPI1->DR == 0xFF)
        result |= 1u << 3;
    SPI1->CR2 = 0;
    RESULT = result; PHASE = 5;

    /* ── bit4: I2C1 addressing an absent slave → NACK + NVIC line ───────── */
    I2C1->CR1 = I2C_CR1_PE | I2C_CR1_NACKIE;
    I2C1->CR2 = ((uint32_t)0x55u << 1) | (1u << I2C_CR2_NBYTES_Pos) | I2C_CR2_AUTOEND | I2C_CR2_START;
    if ((I2C1->ISR & I2C_ISR_NACKF) && (NVIC->ISPR[0] & (1u << I2C1_IRQn)))
        result |= 1u << 4;
    I2C1->ICR = I2C_ICR_NACKCF;
    RESULT = result; PHASE = 6;

    /* ── bit5: RTC calendar set/read-back (WPR unlock → INIT → write) ───── */
    RTC->WPR = 0xCA;
    RTC->WPR = 0x53;
    RTC->ISR = RTC_ISR_INIT; /* L0 names the init/status reg ISR (same 0x0C offset as G0 ICSR) */
    for (volatile uint32_t g = 0; g < 1000 && (RTC->ISR & RTC_ISR_INITF) == 0; g++) { }
    RTC->TR = 0x00123456u;
    if ((RTC->TR & 0x007F7F7Fu) == 0x00123456u)
        result |= 1u << 5;
    RESULT = result; PHASE = 7;

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
    RESULT = result; PHASE = 8;

    /* ── bit7: DMA1 request-driven via CSELR — SPI1_RX maps to channel 2 ── */
    {
        volatile uint8_t *buf = (volatile uint8_t *)0x20000300u;
        buf[0] = 0; buf[1] = 0;

        DMA1_CSELR->CSELR = (1u << DMA_CSELR_C2S_Pos); /* SPI1_RX selector on channel 2 (RM0451) */
        DMA1_Channel2->CPAR  = (uint32_t)&SPI1->DR;
        DMA1_Channel2->CMAR  = (uint32_t)buf;
        DMA1_Channel2->CNDTR = 2;
        DMA1_Channel2->CCR   = DMA_CCR_MINC | DMA_CCR_EN;

        *(volatile uint8_t *)&SPI1->DR = 0x00;
        *(volatile uint8_t *)&SPI1->DR = 0x00;

        if ((DMA1->ISR & DMA_ISR_TCIF2) && buf[0] == 0xFF && buf[1] == 0xFF)
            result |= 1u << 7;
        DMA1_Channel2->CCR = 0;
    }

    RESULT = result; PHASE = 9;
    for (;;) { __asm__ volatile("wfi"); }
}

void Default_Handler(void) { for (;;) { } }
