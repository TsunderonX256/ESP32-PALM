#include <Arduino.h>
#if defined(ESP32)
#include <esp_heap_caps.h>
#include <freertos/FreeRTOS.h>
#include <freertos/semphr.h>
#include <freertos/task.h>
#include <esp_lcd_panel_rgb.h>
#include <esp_lcd_panel_ops.h>
#include <esp_sleep.h>
#endif

#include "palm_config.h"
#include "palm_hw.h"
#include "palm_memory.h"
#include "silkscreen_asset.h"

#include <SPI.h>
#include <SD.h>
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
#define SD_CS         10
#define SD_MOSI       11
#define SD_SCK        12
#define SD_MISO       13

#define TFT_BLACK     0x0000
#define TFT_WHITE     0xffff
#define TFT_DARKGREY  0x7bef
#define TFT_MIDGREY   0x8410
#define TFT_DARKGREEN 0x7d27
#define TFT_BUTTON_BITMAP_0 0x8631  // byte-swapped RGB565 for RGB(48,48,48)
#define TFT_BUTTON_BITMAP_1 0x0C63  // byte-swapped RGB565 for RGB(96,96,96)

static constexpr uint32_t BACKLIGHT_PWM_HZ = 5000;
static constexpr uint8_t BACKLIGHT_PWM_BITS = 8;
static constexpr uint8_t BACKLIGHT_DEFAULT_DUTY = 128;
static constexpr uint8_t BACKLIGHT_PALM_MIN_DUTY = 8;
static constexpr uint8_t BACKLIGHT_PALM_MAX_DUTY = BACKLIGHT_DEFAULT_DUTY;
static constexpr uint8_t BACKLIGHT_SAVE_DUTY = 13;
static constexpr unsigned long PALM_STATE_SAVE_HOLD_MS = 1000;
static constexpr unsigned long PALM_OS_RESET_HOLD_MS = 1000;
static constexpr int PALM_STATE_SAVE_SLEEP_PULSE_SLICES = 220;
static constexpr int PALM_STATE_SAVE_WAKE_PULSE_SLICES = 220;
static constexpr unsigned long PALM_STATE_SAVE_SLEEP_SETTLE_MS = 1000;
static constexpr unsigned long PALM_STATE_SAVE_SLEEP_TIMEOUT_MS = 4000;
static constexpr uint32_t ESP32_ACTIVE_CPU_MHZ = 240;
static constexpr uint32_t ESP32_PALM_SLEEP_CPU_MHZ = 80;
static constexpr int32_t RGB_PANEL_PIXEL_CLOCK_HZ = 8000000;
static constexpr bool RGB_PANEL_PCLK_ACTIVE_NEG = false;
static constexpr int RGB_PANEL_HSYNC_FRONT_PORCH = 8;
static constexpr int RGB_PANEL_HSYNC_PULSE_WIDTH = 4;
static constexpr int RGB_PANEL_HSYNC_BACK_PORCH = 43;
static constexpr int RGB_PANEL_VSYNC_FRONT_PORCH = 8;
static constexpr int RGB_PANEL_VSYNC_PULSE_WIDTH = 4;
static constexpr int RGB_PANEL_VSYNC_BACK_PORCH = 12;
static constexpr bool RGB_PANEL_PCLK_IDLE_HIGH = true;
static constexpr bool PALM_DISABLE_DYNAMIC_LCD_RENDER_TEST = false;
static constexpr bool PALM_RENDER_SYNCHRONOUS_TEST = false;
static constexpr size_t RGB_PANEL_BOUNCE_BUFFER_PX = 480;
static constexpr uint32_t PALM_RENDER_TASK_STACK_BYTES = 8192;
static constexpr UBaseType_t PALM_RENDER_TASK_PRIORITY = 1;
static constexpr BaseType_t PALM_RENDER_TASK_CORE = 0;
static constexpr bool PALM_LCD_BILINEAR_UPSCALE = true;
static esp_lcd_panel_handle_t rgbPanel = nullptr;
static bool rgbPanelNeedsRestart = false;
static TAMC_GT911 gt911(GT911_SDA, GT911_SCL, GT911_INT, GT911_RST, 320, 240);
static SPIClass sdSpi(FSPI);

static constexpr int SCREEN_W = 480;
static constexpr int SCREEN_H = 272;
static constexpr int PALM_SURFACE_W = PALM_DIGITIZER_W;
static constexpr int PALM_SURFACE_H = PALM_DIGITIZER_H;
static constexpr int PALM_VIEW_W = (SCREEN_H * PALM_SURFACE_H) / PALM_SURFACE_W;
static constexpr int PALM_VIEW_H = SCREEN_H;
static constexpr int PALM_VIEW_X = (SCREEN_W - PALM_VIEW_W) / 2;
static constexpr int PALM_VIEW_Y = 0;
static constexpr int PALM_LCD_VIEW_X = PALM_VIEW_X + (PALM_VIEW_W * PALM_SILKSCREEN_H) / PALM_SURFACE_H;
static constexpr int PALM_LCD_VIEW_Y = PALM_VIEW_Y;
static constexpr int PALM_LCD_VIEW_W = PALM_VIEW_W - (PALM_VIEW_W * PALM_SILKSCREEN_H) / PALM_SURFACE_H;
static constexpr int PALM_LCD_VIEW_H = PALM_VIEW_H;

static unsigned long lastFrameMs = 0;
static unsigned long lastStatsMs = 0;
static unsigned long lastPerfStatsMs = 0;
static unsigned long lastWakeStatsMs = 0;
static bool lcdWaitingDrawn = false;
static bool staticShellDrawn = false;
static bool cpuReady = false;
static uint32_t cpuSlices = 0;
#if PALM_PERF_SERIAL_STATS
static uint32_t lastPerfCpuSlices = 0;
static uint32_t lastPerfRenderFrames = 0;
static uint64_t cpuExecutedCycles = 0;
static uint64_t lastPerfCpuExecutedCycles = 0;
static uint32_t renderMeasuredFrames = 0;
static uint32_t renderTotalMicros = 0;
static uint32_t renderMaxMicros = 0;
static uint32_t lastPerfRenderMeasuredFrames = 0;
static uint32_t lastPerfRenderTotalMicros = 0;
#endif
static uint32_t lastWakeStatsSlices = 0;
static bool restoredStateLoaded = false;
#if PALM_DEBUG_SERIAL_ENABLED
static bool debugSerialActive = false;
#endif
static bool palmLowPowerModeActive = false;
static bool palmLowPowerWakeServiceActive = false;
static bool palmLowPowerRequireTouchRelease = false;
static bool wakeDebugLastTouchDown = false;
static bool wakeDebugLastPowerDown = false;
static bool wakeDebugLastSleep = false;
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
#if PALM_TOUCH_EDGE_SERIAL_STATS
static bool touchEdgeSerialLastDown = false;
static uint16_t touchEdgeSerialLastAdcX = 0xffff;
static uint16_t touchEdgeSerialLastAdcY = 0xffff;
#endif
static bool cachedTouchDown = false;
static int cachedTouchScreenX = -1;
static int cachedTouchScreenY = -1;
static int cachedTouchPalmX = -1;
static int cachedTouchPalmY = -1;

#if PALM_DEBUG_SERIAL_ENABLED
#define PALM_DBG_PRINTF(...) do { if (debugSerialActive) Serial.printf(__VA_ARGS__); } while (0)
#define PALM_DBG_PRINTLN(...) do { if (debugSerialActive) Serial.println(__VA_ARGS__); } while (0)
#else
#define PALM_DBG_PRINTF(...) do {} while (0)
#define PALM_DBG_PRINTLN(...) do {} while (0)
#endif

static bool nativePenDown = false;
static uint16_t nativePenAdcX = 0xffff;
static uint16_t nativePenAdcY = 0xffff;
static int nativePenX = -1;
static int nativePenY = -1;
static constexpr int PEN_MOVE_ADC_THRESHOLD = 16;
static bool touchCandidateActive = false;
static unsigned long touchCandidateMs = 0;
static int touchCandidatePalmX = -1;
static int touchCandidatePalmY = -1;
static constexpr unsigned long TOUCH_STABLE_DOWN_MS = 8;
static constexpr int TOUCH_STABLE_PALM_TOLERANCE = 12;
static TaskHandle_t renderTaskHandle = nullptr;
static SemaphoreHandle_t renderSurfaceMutex = nullptr;
static volatile uint32_t renderFrames = 0;
static uint32_t palmViewToSurfaceX[PALM_VIEW_H];
static uint32_t palmViewToSurfaceY[PALM_VIEW_W];
static uint32_t palmLcdViewToSourceX[PALM_LCD_VIEW_H];
static uint32_t palmLcdViewToSourceY[PALM_LCD_VIEW_W];
struct LcdBilinearAxis {
  uint16_t i0;
  uint16_t i1;
  uint16_t w0;
  uint16_t w1;
};
static LcdBilinearAxis palmLcdBilinearX[PALM_LCD_VIEW_H];
static LcdBilinearAxis palmLcdBilinearY[PALM_LCD_VIEW_W];

// GT911 coordinates after setRotation(1) are board-specific and do not match
// the advertised 320x240 logical size. These anchors are measured at Palm OS'
// calibration targets on the rendered 160x160 LCD. Match the VB harness' raw
// ADS path: convert the capacitive point to Palm digitizer coordinates first,
// then derive the ADS raw values from those coordinates.
static constexpr int TOUCH_RAW_DIGITIZER_TOP = 425;     // Palm digitizer y = 0 after axis swap
static constexpr int TOUCH_RAW_DIGITIZER_BOTTOM = 53;   // Palm digitizer y = 219 after axis swap
static constexpr int TOUCH_RAW_LCD_TOP = 31;      // Palm LCD x = 0 after axis swap
static constexpr int TOUCH_RAW_LCD_CENTER_Y = 140;
static constexpr int TOUCH_RAW_LCD_BOTTOM = 253;  // Palm LCD x = 159 after axis swap
static constexpr int TOUCH_ADC_HIGH = 3800;
static constexpr int TOUCH_ADC_LOW = 300;
static constexpr int TOUCH_ADC_X_MAX_VALUE = PALM_LCD_W - 1;
static constexpr int TOUCH_ADC_Y_MAX_VALUE = PALM_DIGITIZER_H - 1;
static constexpr uint16_t SYS_TRAP_EVT_ENQUEUE_PEN_POINT = 0xa126;
static constexpr uint16_t SYS_TRAP_EVT_WAKEUP = 0xa12f;
static constexpr uint32_t PALM_CALL_STUB_ADDRESS = PALM_RAM_BASE + PALM_RAM_LOGICAL_SIZE - 0x100;
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
static constexpr const char *PALM_STATE_SD_PATH = "/palm_iiic_state.bin";
static constexpr const char *PALM_STATE_SD_TEMP_PATH = "/palm_iiic_state.tmp";
#else
static constexpr const char *PALM_STATE_SD_PATH = "/palm_m100_state.bin";
static constexpr const char *PALM_STATE_SD_TEMP_PATH = "/palm_m100_state.tmp";
#endif
static constexpr uint32_t PALM_STATE_MAGIC = 0x50414c4dUL;
#if PALM_HAS_SED1375
static constexpr uint32_t PALM_STATE_VERSION = 3;
#else
static constexpr uint32_t PALM_STATE_VERSION = 2;
#endif
static constexpr uint32_t PALM_STATE_SD_SPI_HZ = 10000000UL;
static constexpr uint32_t PALM_NATIVE_STATE_HEADER_SIZE = 8560;
static constexpr int VIRTUAL_BUTTON_MARGIN_GAP = 10;
static constexpr int VIRTUAL_BUTTON_STRIP_X0 = 0;
static constexpr int VIRTUAL_BUTTON_STRIP_X1 = PALM_VIEW_X - VIRTUAL_BUTTON_MARGIN_GAP;
static constexpr int VIRTUAL_POWER_STRIP_X0 = PALM_VIEW_X + PALM_VIEW_W + VIRTUAL_BUTTON_MARGIN_GAP;
static constexpr int VIRTUAL_POWER_STRIP_X1 = SCREEN_W;
static constexpr int VIRTUAL_BUTTON_COUNT = 6;
static constexpr int VIRTUAL_POWER_SLOT_FIRST = 0;
static constexpr int VIRTUAL_POWER_SLOT_LAST = 3;
static constexpr int VIRTUAL_SAVE_SLOT = 4;
static constexpr int VIRTUAL_RESET_SLOT = 5;
static constexpr int VIRTUAL_POWER_BUTTON_INDEX = VIRTUAL_BUTTON_COUNT;
static constexpr int VIRTUAL_SAVE_BUTTON_INDEX = VIRTUAL_BUTTON_COUNT + 1;
static constexpr int VIRTUAL_RESET_BUTTON_INDEX = VIRTUAL_BUTTON_COUNT + 2;
static constexpr uint16_t VIRTUAL_BUTTON_POWER = 0x0001;
static constexpr uint16_t VIRTUAL_BUTTON_HARD1 = 0x0008;
static constexpr uint16_t VIRTUAL_BUTTON_HARD2 = 0x0010;
static constexpr uint16_t VIRTUAL_BUTTON_HARD3 = 0x0020;
static constexpr uint16_t VIRTUAL_BUTTON_HARD4 = 0x0040;
static constexpr uint16_t VIRTUAL_BUTTON_PAGE_UP = 0x0002;
static constexpr uint16_t VIRTUAL_BUTTON_PAGE_DOWN = 0x0004;
static constexpr uint16_t VIRTUAL_BUTTON_RESET_OS = 0x2000;
static constexpr uint16_t VIRTUAL_BUTTON_SAVE_STATE = 0x4000;
static constexpr uint16_t VIRTUAL_BUTTON_TOP_UNUSED = 0x8000;
static constexpr uint16_t VIRTUAL_BUTTON_HW_MASK = VIRTUAL_BUTTON_POWER |
                                                   VIRTUAL_BUTTON_HARD1 |
                                                   VIRTUAL_BUTTON_HARD2 |
                                                   VIRTUAL_BUTTON_HARD3 |
                                                   VIRTUAL_BUTTON_HARD4 |
                                                   VIRTUAL_BUTTON_PAGE_UP |
                                                   VIRTUAL_BUTTON_PAGE_DOWN;
static constexpr int SYSTEM_BUTTON_ICON_W = 37;
static constexpr int SYSTEM_BUTTON_ICON_H = 37;
static constexpr int SYSTEM_BUTTON_ICON_STRIDE = 5;

static const uint8_t kBtnPowerPngBits[] PROGMEM = {
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0xF0, 0x00, 0x00,
  0x00, 0x07, 0xFC, 0x00, 0x00,
  0x00, 0x1F, 0xFF, 0x00, 0x00,
  0x00, 0x3F, 0x9F, 0x80, 0x00,
  0x00, 0x7C, 0x07, 0xC0, 0x00,
  0x00, 0xF0, 0x01, 0xE0, 0x00,
  0x00, 0xF0, 0x00, 0xE0, 0x00,
  0x01, 0xE0, 0x00, 0x40, 0x00,
  0x01, 0xC0, 0x00, 0x00, 0x00,
  0x01, 0xC0, 0x07, 0xFC, 0x00,
  0x01, 0xC0, 0x0F, 0xFE, 0x00,
  0x01, 0xC0, 0x0F, 0xFE, 0x00,
  0x01, 0xC0, 0x07, 0xFC, 0x00,
  0x01, 0xC0, 0x00, 0x00, 0x00,
  0x01, 0xE0, 0x00, 0x40, 0x00,
  0x00, 0xF0, 0x00, 0xE0, 0x00,
  0x00, 0xF0, 0x01, 0xE0, 0x00,
  0x00, 0x7C, 0x07, 0xC0, 0x00,
  0x00, 0x3F, 0x9F, 0x80, 0x00,
  0x00, 0x1F, 0xFF, 0x00, 0x00,
  0x00, 0x07, 0xFC, 0x00, 0x00,
  0x00, 0x00, 0xF0, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
};

static const uint8_t kBtnSavePngBits[] PROGMEM = {
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x03, 0xFF, 0xFF, 0xFE, 0x00,
  0x03, 0xFF, 0xFF, 0xFE, 0x00,
  0x03, 0xFF, 0xFF, 0xFE, 0x00,
  0x03, 0xFF, 0xFF, 0xFE, 0x00,
  0x03, 0xFF, 0xFF, 0xFE, 0x00,
  0x03, 0x00, 0x7F, 0xFE, 0x00,
  0x03, 0x00, 0x7F, 0xFE, 0x00,
  0x03, 0x00, 0x7E, 0x00, 0x00,
  0x03, 0x00, 0x7E, 0x00, 0x00,
  0x03, 0x00, 0x7E, 0x00, 0x00,
  0x03, 0x00, 0x7E, 0x00, 0x00,
  0x03, 0x00, 0x7E, 0x00, 0x00,
  0x03, 0x00, 0x7E, 0x00, 0x00,
  0x03, 0x00, 0x7E, 0x00, 0x00,
  0x03, 0x00, 0x7E, 0xFE, 0x00,
  0x03, 0x00, 0x7E, 0xFE, 0x00,
  0x03, 0x00, 0x7E, 0xFE, 0x00,
  0x03, 0x00, 0x7E, 0x00, 0x00,
  0x03, 0x00, 0x7E, 0x00, 0x00,
  0x03, 0x00, 0x7F, 0xFE, 0x00,
  0x03, 0xFF, 0xFF, 0xFE, 0x00,
  0x03, 0xFF, 0xFF, 0xFE, 0x00,
  0x03, 0xFF, 0xFF, 0xFE, 0x00,
  0x03, 0xFF, 0xFF, 0xFC, 0x00,
  0x03, 0xFF, 0xFF, 0xF8, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
};

