#include <Arduino.h>
#if defined(ESP32)
#include <esp_heap_caps.h>
#endif

#include "palm_config.h"
#include "palm_hw.h"
#include "palm_memory.h"

#include <Arduino_GFX_Library.h>
#include <TAMC_GT911.h>

#if PALM_ENABLE_MUSASHI
extern "C" {
#include "Musashi-master/m68k.h"
}
extern "C" void m68k_pulse_reset(void);
#endif

#define TFT_BL        2
#define GT911_SCL     20
#define GT911_SDA     19
#define GT911_INT     -1
#define GT911_RST     38

#define TFT_BLACK     0x0000
#define TFT_WHITE     0xffff
#define TFT_DARKGREY  0x7bef
#define TFT_DARKGREEN 0x7d27

static Arduino_ESP32RGBPanel *rgbBus = new Arduino_ESP32RGBPanel(
  40, 41, 39, 42,
  45, 48, 47, 21, 14,
  5, 6, 7, 15, 16, 4,
  8, 3, 46, 9, 1,
  0, 8, 4, 43,
  0, 8, 4, 12,
  1, 9000000, true
);

static Arduino_RGB_Display *gfx = new Arduino_RGB_Display(480, 272, rgbBus, 0, true);
static TAMC_GT911 gt911(GT911_SDA, GT911_SCL, GT911_INT, GT911_RST, 320, 240);

static constexpr int SCREEN_W = 480;
static constexpr int SCREEN_H = 272;
static constexpr int PALM_SURFACE_W = PALM_DIGITIZER_W;
static constexpr int PALM_SURFACE_H = PALM_DIGITIZER_H;
static constexpr int PALM_VIEW_W = (SCREEN_H * PALM_SURFACE_H) / PALM_SURFACE_W;
static constexpr int PALM_VIEW_H = SCREEN_H;
static constexpr int PALM_VIEW_X = (SCREEN_W - PALM_VIEW_W) / 2;
static constexpr int PALM_VIEW_Y = 0;

static unsigned long lastFrameMs = 0;
static unsigned long lastStatsMs = 0;
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
static int lastTouchRawX = -1;
static int lastTouchRawY = -1;
static int lastTouchPalmX = -1;
static int lastTouchPalmY = -1;
static unsigned long lastTouchPollMs = 0;
static unsigned long lastControllerTouchMs = 0;
static bool cachedTouchDown = false;
static int cachedTouchScreenX = -1;
static int cachedTouchScreenY = -1;
static int cachedTouchPalmX = -1;
static int cachedTouchPalmY = -1;
static bool nativePenDown = false;
static uint16_t nativePenAdcX = 0xffff;
static uint16_t nativePenAdcY = 0xffff;
static int nativePenX = -1;
static int nativePenY = -1;
static constexpr int PEN_MOVE_ADC_THRESHOLD = 32;
static bool touchCandidateActive = false;
static unsigned long touchCandidateMs = 0;
static int touchCandidatePalmX = -1;
static int touchCandidatePalmY = -1;
static constexpr unsigned long TOUCH_STABLE_DOWN_MS = 0;
static constexpr int TOUCH_STABLE_PALM_TOLERANCE = 255;

// GT911 coordinates after setRotation(1) are board-specific and do not match
// the advertised 320x240 logical size. These anchors are measured at Palm OS'
// calibration targets on the rendered 160x160 LCD. Match the VB harness' raw
// ADS path: convert the capacitive point to Palm digitizer coordinates first,
// then derive the ADS raw values from those coordinates.
static constexpr int TOUCH_RAW_LCD_LEFT = 393;    // Palm LCD x = 0
static constexpr int TOUCH_RAW_LCD_CENTER_X = 306;
static constexpr int TOUCH_RAW_LCD_RIGHT = 157;   // Palm LCD x = 159
static constexpr int TOUCH_RAW_LCD_TOP = 26;      // Palm LCD y = 0
static constexpr int TOUCH_RAW_LCD_CENTER_Y = 130;
static constexpr int TOUCH_RAW_LCD_BOTTOM = 247;  // Palm LCD y = 159
static constexpr int TOUCH_ADC_HIGH = 3595;
static constexpr int TOUCH_ADC_LOW = 500;

static uint16_t to565(uint16_t color) {
  return color;
}

