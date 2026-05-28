#include <Arduino.h>
#include <SPI.h>
#if defined(ESP32)
#include <esp_heap_caps.h>
#endif

#include "palm_config.h"
#include "palm_hw.h"
#include "palm_memory.h"

#if PALM_ENABLE_MUSASHI
extern "C" {
#include "Musashi-master/m68k.h"
}
extern "C" void m68k_pulse_reset(void);
#endif

#define LCD_Backlight 21
#define T_CLK         25
#define T_CS          33
#define T_DIN         32
#define T_DOUT        39
#define T_IRQ         36

#define LED_R         4
#define LED_G         16
#define LED_B         17
#define TFT_MISO      12
#define TFT_MOSI      13
#define TFT_SCLK      14
#define TFT_CS        15
#define TFT_DC        2
#define TFT_RST       -1

#define TFT_BLACK     0x0000
#define TFT_WHITE     0xffff
#define TFT_DARKGREY  0x7bef
#define TFT_DARKGREEN 0x7d27

static SPIClass tftSPI(VSPI);
static SPIClass touchSPI(HSPI);

static constexpr int SCREEN_W = 240;
static constexpr int SCREEN_H = 320;
static constexpr int PALM_LCD_X = (SCREEN_W - PALM_LCD_W) / 2;
static constexpr int PALM_LCD_Y = 38;

static unsigned long lastFrameMs = 0;
static unsigned long lastStatsMs = 0;
static unsigned long lastLoopBeatMs = 0;
static bool lcdWaitingDrawn = false;
static bool staticShellDrawn = false;
static bool cpuReady = false;
static uint32_t cpuSlices = 0;
static bool wakeTouchActive = false;
static bool lastTouchDown = false;
static uint16_t lastTouchAdcX = 0;
static uint16_t lastTouchAdcY = 0;
static int lastTouchScreenX = -1;
static int lastTouchScreenY = -1;

static void lcdSelect() {
  tftSPI.beginTransaction(SPISettings(40000000, MSBFIRST, SPI_MODE0));
  digitalWrite(TFT_CS, LOW);
}

static void lcdDeselect() {
  digitalWrite(TFT_CS, HIGH);
  tftSPI.endTransaction();
}

static void lcdCommand(uint8_t command) {
  lcdSelect();
  digitalWrite(TFT_DC, LOW);
  tftSPI.transfer(command);
  lcdDeselect();
}

static void lcdData8(uint8_t data) {
  lcdSelect();
  digitalWrite(TFT_DC, HIGH);
  tftSPI.transfer(data);
  lcdDeselect();
}

static void lcdCommandData(uint8_t command, const uint8_t *data, size_t len) {
  lcdSelect();
  digitalWrite(TFT_DC, LOW);
  tftSPI.transfer(command);
  digitalWrite(TFT_DC, HIGH);
  while (len--) tftSPI.transfer(*data++);
  lcdDeselect();
}

static void lcdSetWindowAddr(uint16_t x, uint16_t y, uint16_t w, uint16_t h) {
  uint16_t x2 = x + w - 1;
  uint16_t y2 = y + h - 1;
  uint8_t col[] = {
      static_cast<uint8_t>(x >> 8), static_cast<uint8_t>(x),
      static_cast<uint8_t>(x2 >> 8), static_cast<uint8_t>(x2)};
  uint8_t row[] = {
      static_cast<uint8_t>(y >> 8), static_cast<uint8_t>(y),
      static_cast<uint8_t>(y2 >> 8), static_cast<uint8_t>(y2)};
  lcdCommandData(0x2A, col, sizeof(col));
  lcdCommandData(0x2B, row, sizeof(row));
}

static void lcdBeginPixels(uint16_t x, uint16_t y, uint16_t w, uint16_t h) {
  lcdSetWindowAddr(x, y, w, h);
  lcdSelect();
  digitalWrite(TFT_DC, LOW);
  tftSPI.transfer(0x2C);
  digitalWrite(TFT_DC, HIGH);
}

static void lcdPushColor(uint16_t color, uint32_t count) {
  uint8_t hi = color >> 8;
  uint8_t lo = color & 0xff;
  while (count--) {
    tftSPI.transfer(hi);
    tftSPI.transfer(lo);
  }
  lcdDeselect();
}