static const uint8_t kBtnResetPngBits[] PROGMEM = {
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0xFC, 0x00, 0x00,
  0x00, 0x03, 0xFF, 0x00, 0x00,
  0x00, 0x0F, 0xFF, 0xC0, 0x00,
  0x00, 0x1F, 0xFF, 0xE0, 0x00,
  0x00, 0x3F, 0x87, 0xF0, 0x00,
  0x00, 0x7E, 0x01, 0xF8, 0x00,
  0x00, 0x7C, 0x00, 0x7C, 0x00,
  0x00, 0xF8, 0x00, 0x7C, 0x00,
  0x00, 0xF0, 0x00, 0x3C, 0x00,
  0x00, 0xF0, 0x00, 0x3C, 0x00,
  0x00, 0xE0, 0x00, 0x1E, 0x00,
  0x01, 0xE0, 0x00, 0x1E, 0x00,
  0x01, 0xE0, 0x00, 0x1E, 0x00,
  0x00, 0xE0, 0x00, 0x1E, 0x00,
  0x00, 0xF0, 0x00, 0x3C, 0x00,
  0x00, 0xF0, 0x00, 0x3C, 0x00,
  0x00, 0xF8, 0x18, 0x7C, 0x00,
  0x00, 0x78, 0x38, 0xF8, 0x00,
  0x00, 0x70, 0x79, 0xF8, 0x00,
  0x00, 0x20, 0xFF, 0xF0, 0x00,
  0x00, 0x01, 0xFF, 0xE0, 0x00,
  0x00, 0x03, 0xFF, 0xC0, 0x00,
  0x00, 0x01, 0xFF, 0x00, 0x00,
  0x00, 0x00, 0xFC, 0x00, 0x00,
  0x00, 0x00, 0x78, 0x00, 0x00,
  0x00, 0x00, 0x38, 0x00, 0x00,
  0x00, 0x00, 0x18, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
};

static const uint8_t kBtnAppPngBits[] PROGMEM = {
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x7F, 0xFF, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x40, 0x01, 0xF0, 0x00,
  0x00, 0x7F, 0xFF, 0xF0, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
};

static const uint8_t kBtnUpPngBits[] PROGMEM = {
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x10, 0x00, 0x00,
  0x00, 0x00, 0x18, 0x00, 0x00,
  0x00, 0x00, 0x1C, 0x00, 0x00,
  0x00, 0x00, 0x1E, 0x00, 0x00,
  0x00, 0x00, 0x1F, 0x00, 0x00,
  0x00, 0x3F, 0xFF, 0x80, 0x00,
  0x00, 0x3F, 0xFF, 0xC0, 0x00,
  0x00, 0x3F, 0xFF, 0xE0, 0x00,
  0x00, 0x3F, 0xFF, 0xF0, 0x00,
  0x00, 0x3F, 0xFF, 0xE0, 0x00,
  0x00, 0x3F, 0xFF, 0xC0, 0x00,
  0x00, 0x3F, 0xFF, 0x80, 0x00,
  0x00, 0x00, 0x1F, 0x00, 0x00,
  0x00, 0x00, 0x1E, 0x00, 0x00,
  0x00, 0x00, 0x1C, 0x00, 0x00,
  0x00, 0x00, 0x18, 0x00, 0x00,
  0x00, 0x00, 0x10, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
};

static const uint8_t kBtnDownPngBits[] PROGMEM = {
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x40, 0x00, 0x00,
  0x00, 0x00, 0xC0, 0x00, 0x00,
  0x00, 0x01, 0xC0, 0x00, 0x00,
  0x00, 0x03, 0xC0, 0x00, 0x00,
  0x00, 0x07, 0xC0, 0x00, 0x00,
  0x00, 0x0F, 0xFF, 0xE0, 0x00,
  0x00, 0x1F, 0xFF, 0xE0, 0x00,
  0x00, 0x3F, 0xFF, 0xE0, 0x00,
  0x00, 0x7F, 0xFF, 0xE0, 0x00,
  0x00, 0x3F, 0xFF, 0xE0, 0x00,
  0x00, 0x1F, 0xFF, 0xE0, 0x00,
  0x00, 0x0F, 0xFF, 0xE0, 0x00,
  0x00, 0x07, 0xC0, 0x00, 0x00,
  0x00, 0x03, 0xC0, 0x00, 0x00,
  0x00, 0x01, 0xC0, 0x00, 0x00,
  0x00, 0x00, 0xC0, 0x00, 0x00,
  0x00, 0x00, 0x40, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00,
};

struct __attribute__((packed)) PalmNativeDebugState {
  uint32_t ramReadCount;
  uint32_t ramWriteCount;
  uint32_t romReadCount;
  uint32_t regReadCount;
  uint32_t regWriteCount;
  uint32_t busErrorCount;
  uint32_t instrBusErrorCount;
  uint32_t lastReadAddress;
  uint32_t lastWriteAddress;
  uint32_t lastBusErrorAddress;
  uint16_t lastRegReadOffset;
  uint16_t lastRegWriteOffset;
  uint8_t lastRegWriteValue;
  uint8_t pad0[3];
  uint32_t lcdWriteCount;
  uint16_t lastLcdWriteOffset;
  uint8_t lastLcdWriteValue;
  uint8_t pad1;
  uint32_t instructionCount;
  uint32_t lastInstructionPc;
  uint16_t lastOpcode;
  uint8_t pad2[2];
  uint32_t adsCommandCount;
  uint16_t penXRaw;
  uint16_t penYRaw;
  uint16_t penDown;
  uint16_t lastAdsCommand;
  uint16_t lastAdsChannel;
  uint16_t lastAdsConversion;
  uint16_t lastAdsResponse;
  uint16_t lastAdsChannelConversion[8];
  uint8_t pad3[2];
  uint32_t adsChannelCounts[8];
};

struct __attribute__((packed)) PalmNativeStateHeader {
  uint32_t magic;
  uint32_t version;
  uint32_t totalSize;
  uint32_t ramSize;
  uint32_t regSize;
  uint32_t romSize;
  uint32_t d[8];
  uint32_t a[8];
  uint32_t pc;
  uint32_t sr;
  uint32_t sp;
  uint32_t usp;
  uint32_t isp;
  PalmNativeDebugState debug;
  uint16_t lastTimerStatus;
  uint8_t pad0[2];
  uint32_t timerTicks;
  uint32_t adsBitBufferIn;
  uint16_t adsBitBufferOut;
  uint8_t pad1[2];
  int32_t adsNumBitsIn;
  uint16_t adsPendingResult;
  uint8_t pad2[2];
  int32_t adsHavePending;
  int32_t adsCommandBitsSeen;
  int32_t penDown;
  uint16_t penXRaw;
  uint16_t penYRaw;
  uint16_t buttonBitsDown;
  uint8_t portDEdge;
  uint8_t cpuInitialized;
  uint16_t reserved0;
  uint8_t pad3[6];
  uint64_t systemCycles;
  double timerLastCycles;
  int64_t lastRtcSecond;
  int32_t lcdDirty;
  int32_t lcdFrameReady;
  uint64_t lcdReadyCycle;
  uint8_t uartRxFifo[4096];
  uint8_t uartTxFifo[4096];
  uint32_t uartRxHead;
  uint32_t uartRxTail;
  uint32_t uartRxCount;
  uint32_t uartTxHead;
  uint32_t uartTxTail;
  uint32_t uartTxCount;
  uint32_t uartRxOverrunCount;
  uint32_t uartTxOverrunCount;
};

static_assert(sizeof(PalmNativeDebugState) == 136, "PalmNativeDebugState layout mismatch");
static_assert(sizeof(PalmNativeStateHeader) == PALM_NATIVE_STATE_HEADER_SIZE,
              "PalmNativeStateHeader layout mismatch");
static_assert(PALM_UART_FIFO_SIZE <= sizeof(PalmNativeStateHeader::uartRxFifo),
              "Palm UART FIFO is larger than native state storage");

static PalmNativeStateHeader stateHeaderBuffer;
static uint8_t stateIoBuffer[PALM_DB_REG_SIZE];

static uint16_t to565(uint16_t color) {
  return color;
}

static inline uint16_t rgb565SwapBytes(uint16_t color) {
  return static_cast<uint16_t>((color << 8) | (color >> 8));
}

static void lcdFillRect(int16_t x, int16_t y, int16_t w, int16_t h, uint16_t color) {
  (void)x;
  (void)y;
  (void)w;
  (void)h;
  (void)color;
}

static void lcdDrawFastHLine(int16_t x, int16_t y, int16_t w, uint16_t color) {
  (void)x;
  (void)y;
  (void)w;
  (void)color;
}

static void lcdDrawFastVLine(int16_t x, int16_t y, int16_t h, uint16_t color) {
  (void)x;
  (void)y;
  (void)h;
  (void)color;
}

static void lcdDrawRect(int16_t x, int16_t y, int16_t w, int16_t h, uint16_t color) {
  (void)x;
  (void)y;
  (void)w;
  (void)h;
  (void)color;
}

static void lcdDrawLine(int16_t x0, int16_t y0, int16_t x1, int16_t y1, uint16_t color) {
  (void)x0;
  (void)y0;
  (void)x1;
  (void)y1;
  (void)color;
}

static void lcdPushImage(int16_t x, int16_t y, int16_t w, int16_t h, const uint16_t *pixels) {
  (void)x;
  (void)y;
  (void)w;
  (void)h;
  (void)pixels;
}

static uint16_t *palmSurface = nullptr;
static uint16_t *palmPanelLcd = nullptr;
static uint16_t *panelFrames[2] = {nullptr, nullptr};
static bool panelFramesInInternalRam = false;
static uint16_t * volatile activePanelFrame = nullptr;
static uint16_t *pendingPanelFrame = nullptr;
static volatile bool panelFrameBoundary = true;
static bool panelStaticFramesReady = false;
static uint8_t drawPanelFrameIndex = 0;
static uint16_t virtualButtonBitsDown = 0;
static int virtualButtonIndexDown = -1;
static bool palmStateSaveRequested = false;
static bool palmStateSaveInProgress = false;
static bool touchInputDisabled = false;
static bool saveButtonHoldActive = false;
static bool saveButtonHoldTriggered = false;
static unsigned long saveButtonHoldStartMs = 0;
static bool palmOsResetRequested = false;
static bool palmOsResetInProgress = false;
static bool resetButtonHoldActive = false;
static bool resetButtonHoldTriggered = false;
static unsigned long resetButtonHoldStartMs = 0;
static uint8_t lastPalmBacklightDuty = 0xff;
#if PALM_UART_HOST_SERIAL_BRIDGE
static bool palmUartBridgeActive = false;
static uint8_t palmUartBridgeBuffer[PALM_UART_BRIDGE_CHUNK];
#endif

static void lockRenderSurface() {
  if (renderSurfaceMutex != nullptr) xSemaphoreTake(renderSurfaceMutex, portMAX_DELAY);
}

static void unlockRenderSurface() {
  if (renderSurfaceMutex != nullptr) xSemaphoreGive(renderSurfaceMutex);
}

static bool initRenderBuffers() {
  if (palmSurface != nullptr && palmPanelLcd != nullptr &&
      panelFrames[0] != nullptr && panelFrames[1] != nullptr) return true;
#if defined(ESP32)
  palmSurface = static_cast<uint16_t *>(heap_caps_malloc(PALM_SURFACE_W * PALM_SURFACE_H * sizeof(uint16_t),
                                                        MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT));
  palmPanelLcd = static_cast<uint16_t *>(heap_caps_malloc(PALM_LCD_W * PALM_LCD_H * sizeof(uint16_t),
                                                         MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT));
  panelFrames[0] = static_cast<uint16_t *>(heap_caps_malloc(SCREEN_W * SCREEN_H * sizeof(uint16_t),
                                                           MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT));
  panelFrames[1] = static_cast<uint16_t *>(heap_caps_malloc(SCREEN_W * SCREEN_H * sizeof(uint16_t),
                                                           MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT));
  panelFramesInInternalRam = panelFrames[0] != nullptr && panelFrames[1] != nullptr;
  if (!panelFramesInInternalRam) {
    if (panelFrames[0] != nullptr) {
      heap_caps_free(panelFrames[0]);
      panelFrames[0] = nullptr;
    }
    if (panelFrames[1] != nullptr) {
      heap_caps_free(panelFrames[1]);
      panelFrames[1] = nullptr;
    }
    panelFrames[0] = static_cast<uint16_t *>(heap_caps_malloc(SCREEN_W * SCREEN_H * sizeof(uint16_t),
                                                             MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT));
    panelFrames[1] = static_cast<uint16_t *>(heap_caps_malloc(SCREEN_W * SCREEN_H * sizeof(uint16_t),
                                                             MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT));
  }
#else
  palmSurface = static_cast<uint16_t *>(malloc(PALM_SURFACE_W * PALM_SURFACE_H * sizeof(uint16_t)));
  palmPanelLcd = static_cast<uint16_t *>(malloc(PALM_LCD_W * PALM_LCD_H * sizeof(uint16_t)));
  panelFrames[0] = static_cast<uint16_t *>(malloc(SCREEN_W * SCREEN_H * sizeof(uint16_t)));
  panelFrames[1] = static_cast<uint16_t *>(malloc(SCREEN_W * SCREEN_H * sizeof(uint16_t)));
#endif
  if (palmSurface == nullptr || palmPanelLcd == nullptr ||
      panelFrames[0] == nullptr || panelFrames[1] == nullptr) return false;
  for (uint32_t i = 0; i < static_cast<uint32_t>(PALM_SURFACE_W) * PALM_SURFACE_H; ++i) {
    palmSurface[i] = TFT_WHITE;
  }
  for (uint32_t i = 0; i < static_cast<uint32_t>(PALM_LCD_W) * PALM_LCD_H; ++i) {
    palmPanelLcd[i] = TFT_WHITE;
  }
  for (uint32_t frame = 0; frame < 2; ++frame) {
    for (uint32_t i = 0; i < static_cast<uint32_t>(SCREEN_W) * SCREEN_H; ++i) {
      panelFrames[frame][i] = TFT_MIDGREY;
    }
  }
  activePanelFrame = panelFrames[0];
  return true;
}

static void surfaceFill(uint16_t color) {
  for (uint32_t i = 0; i < static_cast<uint32_t>(PALM_SURFACE_W) * PALM_SURFACE_H; ++i) {
    palmSurface[i] = color;
  }
}

static void surfaceSetPixel(int x, int y, uint16_t color) {
  if (x < 0 || y < 0 || x >= PALM_SURFACE_W || y >= PALM_SURFACE_H) return;
  palmSurface[static_cast<uint32_t>(y) * PALM_SURFACE_W + x] = color;
}

static uint16_t lcdPixelColor(uint8_t value, uint8_t bpp) {
  uint8_t maxValue = (bpp >= 8) ? 0xff : ((1 << bpp) - 1);
  if (maxValue == 0) maxValue = 1;
  uint8_t shade = 255 - ((static_cast<uint16_t>(value) * 255) / maxValue);
  return ((shade & 0xf8) << 8) | ((shade & 0xf8) << 3) | (shade >> 3);
}