static void lcdFillRect(int16_t x, int16_t y, int16_t w, int16_t h, uint16_t color) {
  if (w <= 0 || h <= 0) return;
  gfx->fillRect(x, y, w, h, to565(color));
}

static void lcdDrawFastHLine(int16_t x, int16_t y, int16_t w, uint16_t color) {
  if (w <= 0) return;
  gfx->drawFastHLine(x, y, w, to565(color));
}

static void lcdDrawFastVLine(int16_t x, int16_t y, int16_t h, uint16_t color) {
  if (h <= 0) return;
  gfx->drawFastVLine(x, y, h, to565(color));
}

static void lcdDrawRect(int16_t x, int16_t y, int16_t w, int16_t h, uint16_t color) {
  if (w <= 0 || h <= 0) return;
  gfx->drawRect(x, y, w, h, to565(color));
}

static void lcdDrawLine(int16_t x0, int16_t y0, int16_t x1, int16_t y1, uint16_t color) {
  gfx->drawLine(x0, y0, x1, y1, to565(color));
}

static void lcdPushImage(int16_t x, int16_t y, int16_t w, int16_t h, const uint16_t *pixels) {
  if (w <= 0 || h <= 0) return;
  gfx->draw16bitRGBBitmap(x, y, const_cast<uint16_t *>(pixels), w, h);
}

static uint16_t palmSurface[PALM_SURFACE_W * PALM_SURFACE_H];

static void surfaceFill(uint16_t color) {
  for (uint32_t i = 0; i < static_cast<uint32_t>(PALM_SURFACE_W) * PALM_SURFACE_H; ++i) {
    palmSurface[i] = color;
  }
}

static void surfaceSetPixel(int x, int y, uint16_t color) {
  if (x < 0 || y < 0 || x >= PALM_SURFACE_W || y >= PALM_SURFACE_H) return;
  palmSurface[static_cast<uint32_t>(y) * PALM_SURFACE_W + x] = color;
}

static void surfaceFillRect(int x, int y, int w, int h, uint16_t color) {
  if (w <= 0 || h <= 0) return;
  int x2 = x + w;
  int y2 = y + h;
  if (x < 0) x = 0;
  if (y < 0) y = 0;
  if (x2 > PALM_SURFACE_W) x2 = PALM_SURFACE_W;
  if (y2 > PALM_SURFACE_H) y2 = PALM_SURFACE_H;
  for (int py = y; py < y2; ++py) {
    uint16_t *row = palmSurface + static_cast<uint32_t>(py) * PALM_SURFACE_W;
    for (int px = x; px < x2; ++px) row[px] = color;
  }
}

static void surfaceDrawLine(int x0, int y0, int x1, int y1, uint16_t color) {
  if (x0 == x1) {
    if (y1 < y0) {
      int t = y0; y0 = y1; y1 = t;
    }
    surfaceFillRect(x0, y0, 1, y1 - y0 + 1, color);
  } else if (y0 == y1) {
    if (x1 < x0) {
      int t = x0; x0 = x1; x1 = t;
    }
    surfaceFillRect(x0, y0, x1 - x0 + 1, 1, color);
  }
}

static void surfaceDrawRect(int x, int y, int w, int h, uint16_t color) {
  surfaceDrawLine(x, y, x + w - 1, y, color);
  surfaceDrawLine(x, y + h - 1, x + w - 1, y + h - 1, color);
  surfaceDrawLine(x, y, x, y + h - 1, color);
  surfaceDrawLine(x + w - 1, y, x + w - 1, y + h - 1, color);
}

static void renderPalmSurfaceToPanel() {
  static uint16_t line[PALM_VIEW_W];
  for (int dy = 0; dy < PALM_VIEW_H; ++dy) {
    int rotY = (static_cast<int32_t>(dy) * PALM_SURFACE_W) / PALM_VIEW_H;
    if (rotY >= PALM_SURFACE_W) rotY = PALM_SURFACE_W - 1;
    for (int dx = 0; dx < PALM_VIEW_W; ++dx) {
      int rotX = (static_cast<int32_t>(dx) * PALM_SURFACE_H) / PALM_VIEW_W;
      if (rotX >= PALM_SURFACE_H) rotX = PALM_SURFACE_H - 1;
      int srcX = rotY;
      int srcY = PALM_SURFACE_H - 1 - rotX;
      line[dx] = palmSurface[static_cast<uint32_t>(srcY) * PALM_SURFACE_W + srcX];
    }
    lcdPushImage(PALM_VIEW_X, PALM_VIEW_Y + dy, PALM_VIEW_W, 1, line);
  }
}