static void lcdFillRect(int16_t x, int16_t y, int16_t w, int16_t h, uint16_t color) {
  if (w <= 0 || h <= 0 || x >= SCREEN_W || y >= SCREEN_H) return;
  if (x < 0) {
    w += x;
    x = 0;
  }
  if (y < 0) {
    h += y;
    y = 0;
  }
  if (x + w > SCREEN_W) w = SCREEN_W - x;
  if (y + h > SCREEN_H) h = SCREEN_H - y;
  if (w <= 0 || h <= 0) return;
  lcdBeginPixels(x, y, w, h);
  lcdPushColor(color, static_cast<uint32_t>(w) * h);
}

static void lcdDrawFastHLine(int16_t x, int16_t y, int16_t w, uint16_t color) {
  lcdFillRect(x, y, w, 1, color);
}

static void lcdDrawFastVLine(int16_t x, int16_t y, int16_t h, uint16_t color) {
  lcdFillRect(x, y, 1, h, color);
}

static void lcdDrawRect(int16_t x, int16_t y, int16_t w, int16_t h, uint16_t color) {
  lcdDrawFastHLine(x, y, w, color);
  lcdDrawFastHLine(x, y + h - 1, w, color);
  lcdDrawFastVLine(x, y, h, color);
  lcdDrawFastVLine(x + w - 1, y, h, color);
}

static void lcdDrawLine(int16_t x0, int16_t y0, int16_t x1, int16_t y1, uint16_t color) {
  if (x0 == x1) {
    if (y1 < y0) {
      int16_t t = y0; y0 = y1; y1 = t;
    }
    lcdDrawFastVLine(x0, y0, y1 - y0 + 1, color);
  } else if (y0 == y1) {
    if (x1 < x0) {
      int16_t t = x0; x0 = x1; x1 = t;
    }
    lcdDrawFastHLine(x0, y0, x1 - x0 + 1, color);
  }
}

static void lcdPushImage(int16_t x, int16_t y, int16_t w, int16_t h, const uint16_t *pixels) {
  if (w <= 0 || h <= 0) return;
  lcdBeginPixels(x, y, w, h);
  for (int32_t i = 0; i < static_cast<int32_t>(w) * h; ++i) {
    uint16_t color = pixels[i];
    tftSPI.transfer(color >> 8);
    tftSPI.transfer(color & 0xff);
  }
  lcdDeselect();
}

static void lcdInit() {
  pinMode(TFT_CS, OUTPUT);
  pinMode(TFT_DC, OUTPUT);
  digitalWrite(TFT_CS, HIGH);
  digitalWrite(TFT_DC, HIGH);
  tftSPI.begin(TFT_SCLK, TFT_MISO, TFT_MOSI, TFT_CS);

#if TFT_RST >= 0
  pinMode(TFT_RST, OUTPUT);
  digitalWrite(TFT_RST, HIGH);
  delay(5);
  digitalWrite(TFT_RST, LOW);
  delay(20);
  digitalWrite(TFT_RST, HIGH);
  delay(120);
#endif

  lcdCommand(0x01);
  delay(120);
  const uint8_t pwrB[] = {0x00, 0x83, 0x30};
  lcdCommandData(0xCF, pwrB, sizeof(pwrB));
  const uint8_t pwrSeq[] = {0x64, 0x03, 0x12, 0x81};
  lcdCommandData(0xED, pwrSeq, sizeof(pwrSeq));
  const uint8_t drvTimingA[] = {0x85, 0x01, 0x79};
  lcdCommandData(0xE8, drvTimingA, sizeof(drvTimingA));
  const uint8_t pwrA[] = {0x39, 0x2C, 0x00, 0x34, 0x02};
  lcdCommandData(0xCB, pwrA, sizeof(pwrA));
  uint8_t pumpRatio = 0x20;
  lcdCommandData(0xF7, &pumpRatio, 1);
  const uint8_t drvTimingB[] = {0x00, 0x00};
  lcdCommandData(0xEA, drvTimingB, sizeof(drvTimingB));
  uint8_t power1 = 0x26;
  lcdCommandData(0xC0, &power1, 1);
  uint8_t power2 = 0x11;
  lcdCommandData(0xC1, &power2, 1);
  const uint8_t vcom1[] = {0x35, 0x3E};
  lcdCommandData(0xC5, vcom1, sizeof(vcom1));
  uint8_t vcom2 = 0xBE;
  lcdCommandData(0xC7, &vcom2, 1);
  lcdCommand(0x11);
  delay(120);
  uint8_t pixelFormat = 0x55;
  lcdCommandData(0x3A, &pixelFormat, 1);
  uint8_t madctl = 0x48;
  lcdCommandData(0x36, &madctl, 1);
  const uint8_t frameRate[] = {0x00, 0x1B};
  lcdCommandData(0xB1, frameRate, sizeof(frameRate));
  uint8_t displayFunction[] = {0x0A, 0xA2};
  lcdCommandData(0xB6, displayFunction, sizeof(displayFunction));
  uint8_t gamma = 0x01;
  lcdCommandData(0xF2, &gamma, 1);
  uint8_t gammaSet = 0x01;
  lcdCommandData(0x26, &gammaSet, 1);
  lcdCommand(0x29);
  delay(20);
}