static uint8_t readPackedLcdPixel(uint32_t rowAddress, uint16_t x, uint8_t bpp, uint8_t pan) {
  if (bpp != 1 && bpp != 2 && bpp != 4 && bpp != 8) return 0;
  uint32_t bitIndex = static_cast<uint32_t>(x + pan) * bpp;
  uint8_t byteValue = palmRead8(rowAddress + (bitIndex >> 3));
  uint8_t shift = 8 - bpp - (bitIndex & 7);
  return (byteValue >> shift) & ((1 << bpp) - 1);
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

static void initScaleMaps() {
  for (int y = 0; y < PALM_VIEW_H; ++y) {
    palmViewToSurfaceX[y] = (static_cast<uint32_t>(y) * ((PALM_SURFACE_W - 1) << 16)) / (PALM_VIEW_H - 1);
  }
  for (int x = 0; x < PALM_VIEW_W; ++x) {
    uint32_t forwardY16 = (static_cast<uint32_t>(x) * ((PALM_SURFACE_H - 1) << 16)) / (PALM_VIEW_W - 1);
    palmViewToSurfaceY[x] = ((PALM_SURFACE_H - 1) << 16) - forwardY16;
  }
  for (int y = 0; y < PALM_LCD_VIEW_H; ++y) {
    uint32_t x16 = (static_cast<uint32_t>(y) * ((PALM_LCD_W - 1) << 16)) / (PALM_LCD_VIEW_H - 1);
    uint32_t x0 = x16 >> 16;
    uint32_t x1 = (x0 + 1 < PALM_LCD_W) ? x0 + 1 : x0;
    uint32_t fx = (x16 >> 8) & 0xff;
    palmLcdViewToSourceX[y] = x16;
    palmLcdBilinearX[y] = {
      static_cast<uint16_t>(x0),
      static_cast<uint16_t>(x1),
      static_cast<uint16_t>(256 - fx),
      static_cast<uint16_t>(fx)
    };
  }
  for (int x = 0; x < PALM_LCD_VIEW_W; ++x) {
    uint32_t forwardY16 = (static_cast<uint32_t>(x) * ((PALM_LCD_H - 1) << 16)) / (PALM_LCD_VIEW_W - 1);
    uint32_t y16 = ((PALM_LCD_H - 1) << 16) - forwardY16;
    uint32_t y0 = y16 >> 16;
    uint32_t y1 = (y0 + 1 < PALM_LCD_H) ? y0 + 1 : y0;
    uint32_t fy = (y16 >> 8) & 0xff;
    palmLcdViewToSourceY[x] = y16;
    palmLcdBilinearY[x] = {
      static_cast<uint16_t>(y0),
      static_cast<uint16_t>(y1),
      static_cast<uint16_t>(256 - fy),
      static_cast<uint16_t>(fy)
    };
  }
}

static inline uint16_t bilinear565Fast(const uint16_t *pixels,
                                       const LcdBilinearAxis &x,
                                       const LcdBilinearAxis &y) {
  const uint16_t *row0 = pixels + static_cast<uint32_t>(y.i0) * PALM_SURFACE_W;
  const uint16_t *row1 = pixels + static_cast<uint32_t>(y.i1) * PALM_SURFACE_W;
  uint16_t c00 = row0[x.i0];
  uint16_t c10 = row0[x.i1];
  uint16_t c01 = row1[x.i0];
  uint16_t c11 = row1[x.i1];

  uint32_t w00 = x.w0 * y.w0;
  uint32_t w10 = x.w1 * y.w0;
  uint32_t w01 = x.w0 * y.w1;
  uint32_t w11 = x.w1 * y.w1;

  uint32_t r = (((c00 >> 11) & 0x1f) * w00 +
                ((c10 >> 11) & 0x1f) * w10 +
                ((c01 >> 11) & 0x1f) * w01 +
                ((c11 >> 11) & 0x1f) * w11) >> 16;
  uint32_t g = (((c00 >> 5) & 0x3f) * w00 +
                ((c10 >> 5) & 0x3f) * w10 +
                ((c01 >> 5) & 0x3f) * w01 +
                ((c11 >> 5) & 0x3f) * w11) >> 16;
  uint32_t b = ((c00 & 0x1f) * w00 +
                (c10 & 0x1f) * w10 +
                (c01 & 0x1f) * w01 +
                (c11 & 0x1f) * w11) >> 16;

  return rgb565SwapBytes(static_cast<uint16_t>((r << 11) | (g << 5) | b));
}

static inline uint16_t composePanelPixel(int screenX, int screenY) {
  if (palmSurface == nullptr ||
      screenX < PALM_VIEW_X ||
      screenX >= PALM_VIEW_X + PALM_VIEW_W ||
      screenY < PALM_VIEW_Y ||
      screenY >= PALM_VIEW_Y + PALM_VIEW_H) {
    return TFT_MIDGREY;
  }

  int dx = screenX - PALM_VIEW_X;
  int dy = screenY - PALM_VIEW_Y;
  uint32_t srcX = palmViewToSurfaceX[dy] >> 16;
  uint32_t srcY = palmViewToSurfaceY[dx] >> 16;
  return palmSurface[srcY * PALM_SURFACE_W + srcX];
}

static void renderPanelLcdFrameFromSurface(uint16_t *target) {
  if (target == nullptr || palmSurface == nullptr) return;

  for (int dy = 0; dy < PALM_LCD_VIEW_H; ++dy) {
    uint32_t srcX16 = palmLcdViewToSourceX[dy];
    const LcdBilinearAxis &srcX = palmLcdBilinearX[dy];
    uint16_t *lcdRow = target + static_cast<uint32_t>(PALM_LCD_VIEW_Y + dy) * SCREEN_W + PALM_LCD_VIEW_X;
    for (int dx = 0; dx < PALM_LCD_VIEW_W; ++dx) {
      uint32_t srcY16 = palmLcdViewToSourceY[dx];
      if (PALM_LCD_BILINEAR_UPSCALE) {
        lcdRow[dx] = bilinear565Fast(palmSurface, srcX, palmLcdBilinearY[dx]);
      } else {
        lcdRow[dx] = palmSurface[static_cast<uint32_t>(srcY16 >> 16) * PALM_SURFACE_W + (srcX16 >> 16)];
      }
    }
  }
}

static void panelFillRect(uint16_t *target, int x, int y, int w, int h, uint16_t color) {
  if (target == nullptr || w <= 0 || h <= 0) return;
  int x2 = x + w;
  int y2 = y + h;
  if (x < 0) x = 0;
  if (y < 0) y = 0;
  if (x2 > SCREEN_W) x2 = SCREEN_W;
  if (y2 > SCREEN_H) y2 = SCREEN_H;
  if (x >= x2 || y >= y2) return;

  for (int py = y; py < y2; ++py) {
    uint16_t *row = target + static_cast<uint32_t>(py) * SCREEN_W;
    for (int px = x; px < x2; ++px) row[px] = color;
  }
}

static void panelDrawRect(uint16_t *target, int x, int y, int w, int h, uint16_t color) {
  panelFillRect(target, x, y, w, 1, color);
  panelFillRect(target, x, y + h - 1, w, 1, color);
  panelFillRect(target, x, y, 1, h, color);
  panelFillRect(target, x + w - 1, y, 1, h, color);
}

static void panelDrawSystemBitmapIcon(uint16_t *target, int x, int y, int w, int h, const uint8_t *bits) {
  int ix = x + (w - SYSTEM_BUTTON_ICON_W) / 2;
  int iy = y + (h - SYSTEM_BUTTON_ICON_H) / 2;

  for (int py = 0; py < SYSTEM_BUTTON_ICON_H; ++py) {
    for (int px = 0; px < SYSTEM_BUTTON_ICON_W; ++px) {
      uint8_t rowByte = pgm_read_byte(bits + py * SYSTEM_BUTTON_ICON_STRIDE + px / 8);
      bool bit = (rowByte & (0x80 >> (px & 7))) != 0;
      panelFillRect(target, ix + px, iy + py, 1, 1, bit ? TFT_BUTTON_BITMAP_1 : TFT_BUTTON_BITMAP_0);
    }
  }
}

static inline uint8_t silkscreenPixelIndex(int x, int y) {
  uint8_t packed = pgm_read_byte(kSilkscreenPixels4bpp +
                                 static_cast<uint32_t>(y) * SILKSCREEN_IMAGE_STRIDE +
                                 (x >> 1));
  return (x & 1) != 0 ? (packed & 0x0f) : (packed >> 4);
}

static void panelDrawSilkscreenBitmap(uint16_t *target, int x, int y, int w, int h) {
  if (target == nullptr || w <= 0 || h <= 0) return;

  int x0 = x < 0 ? 0 : x;
  int y0 = y < 0 ? 0 : y;
  int x1 = x + w > SCREEN_W ? SCREEN_W : x + w;
  int y1 = y + h > SCREEN_H ? SCREEN_H : y + h;
  if (x0 >= x1 || y0 >= y1) return;

  for (int py = y0; py < y1; ++py) {
    int sy = ((py - y) * SILKSCREEN_IMAGE_H) / h;
    uint16_t *row = target + static_cast<uint32_t>(py) * SCREEN_W;
    for (int px = x0; px < x1; ++px) {
      int sx = ((px - x) * SILKSCREEN_IMAGE_W) / w;
      uint8_t index = silkscreenPixelIndex(sx, sy);
      row[px] = pgm_read_word(kSilkscreenPalette565 + (index & 0x0f));
    }
  }
}

static void panelDrawAppIcon(uint16_t *target, int x, int y, int w, int h, int bars) {
  int iconW = w / 2;
  int iconH = h / 2;
  int ix = x + (w - iconW) / 2;
  int iy = y + (h - iconH) / 2;
  panelDrawRect(target, ix, iy, iconW, iconH, TFT_WHITE);
  int usable = iconH - 8;
  if (usable < bars) usable = bars;
  for (int i = 0; i < bars; ++i) {
    int by = iy + 4 + (usable * i) / bars;
    panelFillRect(target, ix + 4, by, iconW - 8, 2, TFT_WHITE);
  }
}

static void panelDrawArrowIcon(uint16_t *target, int x, int y, int w, int h, bool up) {
  int cx = x + w / 2;
  int cy = y + h / 2;
  int half = w / 5;
  int height = h / 4;
  if (half < 5) half = 5;
  if (height < 7) height = 7;

  for (int row = 0; row < height; ++row) {
    int span = (half * row) / height;
    int py = up ? (cy - height / 2 + row) : (cy + height / 2 - row);
    panelFillRect(target, cx - span, py, span * 2 + 1, 1, TFT_WHITE);
  }
  panelFillRect(target, cx - 3, up ? cy : cy - height / 2, 7, height / 2 + 5, TFT_WHITE);
}

static void panelDrawPowerIcon(uint16_t *target, int x, int y, int w, int h) {
  int cx = x + w / 2;
  int cy = y + h / 2;
  int radius = min(w, h) / 4;
  if (radius < 8) radius = 8;

  panelFillRect(target, cx - 2, cy - radius - 3, 5, radius, TFT_WHITE);
  panelFillRect(target, cx - radius, cy - radius / 2, 4, radius + 3, TFT_WHITE);
  panelFillRect(target, cx + radius - 3, cy - radius / 2, 4, radius + 3, TFT_WHITE);
  panelFillRect(target, cx - radius / 2, cy + radius - 2, radius + 1, 4, TFT_WHITE);
}

static void panelDrawSaveIcon(uint16_t *target, int x, int y, int w, int h) {
  int iconW = w / 2;
  int iconH = h / 2;
  int ix = x + (w - iconW) / 2;
  int iy = y + (h - iconH) / 2;
  panelFillRect(target, ix, iy, iconW, iconH, TFT_WHITE);
  panelFillRect(target, ix + 3, iy + 3, iconW - 6, iconH / 3, TFT_BLACK);
  panelFillRect(target, ix + 5, iy + iconH - 8, iconW - 10, 5, TFT_BLACK);
  panelFillRect(target, ix + iconW - 8, iy + 3, 4, iconH / 3, TFT_WHITE);
}

static void panelDrawResetIcon(uint16_t *target, int x, int y, int w, int h) {
  int cx = x + w / 2;
  int cy = y + h / 2;
  int r = min(w, h) / 4;
  if (r < 8) r = 8;
  int t = 4;

  panelFillRect(target, cx - r, cy - r, r + 2, t, TFT_WHITE);
  panelFillRect(target, cx - r, cy - r, t, r + 2, TFT_WHITE);
  panelFillRect(target, cx - 2, cy + r - t, r + 2, t, TFT_WHITE);
  panelFillRect(target, cx + r - t, cy - 2, t, r + 2, TFT_WHITE);

  for (int row = 0; row < 8; ++row) {
    panelFillRect(target, cx + r - 7 + row, cy - r - 4 + row, 8 - row, 1, TFT_WHITE);
  }
}

static void renderVirtualButtonStrip(uint16_t *target) {
  int stripW = VIRTUAL_BUTTON_STRIP_X1 - VIRTUAL_BUTTON_STRIP_X0;
  if (stripW <= 12) return;

  panelFillRect(target, VIRTUAL_BUTTON_STRIP_X0, 0, stripW, SCREEN_H, TFT_BLACK);

  for (int i = 0; i < VIRTUAL_BUTTON_COUNT; ++i) {
    int y0 = (SCREEN_H * i) / VIRTUAL_BUTTON_COUNT;
    int y1 = (SCREEN_H * (i + 1)) / VIRTUAL_BUTTON_COUNT;
    int inset = 3;
    int bx = VIRTUAL_BUTTON_STRIP_X0 + inset;
    int by = y0 + inset;
    int bw = stripW - inset * 2;
    int bh = (y1 - y0) - inset * 2;
    panelFillRect(target, bx, by, bw, bh, TFT_BUTTON_BITMAP_0);

    if (i == 2) {
      panelDrawSystemBitmapIcon(target, bx, by, bw, bh, kBtnUpPngBits);
    } else if (i == 3) {
      panelDrawSystemBitmapIcon(target, bx, by, bw, bh, kBtnDownPngBits);
    } else {
      panelDrawSystemBitmapIcon(target, bx, by, bw, bh, kBtnAppPngBits);
    }
  }
}

static void renderVirtualPowerStrip(uint16_t *target) {
  int stripW = VIRTUAL_POWER_STRIP_X1 - VIRTUAL_POWER_STRIP_X0;
  if (stripW <= 12) return;

  panelFillRect(target, VIRTUAL_POWER_STRIP_X0, 0, stripW, SCREEN_H, TFT_BLACK);

  int inset = 3;
  int bx = VIRTUAL_POWER_STRIP_X0 + inset;
  int bw = stripW - inset * 2;

  int powerY0 = (SCREEN_H * VIRTUAL_POWER_SLOT_FIRST) / VIRTUAL_BUTTON_COUNT;
  int powerY1 = (SCREEN_H * (VIRTUAL_POWER_SLOT_LAST + 1)) / VIRTUAL_BUTTON_COUNT;
  int powerBy = powerY0 + inset;
  int powerBh = (powerY1 - powerY0) - inset * 2;
  panelFillRect(target, bx, powerBy, bw, powerBh, TFT_BUTTON_BITMAP_0);
  panelDrawSystemBitmapIcon(target, bx, powerBy, bw, powerBh, kBtnPowerPngBits);

  int saveY0 = (SCREEN_H * VIRTUAL_SAVE_SLOT) / VIRTUAL_BUTTON_COUNT;
  int saveY1 = (SCREEN_H * (VIRTUAL_SAVE_SLOT + 1)) / VIRTUAL_BUTTON_COUNT;
  int saveBy = saveY0 + inset;
  int saveBh = (saveY1 - saveY0) - inset * 2;
  panelFillRect(target, bx, saveBy, bw, saveBh, TFT_BUTTON_BITMAP_0);
  panelDrawSystemBitmapIcon(target, bx, saveBy, bw, saveBh, kBtnSavePngBits);

  int resetY0 = (SCREEN_H * VIRTUAL_RESET_SLOT) / VIRTUAL_BUTTON_COUNT;
  int resetY1 = (SCREEN_H * (VIRTUAL_RESET_SLOT + 1)) / VIRTUAL_BUTTON_COUNT;
  int resetBy = resetY0 + inset;
  int resetBh = (resetY1 - resetY0) - inset * 2;
  panelFillRect(target, bx, resetBy, bw, resetBh, TFT_BUTTON_BITMAP_0);
  panelDrawSystemBitmapIcon(target, bx, resetBy, bw, resetBh, kBtnResetPngBits);
}

static void renderPanelStaticFrameFromSurface(uint16_t *target) {
  if (target == nullptr || palmSurface == nullptr) return;

  for (int y = 0; y < SCREEN_H; ++y) {
    uint16_t *row = target + static_cast<uint32_t>(y) * SCREEN_W;
    for (int x = 0; x < SCREEN_W; ++x) {
      row[x] = TFT_MIDGREY;
    }
  }
  renderVirtualButtonStrip(target);
  renderVirtualPowerStrip(target);

  int shellViewW = PALM_LCD_VIEW_X - PALM_VIEW_X;
  panelDrawSilkscreenBitmap(target, PALM_VIEW_X, PALM_VIEW_Y, shellViewW, PALM_VIEW_H);
  renderPanelLcdFrameFromSurface(target);
}

static void publishPanelFrame() {
  if (!panelStaticFramesReady) {
    renderPanelStaticFrameFromSurface(panelFrames[0]);
    memcpy(panelFrames[1], panelFrames[0], SCREEN_W * SCREEN_H * sizeof(uint16_t));
    activePanelFrame = panelFrames[0];
    drawPanelFrameIndex = 0;
    panelStaticFramesReady = true;
    ++renderFrames;
    return;
  }

  uint8_t nextIndex = drawPanelFrameIndex ^ 1;
  renderPanelLcdFrameFromSurface(panelFrames[nextIndex]);
  pendingPanelFrame = panelFrames[nextIndex];
  if (activePanelFrame == nullptr) {
    activePanelFrame = pendingPanelFrame;
    pendingPanelFrame = nullptr;
  }
  drawPanelFrameIndex = nextIndex;
  ++renderFrames;
}

static bool rgbFrameDoneCallback(esp_lcd_panel_handle_t panel,
                                 const esp_lcd_rgb_panel_event_data_t *edata,
                                 void *userCtx) {
  (void)panel;
  (void)edata;
  (void)userCtx;
  panelFrameBoundary = true;
  return false;
}

static bool rgbBounceFillCallback(esp_lcd_panel_handle_t panel, void *bounceBuf,
                                  int posPx, int lenBytes, void *userCtx) {
  (void)panel;
  (void)userCtx;
  if (posPx == 0 && pendingPanelFrame != nullptr) {
    activePanelFrame = pendingPanelFrame;
    pendingPanelFrame = nullptr;
    panelFrameBoundary = false;
  }
  uint16_t *source = activePanelFrame;
  if (source == nullptr) {
    uint16_t *out = static_cast<uint16_t *>(bounceBuf);
    int pixels = lenBytes / sizeof(uint16_t);
    for (int i = 0; i < pixels; ++i) out[i] = TFT_MIDGREY;
    return false;
  }
  memcpy(bounceBuf, source + posPx, lenBytes);
  return false;
}

static void renderPalmSurfaceToPanel() {
  static uint16_t line[PALM_VIEW_W];
  for (int dy = 0; dy < PALM_VIEW_H; ++dy) {
    uint32_t srcX = palmViewToSurfaceX[dy] >> 16;
    for (int dx = 0; dx < PALM_VIEW_W; ++dx) {
      line[dx] = palmSurface[static_cast<uint32_t>(palmViewToSurfaceY[dx] >> 16) * PALM_SURFACE_W + srcX];
    }
    lcdPushImage(PALM_VIEW_X, PALM_VIEW_Y + dy, PALM_VIEW_W, 1, line);
  }
}

static void renderPalmLcdToPanel() {
  static uint16_t line[PALM_LCD_VIEW_W];
  for (int dy = 0; dy < PALM_LCD_VIEW_H; ++dy) {
    uint32_t srcX = palmLcdViewToSourceX[dy] >> 16;
    for (int dx = 0; dx < PALM_LCD_VIEW_W; ++dx) {
      line[dx] = palmPanelLcd[static_cast<uint32_t>(palmLcdViewToSourceY[dx] >> 16) * PALM_LCD_W + srcX];
    }
    lcdPushImage(PALM_LCD_VIEW_X, PALM_LCD_VIEW_Y + dy, PALM_LCD_VIEW_W, 1, line);
  }
}

static void renderTaskMain(void *parameter) {
  (void)parameter;
  for (;;) {
    ulTaskNotifyTake(pdTRUE, portMAX_DELAY);
    lockRenderSurface();
    publishPanelFrame();
    unlockRenderSurface();
  }
}

static void renderStaticSurfaceToPanel() {
  for (int y = 0; y < PALM_LCD_H; ++y) {
    memcpy(palmPanelLcd + static_cast<uint32_t>(y) * PALM_LCD_W,
           palmSurface + static_cast<uint32_t>(y) * PALM_SURFACE_W,
           PALM_LCD_W * sizeof(uint16_t));
  }
  publishPanelFrame();
}

static void requestPanelRender() {
  if (PALM_DISABLE_DYNAMIC_LCD_RENDER_TEST) return;
  if (!PALM_RENDER_SYNCHRONOUS_TEST && renderTaskHandle != nullptr) {
    xTaskNotifyGive(renderTaskHandle);
    return;
  }
  publishPanelFrame();
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
  // The LCD is rendered rotated on the 480x272 panel. GT911 X follows the
  // physical screen X axis, which maps to Palm Y; GT911 Y maps to Palm X.
  palmX = mapPiecewiseClamped(rawY,
                              TOUCH_RAW_LCD_TOP, TOUCH_RAW_LCD_CENTER_Y, TOUCH_RAW_LCD_BOTTOM,
                              0, PALM_LCD_W / 2, PALM_LCD_W - 1);
  palmY = mapClamped(rawX,
                     TOUCH_RAW_DIGITIZER_TOP, TOUCH_RAW_DIGITIZER_BOTTOM,
                     0, PALM_DIGITIZER_H - 1);
}

static uint16_t virtualButtonBitsForIndex(int index) {
  switch (index) {
    case 0: return VIRTUAL_BUTTON_HARD1;
    case 1: return VIRTUAL_BUTTON_HARD2;
    case 2: return VIRTUAL_BUTTON_PAGE_UP;
    case 3: return VIRTUAL_BUTTON_PAGE_DOWN;
    case 4: return VIRTUAL_BUTTON_HARD3;
    case 5: return VIRTUAL_BUTTON_HARD4;
    default: return 0;
  }
}

static uint16_t virtualButtonBitsForRaw(int rawX, int rawY, int &index) {
  index = -1;
  if (rawX >= VIRTUAL_POWER_STRIP_X0 && rawX < VIRTUAL_POWER_STRIP_X1 &&
      rawY >= 0 && rawY < SCREEN_H) {
    int topIndex = (rawY * VIRTUAL_BUTTON_COUNT) / SCREEN_H;
    if (topIndex < 0) topIndex = 0;
    if (topIndex >= VIRTUAL_BUTTON_COUNT) topIndex = VIRTUAL_BUTTON_COUNT - 1;
    if (topIndex >= VIRTUAL_POWER_SLOT_FIRST && topIndex <= VIRTUAL_POWER_SLOT_LAST) {
      index = VIRTUAL_POWER_BUTTON_INDEX;
      return VIRTUAL_BUTTON_POWER;
    }
    if (topIndex == VIRTUAL_SAVE_SLOT) {
      index = VIRTUAL_SAVE_BUTTON_INDEX;
      return VIRTUAL_BUTTON_SAVE_STATE;
    }
    if (topIndex == VIRTUAL_RESET_SLOT) {
      index = VIRTUAL_RESET_BUTTON_INDEX;
      return VIRTUAL_BUTTON_RESET_OS;
    }
    index = VIRTUAL_RESET_BUTTON_INDEX + topIndex - VIRTUAL_RESET_SLOT;
    return VIRTUAL_BUTTON_TOP_UNUSED;
  }

  if (rawX < VIRTUAL_BUTTON_STRIP_X0 || rawX >= VIRTUAL_BUTTON_STRIP_X1 ||
      rawY < 0 || rawY >= SCREEN_H) {
    return 0;
  }

  index = (rawY * VIRTUAL_BUTTON_COUNT) / SCREEN_H;
  if (index < 0) index = 0;
  if (index >= VIRTUAL_BUTTON_COUNT) index = VIRTUAL_BUTTON_COUNT - 1;
  return virtualButtonBitsForIndex(index);
}

static bool virtualPowerButtonDown() {
  return (virtualButtonBitsDown & VIRTUAL_BUTTON_POWER) != 0;
}

static void setPalmPowerButtonFromTouch(bool touchPowerDown) {
  palmHwSetPowerButton(touchPowerDown || virtualPowerButtonDown());
}

static void setVirtualButtonBits(uint16_t bits, int index, int rawX, int rawY) {
  if (bits == virtualButtonBitsDown && index == virtualButtonIndexDown) return;

  uint16_t oldBits = virtualButtonBitsDown;
  int oldIndex = virtualButtonIndexDown;
  uint16_t oldHwBits = oldBits & VIRTUAL_BUTTON_HW_MASK;
  uint16_t newHwBits = bits & VIRTUAL_BUTTON_HW_MASK;
  uint16_t releaseBits = oldHwBits & static_cast<uint16_t>(~newHwBits);
  uint16_t pressBits = newHwBits & static_cast<uint16_t>(~oldHwBits);

  if (releaseBits != 0) palmHwSetButtonBits(releaseBits, false);
  if (pressBits != 0) palmHwSetButtonBits(pressBits, true);

#if PALM_TOUCH_EDGE_SERIAL_STATS
  if (oldBits != 0 && oldBits != bits) {
    PALM_DBG_PRINTF("Button up idx=%d bits=0x%04x raw=%d,%d\n",
                  oldIndex,
                  oldBits,
                  rawX,
                  rawY);
  }
  if (bits != 0 && oldBits != bits) {
    PALM_DBG_PRINTF("Button down idx=%d bits=0x%04x raw=%d,%d\n",
                  index,
                  bits,
                  rawX,
                  rawY);
  }
#else
  (void)oldIndex;
  (void)rawX;
  (void)rawY;
#endif

  virtualButtonBitsDown = bits;
  virtualButtonIndexDown = bits != 0 ? index : -1;
}

static void resetSaveButtonHold() {
  saveButtonHoldActive = false;
  saveButtonHoldTriggered = false;
  saveButtonHoldStartMs = 0;
}

static void resetResetButtonHold() {
  resetButtonHoldActive = false;
  resetButtonHoldTriggered = false;
  resetButtonHoldStartMs = 0;
}

static void updateSaveButtonHold(bool down, unsigned long now) {
  if (!down || palmStateSaveInProgress || touchInputDisabled) {
    resetSaveButtonHold();
    return;
  }

  if (!saveButtonHoldActive) {
    saveButtonHoldActive = true;
    saveButtonHoldTriggered = false;
    saveButtonHoldStartMs = now;
#if PALM_TOUCH_EDGE_SERIAL_STATS
    PALM_DBG_PRINTLN("State save hold started");
#endif
    return;
  }

  if (!saveButtonHoldTriggered && now - saveButtonHoldStartMs >= PALM_STATE_SAVE_HOLD_MS) {
    saveButtonHoldTriggered = true;
    palmStateSaveRequested = true;
#if PALM_TOUCH_EDGE_SERIAL_STATS
    PALM_DBG_PRINTLN("State save hold accepted");
#endif
  }
}

static void updateResetButtonHold(bool down, unsigned long now) {
  if (!down || palmOsResetInProgress || touchInputDisabled) {
    resetResetButtonHold();
    return;
  }

  if (!resetButtonHoldActive) {
    resetButtonHoldActive = true;
    resetButtonHoldTriggered = false;
    resetButtonHoldStartMs = now;
#if PALM_TOUCH_EDGE_SERIAL_STATS
    PALM_DBG_PRINTLN("Palm reset hold started");
#endif
    return;
  }

  if (!resetButtonHoldTriggered && now - resetButtonHoldStartMs >= PALM_OS_RESET_HOLD_MS) {
    resetButtonHoldTriggered = true;
    palmOsResetRequested = true;
#if PALM_TOUCH_EDGE_SERIAL_STATS
    PALM_DBG_PRINTLN("Palm reset hold accepted");
#endif
  }
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
#if defined(ESP32)
  esp_lcd_rgb_panel_config_t panelConfig = {};
  panelConfig.clk_src = LCD_CLK_SRC_DEFAULT;
  panelConfig.timings.pclk_hz = RGB_PANEL_PIXEL_CLOCK_HZ;
  panelConfig.timings.h_res = SCREEN_W;
  panelConfig.timings.v_res = SCREEN_H;
  panelConfig.timings.hsync_pulse_width = RGB_PANEL_HSYNC_PULSE_WIDTH;
  panelConfig.timings.hsync_back_porch = RGB_PANEL_HSYNC_BACK_PORCH;
  panelConfig.timings.hsync_front_porch = RGB_PANEL_HSYNC_FRONT_PORCH;
  panelConfig.timings.vsync_pulse_width = RGB_PANEL_VSYNC_PULSE_WIDTH;
  panelConfig.timings.vsync_back_porch = RGB_PANEL_VSYNC_BACK_PORCH;
  panelConfig.timings.vsync_front_porch = RGB_PANEL_VSYNC_FRONT_PORCH;
  panelConfig.timings.flags.hsync_idle_low = 1;
  panelConfig.timings.flags.vsync_idle_low = 1;
  panelConfig.timings.flags.de_idle_high = 0;
  panelConfig.timings.flags.pclk_active_neg = RGB_PANEL_PCLK_ACTIVE_NEG ? 1 : 0;
  panelConfig.timings.flags.pclk_idle_high = RGB_PANEL_PCLK_IDLE_HIGH ? 1 : 0;
  panelConfig.data_width = 16;
  panelConfig.bits_per_pixel = 16;
  panelConfig.num_fbs = 0;
  panelConfig.bounce_buffer_size_px = RGB_PANEL_BOUNCE_BUFFER_PX;
  panelConfig.dma_burst_size = 64;
  panelConfig.hsync_gpio_num = 39;
  panelConfig.vsync_gpio_num = 41;
  panelConfig.de_gpio_num = 40;
  panelConfig.pclk_gpio_num = 42;
  panelConfig.disp_gpio_num = -1;
  panelConfig.data_gpio_nums[0] = 15;
  panelConfig.data_gpio_nums[1] = 16;
  panelConfig.data_gpio_nums[2] = 4;
  panelConfig.data_gpio_nums[3] = 45;
  panelConfig.data_gpio_nums[4] = 48;
  panelConfig.data_gpio_nums[5] = 47;
  panelConfig.data_gpio_nums[6] = 21;
  panelConfig.data_gpio_nums[7] = 14;
  panelConfig.data_gpio_nums[8] = 8;
  panelConfig.data_gpio_nums[9] = 3;
  panelConfig.data_gpio_nums[10] = 46;
  panelConfig.data_gpio_nums[11] = 9;
  panelConfig.data_gpio_nums[12] = 1;
  panelConfig.data_gpio_nums[13] = 5;
  panelConfig.data_gpio_nums[14] = 6;
  panelConfig.data_gpio_nums[15] = 7;
  panelConfig.flags.disp_active_low = 1;
  panelConfig.flags.refresh_on_demand = 0;
  panelConfig.flags.fb_in_psram = 0;
  panelConfig.flags.double_fb = 0;
  panelConfig.flags.no_fb = 1;
  panelConfig.flags.bb_invalidate_cache = 0;

  ESP_ERROR_CHECK(esp_lcd_new_rgb_panel(&panelConfig, &rgbPanel));

  esp_lcd_rgb_panel_event_callbacks_t callbacks = {};
  callbacks.on_bounce_empty = rgbBounceFillCallback;
  callbacks.on_frame_buf_complete = rgbFrameDoneCallback;
  ESP_ERROR_CHECK(esp_lcd_rgb_panel_register_event_callbacks(rgbPanel, &callbacks, nullptr));
  ESP_ERROR_CHECK(esp_lcd_panel_reset(rgbPanel));
  ESP_ERROR_CHECK(esp_lcd_panel_init(rgbPanel));
#endif
#if TFT_BL >= 0
  ledcAttach(TFT_BL, BACKLIGHT_PWM_HZ, BACKLIGHT_PWM_BITS);
  ledcWrite(TFT_BL, BACKLIGHT_DEFAULT_DUTY);
#endif
}

static void restartRgbPanelIfNeeded() {
#if defined(ESP32)
  if (rgbPanel != nullptr && rgbPanelNeedsRestart) {
    (void)esp_lcd_rgb_panel_restart(rgbPanel);
    rgbPanelNeedsRestart = false;
  }
#endif
}

static void setPanelOutputEnabled(bool enabled) {
#if defined(ESP32)
  if (rgbPanel != nullptr) {
    if (enabled) restartRgbPanelIfNeeded();
    esp_lcd_panel_disp_on_off(rgbPanel, enabled);
  }
#else
  (void)enabled;
#endif
}

static void setBacklightDuty(uint8_t duty) {
#if TFT_BL >= 0
  ledcWrite(TFT_BL, duty);
#else
  (void)duty;
#endif
}

static uint8_t palmBacklightDutyFromOs() {
  uint8_t level = palmHwDisplayBrightnessLevel();
  uint16_t span = BACKLIGHT_PALM_MAX_DUTY - BACKLIGHT_PALM_MIN_DUTY;
  return BACKLIGHT_PALM_MIN_DUTY + ((static_cast<uint16_t>(level) * span + 127) / 255);
}

static void setPalmBacklightDuty() {
  uint8_t duty = palmBacklightDutyFromOs();
  lastPalmBacklightDuty = duty;
  setBacklightDuty(duty);
}

static void updatePalmBacklightFromOs() {
  if (!cpuReady || palmLowPowerModeActive || palmStateSaveInProgress || palmOsResetInProgress) return;

  uint8_t duty = palmBacklightDutyFromOs();
  if (duty == lastPalmBacklightDuty) return;
  lastPalmBacklightDuty = duty;
  setBacklightDuty(duty);
}

static void setBacklightEnabled(bool enabled) {
  if (enabled) {
    setPalmBacklightDuty();
  } else {
    setBacklightDuty(0);
  }
}

static void setPalmLowPowerMode(bool enabled) {
  if (enabled == palmLowPowerModeActive) return;

  if (enabled) {
    palmLowPowerWakeServiceActive = false;
    setBacklightEnabled(false);
    setPanelOutputEnabled(false);
    delay(2);
#if defined(ESP32)
    setCpuFrequencyMhz(ESP32_PALM_SLEEP_CPU_MHZ);
#endif
    palmLowPowerRequireTouchRelease = cachedTouchDown || lastTouchDown || virtualButtonBitsDown != 0;
    lastTouchPollMs = 0;
  } else {
#if defined(ESP32)
    setCpuFrequencyMhz(ESP32_ACTIVE_CPU_MHZ);
#endif
    delay(2);
    setPanelOutputEnabled(true);
    setBacklightEnabled(true);
    lastFrameMs = 0;
    palmLowPowerWakeServiceActive = false;
    palmLowPowerRequireTouchRelease = false;
    lastTouchPollMs = 0;
  }

  palmLowPowerModeActive = enabled;
#if PALM_WAKE_SERIAL_STATS
  PALM_DBG_PRINTF("ESP32 palm-sleep mode %s cpu=%lu releaseGuard=%u\n",
                enabled ? "on" : "off",
#if defined(ESP32)
                (unsigned long)getCpuFrequencyMhz(),
#else
                0UL,
#endif
                palmLowPowerRequireTouchRelease ? 1 : 0);
#endif
}

static void setPalmLowPowerWakeService(bool enabled) {
  if (!palmLowPowerModeActive || enabled == palmLowPowerWakeServiceActive) return;

#if defined(ESP32)
  setCpuFrequencyMhz(enabled ? ESP32_ACTIVE_CPU_MHZ : ESP32_PALM_SLEEP_CPU_MHZ);
#endif
  palmLowPowerWakeServiceActive = enabled;
#if PALM_WAKE_SERIAL_STATS
  PALM_DBG_PRINTF("ESP32 palm-wake service %s cpu=%lu\n",
                enabled ? "on" : "off",
#if defined(ESP32)
                (unsigned long)getCpuFrequencyMhz()
#else
                0UL
#endif
  );
#endif
}

static void updatePalmLowPowerMode() {
  if (!cpuReady) return;
  if (palmStateSaveInProgress) return;
  if (!palmHwIsAsleep()) {
    setPalmLowPowerMode(false);
  } else if (!palmLowPowerModeActive && !wakeTouchActive) {
    setPalmLowPowerMode(true);
  }
}

static unsigned long touchPollIntervalMs() {
  if (palmLowPowerModeActive && !palmLowPowerWakeServiceActive) {
    return PALM_SLEEP_TOUCH_POLL_INTERVAL_MS;
  }
  return PALM_TOUCH_POLL_INTERVAL_MS;
}

static bool palmLowPowerNeedsWakeService() {
  return !palmLowPowerRequireTouchRelease &&
         (wakeTouchActive || palmHwGetInterruptLevel() > 0 || palmHwHasWakeSource());
}

static void enterPalmDeepIdle() {
  const unsigned long sleepMs = PALM_SLEEP_TOUCH_POLL_INTERVAL_MS > 0 ?
                                PALM_SLEEP_TOUCH_POLL_INTERVAL_MS : 1;
#if defined(ESP32) && PALM_DEEP_IDLE_LIGHT_SLEEP
  const uint64_t sleepUs = static_cast<uint64_t>(sleepMs) * 1000ULL;
  if (esp_sleep_enable_timer_wakeup(sleepUs) == ESP_OK &&
      esp_light_sleep_start() == ESP_OK) {
    // This RGB panel driver exposes restart/set-clock hooks, but not a safe
    // public DMA pause/resume call. Keep the display dark while light sleep
    // stops the CPU between touch polls, then resync DMA before showing pixels.
    rgbPanelNeedsRestart = true;
    return;
  }
#endif
  delay(sleepMs);
}

static bool getPalmScreenTouch(int &screenX, int &screenY) {
  unsigned long now = millis();
  if (touchInputDisabled) {
    setVirtualButtonBits(0, -1, lastTouchRawX, lastTouchRawY);
    resetSaveButtonHold();
    resetResetButtonHold();
    cachedTouchDown = false;
    touchCandidateActive = false;
    lastTouchDown = false;
    return false;
  }

  if (now - lastTouchPollMs >= touchPollIntervalMs()) {
    lastTouchPollMs = now;
    gt911.read();
    bool controllerDown = gt911.isTouched && gt911.touches > 0;
    if (controllerDown) {
      int rawX = gt911.points[0].x;
      int rawY = gt911.points[0].y;
      if (palmLowPowerModeActive && palmLowPowerRequireTouchRelease) {
        setVirtualButtonBits(0, -1, rawX, rawY);
        lastTouchRawX = rawX;
        lastTouchRawY = rawY;
        cachedTouchDown = false;
        touchCandidateActive = false;
        lastTouchDown = false;
        return false;
      }

      int buttonIndex;
      uint16_t buttonBits = virtualButtonBitsForRaw(rawX, rawY, buttonIndex);
      if (buttonBits != 0) {
        if (palmHwIsAsleep()) {
          setVirtualButtonBits(0, -1, rawX, rawY);
          buttonBits = 0;
        } else {
          setVirtualButtonBits(buttonBits, buttonIndex, rawX, rawY);
          updateSaveButtonHold((buttonBits & VIRTUAL_BUTTON_SAVE_STATE) != 0, now);
          updateResetButtonHold((buttonBits & VIRTUAL_BUTTON_RESET_OS) != 0, now);
        }
      } else {
        resetSaveButtonHold();
        resetResetButtonHold();
      }
      if (buttonBits != 0) {
        lastTouchRawX = rawX;
        lastTouchRawY = rawY;
        cachedTouchDown = false;
        touchCandidateActive = false;
        lastTouchDown = false;
        return false;
      }

      int palmX;
      int palmY;
      rawTouchToPalm(rawX, rawY, palmX, palmY);
      lastTouchRawX = rawX;
      lastTouchRawY = rawY;
      bool bypassStableTouch = palmHwIsAsleep();
      if (!cachedTouchDown && !bypassStableTouch) {
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
      resetSaveButtonHold();
      resetResetButtonHold();
      palmLowPowerRequireTouchRelease = false;
      setVirtualButtonBits(0, -1, lastTouchRawX, lastTouchRawY);
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
  lastTouchAdcX = palmToAdc(palmX, TOUCH_ADC_X_MAX_VALUE);
  lastTouchAdcY = palmToAdc(palmY, TOUCH_ADC_Y_MAX_VALUE);
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
  // Match the VB harness' raw ADC path. X spans the 160-pixel LCD width, while
  // Y spans the full 220-pixel digitizer height including the silkscreen.
  adcX = palmToAdc(palmX, TOUCH_ADC_X_MAX_VALUE);
  adcY = palmToAdc(palmY, TOUCH_ADC_Y_MAX_VALUE);
  lastTouchAdcX = adcX;
  lastTouchAdcY = adcY;
  return true;
}

#if PALM_TOUCH_EDGE_SERIAL_STATS
static void logTouchEdge(const char *mode, bool down, uint16_t adcX, uint16_t adcY) {
  bool moved = down &&
               (abs(static_cast<int>(adcX) - static_cast<int>(touchEdgeSerialLastAdcX)) >= PEN_MOVE_ADC_THRESHOLD ||
                abs(static_cast<int>(adcY) - static_cast<int>(touchEdgeSerialLastAdcY)) >= PEN_MOVE_ADC_THRESHOLD);
  if (down == touchEdgeSerialLastDown && !moved) return;

  PALM_DBG_PRINTF("Touch %s %s gt=%d,%d palm=%d,%d screen=%d,%d adc=%u,%u\n",
                mode,
                down ? "down" : "up",
                lastTouchRawX,
                lastTouchRawY,
                lastTouchPalmX,
                lastTouchPalmY,
                lastTouchScreenX,
                lastTouchScreenY,
                adcX,
                adcY);
  touchEdgeSerialLastDown = down;
  touchEdgeSerialLastAdcX = down ? adcX : 0xffff;
  touchEdgeSerialLastAdcY = down ? adcY : 0xffff;
}
#else
static void logTouchEdge(const char *mode, bool down, uint16_t adcX, uint16_t adcY) {
  (void)mode;
  (void)down;
  (void)adcX;
  (void)adcY;
}
#endif

static bool palmRamRangeOk(uint32_t address, uint32_t bytes) {
  uint32_t end = address + bytes;
  return end >= address && address >= PALM_RAM_BASE &&
         end <= PALM_RAM_BASE + palmRamSize();
}

static void palmWriteStack16(uint8_t *target, int offset, int16_t value) {
  uint16_t raw = static_cast<uint16_t>(value);
  target[offset] = static_cast<uint8_t>(raw >> 8);
  target[offset + 1] = static_cast<uint8_t>(raw);
}

static void palmWriteStack32(uint8_t *target, int offset, uint32_t value) {
  target[offset] = static_cast<uint8_t>(value >> 24);
  target[offset + 1] = static_cast<uint8_t>(value >> 16);
  target[offset + 2] = static_cast<uint8_t>(value >> 8);
  target[offset + 3] = static_cast<uint8_t>(value);
}

#if PALM_ENABLE_MUSASHI
static uint32_t callPalmTrapStack(uint16_t trap, const uint8_t *stackBytes,
                                  uint32_t stackByteCount) {
  if (!cpuReady || !palmRamRangeOk(PALM_CALL_STUB_ADDRESS, 6)) return 0;

  uint32_t oldPc = m68k_get_reg(nullptr, M68K_REG_PC);
  uint32_t oldSp = m68k_get_reg(nullptr, M68K_REG_SP);
  uint32_t oldSr = m68k_get_reg(nullptr, M68K_REG_SR);
  uint32_t oldUsp = m68k_get_reg(nullptr, M68K_REG_USP);
  uint32_t oldIsp = m68k_get_reg(nullptr, M68K_REG_ISP);
  uint32_t oldD[8];
  uint32_t oldA[8];
  for (int i = 0; i < 8; ++i) {
    oldD[i] = m68k_get_reg(nullptr, static_cast<m68k_register_t>(M68K_REG_D0 + i));
    oldA[i] = m68k_get_reg(nullptr, static_cast<m68k_register_t>(M68K_REG_A0 + i));
  }

  if (oldSp < stackByteCount) return 0;
  uint32_t callSp = oldSp - stackByteCount;
  if (!palmRamRangeOk(callSp, stackByteCount)) return 0;

  uint8_t savedStub[6];
  for (uint32_t i = 0; i < sizeof(savedStub); ++i) {
    savedStub[i] = palmRead8(PALM_CALL_STUB_ADDRESS + i);
  }

  palmWrite16(PALM_CALL_STUB_ADDRESS, 0x4e4f);
  palmWrite16(PALM_CALL_STUB_ADDRESS + 2, trap);
  palmWrite16(PALM_CALL_STUB_ADDRESS + 4, 0x60fe);
  for (uint32_t i = 0; i < stackByteCount; ++i) {
    palmWrite8(callSp + i, stackBytes != nullptr ? stackBytes[i] : 0);
  }

  m68k_set_reg(M68K_REG_SP, callSp);
  m68k_set_reg(M68K_REG_PC, PALM_CALL_STUB_ADDRESS);
  for (int i = 0; i < 1200; ++i) {
    palmHwCycle();
    m68k_set_irq(palmHwGetInterruptLevel());
    int usedCycles = m68k_execute(2000);
    palmHwAdvanceCycles(usedCycles > 0 ? static_cast<uint32_t>(usedCycles) : 0);
    palmHwCycle();
    m68k_set_irq(palmHwGetInterruptLevel());
    uint32_t pc = m68k_get_reg(nullptr, M68K_REG_PC);
    if (pc == PALM_CALL_STUB_ADDRESS + 4 || pc == PALM_CALL_STUB_ADDRESS + 6) break;
  }

  uint32_t d0 = m68k_get_reg(nullptr, M68K_REG_D0);
  for (uint32_t i = 0; i < sizeof(savedStub); ++i) {
    palmWrite8(PALM_CALL_STUB_ADDRESS + i, savedStub[i]);
  }
  for (int i = 0; i < 8; ++i) {
    m68k_set_reg(static_cast<m68k_register_t>(M68K_REG_D0 + i), oldD[i]);
    m68k_set_reg(static_cast<m68k_register_t>(M68K_REG_A0 + i), oldA[i]);
  }
  m68k_set_reg(M68K_REG_SR, oldSr);
  m68k_set_reg(M68K_REG_USP, oldUsp);
  m68k_set_reg(M68K_REG_ISP, oldIsp);
  m68k_set_reg(M68K_REG_SP, oldSp);
  m68k_set_reg(M68K_REG_PC, oldPc);
  return d0;
}

static uint32_t callPalmTrapNoArgs(uint16_t trap) {
  return callPalmTrapStack(trap, nullptr, 0);
}

static uint32_t callPalmEvtEnqueuePenPoint(bool down, int16_t palmX, int16_t palmY) {
  uint32_t oldSp = m68k_get_reg(nullptr, M68K_REG_SP);
  if (oldSp < 8) return 0;
  uint32_t callSp = oldSp - 8;
  uint32_t pointAddress = callSp + 4;
  uint8_t stackBytes[8] = {};
  palmWriteStack32(stackBytes, 0, pointAddress);
  palmWriteStack16(stackBytes, 4, down ? palmX : -1);
  palmWriteStack16(stackBytes, 6, down ? palmY : -1);
  return callPalmTrapStack(SYS_TRAP_EVT_ENQUEUE_PEN_POINT, stackBytes, sizeof(stackBytes));
}
#else
static uint32_t callPalmTrapNoArgs(uint16_t trap) {
  (void)trap;
  return 0;
}

static uint32_t callPalmEvtEnqueuePenPoint(bool down, int16_t palmX, int16_t palmY) {
  (void)down;
  (void)palmX;
  (void)palmY;
  return 0;
}
#endif

static void enqueuePalmOsPenPoint(bool down, int palmX, int palmY) {
  uint32_t err = callPalmEvtEnqueuePenPoint(down,
                                            static_cast<int16_t>(palmX),
                                            static_cast<int16_t>(palmY));
  callPalmTrapNoArgs(SYS_TRAP_EVT_WAKEUP);
#if PALM_TOUCH_EDGE_SERIAL_STATS
  PALM_DBG_PRINTF("Touch evt %s palm=%d,%d err=%lu\n",
                down ? "down" : "up",
                down ? palmX : -1,
                down ? palmY : -1,
                (unsigned long)err);
#endif
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

static void drawPalmShellForFrame() {
  drawPalmBorder();
  drawPalmSilkscreen();
  staticShellDrawn = true;
}

static void drawWaitingFrame() {
  if (lcdWaitingDrawn) return;
  lcdWaitingDrawn = true;

  lockRenderSurface();
  drawPalmShellOnce();
  surfaceFillRect(0, 0, PALM_LCD_W, PALM_LCD_H, TFT_WHITE);
  renderStaticSurfaceToPanel();
  unlockRenderSurface();
#if PALM_BOOT_STATUS_TEXT
#endif
}

static void drawBootPattern() {
  lockRenderSurface();
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
  renderStaticSurfaceToPanel();
  unlockRenderSurface();
}

static bool drawPalmFrameFromEmulatedLcd() {
#if PALM_PERF_SERIAL_STATS
  uint32_t renderStartMicros = micros();
#endif
#if PALM_HAS_SED1375
  PalmLcdState lcd;
  bool useSed1375 = palmSed1375GetLcdState(lcd);
#else
  PalmLcdState lcd = palmHwGetLcdState();
  bool useSed1375 = false;
#endif
  if (!lcd.valid) {
    drawWaitingFrame();
    return false;
  }

  lockRenderSurface();
  lcdWaitingDrawn = false;
  drawPalmShellOnce();

  uint16_t drawW = min<uint16_t>(PALM_LCD_W, lcd.width);
  uint16_t drawH = min<uint16_t>(PALM_LCD_H, lcd.height);
  surfaceFillRect(0, 0, PALM_LCD_W, PALM_LCD_H, TFT_WHITE);

  for (uint16_t y = 0; y < drawH; ++y) {
    uint32_t srcLine = lcd.startAddr + static_cast<uint32_t>(y) * lcd.bytesPerLine;
    const uint8_t *srcBytes = useSed1375 ? palmSed1375VramPointer(srcLine, lcd.bytesPerLine) : nullptr;
    uint32_t cachedByteIndex = 0xffffffffUL;
    uint8_t cachedByte = 0;
    for (uint16_t x = 0; x < drawW; ++x) {
      uint8_t value = 0;
      if (lcd.bpp == 1 || lcd.bpp == 2 || lcd.bpp == 4 || lcd.bpp == 8) {
        uint32_t bitIndex = static_cast<uint32_t>(x + lcd.margin) * lcd.bpp;
        uint32_t byteIndex = bitIndex >> 3;
        if (byteIndex != cachedByteIndex) {
          cachedByteIndex = byteIndex;
          cachedByte = srcBytes != nullptr ? srcBytes[byteIndex] : palmRead8(srcLine + byteIndex);
        }
        uint8_t shift = 8 - lcd.bpp - (bitIndex & 7);
        value = (cachedByte >> shift) & ((1 << lcd.bpp) - 1);
      }
      uint16_t color = useSed1375 ? palmSed1375PaletteColor565(value) :
                                    lcdPixelColor(value, lcd.bpp);
      surfaceSetPixel(x, y, color);
    }
  }
  unlockRenderSurface();

  requestPanelRender();
  if (useSed1375) {
    palmSed1375MarkClean();
  } else {
    palmHwMarkLcdClean();
  }
#if PALM_PERF_SERIAL_STATS
  uint32_t renderMicros = micros() - renderStartMicros;
  renderTotalMicros += renderMicros;
  if (renderMicros > renderMaxMicros) renderMaxMicros = renderMicros;
  ++renderMeasuredFrames;
#endif
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

static void drawStaticDisplayTest() {
  surfaceFill(TFT_WHITE);
  surfaceFillRect(0, 0, PALM_LCD_W / 3, PALM_LCD_H, 0xf800);
  surfaceFillRect(PALM_LCD_W / 3, 0, PALM_LCD_W / 3, PALM_LCD_H, 0x07e0);
  surfaceFillRect((PALM_LCD_W * 2) / 3, 0, PALM_LCD_W / 3, PALM_LCD_H, 0x001f);
  surfaceDrawRect(4, 4, PALM_LCD_W - 8, PALM_LCD_H - 8, TFT_BLACK);
  surfaceDrawLine(0, 0, PALM_LCD_W - 1, PALM_LCD_H - 1, TFT_BLACK);
  surfaceDrawLine(PALM_LCD_W - 1, 0, 0, PALM_LCD_H - 1, TFT_BLACK);
}

static void initPins() {
#if TFT_BL >= 0
  pinMode(TFT_BL, OUTPUT);
  digitalWrite(TFT_BL, LOW);
#endif
}

static void initSerialDebug() {
#if PALM_DEBUG_SERIAL_ENABLED
  Serial.begin(115200);
  debugSerialActive = true;
  delay(250);
#endif
}

static void beginPalmUartBridge() {
#if PALM_UART_HOST_SERIAL_BRIDGE
  if (palmUartBridgeActive) return;
#if PALM_DEBUG_SERIAL_ENABLED
  if (!debugSerialActive) {
    Serial.begin(PALM_UART_HOST_SERIAL_BAUD);
  }
#else
  Serial.begin(PALM_UART_HOST_SERIAL_BAUD);
#endif
  palmUartBridgeActive = true;
#endif
}

static void releaseSerialDebugForPalmUart() {
#if PALM_DEBUG_SERIAL_BOOT_ONLY && \
    PALM_BOOT_SERIAL_STATS && \
    !PALM_STATE_SERIAL_STATS && \
    !PALM_RUNTIME_SERIAL_STATS && \
    !PALM_PERF_SERIAL_STATS && \
    !PALM_TOUCH_EDGE_SERIAL_STATS && \
    !PALM_WAKE_SERIAL_STATS
  if (debugSerialActive) {
    PALM_DBG_PRINTLN("Debug serial released for Palm UART");
    Serial.flush();
    delay(20);
#if !PALM_UART_HOST_SERIAL_BRIDGE
    Serial.end();
#endif
    debugSerialActive = false;
  }
#endif
  beginPalmUartBridge();
}

static void servicePalmUartBridge() {
#if PALM_UART_HOST_SERIAL_BRIDGE
  if (!palmUartBridgeActive) return;

  uint32_t rxFree = palmHwUartRxFree();
  uint32_t rxRead = 0;
  while (rxRead < sizeof(palmUartBridgeBuffer) && rxFree > 0 && Serial.available() > 0) {
    int value = Serial.read();
    if (value < 0) break;
    palmUartBridgeBuffer[rxRead++] = static_cast<uint8_t>(value);
    --rxFree;
  }
  if (rxRead > 0) {
    palmHwUartWriteRx(palmUartBridgeBuffer, rxRead);
  }

  uint32_t txQueued = palmHwUartTxCount();
  while (txQueued > 0) {
    int canWrite = Serial.availableForWrite();
    if (canWrite <= 0) break;

    uint32_t chunk = txQueued;
    if (chunk > sizeof(palmUartBridgeBuffer)) chunk = sizeof(palmUartBridgeBuffer);
    if (chunk > static_cast<uint32_t>(canWrite)) chunk = static_cast<uint32_t>(canWrite);

    uint32_t txRead = palmHwUartReadTx(palmUartBridgeBuffer, chunk);
    if (txRead == 0) break;
    Serial.write(palmUartBridgeBuffer, txRead);
    txQueued = palmHwUartTxCount();
  }
#endif
}

static void initTouch() {
  gt911.begin();
  gt911.setRotation(1);
}

static void initDisplay() {
  initScaleMaps();
  if (renderSurfaceMutex == nullptr) {
    renderSurfaceMutex = xSemaphoreCreateMutex();
  }
  lcdInit();
  if (!PALM_RENDER_SYNCHRONOUS_TEST && renderTaskHandle == nullptr) {
    BaseType_t created = xTaskCreatePinnedToCore(renderTaskMain,
                                                "palmRender",
                                                PALM_RENDER_TASK_STACK_BYTES,
                                                nullptr,
                                                PALM_RENDER_TASK_PRIORITY,
                                                &renderTaskHandle,
                                                PALM_RENDER_TASK_CORE);
    if (created != pdPASS) {
      renderTaskHandle = nullptr;
    }
  }
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

static bool readExact(File &file, uint8_t *dest, size_t count) {
  size_t done = 0;
  while (done < count) {
    int n = file.read(dest + done, count - done);
    if (n <= 0) return false;
    done += static_cast<size_t>(n);
    if ((done & 0x0fff) == 0) yield();
  }
  return true;
}

static bool writeExact(File &file, const uint8_t *src, size_t count) {
  size_t done = 0;
  while (done < count) {
    size_t n = file.write(src + done, count - done);
    if (n == 0) return false;
    done += n;
    if ((done & 0x0fff) == 0) yield();
  }
  return true;
}

static bool copyStateRamFromFile(File &file, uint32_t ramSize) {
  uint32_t offset = 0;
  while (offset < ramSize) {
    uint32_t remaining = ramSize - offset;
    uint32_t chunk = remaining > sizeof(stateIoBuffer) ? sizeof(stateIoBuffer) : remaining;
    if (!readExact(file, stateIoBuffer, chunk)) {
#if PALM_BOOT_SERIAL_STATS
      PALM_DBG_PRINTF("State restore failed: RAM read stopped at %lu/%lu\n",
                    (unsigned long)offset, (unsigned long)ramSize);
#endif
      return false;
    }
    if (!palmRamWriteBytes(offset, stateIoBuffer, chunk)) {
#if PALM_BOOT_SERIAL_STATS
      PALM_DBG_PRINTF("State restore failed: RAM write stopped at %lu/%lu\n",
                    (unsigned long)offset, (unsigned long)ramSize);
#endif
      return false;
    }
    offset += chunk;
#if PALM_BOOT_SERIAL_STATS
    if ((offset & 0x3ffffUL) == 0) {
      PALM_DBG_PRINTF("State RAM restored: %lu/%lu\n",
                    (unsigned long)offset, (unsigned long)ramSize);
    }
#endif
    yield();
  }
  return true;
}

static bool copyStateRamToFile(File &file, uint32_t ramSize) {
  uint32_t offset = 0;
  while (offset < ramSize) {
    uint32_t remaining = ramSize - offset;
    uint32_t chunk = remaining > sizeof(stateIoBuffer) ? sizeof(stateIoBuffer) : remaining;
    if (!palmRamReadBytes(offset, stateIoBuffer, chunk)) {
#if PALM_STATE_SERIAL_STATS
      PALM_DBG_PRINTF("State save failed: RAM read stopped at %lu/%lu\n",
                    (unsigned long)offset, (unsigned long)ramSize);
#endif
      return false;
    }
    if (!writeExact(file, stateIoBuffer, chunk)) {
#if PALM_STATE_SERIAL_STATS
      PALM_DBG_PRINTF("State save failed: RAM write stopped at %lu/%lu\n",
                    (unsigned long)offset, (unsigned long)ramSize);
#endif
      return false;
    }
    offset += chunk;
#if PALM_STATE_SERIAL_STATS
    if ((offset & 0x3ffffUL) == 0) {
      PALM_DBG_PRINTF("State RAM saved: %lu/%lu\n",
                    (unsigned long)offset, (unsigned long)ramSize);
    }
#endif
    yield();
  }
  return true;
}

static uint32_t palmStateExtraDeviceBytes() {
  return static_cast<uint32_t>(palmSed1375StateSize());
}

static bool copyExtraDeviceStateFromFile(File &file, uint32_t stateSize) {
  uint32_t offset = 0;
  while (offset < stateSize) {
    uint32_t remaining = stateSize - offset;
    uint32_t chunk = remaining > sizeof(stateIoBuffer) ? sizeof(stateIoBuffer) : remaining;
    if (!readExact(file, stateIoBuffer, chunk)) {
#if PALM_BOOT_SERIAL_STATS
      PALM_DBG_PRINTF("State restore failed: device read stopped at %lu/%lu\n",
                    (unsigned long)offset, (unsigned long)stateSize);
#endif
      return false;
    }
    if (!palmSed1375WriteStateBytes(offset, stateIoBuffer, chunk)) {
#if PALM_BOOT_SERIAL_STATS
      PALM_DBG_PRINTF("State restore failed: device write stopped at %lu/%lu\n",
                    (unsigned long)offset, (unsigned long)stateSize);
#endif
      return false;
    }
    offset += chunk;
    yield();
  }
  return true;
}

static bool copyExtraDeviceStateToFile(File &file, uint32_t stateSize) {
  uint32_t offset = 0;
  while (offset < stateSize) {
    uint32_t remaining = stateSize - offset;
    uint32_t chunk = remaining > sizeof(stateIoBuffer) ? sizeof(stateIoBuffer) : remaining;
    if (!palmSed1375ReadStateBytes(offset, stateIoBuffer, chunk)) {
#if PALM_STATE_SERIAL_STATS
      PALM_DBG_PRINTF("State save failed: device read stopped at %lu/%lu\n",
                    (unsigned long)offset, (unsigned long)stateSize);
#endif
      return false;
    }
    if (!writeExact(file, stateIoBuffer, chunk)) {
#if PALM_STATE_SERIAL_STATS
      PALM_DBG_PRINTF("State save failed: device write stopped at %lu/%lu\n",
                    (unsigned long)offset, (unsigned long)stateSize);
#endif
      return false;
    }
    offset += chunk;
    yield();
  }
  return true;
}

static void restoreCpuFromState(const PalmNativeStateHeader &header) {
#if PALM_ENABLE_MUSASHI
  for (int i = 0; i < 8; ++i) {
    m68k_set_reg(static_cast<m68k_register_t>(M68K_REG_D0 + i), header.d[i]);
    m68k_set_reg(static_cast<m68k_register_t>(M68K_REG_A0 + i), header.a[i]);
  }
  m68k_set_reg(M68K_REG_SR, header.sr);
  m68k_set_reg(M68K_REG_USP, header.usp);
  m68k_set_reg(M68K_REG_ISP, header.isp);
  m68k_set_reg(M68K_REG_SP, header.sp);
  m68k_set_reg(M68K_REG_PC, header.pc);
#else
  (void)header;
#endif
}

static uint32_t debugCpuReg(m68k_register_t reg) {
#if PALM_ENABLE_MUSASHI
  return m68k_get_reg(nullptr, reg);
#else
  (void)reg;
  return 0;
#endif
}

static void printWakeDebugLine(const char *tag, uint16_t touchAdcX, uint16_t touchAdcY) {
#if PALM_WAKE_SERIAL_STATS
  PalmLcdState lcd = palmHwGetLcdState();
  uint32_t slicesNow = cpuSlices;
  uint32_t sliceDelta = slicesNow - lastWakeStatsSlices;
  lastWakeStatsSlices = slicesNow;
  uint16_t pll = palmHwPeekReg16(0x200);
  uint16_t imHi = palmHwPeekReg16(0x304);
  uint16_t imLo = palmHwPeekReg16(0x306);
  uint16_t stHi = palmHwPeekReg16(0x30C);
  uint16_t stLo = palmHwPeekReg16(0x30E);
  uint16_t pendHi = palmHwPeekReg16(0x310);
  uint16_t pendLo = palmHwPeekReg16(0x312);
  uint16_t tmrCtl = palmHwPeekReg16(0x600);
  uint16_t tmrPre = palmHwPeekReg16(0x602);
  uint16_t tmrCmp = palmHwPeekReg16(0x604);
  uint16_t tmrCnt = palmHwPeekReg16(0x608);
  uint16_t tmrStat = palmHwPeekReg16(0x60A);
  uint8_t portDDir = palmHwPeekReg8(0x418);
  uint8_t portDData = palmHwPeekReg8(0x419);
  uint8_t portDPol = palmHwPeekReg8(0x41C);
  uint8_t portDReq = palmHwPeekReg8(0x41D);
  uint8_t portDKbd = palmHwPeekReg8(0x41E);
  uint8_t portDEdge = palmHwPeekReg8(0x41F);
  PALM_DBG_PRINTF("DBG %s t=%lu pc=%08lx sp=%08lx sr=%04lx sleep=%u wake=%u irq=%u slices=%lu(+%lu) "
                "pll=%04x im=%04x/%04x st=%04x/%04x pend=%04x/%04x tmr=%04x/%04x/%04x/%04x/%04x "
                "pd=%02x/%02x pol=%02x req=%02x kbd=%02x edge=%02x "
                "lcd=%u/%u/%u start=%08lx bpp=%u touch=%u raw=%d,%d palm=%d,%d adc=%u,%u wakeTouch=%u power=%u\n",
                tag,
                (unsigned long)millis(),
                (unsigned long)debugCpuReg(M68K_REG_PC),
                (unsigned long)debugCpuReg(M68K_REG_SP),
                (unsigned long)(debugCpuReg(M68K_REG_SR) & 0xffffUL),
                palmHwIsAsleep() ? 1 : 0,
                palmHwHasWakeSource() ? 1 : 0,
                palmHwGetInterruptLevel(),
                (unsigned long)slicesNow,
                (unsigned long)sliceDelta,
                pll,
                imHi,
                imLo,
                stHi,
                stLo,
                pendHi,
                pendLo,
                tmrCtl,
                tmrPre,
                tmrCmp,
                tmrCnt,
                tmrStat,
                portDDir,
                portDData,
                portDPol,
                portDReq,
                portDKbd,
                portDEdge,
                lcd.valid ? 1 : 0,
                lcd.dirty ? 1 : 0,
                lcd.frameReady ? 1 : 0,
                (unsigned long)lcd.startAddr,
                lcd.bpp,
                lastTouchDown ? 1 : 0,
                lastTouchRawX,
                lastTouchRawY,
                lastTouchPalmX,
                lastTouchPalmY,
                touchAdcX,
                touchAdcY,
                wakeTouchActive ? 1 : 0,
                wakeDebugLastPowerDown ? 1 : 0);
#else
  (void)tag;
  (void)touchAdcX;
  (void)touchAdcY;
#endif
}

static void maybePrintWakeHeartbeat(unsigned long now) {
#if PALM_WAKE_SERIAL_STATS
  if (!restoredStateLoaded && !palmHwIsAsleep()) return;
  bool asleep = palmHwIsAsleep();
  if (now - lastWakeStatsMs >= 1000 || asleep != wakeDebugLastSleep) {
    lastWakeStatsMs = now;
    wakeDebugLastSleep = asleep;
    printWakeDebugLine("tick", lastTouchAdcX, lastTouchAdcY);
  }
#else
  (void)now;
#endif
}

static PalmHwSavedState hwStateFromNativeHeader(const PalmNativeStateHeader &header) {
  PalmHwSavedState hw;
  hw.lastTimerStatus = header.lastTimerStatus;
  hw.adsBitBufferIn = header.adsBitBufferIn;
  hw.adsBitBufferOut = header.adsBitBufferOut;
  hw.adsNumBitsIn = header.adsNumBitsIn;
  hw.adsPendingResult = header.adsPendingResult;
  hw.adsHavePending = header.adsHavePending;
  hw.adsCommandBitsSeen = header.adsCommandBitsSeen;
  hw.penDown = header.penDown;
  hw.penXRaw = header.penXRaw;
  hw.penYRaw = header.penYRaw;
  hw.buttonBitsDown = header.buttonBitsDown;
  hw.portDEdge = header.portDEdge;
  hw.systemCycles = header.systemCycles;
  hw.timerLastCycles = header.timerLastCycles;
  hw.lastRtcSecond = header.lastRtcSecond;
  hw.lcdDirty = header.lcdDirty;
  hw.lcdFrameReady = header.lcdFrameReady;
  return hw;
}

static void fillNativeDebugState(PalmNativeDebugState &debug, const PalmHwSavedState &hw) {
  memset(&debug, 0, sizeof(debug));
  PalmMemoryDebug memDebug = palmMemoryGetDebug();
  PalmHwDebug hwDebug = palmHwGetDebug();
  debug.ramReadCount = memDebug.mirrorReadCount;
  debug.ramWriteCount = memDebug.mirrorWriteCount;
  debug.regReadCount = hwDebug.regReadCount;
  debug.regWriteCount = hwDebug.regWriteCount;
  debug.busErrorCount = memDebug.busErrorCount;
  debug.instrBusErrorCount = memDebug.instrBusErrorCount;
  debug.lastReadAddress = memDebug.lastMirrorRead;
  debug.lastWriteAddress = memDebug.lastMirrorWrite;
  debug.lastBusErrorAddress = memDebug.lastBusError;
  debug.lastRegReadOffset = hwDebug.lastRegReadOffset;
  debug.lastRegWriteOffset = hwDebug.lastRegWriteOffset;
  debug.lastRegWriteValue = hwDebug.lastRegWriteValue;
  debug.lcdWriteCount = hwDebug.lcdWriteCount;
  debug.lastLcdWriteOffset = hwDebug.lastLcdWriteOffset;
  debug.lastLcdWriteValue = hwDebug.lastLcdWriteValue;
  debug.penXRaw = hw.penXRaw;
  debug.penYRaw = hw.penYRaw;
  debug.penDown = hw.penDown != 0 ? 1 : 0;
}

static bool fillNativeStateHeaderForSave(PalmNativeStateHeader &header,
                                         const PalmHwSavedState &hw) {
  memset(&header, 0, sizeof(header));
  header.magic = PALM_STATE_MAGIC;
  header.version = PALM_STATE_VERSION;
  header.totalSize = static_cast<uint32_t>(sizeof(PalmNativeStateHeader) +
                                           PALM_DB_REG_SIZE + palmRamSize() +
                                           palmStateExtraDeviceBytes());
  header.ramSize = palmRamSize();
  header.regSize = PALM_DB_REG_SIZE;
  header.romSize = palmRomSize();
#if PALM_ENABLE_MUSASHI
  for (int i = 0; i < 8; ++i) {
    header.d[i] = m68k_get_reg(nullptr, static_cast<m68k_register_t>(M68K_REG_D0 + i));
    header.a[i] = m68k_get_reg(nullptr, static_cast<m68k_register_t>(M68K_REG_A0 + i));
  }
  header.pc = m68k_get_reg(nullptr, M68K_REG_PC);
  header.sr = m68k_get_reg(nullptr, M68K_REG_SR);
  header.sp = m68k_get_reg(nullptr, M68K_REG_SP);
  header.usp = m68k_get_reg(nullptr, M68K_REG_USP);
  header.isp = m68k_get_reg(nullptr, M68K_REG_ISP);
#else
  return false;
#endif
  fillNativeDebugState(header.debug, hw);
  header.lastTimerStatus = hw.lastTimerStatus;
  header.adsBitBufferIn = hw.adsBitBufferIn;
  header.adsBitBufferOut = hw.adsBitBufferOut;
  header.adsNumBitsIn = hw.adsNumBitsIn;
  header.adsPendingResult = hw.adsPendingResult;
  header.adsHavePending = hw.adsHavePending;
  header.adsCommandBitsSeen = hw.adsCommandBitsSeen;
  header.penDown = hw.penDown;
  header.penXRaw = hw.penXRaw;
  header.penYRaw = hw.penYRaw;
  header.buttonBitsDown = hw.buttonBitsDown;
  header.portDEdge = hw.portDEdge;
  header.cpuInitialized = cpuReady ? 1 : 0;
  header.systemCycles = hw.systemCycles;
  header.timerLastCycles = hw.timerLastCycles;
  header.lastRtcSecond = hw.lastRtcSecond;
  header.lcdDirty = hw.lcdDirty;
  header.lcdFrameReady = hw.lcdFrameReady;
  uint32_t uartRxHead;
  uint32_t uartRxTail;
  uint32_t uartRxCount;
  uint32_t uartTxHead;
  uint32_t uartTxTail;
  uint32_t uartTxCount;
  uint32_t uartRxOverrunCount;
  uint32_t uartTxOverrunCount;
  palmHwSaveUartState(header.uartRxFifo,
                      header.uartTxFifo,
                      uartRxHead,
                      uartRxTail,
                      uartRxCount,
                      uartTxHead,
                      uartTxTail,
                      uartTxCount,
                      uartRxOverrunCount,
                      uartTxOverrunCount);
  header.uartRxHead = uartRxHead;
  header.uartRxTail = uartRxTail;
  header.uartRxCount = uartRxCount;
  header.uartTxHead = uartTxHead;
  header.uartTxTail = uartTxTail;
  header.uartTxCount = uartTxCount;
  header.uartRxOverrunCount = uartRxOverrunCount;
  header.uartTxOverrunCount = uartTxOverrunCount;
  return true;
}

static bool restoreRawRamFromFile(File &file, uint64_t fileSize) {
  if (fileSize != palmRamSize()) return false;
  if (!file.seek(0)) return false;
#if PALM_BOOT_SERIAL_STATS
  PALM_DBG_PRINTF("Raw RAM image found on SD: %lu bytes\n", (unsigned long)fileSize);
#endif
  return copyStateRamFromFile(file, static_cast<uint32_t>(fileSize));
}

static bool restoreNativeStateFromFile(File &file, uint64_t fileSize) {
  if (fileSize < sizeof(PalmNativeStateHeader)) {
#if PALM_BOOT_SERIAL_STATS
    PALM_DBG_PRINTF("State restore skipped: file too small (%lu bytes)\n", (unsigned long)fileSize);
#endif
    return false;
  }
  if (!file.seek(0) || !readExact(file, reinterpret_cast<uint8_t *>(&stateHeaderBuffer),
                                  sizeof(stateHeaderBuffer))) {
#if PALM_BOOT_SERIAL_STATS
    PALM_DBG_PRINTLN("State restore failed: could not read native header");
#endif
    return false;
  }

  const uint32_t expectedTotal =
      static_cast<uint32_t>(sizeof(PalmNativeStateHeader) + PALM_DB_REG_SIZE +
                            palmRamSize() + palmStateExtraDeviceBytes());
#if PALM_BOOT_SERIAL_STATS
  PALM_DBG_PRINTF("State header: magic=0x%08lx version=%lu total=%lu ram=%lu regs=%lu rom=%lu pc=0x%08lx\n",
                (unsigned long)stateHeaderBuffer.magic,
                (unsigned long)stateHeaderBuffer.version,
                (unsigned long)stateHeaderBuffer.totalSize,
                (unsigned long)stateHeaderBuffer.ramSize,
                (unsigned long)stateHeaderBuffer.regSize,
                (unsigned long)stateHeaderBuffer.romSize,
                (unsigned long)stateHeaderBuffer.pc);
#endif

  if (stateHeaderBuffer.magic != PALM_STATE_MAGIC ||
      stateHeaderBuffer.version != PALM_STATE_VERSION ||
      stateHeaderBuffer.totalSize != expectedTotal ||
      stateHeaderBuffer.ramSize != palmRamSize() ||
      stateHeaderBuffer.regSize != PALM_DB_REG_SIZE ||
      stateHeaderBuffer.romSize != palmRomSize() ||
      fileSize < stateHeaderBuffer.totalSize) {
#if PALM_BOOT_SERIAL_STATS
    PALM_DBG_PRINTF("State restore rejected: expected total=%lu ram=%lu regs=%lu extra=%lu rom=%lu file=%lu\n",
                  (unsigned long)expectedTotal,
                  (unsigned long)palmRamSize(),
                  (unsigned long)PALM_DB_REG_SIZE,
                  (unsigned long)palmStateExtraDeviceBytes(),
                  (unsigned long)palmRomSize(),
                  (unsigned long)fileSize);
#endif
    return false;
  }

  if (!readExact(file, stateIoBuffer, PALM_DB_REG_SIZE)) {
#if PALM_BOOT_SERIAL_STATS
    PALM_DBG_PRINTLN("State restore failed: could not read DragonBall registers");
#endif
    return false;
  }
  PalmHwSavedState hw = hwStateFromNativeHeader(stateHeaderBuffer);
  if (!palmHwLoadState(stateIoBuffer, PALM_DB_REG_SIZE, hw)) {
#if PALM_BOOT_SERIAL_STATS
    PALM_DBG_PRINTLN("State restore failed: DragonBall restore rejected");
#endif
    return false;
  }
  palmHwLoadUartState(stateHeaderBuffer.uartRxFifo,
                      stateHeaderBuffer.uartTxFifo,
                      sizeof(stateHeaderBuffer.uartRxFifo),
                      stateHeaderBuffer.uartRxHead,
                      stateHeaderBuffer.uartRxTail,
                      stateHeaderBuffer.uartRxCount,
                      stateHeaderBuffer.uartTxHead,
                      stateHeaderBuffer.uartTxTail,
                      stateHeaderBuffer.uartTxCount,
                      stateHeaderBuffer.uartRxOverrunCount,
                      stateHeaderBuffer.uartTxOverrunCount);
  if (!copyStateRamFromFile(file, stateHeaderBuffer.ramSize)) return false;
  if (!copyExtraDeviceStateFromFile(file, palmStateExtraDeviceBytes())) return false;
  restoreCpuFromState(stateHeaderBuffer);
  restoredStateLoaded = true;
  lastWakeStatsSlices = cpuSlices;
  wakeDebugLastSleep = palmHwIsAsleep();
#if PALM_BOOT_SERIAL_STATS
  PALM_DBG_PRINTF("Native state restored: PC=0x%08lx SP=0x%08lx SR=0x%04lx cpuInit=%u\n",
                (unsigned long)stateHeaderBuffer.pc,
                (unsigned long)stateHeaderBuffer.sp,
                (unsigned long)(stateHeaderBuffer.sr & 0xffffUL),
                stateHeaderBuffer.cpuInitialized);
#endif
  printWakeDebugLine("restore", stateHeaderBuffer.penXRaw, stateHeaderBuffer.penYRaw);
  return true;
}

static bool restorePalmStateFromSd() {
  pinMode(SD_CS, OUTPUT);
  digitalWrite(SD_CS, HIGH);
  sdSpi.begin(SD_SCK, SD_MISO, SD_MOSI, SD_CS);
  if (!SD.begin(SD_CS, sdSpi, PALM_STATE_SD_SPI_HZ)) {
#if PALM_BOOT_SERIAL_STATS
    PALM_DBG_PRINTLN("SD state restore skipped: SD init failed");
#endif
    return false;
  }

  File stateFile = SD.open(PALM_STATE_SD_PATH, FILE_READ);
  if (!stateFile) {
#if PALM_BOOT_SERIAL_STATS
    PALM_DBG_PRINTF("SD state restore skipped: %s not found\n", PALM_STATE_SD_PATH);
#endif
    return false;
  }

  uint64_t fileSize = stateFile.size();
#if PALM_BOOT_SERIAL_STATS
  PALM_DBG_PRINTF("SD state file found: %s, %lu bytes\n",
                PALM_STATE_SD_PATH, (unsigned long)fileSize);
#endif
  bool restored = restoreNativeStateFromFile(stateFile, fileSize);
  if (!restored) restored = restoreRawRamFromFile(stateFile, fileSize);
  stateFile.close();
  return restored;
}

static bool savePalmStateToSd() {
  if (!cpuReady || palmRamSize() == 0) return false;

  pinMode(SD_CS, OUTPUT);
  digitalWrite(SD_CS, HIGH);
  sdSpi.begin(SD_SCK, SD_MISO, SD_MOSI, SD_CS);
  if (!SD.begin(SD_CS, sdSpi, PALM_STATE_SD_SPI_HZ)) {
#if PALM_STATE_SERIAL_STATS
    PALM_DBG_PRINTLN("State save failed: SD init failed");
#endif
    return false;
  }

  PalmHwSavedState hw;
  if (!palmHwSaveState(stateIoBuffer, PALM_DB_REG_SIZE, hw) ||
      !fillNativeStateHeaderForSave(stateHeaderBuffer, hw)) {
#if PALM_STATE_SERIAL_STATS
    PALM_DBG_PRINTLN("State save failed: could not capture emulator state");
#endif
    return false;
  }

#if PALM_STATE_SERIAL_STATS
  PALM_DBG_PRINTF("State save starting: %s, total=%lu ram=%lu pc=0x%08lx\n",
                PALM_STATE_SD_PATH,
                (unsigned long)stateHeaderBuffer.totalSize,
                (unsigned long)stateHeaderBuffer.ramSize,
                (unsigned long)stateHeaderBuffer.pc);
#endif

  SD.remove(PALM_STATE_SD_TEMP_PATH);
  File stateFile = SD.open(PALM_STATE_SD_TEMP_PATH, FILE_WRITE);
  if (!stateFile) {
#if PALM_STATE_SERIAL_STATS
    PALM_DBG_PRINTF("State save failed: could not open %s\n", PALM_STATE_SD_TEMP_PATH);
#endif
    return false;
  }

  bool ok = writeExact(stateFile, reinterpret_cast<const uint8_t *>(&stateHeaderBuffer),
                       sizeof(stateHeaderBuffer)) &&
            writeExact(stateFile, stateIoBuffer, PALM_DB_REG_SIZE) &&
            copyStateRamToFile(stateFile, stateHeaderBuffer.ramSize) &&
            copyExtraDeviceStateToFile(stateFile, palmStateExtraDeviceBytes());
  stateFile.flush();
  stateFile.close();
  if (!ok) {
    SD.remove(PALM_STATE_SD_TEMP_PATH);
    return false;
  }

  SD.remove(PALM_STATE_SD_PATH);
  ok = SD.rename(PALM_STATE_SD_TEMP_PATH, PALM_STATE_SD_PATH);
#if PALM_STATE_SERIAL_STATS
  PALM_DBG_PRINTLN(ok ? "State save complete" : "State save failed: rename failed");
#endif
  if (!ok) SD.remove(PALM_STATE_SD_TEMP_PATH);
  return ok;
}

static void executePalmCpuSlice(uint32_t cycles);

static void releaseTouchForStateSave() {
  setVirtualButtonBits(0, -1, lastTouchRawX, lastTouchRawY);
  resetSaveButtonHold();
  resetResetButtonHold();
  cachedTouchDown = false;
  touchCandidateActive = false;
  lastTouchDown = false;
  wakeTouchActive = false;
  wakeDebugLastPowerDown = false;
  nativePenDown = false;
  nativePenX = -1;
  nativePenY = -1;
  nativePenAdcX = 0xffff;
  nativePenAdcY = 0xffff;
  palmHwSetPenRaw(false, 0, 0);
  palmHwSetPen(false, 0, 0);
  palmHwSetPowerButton(false);
  palmHwPrepareForSleepSnapshot();
}

static bool waitPalmSleepSettledForStateSave() {
  unsigned long startMs = millis();
  unsigned long quietStartMs = startMs;

  while (millis() - startMs < PALM_STATE_SAVE_SLEEP_TIMEOUT_MS) {
    palmHwPrepareForSleepSnapshot();
    executePalmCpuSlice(10000);
    if (palmHwIsAsleep() && palmHwSleepSnapshotInputsQuiet()) {
      if (millis() - quietStartMs >= PALM_STATE_SAVE_SLEEP_SETTLE_MS) return true;
    } else {
      quietStartMs = millis();
    }
    yield();
  }
  return palmHwIsAsleep() && palmHwSleepSnapshotInputsQuiet();
}

static bool requestPalmSleepForStateSave() {
  if (palmHwIsAsleep()) return waitPalmSleepSettledForStateSave();

#if PALM_STATE_SERIAL_STATS
  PALM_DBG_PRINTLN("State save: requesting Palm sleep");
#endif
  palmHwSetPowerButton(true);
  for (int i = 0; i < PALM_STATE_SAVE_SLEEP_PULSE_SLICES; ++i) {
    executePalmCpuSlice(10000);
    if (palmHwIsAsleep()) break;
  }
  palmHwSetPowerButton(false);
  bool settled = waitPalmSleepSettledForStateSave();
#if PALM_STATE_SERIAL_STATS
  PALM_DBG_PRINTF("State save: Palm sleep %s\n", settled ? "settled" : "timeout");
#endif
  return settled;
}

static bool wakePalmAfterStateSave() {
  if (!palmHwIsAsleep()) {
    unsigned long settleStart = millis();
    while (millis() - settleStart < 1000) {
      executePalmCpuSlice(10000);
      if (palmHwIsAsleep()) break;
    }
    if (!palmHwIsAsleep()) return true;
  }

#if PALM_STATE_SERIAL_STATS
  PALM_DBG_PRINTLN("State save: waking Palm");
#endif

  palmHwPrepareForSleepSnapshot();
  for (int attempt = 0; attempt < 3; ++attempt) {
#if PALM_STATE_SERIAL_STATS
    PALM_DBG_PRINTF("State save: wake attempt %d\n", attempt + 1);
#endif
    palmHwSetPowerButton(true);
    for (int i = 0; i < PALM_STATE_SAVE_WAKE_PULSE_SLICES; ++i) {
      executePalmCpuSlice(10000);
      if (!palmHwIsAsleep()) break;
    }
    palmHwSetPowerButton(false);

    unsigned long settleStart = millis();
    while (millis() - settleStart < 1000) {
      executePalmCpuSlice(10000);
      if (palmHwIsAsleep()) break;
      yield();
    }
    if (!palmHwIsAsleep()) break;
  }
#if PALM_STATE_SERIAL_STATS
  PALM_DBG_PRINTF("State save: Palm wake %s\n", !palmHwIsAsleep() ? "OK" : "timeout");
#endif
  return !palmHwIsAsleep();
}

static void servicePalmStateSaveRequest() {
  if (!palmStateSaveRequested || palmStateSaveInProgress) return;
  palmStateSaveRequested = false;
  palmStateSaveInProgress = true;
  touchInputDisabled = true;
  releaseTouchForStateSave();
  if (palmLowPowerModeActive) setPalmLowPowerMode(false);
#if defined(ESP32)
  setCpuFrequencyMhz(ESP32_ACTIVE_CPU_MHZ);
#endif
  setPanelOutputEnabled(true);
  setPalmBacklightDuty();
  bool sleptForSave = requestPalmSleepForStateSave();
  setPanelOutputEnabled(false);
  setBacklightDuty(BACKLIGHT_SAVE_DUTY);
  bool saveOk = false;
  if (sleptForSave) {
    palmHwPrepareForSleepSnapshot();
    saveOk = savePalmStateToSd();
  } else {
#if PALM_STATE_SERIAL_STATS
    PALM_DBG_PRINTLN("State save skipped: Palm sleep did not settle");
#endif
  }
  bool wakeOk = true;
  if (sleptForSave || palmHwIsAsleep()) wakeOk = wakePalmAfterStateSave();
#if PALM_STATE_SERIAL_STATS
  PALM_DBG_PRINTF("State save result: save=%s wake=%s asleep=%u\n",
                saveOk ? "OK" : "NO",
                wakeOk ? "OK" : "NO",
                palmHwIsAsleep() ? 1 : 0);
#endif
  if (wakeOk && !palmHwIsAsleep()) {
    setPanelOutputEnabled(true);
    setPalmBacklightDuty();
  } else {
    setPanelOutputEnabled(false);
    setBacklightDuty(0);
  }
  lastFrameMs = 0;
  touchInputDisabled = false;
  saveButtonHoldActive = true;
  saveButtonHoldTriggered = true;
  saveButtonHoldStartMs = millis();
  palmStateSaveInProgress = false;
}

static void servicePalmOsResetRequest() {
  if (!palmOsResetRequested || palmOsResetInProgress || palmStateSaveInProgress) return;
  palmOsResetRequested = false;
  palmOsResetInProgress = true;
  touchInputDisabled = true;
  releaseTouchForStateSave();
  if (palmLowPowerModeActive) setPalmLowPowerMode(false);
#if defined(ESP32)
  setCpuFrequencyMhz(ESP32_ACTIVE_CPU_MHZ);
#endif
  setPanelOutputEnabled(true);
  setPalmBacklightDuty();

#if PALM_STATE_SERIAL_STATS
  PALM_DBG_PRINTLN("Palm OS reset starting");
#endif
  cpuReady = false;
  palmHwInit();
  lastPalmBacklightDuty = 0xff;
  initCpu();
  cpuReady = true;
  restoredStateLoaded = false;
  palmLowPowerModeActive = false;
  palmLowPowerWakeServiceActive = false;
  palmLowPowerRequireTouchRelease = false;
  wakeTouchActive = false;
  wakeDebugLastTouchDown = false;
  wakeDebugLastPowerDown = false;
  wakeDebugLastSleep = false;
  lastFrameMs = 0;
  lastWakeStatsSlices = cpuSlices;
#if PALM_STATE_SERIAL_STATS
  PALM_DBG_PRINTLN("Palm OS reset complete");
#endif

  touchInputDisabled = false;
  resetButtonHoldActive = true;
  resetButtonHoldTriggered = true;
  resetButtonHoldStartMs = millis();
  palmOsResetInProgress = false;
}

static bool timeReached(unsigned long now, unsigned long target) {
  return static_cast<long>(now - target) >= 0;
}

static void executePalmCpuSlice(uint32_t cycles) {
#if PALM_ENABLE_MUSASHI
  if (!cpuReady || cycles == 0) return;

  palmHwCycle();
  uint8_t irqLevel = palmHwGetInterruptLevel();
  m68k_set_irq(irqLevel);
  uint32_t elapsedCycles = cycles;
  if (!palmHwIsAsleep() || irqLevel > 0 || palmHwHasWakeSource()) {
    int usedCycles = m68k_execute(cycles);
    if (usedCycles <= 0) {
      elapsedCycles = 0;
    } else if (static_cast<uint32_t>(usedCycles) < cycles) {
      elapsedCycles = static_cast<uint32_t>(usedCycles);
    }
  }
  palmHwAdvanceCycles(elapsedCycles);
  palmHwCycle();
  m68k_set_irq(palmHwGetInterruptLevel());
  ++cpuSlices;
#if PALM_PERF_SERIAL_STATS
  cpuExecutedCycles += elapsedCycles;
#endif
  if ((cpuSlices & 0x0f) == 0) yield();
#else
  (void)cycles;
#endif
}

static void runPalmCpuForBudget(unsigned long budgetMs) {
#if PALM_ENABLE_MUSASHI
  if (!cpuReady || budgetMs == 0) return;

  unsigned long cpuStartMs = millis();
  do {
    executePalmCpuSlice(PALM_CPU_SLICE_CYCLES);
    if (palmHwIsAsleep() && palmHwGetInterruptLevel() == 0 && !palmHwHasWakeSource()) break;
  } while (millis() - cpuStartMs < budgetMs);
#else
  (void)budgetMs;
#endif
}

static void maybePrintPerfStats(unsigned long now) {
#if PALM_PERF_SERIAL_STATS
  if (lastPerfStatsMs == 0) {
    lastPerfStatsMs = now;
    lastPerfCpuSlices = cpuSlices;
    lastPerfRenderFrames = renderFrames;
    lastPerfCpuExecutedCycles = cpuExecutedCycles;
    lastPerfRenderMeasuredFrames = renderMeasuredFrames;
    lastPerfRenderTotalMicros = renderTotalMicros;
    return;
  }

  unsigned long elapsedMs = now - lastPerfStatsMs;
  if (elapsedMs < 2000) return;

  uint32_t sliceDelta = cpuSlices - lastPerfCpuSlices;
  uint32_t renderDelta = renderFrames - lastPerfRenderFrames;
  uint64_t cycleDelta = cpuExecutedCycles - lastPerfCpuExecutedCycles;
  uint32_t measuredRenderDelta = renderMeasuredFrames - lastPerfRenderMeasuredFrames;
  uint32_t renderMicrosDelta = renderTotalMicros - lastPerfRenderTotalMicros;
  uint32_t avgRenderMicros = measuredRenderDelta == 0 ? 0 : renderMicrosDelta / measuredRenderDelta;

  PALM_DBG_PRINTF("PERF t=%lu burst=%u slice/s=%lu cyc/s=%llu render/s=%lu render_us=%lu/%lu touch=%u sleep=%u\n",
                (unsigned long)now,
                PALM_CPU_BURST_MS,
                (unsigned long)((static_cast<uint64_t>(sliceDelta) * 1000ULL) / elapsedMs),
                static_cast<unsigned long long>((cycleDelta * 1000ULL) / elapsedMs),
                (unsigned long)((static_cast<uint64_t>(renderDelta) * 1000ULL) / elapsedMs),
                (unsigned long)avgRenderMicros,
                (unsigned long)renderMaxMicros,
                lastTouchDown ? 1 : 0,
                palmHwIsAsleep() ? 1 : 0);

  lastPerfStatsMs = now;
  lastPerfCpuSlices = cpuSlices;
  lastPerfRenderFrames = renderFrames;
  lastPerfCpuExecutedCycles = cpuExecutedCycles;
  lastPerfRenderMeasuredFrames = renderMeasuredFrames;
  lastPerfRenderTotalMicros = renderTotalMicros;
  renderMaxMicros = 0;
#else
  (void)now;
#endif
}

static void autoWakeRestoredPalm() {
#if PALM_AUTO_WAKE_RESTORED_STATE && PALM_ENABLE_MUSASHI
  if (!cpuReady || !restoredStateLoaded || !palmHwIsAsleep()) return;

  palmHwSetPenRaw(false, 0, 0);
  palmHwPrepareForSleepSnapshot();
  nativePenDown = false;
  wakeTouchActive = false;
  printWakeDebugLine("auto-before", lastTouchAdcX, lastTouchAdcY);

  palmHwSetPowerButton(true);
  wakeDebugLastPowerDown = true;
  printWakeDebugLine("auto-down", lastTouchAdcX, lastTouchAdcY);

  for (int i = 0; i < 160; ++i) {
    executePalmCpuSlice(10000);
    if (i == 0 || i == 7 || i == 39 || !palmHwIsAsleep()) {
      printWakeDebugLine("auto-service", lastTouchAdcX, lastTouchAdcY);
    }
    if (!palmHwIsAsleep()) break;
  }

  palmHwSetPowerButton(false);
  wakeDebugLastPowerDown = false;
  printWakeDebugLine("auto-up", lastTouchAdcX, lastTouchAdcY);

  for (int i = 0; i < 24; ++i) {
    executePalmCpuSlice(10000);
  }
  printWakeDebugLine("auto-settle", lastTouchAdcX, lastTouchAdcY);
#endif
}

void setup() {
  initPins();
  initSerialDebug();
  bool renderBuffersOk = initRenderBuffers();
#if PALM_BOOT_SERIAL_STATS
  PALM_DBG_PRINTF("Heap before Palm RAM: free=%lu max=%lu\n",
                (unsigned long)ESP.getFreeHeap(),
                (unsigned long)ESP.getMaxAllocHeap());
#if defined(ESP32)
  PALM_DBG_PRINTF("Render buffers: surface=%p lcd=%p %s\n",
                palmSurface,
                palmPanelLcd,
                renderBuffersOk ? "OK" : "FAILED");
  PALM_DBG_PRINTF("Panel frames: a=%p b=%p %s\n",
                panelFrames[0],
                panelFrames[1],
                panelFramesInInternalRam ? "INTERNAL" : "PSRAM");
  PALM_DBG_PRINTF("RGB timing: pclk=%ld edge=%s idle=%s bounce=%u h=%d/%d/%d v=%d/%d/%d\n",
                (long)RGB_PANEL_PIXEL_CLOCK_HZ,
                RGB_PANEL_PCLK_ACTIVE_NEG ? "neg" : "pos",
                RGB_PANEL_PCLK_IDLE_HIGH ? "high" : "low",
                (unsigned)RGB_PANEL_BOUNCE_BUFFER_PX,
                RGB_PANEL_HSYNC_FRONT_PORCH,
                RGB_PANEL_HSYNC_PULSE_WIDTH,
                RGB_PANEL_HSYNC_BACK_PORCH,
                RGB_PANEL_VSYNC_FRONT_PORCH,
                RGB_PANEL_VSYNC_PULSE_WIDTH,
                RGB_PANEL_VSYNC_BACK_PORCH);
  PALM_DBG_PRINTF("8-bit heap before Palm RAM: free=%lu max=%lu\n",
                (unsigned long)heap_caps_get_free_size(MALLOC_CAP_8BIT),
                (unsigned long)heap_caps_get_largest_free_block(MALLOC_CAP_8BIT));
#if defined(MALLOC_CAP_SPIRAM)
  PALM_DBG_PRINTF("PSRAM before Palm RAM: free=%lu max=%lu\n",
                (unsigned long)heap_caps_get_free_size(MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT),
                (unsigned long)heap_caps_get_largest_free_block(MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT));
#endif
#endif
#endif

  if (!renderBuffersOk) {
#if PALM_BOOT_SERIAL_STATS
    PALM_DBG_PRINTLN("Render buffer allocation failed");
#endif
    return;
  }

#if PALM_STATIC_DISPLAY_TEST
  initDisplay();
  drawStaticDisplayTest();
#if PALM_BOOT_SERIAL_STATS
  PALM_DBG_PRINTLN("Static display test active; Palm emulator is not running.");
#endif
  return;
#endif

  bool memoryOk = palmMemoryInit();

#if PALM_BOOT_SERIAL_STATS
  PALM_DBG_PRINTLN();
  PALM_DBG_PRINTLN("ESP32-PALM bring-up");
  PALM_DBG_PRINTF("ROM base: 0x%08lx, ROM bytes: %lu\n", (unsigned long)PALM_ROM_BASE, (unsigned long)palmRomSize());
  PALM_DBG_PRINTF("Initial SP: 0x%08lx\n", (unsigned long)palmRead32(PALM_ROM_BASE));
  PALM_DBG_PRINTF("Initial PC: 0x%08lx\n", (unsigned long)palmRead32(PALM_ROM_BASE + 4));
  PALM_DBG_PRINTF("Palm RAM logical: %lu bytes, alloc target: %lu bytes, allocated: %lu bytes %s\n",
                (unsigned long)PALM_RAM_LOGICAL_SIZE,
                (unsigned long)PALM_RAM_ALLOC_TARGET_SIZE,
                (unsigned long)(memoryOk ? palmRamSize() : palmRamLastAllocAttemptSize()),
                memoryOk ? "OK" : "FAILED");
  PALM_DBG_PRINTF("Palm RAM segments: %lu\n",
                (unsigned long)palmRamLastAllocAttemptSegments());
  PALM_DBG_PRINTF("Heap after Palm RAM: free=%lu max=%lu\n",
                (unsigned long)ESP.getFreeHeap(),
                (unsigned long)ESP.getMaxAllocHeap());
#if defined(ESP32) && defined(MALLOC_CAP_SPIRAM)
  PALM_DBG_PRINTF("PSRAM after Palm RAM: free=%lu max=%lu\n",
                (unsigned long)heap_caps_get_free_size(MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT),
                (unsigned long)heap_caps_get_largest_free_block(MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT));
#endif
#endif

  initTouch();
#if PALM_BOOT_SERIAL_STATS
  PALM_DBG_PRINTLN("Touch initialized");
#endif
  initDisplay();
#if PALM_BOOT_SERIAL_STATS
  PALM_DBG_PRINTLN("LCD initialized");
  PALM_DBG_PRINTF("Render task: %s core=%d\n",
                renderTaskHandle != nullptr ? "core0" : "sync",
                renderTaskHandle != nullptr ? PALM_RENDER_TASK_CORE : -1);
#endif
  drawBootPattern();
#if PALM_BOOT_SERIAL_STATS
  PALM_DBG_PRINTLN("Boot pattern drawn");
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
    PALM_DBG_PRINTLN("CPU init starting");
#endif
    initCpu();
    bool stateRestored = restorePalmStateFromSd();
    cpuReady = true;
    if (stateRestored) {
      autoWakeRestoredPalm();
      drawPalmFrameFromEmulatedLcd();
    }
#if PALM_BOOT_SERIAL_STATS
    PALM_DBG_PRINTLN("CPU init complete");
#endif
    releaseSerialDebugForPalmUart();
  }
}

void loop() {
  updatePalmLowPowerMode();

#if PALM_TOUCH_USE_EVENT_QUEUE
  int palmX;
  int palmY;
  bool touchDown = getPalmTouch(palmX, palmY);
  if (wakeTouchActive || (touchDown && palmHwIsAsleep())) {
    if (nativePenDown) {
      enqueuePalmOsPenPoint(false, nativePenX, nativePenY);
      nativePenDown = false;
      nativePenX = -1;
      nativePenY = -1;
    }
    setPalmPowerButtonFromTouch(touchDown);
    if (touchDown != wakeDebugLastTouchDown || touchDown != wakeDebugLastPowerDown) {
      wakeDebugLastTouchDown = touchDown;
      wakeDebugLastPowerDown = touchDown;
      printWakeDebugLine(touchDown ? "wake-down" : "wake-up",
                         touchDown ? palmAdcX : lastTouchAdcX,
                         touchDown ? palmAdcY : lastTouchAdcY);
    }
    wakeTouchActive = touchDown;
  } else if (touchDown) {
    if (!nativePenDown ||
        abs(palmX - nativePenX) >= 2 ||
        abs(palmY - nativePenY) >= 2) {
      enqueuePalmOsPenPoint(true, palmX, palmY);
      nativePenDown = true;
      nativePenX = palmX;
      nativePenY = palmY;
    }
  } else if (nativePenDown) {
    enqueuePalmOsPenPoint(false, nativePenX, nativePenY);
    nativePenDown = false;
    nativePenX = -1;
    nativePenY = -1;
    setPalmPowerButtonFromTouch(false);
  } else {
    setPalmPowerButtonFromTouch(false);
  }
#elif PALM_TOUCH_USE_RAW_ADC
  uint16_t palmAdcX;
  uint16_t palmAdcY;
  bool touchDown = getPalmAdcTouch(palmAdcX, palmAdcY);
  if (wakeTouchActive || (touchDown && palmHwIsAsleep())) {
    if (nativePenDown) {
      palmHwSetPenRaw(false, 0, 0);
      logTouchEdge("wake", false, nativePenAdcX, nativePenAdcY);
      nativePenDown = false;
    }
    setPalmPowerButtonFromTouch(touchDown);
    wakeTouchActive = touchDown;
  } else if (touchDown) {
    int dx = static_cast<int>(palmAdcX) - static_cast<int>(nativePenAdcX);
    int dy = static_cast<int>(palmAdcY) - static_cast<int>(nativePenAdcY);
    if (!nativePenDown || abs(dx) >= PEN_MOVE_ADC_THRESHOLD || abs(dy) >= PEN_MOVE_ADC_THRESHOLD) {
      palmHwSetPenRaw(true, palmAdcX, palmAdcY);
      logTouchEdge("pen", true, palmAdcX, palmAdcY);
      nativePenDown = true;
      nativePenAdcX = palmAdcX;
      nativePenAdcY = palmAdcY;
    }
  } else if (nativePenDown) {
    palmHwSetPenRaw(false, 0, 0);
    logTouchEdge("pen", false, nativePenAdcX, nativePenAdcY);
    nativePenDown = false;
    nativePenAdcX = 0xffff;
    nativePenAdcY = 0xffff;
    setPalmPowerButtonFromTouch(false);
    if (wakeDebugLastPowerDown) {
      wakeDebugLastPowerDown = false;
      printWakeDebugLine("power-up", lastTouchAdcX, lastTouchAdcY);
    }
  } else {
    setPalmPowerButtonFromTouch(false);
    if (wakeDebugLastPowerDown) {
      wakeDebugLastPowerDown = false;
      printWakeDebugLine("power-up", lastTouchAdcX, lastTouchAdcY);
    }
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
    setPalmPowerButtonFromTouch(touchDown);
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
    setPalmPowerButtonFromTouch(false);
  } else {
    setPalmPowerButtonFromTouch(false);
  }
#endif

  servicePalmStateSaveRequest();
  servicePalmOsResetRequest();
  updatePalmLowPowerMode();
  updatePalmBacklightFromOs();
  servicePalmUartBridge();

  unsigned long now = millis();
  maybePrintWakeHeartbeat(now);
  maybePrintPerfStats(now);
  unsigned long nextFrameMs = lastFrameMs + PALM_LCD_REDRAW_INTERVAL_MS;
  if (!palmLowPowerModeActive && (lastFrameMs == 0 || timeReached(now, nextFrameMs))) {
    lastFrameMs = now;
    drawPalmFrameFromEmulatedLcd();
    now = millis();
    nextFrameMs = lastFrameMs + PALM_LCD_REDRAW_INTERVAL_MS;
  }

  if (palmLowPowerModeActive) {
    palmHwCycle();
    bool needsWakeService = palmLowPowerNeedsWakeService();
    if (needsWakeService) {
      setPalmLowPowerWakeService(true);
      runPalmCpuForBudget(PALM_CPU_BURST_MS);
      if (!palmHwIsAsleep()) {
        setPalmLowPowerMode(false);
      } else if (!wakeTouchActive && palmHwGetInterruptLevel() == 0 && !palmHwHasWakeSource()) {
        setPalmLowPowerWakeService(false);
        enterPalmDeepIdle();
      }
    } else {
      setPalmLowPowerWakeService(false);
      enterPalmDeepIdle();
    }
  } else {
    long msUntilNextFrame = static_cast<long>(nextFrameMs - now);
    if (msUntilNextFrame > 0) {
      unsigned long cpuBudgetMs = static_cast<unsigned long>(msUntilNextFrame);
      if (cpuBudgetMs > PALM_CPU_BURST_MS) cpuBudgetMs = PALM_CPU_BURST_MS;
      runPalmCpuForBudget(cpuBudgetMs);
    }
  }
  servicePalmUartBridge();

#if PALM_RUNTIME_SERIAL_STATS
  if (now - lastStatsMs >= 1000) {
    lastStatsMs = now;
#if PALM_ENABLE_MUSASHI
    if (!cpuReady) {
      PALM_DBG_PRINTF("CPU stopped, Palm RAM allocation failed. attempted=%lu seg=%lu free=%lu\n",
                    (unsigned long)palmRamLastAllocAttemptSize(),
                    (unsigned long)palmRamLastAllocAttemptSegments(),
                    (unsigned long)ESP.getFreeHeap());
#if defined(ESP32)
      PALM_DBG_PRINTF("8-bit heap now: free=%lu\n",
                    (unsigned long)heap_caps_get_free_size(MALLOC_CAP_8BIT));
#endif
      return;
    }

    PalmLcdState lcd = palmHwGetLcdState();
    uint8_t irq = palmHwGetInterruptLevel();
    PALM_DBG_PRINTF("PC=%08x irq=%u slices=%lu render=%lu t=%04x/%04x/%04x im=%04x/%04x is=%04x/%04x lcd=%u/%u/%u bpp=%u panel=%02x pitch=%u touch=%u raw=%d,%d scr=%d,%d palm=%d,%d adc=%u,%u\n",
                  m68k_get_reg(nullptr, M68K_REG_PC),
                  irq,
                  (unsigned long)cpuSlices,
                  (unsigned long)renderFrames,
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
                  lcd.bpp,
                  lcd.panelControl,
                  lcd.bytesPerLine,
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
    PALM_DBG_PRINTF("Touch framebuffer alive, free heap=%lu\n", (unsigned long)ESP.getFreeHeap());
#endif
  }
#endif

  yield();
}