static int mapClamped(int value, int inMin, int inMax, int outMin, int outMax) {
  long mapped = map(value, inMin, inMax, outMin, outMax);
  if (outMin < outMax) {
    return constrain(mapped, outMin, outMax);
  }
  return constrain(mapped, outMax, outMin);
}

static int mapPiecewiseClamped(int value, int inA, int inB, int inC,
                               int outA, int outB, int outC) {
  if (inA <= inC) {
    if (value <= inB) return mapClamped(value, inA, inB, outA, outB);
    return mapClamped(value, inB, inC, outB, outC);
  }
  if (value >= inB) return mapClamped(value, inA, inB, outA, outB);
  return mapClamped(value, inB, inC, outB, outC);
}

static int palmToAdc(int value, int maxValue) {
  return constrain(map(value, 0, maxValue, TOUCH_ADC_HIGH, TOUCH_ADC_LOW), 0, 4095);
}

static void rawTouchToPalm(int rawX, int rawY, int &palmX, int &palmY) {
  // The rotated GT911 space is slightly nonlinear on this panel. Fit the Palm
  // calibration targets directly so OS calibration receives stable points.
  palmX = mapPiecewiseClamped(rawX,
                              TOUCH_RAW_LCD_LEFT, TOUCH_RAW_LCD_CENTER_X, TOUCH_RAW_LCD_RIGHT,
                              0, PALM_LCD_W / 2, PALM_LCD_W - 1);
  palmY = mapPiecewiseClamped(rawY,
                              TOUCH_RAW_LCD_TOP, TOUCH_RAW_LCD_CENTER_Y, TOUCH_RAW_LCD_BOTTOM,
                              0, PALM_LCD_H / 2, PALM_LCD_H - 1);
}

static void palmTouchToScreen(int palmX, int palmY, int &screenX, int &screenY) {
  int rotX = PALM_SURFACE_H - 1 - palmY;
  int rotY = palmX;
  screenX = PALM_VIEW_X + (static_cast<int32_t>(rotX) * PALM_VIEW_W) / PALM_SURFACE_H;
  screenY = PALM_VIEW_Y + (static_cast<int32_t>(rotY) * PALM_VIEW_H) / PALM_SURFACE_W;
  screenX = constrain(screenX, PALM_VIEW_X, PALM_VIEW_X + PALM_VIEW_W - 1);
  screenY = constrain(screenY, PALM_VIEW_Y, PALM_VIEW_Y + PALM_VIEW_H - 1);
}

static void lcdInit() {
  gfx->begin();
  gfx->fillScreen(TFT_BLACK);
#if TFT_BL >= 0
  pinMode(TFT_BL, OUTPUT);
  digitalWrite(TFT_BL, HIGH);
#endif
}

static bool getPalmScreenTouch(int &screenX, int &screenY) {
  unsigned long now = millis();
  if (now - lastTouchPollMs >= PALM_TOUCH_POLL_INTERVAL_MS) {
    lastTouchPollMs = now;
    gt911.read();
    bool controllerDown = gt911.isTouched && gt911.touches > 0;
    if (controllerDown) {
      int rawX = gt911.points[0].x;
      int rawY = gt911.points[0].y;
      int palmX;
      int palmY;
      rawTouchToPalm(rawX, rawY, palmX, palmY);
      lastTouchRawX = rawX;
      lastTouchRawY = rawY;
      if (!cachedTouchDown) {
        if (!touchCandidateActive ||
            abs(palmX - touchCandidatePalmX) > TOUCH_STABLE_PALM_TOLERANCE ||
            abs(palmY - touchCandidatePalmY) > TOUCH_STABLE_PALM_TOLERANCE) {
          touchCandidateActive = true;
          touchCandidateMs = now;
          touchCandidatePalmX = palmX;
          touchCandidatePalmY = palmY;
          return false;
        }
        if (now - touchCandidateMs < TOUCH_STABLE_DOWN_MS) return false;
      }
      cachedTouchPalmX = palmX;
      cachedTouchPalmY = palmY;
      palmTouchToScreen(cachedTouchPalmX, cachedTouchPalmY, cachedTouchScreenX, cachedTouchScreenY);
      cachedTouchDown = true;
      lastControllerTouchMs = now;
    } else {
      cachedTouchDown = cachedTouchDown && ((now - lastControllerTouchMs) < PALM_TOUCH_RELEASE_DEBOUNCE_MS);
      if (!cachedTouchDown) touchCandidateActive = false;
    }
  }

  if (!cachedTouchDown) {
    lastTouchDown = false;
    return false;
  }

  screenX = cachedTouchScreenX;
  screenY = cachedTouchScreenY;
  lastTouchDown = true;
  lastTouchScreenX = screenX;
  lastTouchScreenY = screenY;
  return screenX >= PALM_VIEW_X && screenX < PALM_VIEW_X + PALM_VIEW_W &&
         screenY >= PALM_VIEW_Y && screenY < PALM_VIEW_Y + PALM_VIEW_H;
}