static uint16_t readTouchRaw(uint8_t cmd) {
  digitalWrite(T_CS, LOW);
  touchSPI.transfer(cmd);
  uint8_t hi = touchSPI.transfer(0x00);
  uint8_t lo = touchSPI.transfer(0x00);
  digitalWrite(T_CS, HIGH);
  return ((uint16_t)hi << 5) | (lo >> 3);
}

static uint16_t readTouchFiltered(uint8_t cmd) {
  readTouchRaw(cmd);
  uint16_t a = readTouchRaw(cmd);
  uint16_t b = readTouchRaw(cmd);
  uint16_t c = readTouchRaw(cmd);
  if (a > b) {
    uint16_t t = a; a = b; b = t;
  }
  if (b > c) {
    uint16_t t = b; b = c; c = t;
  }
  if (a > b) {
    uint16_t t = a; a = b; b = t;
  }
  return b;
}

static bool getRawTouch(int &rawX, int &rawY) {
  if (digitalRead(T_IRQ) == HIGH) return false;

  uint32_t sumX = 0;
  uint32_t sumY = 0;
  int good = 0;
  for (int i = 0; i < 8; ++i) {
    if (digitalRead(T_IRQ) == HIGH) break;
    uint16_t z1 = readTouchRaw(0xB0);
    if (z1 < 150) continue;
    sumX += readTouchFiltered(0xD0);
    sumY += readTouchFiltered(0x90);
    ++good;
  }
  if (good < 2) return false;

  rawX = sumX / good;
  rawY = sumY / good;
  return true;
}

static bool getPalmScreenTouch(int &screenX, int &screenY) {
  int rawX;
  int rawY;
  if (!getRawTouch(rawX, rawY)) {
    lastTouchDown = false;
    return false;
  }

  // Calibration values copied from the CYD reference sketch defaults.
  screenX = map(rawY, 3800, 250, 0, SCREEN_W - 1);
  screenY = map(rawX, 250, 3800, 0, SCREEN_H - 1);
  screenX = constrain(screenX, 0, SCREEN_W - 1);
  screenY = constrain(screenY, 0, SCREEN_H - 1);
  lastTouchDown = true;
  lastTouchAdcX = rawX;
  lastTouchAdcY = rawY;
  lastTouchScreenX = screenX;
  lastTouchScreenY = screenY;
  return screenX >= PALM_LCD_X && screenX < PALM_LCD_X + PALM_LCD_W &&
         screenY >= PALM_LCD_Y && screenY < PALM_LCD_Y + PALM_DIGITIZER_H;
}

static bool getPalmTouch(int &palmX, int &palmY) {
  int screenX;
  int screenY;
  if (!getPalmScreenTouch(screenX, screenY)) return false;
  palmX = constrain(screenX - PALM_LCD_X, 0, PALM_LCD_W - 1);
  palmY = constrain(screenY - PALM_LCD_Y, 0, PALM_DIGITIZER_H - 1);
  return true;
}

