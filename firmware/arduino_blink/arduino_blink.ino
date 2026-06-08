/*
 * Arduino (STM32duino) sketch for the emulator. Built with the official STMicroelectronics
 * Arduino core, which sits on top of ST's HAL — so the resulting binary boots through
 * SystemClock_Config(), HAL GPIO and HAL UART exactly like real Arduino firmware.
 *
 * On the Nucleo-G071RB, LED_BUILTIN is PA5 and Serial is wired to USART2 (the ST-Link VCP).
 * The emulator asserts the banner over USART2 and toggles PA5, both observable from the TestKit.
 */
void setup() {
  pinMode(LED_BUILTIN, OUTPUT);
  Serial.begin(9600);
  Serial.println("STM32DUINO-OK");
}

void loop() {
  digitalWrite(LED_BUILTIN, HIGH);
  delay(20);
  digitalWrite(LED_BUILTIN, LOW);
  delay(20);
  Serial.println("tick");
}