static bool getPalmTouch(int &palmX, int &palmY) {
  int screenX;
  int screenY;
  if (!getPalmScreenTouch(screenX, screenY)) return false;
  palmX = cachedTouchPalmX;
  palmY = cachedTouchPalmY;
  lastTouchPalmX = palmX;
  lastTouchPalmY = palmY;
  lastTouchAdcX = palmToAdc(palmX, PALM_DIGITIZER_W - 1);
  lastTouchAdcY = palmToAdc(palmY, PALM_DIGITIZER_H - 1);
  return true;
}

static bool getPalmAdcTouch(uint16_t &adcX, uint16_t &adcY) {
  int screenX;
  int screenY;
  if (!getPalmScreenTouch(screenX, screenY)) return false;
  int palmX = cachedTouchPalmX;
  int palmY = cachedTouchPalmY;
  lastTouchPalmX = palmX;
  lastTouchPalmY = palmY;
  // ADS reports the full 160x220 digitizer, even when Palm OS calibration
  // targets are inside the 160x160 LCD region.
  adcX = palmToAdc(palmX, PALM_DIGITIZER_W - 1);
  adcY = palmToAdc(palmY, PALM_DIGITIZER_H - 1);
  lastTouchAdcX = adcX;
  lastTouchAdcY = adcY;
  return true;
}

static void drawPalmBorder() {
  surfaceFill(TFT_BLACK);
}

static void drawPalmSilkscreen() {
#if PALM_DRAW_STATIC_SHELL
  int y = PALM_LCD_H;
  surfaceFillRect(0, y, PALM_LCD_W, PALM_SILKSCREEN_H, TFT_DARKGREEN);
  surfaceDrawRect(0, y, PALM_LCD_W, PALM_SILKSCREEN_H, TFT_BLACK);
  surfaceDrawLine(27, y + 4, 27, y + PALM_SILKSCREEN_H - 4, TFT_BLACK);
  surfaceDrawLine(80, y + 4, 80, y + PALM_SILKSCREEN_H - 4, TFT_BLACK);
  surfaceDrawLine(133, y + 4, 133, y + PALM_SILKSCREEN_H - 4, TFT_BLACK);
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
  surfaceFillRect(0, 0, PALM_LCD_W, PALM_LCD_H, TFT_WHITE);
  renderPalmSurfaceToPanel();
#if PALM_BOOT_STATUS_TEXT
#endif
}

static void drawBootPattern() {
  drawPalmShellOnce();
  surfaceFillRect(0, 0, PALM_LCD_W, PALM_LCD_H, TFT_WHITE);
#if PALM_BOOT_STATUS_TEXT
  surfaceDrawRect(0, 0, PALM_LCD_W, PALM_LCD_H, TFT_BLACK);
  for (int y = 24; y < 136; y += 8) {
    for (int x = 16; x < 144; x += 8) {
      if (((x + y) / 8) & 1) surfaceFillRect(x, y, 4, 4, TFT_BLACK);
    }
  }
#endif
  renderPalmSurfaceToPanel();
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
  surfaceFillRect(0, 0, PALM_LCD_W, PALM_LCD_H, TFT_WHITE);

  for (uint16_t y = 0; y < drawH; ++y) {
    uint32_t srcLine = lcd.startAddr + static_cast<uint32_t>(y) * lcd.bytesPerLine;
    for (uint16_t x = 0; x < drawW; ++x) {
      uint32_t bit = x + lcd.margin;
      uint8_t value = palmRead8(srcLine + (bit >> 3));
      bool black = (value & (0x80 >> (bit & 7))) != 0;
      surfaceSetPixel(x, y, black ? TFT_BLACK : TFT_WHITE);
    }
  }

  renderPalmSurfaceToPanel();
  palmHwMarkLcdClean();
  return true;
}