static bool getPalmAdcTouch(uint16_t &adcX, uint16_t &adcY) {
  int screenX;
  int screenY;
  if (!getPalmScreenTouch(screenX, screenY)) return false;

  adcX = constrain(map(screenX, PALM_LCD_X, PALM_LCD_X + PALM_LCD_W - 1, 3800, 300), 0, 4095);
  adcY = constrain(map(screenY, PALM_LCD_Y, PALM_LCD_Y + PALM_DIGITIZER_H - 1, 3800, 300), 0, 4095);
  return true;
}

static void drawPalmBorder() {
  lcdFillRect(PALM_LCD_X - 2, PALM_LCD_Y - 2, PALM_LCD_W + 4, PALM_LCD_H + 4, TFT_BLACK);
}

static void drawPalmSilkscreen() {
#if PALM_DRAW_STATIC_SHELL
  int y = PALM_LCD_Y + PALM_LCD_H;
  lcdFillRect(PALM_LCD_X, y, PALM_LCD_W, PALM_SILKSCREEN_H, TFT_DARKGREEN);
  lcdDrawRect(PALM_LCD_X, y, PALM_LCD_W, PALM_SILKSCREEN_H, TFT_BLACK);
  lcdDrawLine(PALM_LCD_X + 27, y + 4, PALM_LCD_X + 27, y + PALM_SILKSCREEN_H - 4, TFT_BLACK);
  lcdDrawLine(PALM_LCD_X + 80, y + 4, PALM_LCD_X + 80, y + PALM_SILKSCREEN_H - 4, TFT_BLACK);
  lcdDrawLine(PALM_LCD_X + 133, y + 4, PALM_LCD_X + 133, y + PALM_SILKSCREEN_H - 4, TFT_BLACK);
#endif
}

static void drawPalmShellOnce() {
  if (staticShellDrawn) return;
  staticShellDrawn = true;
  drawPalmBorder();
  drawPalmSilkscreen();
}

static void drawWaitingFrame() {
  if (lcdWaitingDrawn) return;
  lcdWaitingDrawn = true;

  drawPalmShellOnce();
  lcdFillRect(PALM_LCD_X, PALM_LCD_Y, PALM_LCD_W, PALM_LCD_H, TFT_WHITE);
#if PALM_BOOT_STATUS_TEXT
#endif
}

static void drawBootPattern() {
  drawPalmShellOnce();
  lcdFillRect(PALM_LCD_X, PALM_LCD_Y, PALM_LCD_W, PALM_LCD_H, TFT_WHITE);
#if PALM_BOOT_STATUS_TEXT
  lcdDrawRect(PALM_LCD_X, PALM_LCD_Y, PALM_LCD_W, PALM_LCD_H, TFT_BLACK);
  for (int y = 24; y < 136; y += 8) {
    for (int x = 16; x < 144; x += 8) {
      if (((x + y) / 8) & 1) lcdFillRect(PALM_LCD_X + x, PALM_LCD_Y + y, 4, 4, TFT_BLACK);
    }
  }
#endif
}

static bool drawPalmFrameFromEmulatedLcd() {
  PalmLcdState lcd = palmHwGetLcdState();
  if (!lcd.valid) {
    drawWaitingFrame();
    return false;
  }

  if (!lcd.dirty) return true;
  lcdWaitingDrawn = false;
  drawPalmShellOnce();

  uint16_t drawW = min<uint16_t>(PALM_LCD_W, lcd.width);
  uint16_t drawH = min<uint16_t>(PALM_LCD_H, lcd.height);
  static uint16_t line[PALM_LCD_W];

  for (uint16_t y = 0; y < drawH; ++y) {
    uint32_t srcLine = lcd.startAddr + static_cast<uint32_t>(y) * lcd.bytesPerLine;
    for (uint16_t x = 0; x < drawW; ++x) {
      uint32_t bit = x + lcd.margin;
      uint8_t value = palmRead8(srcLine + (bit >> 3));
      bool black = (value & (0x80 >> (bit & 7))) != 0;
      line[x] = black ? TFT_BLACK : TFT_WHITE;
    }
    for (uint16_t x = drawW; x < PALM_LCD_W; ++x) line[x] = TFT_WHITE;
    lcdPushImage(PALM_LCD_X, PALM_LCD_Y + y, PALM_LCD_W, 1, line);
  }

  if (drawH < PALM_LCD_H) {
    lcdFillRect(PALM_LCD_X, PALM_LCD_Y + drawH, PALM_LCD_W, PALM_LCD_H - drawH, TFT_WHITE);
  }

  palmHwMarkLcdClean();
  return true;
}