static void drawStatusText(const char *line1, const char *line2, const char *line3) {
#if PALM_BOOT_STATUS_TEXT
  lcdFillRect(0, 0, SCREEN_W, 16, TFT_DARKGREY);
#else
  (void)line1;
  (void)line2;
  (void)line3;
#endif
}

static void initPins() {
#if TFT_BL >= 0
  pinMode(TFT_BL, OUTPUT);
  digitalWrite(TFT_BL, HIGH);
#endif
}

static void initSerialDebug() {
#if PALM_BOOT_SERIAL_STATS || PALM_RUNTIME_SERIAL_STATS
  Serial.begin(115200);
  delay(250);
#endif
}

static void initTouch() {
  gt911.begin();
  gt911.setRotation(1);
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
#if PALM_BOOT_SERIAL_STATS
  Serial.printf("Heap before Palm RAM: free=%lu max=%lu\n",
                (unsigned long)ESP.getFreeHeap(),
                (unsigned long)ESP.getMaxAllocHeap());
#if defined(ESP32)
  Serial.printf("8-bit heap before Palm RAM: free=%lu max=%lu\n",
                (unsigned long)heap_caps_get_free_size(MALLOC_CAP_8BIT),
                (unsigned long)heap_caps_get_largest_free_block(MALLOC_CAP_8BIT));
#if defined(MALLOC_CAP_SPIRAM)
  Serial.printf("PSRAM before Palm RAM: free=%lu max=%lu\n",
                (unsigned long)heap_caps_get_free_size(MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT),
                (unsigned long)heap_caps_get_largest_free_block(MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT));
#endif
#endif
#endif

  bool memoryOk = palmMemoryInit();

#if PALM_BOOT_SERIAL_STATS
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
#if defined(ESP32) && defined(MALLOC_CAP_SPIRAM)
  Serial.printf("PSRAM after Palm RAM: free=%lu max=%lu\n",
                (unsigned long)heap_caps_get_free_size(MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT),
                (unsigned long)heap_caps_get_largest_free_block(MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT));
#endif
#endif

  initTouch();
#if PALM_BOOT_SERIAL_STATS
  Serial.println("Touch initialized");
#endif
  initDisplay();
#if PALM_BOOT_SERIAL_STATS
  Serial.println("LCD initialized");
#endif
  drawBootPattern();
#if PALM_BOOT_SERIAL_STATS
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
#if PALM_BOOT_SERIAL_STATS
    Serial.println("CPU init starting");
#endif
    initCpu();
    cpuReady = true;
#if PALM_BOOT_SERIAL_STATS
    Serial.println("CPU init complete");
#endif
  }
}