static void drawStatusText(const char *line1, const char *line2, const char *line3) {
#if PALM_BOOT_STATUS_TEXT
  lcdFillRect(0, 0, SCREEN_W, PALM_LCD_Y - 6, TFT_DARKGREY);
#else
  (void)line1;
  (void)line2;
  (void)line3;
#endif
}

static void initPins() {
  pinMode(LCD_Backlight, OUTPUT);
  pinMode(LED_R, OUTPUT);
  pinMode(LED_G, OUTPUT);
  pinMode(LED_B, OUTPUT);
  pinMode(T_CS, OUTPUT);
  pinMode(T_IRQ, INPUT);

  analogWrite(LCD_Backlight, 8);
  digitalWrite(LED_R, HIGH);
  digitalWrite(LED_G, HIGH);
  digitalWrite(LED_B, HIGH);
  digitalWrite(T_CS, HIGH);
}

static void initSerialDebug() {
#if PALM_SERIAL_STATS
  Serial.begin(115200);
  delay(250);
#endif
}

static void initTouch() {
  touchSPI.begin(T_CLK, T_DOUT, T_DIN, T_CS);
  touchSPI.setFrequency(500000);
  touchSPI.setDataMode(SPI_MODE0);
}

static void initDisplay() {
  lcdInit();
}

static void initCpu() {
#if PALM_ENABLE_MUSASHI
  m68k_init();
  m68k_set_cpu_type(M68K_CPU_TYPE_68000);
  uint32_t resetSp = palmRead32(PALM_ROM_BASE);
  m68k_pulse_reset();
  m68k_set_reg(M68K_REG_SR, 0x2700);
  m68k_set_reg(M68K_REG_SP, resetSp);
  m68k_set_reg(M68K_REG_ISP, resetSp);
  m68k_set_reg(M68K_REG_USP, resetSp);
  m68k_set_reg(M68K_REG_PC, palmRead32(PALM_ROM_BASE + 4));
#endif
}

void setup() {
  initPins();
  initSerialDebug();
#if PALM_SERIAL_STATS
  Serial.printf("Heap before Palm RAM: free=%lu max=%lu\n",
                (unsigned long)ESP.getFreeHeap(),
                (unsigned long)ESP.getMaxAllocHeap());
#if defined(ESP32)
  Serial.printf("8-bit heap before Palm RAM: free=%lu max=%lu\n",
                (unsigned long)heap_caps_get_free_size(MALLOC_CAP_8BIT),
                (unsigned long)heap_caps_get_largest_free_block(MALLOC_CAP_8BIT));
#endif
#endif

  bool memoryOk = palmMemoryInit();

#if PALM_SERIAL_STATS
  Serial.println();
  Serial.println("ESP32-PALM bring-up");
  Serial.printf("ROM base: 0x%08lx, ROM bytes: %lu\n", (unsigned long)PALM_ROM_BASE, (unsigned long)palmRomSize());
  Serial.printf("Initial SP: 0x%08lx\n", (unsigned long)palmRead32(PALM_ROM_BASE));
  Serial.printf("Initial PC: 0x%08lx\n", (unsigned long)palmRead32(PALM_ROM_BASE + 4));
  Serial.printf("Palm RAM logical: %lu bytes, alloc target: %lu bytes, allocated: %lu bytes %s\n",
                (unsigned long)PALM_RAM_LOGICAL_SIZE,
                (unsigned long)PALM_RAM_ALLOC_TARGET_SIZE,
                (unsigned long)(memoryOk ? palmRamSize() : palmRamLastAllocAttemptSize()),
                memoryOk ? "OK" : "FAILED");
  Serial.printf("Palm RAM segments: %lu\n",
                (unsigned long)palmRamLastAllocAttemptSegments());
  Serial.printf("Heap after Palm RAM: free=%lu max=%lu\n",
                (unsigned long)ESP.getFreeHeap(),
                (unsigned long)ESP.getMaxAllocHeap());
#endif

  initTouch();
#if PALM_SERIAL_STATS
  Serial.println("Touch initialized");
#endif
  initDisplay();
#if PALM_SERIAL_STATS
  Serial.println("LCD initialized");
#endif
  drawBootPattern();
#if PALM_SERIAL_STATS
  Serial.println("Boot pattern drawn");
#endif
#if PALM_BOOT_STATUS_TEXT
  char romLine[48];
  char pcLine[48];
  snprintf(romLine, sizeof(romLine), "ROM %lu KB RAM %lu/%lu KB",
           (unsigned long)(palmRomSize() / 1024),
           (unsigned long)(palmRamSize() / 1024),
           (unsigned long)(PALM_RAM_LOGICAL_SIZE / 1024));
  snprintf(pcLine, sizeof(pcLine), "SP %08lX PC %08lX",
           (unsigned long)palmRead32(PALM_ROM_BASE), (unsigned long)palmRead32(PALM_ROM_BASE + 4));
  drawStatusText(PALM_ENABLE_MUSASHI ? "Musashi enabled" : "Bring-up scaffold",
                 romLine,
                 memoryOk ? pcLine : "RAM allocation failed");
#endif

  if (memoryOk) {
#if PALM_SERIAL_STATS
    Serial.println("CPU init starting");
#endif
    initCpu();
    cpuReady = true;
#if PALM_SERIAL_STATS
    Serial.println("CPU init complete");
#endif
  }
}