void loop() {
#if PALM_TOUCH_USE_RAW_ADC
  uint16_t palmAdcX;
  uint16_t palmAdcY;
  bool touchDown = getPalmAdcTouch(palmAdcX, palmAdcY);
  if (wakeTouchActive || (touchDown && palmHwIsAsleep())) {
    if (nativePenDown) {
      palmHwSetPenRaw(false, 0, 0);
      nativePenDown = false;
    }
    palmHwSetPowerButton(touchDown);
    wakeTouchActive = touchDown;
  } else if (touchDown) {
    int dx = static_cast<int>(palmAdcX) - static_cast<int>(nativePenAdcX);
    int dy = static_cast<int>(palmAdcY) - static_cast<int>(nativePenAdcY);
    if (!nativePenDown || abs(dx) >= PEN_MOVE_ADC_THRESHOLD || abs(dy) >= PEN_MOVE_ADC_THRESHOLD) {
      palmHwSetPenRaw(true, palmAdcX, palmAdcY);
      nativePenDown = true;
      nativePenAdcX = palmAdcX;
      nativePenAdcY = palmAdcY;
    }
  } else if (nativePenDown) {
    palmHwSetPenRaw(false, 0, 0);
    nativePenDown = false;
    nativePenAdcX = 0xffff;
    nativePenAdcY = 0xffff;
    palmHwSetPowerButton(false);
  } else {
    palmHwSetPowerButton(false);
  }
#else
  int palmX;
  int palmY;
  bool touchDown = getPalmTouch(palmX, palmY);
  if (wakeTouchActive || (touchDown && palmHwIsAsleep())) {
    if (nativePenDown) {
      palmHwSetPen(false, 0, 0);
      nativePenDown = false;
    }
    palmHwSetPowerButton(touchDown);
    wakeTouchActive = touchDown;
  } else if (touchDown) {
    if (!nativePenDown || nativePenX != palmX || nativePenY != palmY) {
      palmHwSetPen(true, palmX, palmY);
      nativePenDown = true;
      nativePenX = palmX;
      nativePenY = palmY;
    }
  } else if (nativePenDown) {
    palmHwSetPen(false, 0, 0);
    nativePenDown = false;
    nativePenX = -1;
    nativePenY = -1;
    palmHwSetPowerButton(false);
  } else {
    palmHwSetPowerButton(false);
  }
#endif

#if PALM_ENABLE_MUSASHI
  if (cpuReady) {
    unsigned long cpuStartMs = millis();
    do {
      palmHwAdvanceCycles(PALM_CPU_SLICE_CYCLES);
      palmHwCycle();
      uint8_t irqLevel = palmHwGetInterruptLevel();
      m68k_set_irq(irqLevel);
      if (!palmHwIsAsleep() || irqLevel > 0 || palmHwHasWakeSource()) {
        m68k_execute(PALM_CPU_SLICE_CYCLES);
      } else {
        break;
      }
      palmHwCycle();
      m68k_set_irq(palmHwGetInterruptLevel());
      ++cpuSlices;
      if ((cpuSlices & 0x0f) == 0) yield();
    } while (millis() - cpuStartMs < PALM_CPU_BURST_MS);
  }
#endif

  unsigned long now = millis();
  PalmLcdState currentLcd = palmHwGetLcdState();
  if (now - lastFrameMs >= PALM_LCD_REDRAW_INTERVAL_MS &&
      (!currentLcd.valid || currentLcd.dirty)) {
    lastFrameMs = now;
    drawPalmFrameFromEmulatedLcd();
  }

#if PALM_RUNTIME_SERIAL_STATS
  if (now - lastStatsMs >= 1000) {
    lastStatsMs = now;
#if PALM_ENABLE_MUSASHI
    if (!cpuReady) {
      Serial.printf("CPU stopped, Palm RAM allocation failed. attempted=%lu seg=%lu free=%lu\n",
                    (unsigned long)palmRamLastAllocAttemptSize(),
                    (unsigned long)palmRamLastAllocAttemptSegments(),
                    (unsigned long)ESP.getFreeHeap());
#if defined(ESP32)
      Serial.printf("8-bit heap now: free=%lu\n",
                    (unsigned long)heap_caps_get_free_size(MALLOC_CAP_8BIT));
#endif
      return;
    }

    PalmLcdState lcd = palmHwGetLcdState();
    uint8_t irq = palmHwGetInterruptLevel();
    Serial.printf("PC=%08x irq=%u slices=%lu t=%04x/%04x/%04x im=%04x/%04x is=%04x/%04x lcd=%u/%u/%u touch=%u raw=%d,%d scr=%d,%d palm=%d,%d adc=%u,%u\n",
                  m68k_get_reg(nullptr, M68K_REG_PC),
                  irq,
                  (unsigned long)cpuSlices,
                  palmRead16(PALM_DB_REG_BASE + 0x600),
                  palmRead16(PALM_DB_REG_BASE + 0x604),
                  palmRead16(PALM_DB_REG_BASE + 0x60A),
                  palmRead16(PALM_DB_REG_BASE + 0x304),
                  palmRead16(PALM_DB_REG_BASE + 0x306),
                  palmRead16(PALM_DB_REG_BASE + 0x30C),
                  palmRead16(PALM_DB_REG_BASE + 0x30E),
                  lcd.valid ? 1 : 0,
                  lcd.dirty ? 1 : 0,
                  lcd.frameReady ? 1 : 0,
                  lastTouchDown ? 1 : 0,
                  lastTouchRawX,
                  lastTouchRawY,
                  lastTouchScreenX,
                  lastTouchScreenY,
                  lastTouchPalmX,
                  lastTouchPalmY,
                  lastTouchAdcX,
                  lastTouchAdcY);
#else
    Serial.printf("Touch framebuffer alive, free heap=%lu\n", (unsigned long)ESP.getFreeHeap());
#endif
  }
#endif

  yield();
}