void loop() {
#if PALM_TOUCH_USE_CYD_RAW_ADC
  uint16_t palmAdcX;
  uint16_t palmAdcY;
  bool touchDown = getPalmAdcTouch(palmAdcX, palmAdcY);
  if (wakeTouchActive || (touchDown && palmHwIsAsleep())) {
    palmHwSetPenRaw(false, 0, 0);
    palmHwSetPowerButton(touchDown);
    wakeTouchActive = touchDown;
  } else if (touchDown) {
    palmHwSetPenRaw(true, palmAdcX, palmAdcY);
  } else {
    palmHwSetPenRaw(false, 0, 0);
    palmHwSetPowerButton(false);
  }
#else
  int palmX;
  int palmY;
  bool touchDown = getPalmTouch(palmX, palmY);
  if (wakeTouchActive || (touchDown && palmHwIsAsleep())) {
    palmHwSetPen(false, 0, 0);
    palmHwSetPowerButton(touchDown);
    wakeTouchActive = touchDown;
  } else if (touchDown) {
    palmHwSetPen(true, palmX, palmY);
  } else {
    palmHwSetPen(false, 0, 0);
    palmHwSetPowerButton(false);
  }
#endif

#if PALM_ENABLE_MUSASHI
  if (cpuReady) {
    unsigned long beatNow = millis();
    if (beatNow - lastLoopBeatMs >= 1000) {
      lastLoopBeatMs = beatNow;
#if PALM_SERIAL_STATS
      Serial.printf("Loop alive PC=%08x slices=%lu\n",
                    m68k_get_reg(nullptr, M68K_REG_PC),
                    (unsigned long)cpuSlices);
#endif
    }
    palmHwCycle();
    uint8_t irqLevel = palmHwGetInterruptLevel();
    m68k_set_irq(irqLevel);
    if (!palmHwIsAsleep() || irqLevel > 0 || palmHwHasWakeSource()) {
      m68k_execute(PALM_CPU_SLICE_CYCLES);
    }
    palmHwCycle();
    m68k_set_irq(palmHwGetInterruptLevel());
    ++cpuSlices;
  }
#endif

  unsigned long now = millis();
  PalmLcdState currentLcd = palmHwGetLcdState();
  if (now - lastFrameMs >= PALM_LCD_REDRAW_INTERVAL_MS &&
      (!currentLcd.valid || currentLcd.dirty)) {
    lastFrameMs = now;
    drawPalmFrameFromEmulatedLcd();
  }

#if PALM_SERIAL_STATS
  if (now - lastStatsMs >= 1000) {
    lastStatsMs = now;
#if PALM_ENABLE_MUSASHI
    if (!cpuReady) {
      Serial.printf("CPU stopped, Palm RAM allocation failed. attempted=%lu seg=%lu free=%lu max=%lu\n",
                    (unsigned long)palmRamLastAllocAttemptSize(),
                    (unsigned long)palmRamLastAllocAttemptSegments(),
                    (unsigned long)ESP.getFreeHeap(),
                    (unsigned long)ESP.getMaxAllocHeap());
#if defined(ESP32)
      Serial.printf("8-bit heap now: free=%lu max=%lu\n",
                    (unsigned long)heap_caps_get_free_size(MALLOC_CAP_8BIT),
                    (unsigned long)heap_caps_get_largest_free_block(MALLOC_CAP_8BIT));
#endif
      return;
    }

    PalmLcdState lcd = palmHwGetLcdState();
    PalmHwDebug hw = palmHwGetDebug();
    PalmMemoryDebug mem = palmMemoryGetDebug();
    Serial.printf("PC=%08x slices=%lu ram=%luK heap=%lu/%lu sleep=%u pll=%04x/%04x lcdReg=%08lx,%02x,%04x,%04x,%02x LCD=%s base=%08lx %ux%u %ubpp pitch=%u touch=%s/%u %d,%d raw=%u,%u hw=%lu/%lu r$%03x w$%03x:%02x lcdWr=%lu lastLcd=$%03x:%02x bus=%lu@%08lx ibus=%lu@%08lx mirror=%lu/%lu@%08lx sparse=%lu/%lu@%08lx unmR=%lu@%08lx unmW=%lu@%08lx:%02lx\n",
                  m68k_get_reg(nullptr, M68K_REG_PC),
                  (unsigned long)cpuSlices,
                  (unsigned long)(palmRamSize() / 1024),
                  (unsigned long)ESP.getFreeHeap(),
                  (unsigned long)ESP.getMaxAllocHeap(),
                  palmHwIsAsleep() ? 1 : 0,
                  palmRead16(PALM_DB_REG_BASE + 0x200),
                  palmRead16(PALM_DB_REG_BASE + 0x202),
                  (unsigned long)palmRead32(PALM_DB_REG_BASE + 0xA00),
                  palmRead8(PALM_DB_REG_BASE + 0xA05),
                  palmRead16(PALM_DB_REG_BASE + 0xA08),
                  palmRead16(PALM_DB_REG_BASE + 0xA0A),
                  palmRead8(PALM_DB_REG_BASE + 0xA20),
                  lcd.valid ? "on" : "off",
                  (unsigned long)lcd.startAddr,
                  lcd.width,
                  lcd.height,
                  lcd.bpp,
                  lcd.bytesPerLine,
                  PALM_TOUCH_USE_CYD_RAW_ADC ? "cyd-adc" : "mapped",
                  lastTouchDown ? 1 : 0,
                  lastTouchScreenX,
                  lastTouchScreenY,
                  lastTouchAdcX,
                  lastTouchAdcY,
                  (unsigned long)hw.regReadCount,
                  (unsigned long)hw.regWriteCount,
                  hw.lastRegReadOffset,
                  hw.lastRegWriteOffset,
                  hw.lastRegWriteValue,
                  (unsigned long)hw.lcdWriteCount,
                  hw.lastLcdWriteOffset,
                  hw.lastLcdWriteValue,
                  (unsigned long)mem.busErrorCount,
                  (unsigned long)mem.lastBusError,
                  (unsigned long)mem.instrBusErrorCount,
                  (unsigned long)mem.lastInstrBusError,
                  (unsigned long)mem.mirrorReadCount,
                  (unsigned long)mem.mirrorWriteCount,
                  (unsigned long)(mem.lastMirrorWrite ? mem.lastMirrorWrite : mem.lastMirrorRead),
                  (unsigned long)mem.sparseReadCount,
                  (unsigned long)mem.sparseWriteCount,
                  (unsigned long)(mem.lastSparseWrite ? mem.lastSparseWrite : mem.lastSparseRead),
                  (unsigned long)mem.unmappedReadCount,
                  (unsigned long)mem.lastUnmappedRead,
                  (unsigned long)mem.unmappedWriteCount,
                  (unsigned long)mem.lastUnmappedWrite,
                  (unsigned long)mem.lastUnmappedWriteValue);
#else
    Serial.printf("Touch framebuffer alive, free heap=%lu\n", (unsigned long)ESP.getFreeHeap());
#endif
  }
#endif
}
