#include "palm_hw.h"

#include <string.h>
#if defined(ESP32)
#include <esp_timer.h>
#include <sys/time.h>
#endif

static uint8_t dbRegs[PALM_DB_REG_SIZE];
static bool lcdDirty = true;
static bool lcdFrameReady = true;
static uint32_t lcdDirtyGeneration = 1;
static uint32_t lcdLastDirtyMillis = 0;
static bool lcdWriteRangeValid = false;
static uint32_t lcdWriteRangeStart = 0;
static uint32_t lcdWriteRangeEnd = 0;
static PalmHwDebug hwDebug;
static uint16_t lastTimerStatus = 0;
static uint64_t systemCycles = 0;
static double timerLastCycles = 0.0;
static uint64_t timerLastHostMicros = 0;
static uint64_t cyclePaceHostMicros = 0;
static uint64_t cyclePaceBaseCycles = 0;
static uint32_t lastRtcSecond = 0xffffffffUL;
static uint32_t adsBitBufferIn = 0;
static uint16_t adsBitBufferOut = 0;
static int adsNumBitsIn = 0;
static uint16_t adsPendingResult = 0;
static bool adsHavePending = false;
static int adsCommandBitsSeen = 0;
static bool penDown = false;
static uint16_t penXRaw = 0;
static uint16_t penYRaw = 0;
static uint16_t buttonBitsDown = 0;
static bool cradleButtonLineLow = false;
static uint8_t portDEdge = 0;
static uint8_t uartRxFifo[PALM_UART_FIFO_SIZE];
static uint8_t uartTxFifo[PALM_UART_FIFO_SIZE];
static uint32_t uartRxHead = 0;
static uint32_t uartRxTail = 0;
static uint32_t uartRxCount = 0;
static uint32_t uartTxHead = 0;
static uint32_t uartTxTail = 0;
static uint32_t uartTxCount = 0;
static uint32_t uartRxOverrunCount = 0;
static uint32_t uartTxOverrunCount = 0;
static bool uartIrdaEnabledLast = false;
static uint16_t uartIrdaProbeEchoBudget = 0;
static uint32_t uartIrdaProbeEchoUntilMs = 0;
static uint8_t displayBrightnessLevel = 255;
#if PALM_CONTRAST_SERIAL_STATS
static uint16_t lastContrastTraceValue = 0xffff;
#endif

static constexpr uint8_t PORT_D_POWER_FAIL = 0x80;
static constexpr uint8_t PORT_F_BACKLIGHT_ON = 0x20;
static constexpr uint8_t M100_CONTRAST_LEVEL_MIN = 0x80;
static constexpr uint8_t M100_CONTRAST_LEVEL_MAX = 0xaa;
static constexpr uint16_t INT_HI_PEN = 0x0010;
static constexpr uint16_t INT_HI_IRQ6 = 0x0008;
static constexpr uint16_t INT_HI_IRQ3 = 0x0004;
static constexpr uint16_t INT_HI_IRQ2 = 0x0002;
static constexpr uint16_t INT_HI_IRQ1 = 0x0001;
static constexpr uint16_t INT_HI_EMU = 0x0080;
static constexpr uint16_t INT_HI_SAMPLE_TIMER = 0x0040;
static constexpr uint16_t INT_LO_SPIM = 0x0001;
static constexpr uint16_t INT_LO_TIMER = 0x0002;
static constexpr uint16_t INT_LO_UART = 0x0004;
static constexpr uint16_t INT_LO_WDT = 0x0008;
static constexpr uint16_t INT_LO_RTC = 0x0010;
static constexpr uint16_t INT_LO_KBD = 0x0040;
static constexpr uint16_t INT_LO_PWM = 0x0080;
static constexpr uint16_t INT_LO_INT0 = 0x0100;
static constexpr uint16_t INT_LO_INT1 = 0x0200;
static constexpr uint16_t INT_LO_INT2 = 0x0400;
static constexpr uint16_t INT_LO_INT3 = 0x0800;
static constexpr uint16_t INT_LO_ALL_KEYS = 0x0f00;
static constexpr uint16_t ICR_POL1 = 0x8000;
static constexpr uint16_t ICR_ET1 = 0x0800;
static constexpr uint16_t SPIM_ENABLE = 0x0200;
static constexpr uint16_t SPIM_EXCHANGE = 0x0100;
static constexpr uint16_t SPIM_INT_STATUS = 0x0080;
static constexpr uint16_t SPIM_INT_ENABLE = 0x0040;
static constexpr uint16_t UART_CONTROL_ENABLE = 0x8000;
static constexpr uint16_t UART_CONTROL_RX_ENABLE = 0x4000;
static constexpr uint16_t UART_CONTROL_TX_ENABLE = 0x2000;
static constexpr uint16_t UART_CONTROL_RX_FULL_INT_ENABLE = 0x0020;
static constexpr uint16_t UART_CONTROL_RX_HALF_INT_ENABLE = 0x0010;
static constexpr uint16_t UART_CONTROL_RX_RDY_INT_ENABLE = 0x0008;
static constexpr uint16_t UART_CONTROL_TX_EMPTY_INT_ENABLE = 0x0004;
static constexpr uint16_t UART_CONTROL_TX_HALF_INT_ENABLE = 0x0002;
static constexpr uint16_t UART_CONTROL_TX_AVAIL_INT_ENABLE = 0x0001;
static constexpr uint16_t UART_RX_FIFO_FULL = 0x8000;
static constexpr uint16_t UART_RX_FIFO_HALF = 0x4000;
static constexpr uint16_t UART_RX_DATA_READY = 0x2000;
static constexpr uint16_t UART_TX_FIFO_EMPTY = 0x8000;
static constexpr uint16_t UART_TX_FIFO_HALF = 0x4000;
static constexpr uint16_t UART_TX_AVAILABLE = 0x2000;
static constexpr uint16_t UART_TX_IGNORE_CTS = 0x0800;
static constexpr uint16_t UART_MISC_IRDA_ENABLE = 0x0020;
static constexpr uint8_t IRDA_SIR_BOF = 0xc0;
static constexpr uint8_t IRDA_SIR_EOF = 0xc1;
static constexpr uint8_t IRDA_SIR_ESCAPE = 0x7d;
static constexpr uint16_t TMR_STATUS_COMPARE = 0x0001;
static constexpr uint16_t TMR_CONTROL_ENABLE = 0x0001;
static constexpr uint16_t TMR_CONTROL_INT_ENABLE = 0x0010;
static constexpr uint16_t PLL_CONTROL_DISABLE = 0x0008;
static constexpr uint16_t RTC_CONTROL_ENABLE = 0x0080;
static constexpr uint16_t RTC_INT_STOPWATCH = 0x0001;
static constexpr uint16_t RTC_INT_MINUTE = 0x0002;
static constexpr uint16_t RTC_INT_ALARM = 0x0004;
static constexpr uint16_t RTC_INT_24HR = 0x0008;
static constexpr uint16_t RTC_INT_SECOND = 0x0010;
static constexpr uint16_t RTC_INT_HOUR = 0x0020;
static constexpr uint16_t KEY_BIT_POWER = 0x0001;
static constexpr uint16_t KEY_BIT_PAGE_UP = 0x0002;
static constexpr uint16_t KEY_BIT_PAGE_DOWN = 0x0004;
static constexpr uint16_t KEY_BIT_HARD1 = 0x0008;
static constexpr uint16_t KEY_BIT_HARD2 = 0x0010;
static constexpr uint16_t KEY_BIT_HARD3 = 0x0020;
static constexpr uint16_t KEY_BIT_HARD4 = 0x0040;
static constexpr uint16_t KEY_BIT_CONTRAST = 0x0080;
static constexpr uint8_t PORT_G_ID_DETECT = 0x04;
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_M100_EXPERIMENTAL
static constexpr uint8_t HARDWARE_ID_KEY_STATE = 0xfa;  // m100 ID1 and ID3 active-low.
#elif PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
static constexpr uint8_t HARDWARE_ID_KEY_STATE = 0xf6;  // IIIc/Austin ID1 and ID4 active-low.
#else
static constexpr uint8_t HARDWARE_ID_KEY_STATE = 0xf2;  // IIIx/Brad ID1, ID3, and ID4 active-low.
#endif
static constexpr uint16_t PEN_RAW_LEFT = 3800;
static constexpr uint16_t PEN_RAW_RIGHT = 300;
static constexpr uint16_t PEN_RAW_TOP = 3800;
static constexpr uint16_t PEN_RAW_BOTTOM = 300;
static constexpr uint16_t PEN_DIGITIZER_MAX_X = PALM_DIGITIZER_W - 1;
static constexpr uint16_t PEN_DIGITIZER_MAX_Y = 219;

static void put16(uint16_t offset, uint16_t value) {
  dbRegs[offset] = value >> 8;
  dbRegs[offset + 1] = value & 0xff;
}

static void put32(uint16_t offset, uint32_t value) {
  dbRegs[offset] = value >> 24;
  dbRegs[offset + 1] = (value >> 16) & 0xff;
  dbRegs[offset + 2] = (value >> 8) & 0xff;
  dbRegs[offset + 3] = value & 0xff;
}

static uint16_t get16(uint16_t offset) {
  return (static_cast<uint16_t>(dbRegs[offset]) << 8) | dbRegs[offset + 1];
}

static uint32_t get32(uint16_t offset) {
  return (static_cast<uint32_t>(get16(offset)) << 16) | get16(offset + 2);
}

static uint64_t hostMonotonicMicros() {
#if defined(ESP32)
  return static_cast<uint64_t>(esp_timer_get_time());
#else
  return static_cast<uint64_t>(micros());
#endif
}

static bool irq1IsEdgeTriggered() {
  return (get16(0x302) & ICR_ET1) != 0;
}

static bool cradleButtonIrq1Asserted() {
  bool activeHigh = (get16(0x302) & ICR_POL1) != 0;
  return activeHigh ? !cradleButtonLineLow : cradleButtonLineLow;
}

static void updateCradleIrq1Level() {
  if (irq1IsEdgeTriggered()) return;
  if (cradleButtonIrq1Asserted()) {
    put16(0x310, get16(0x310) | INT_HI_IRQ1);
  } else {
    put16(0x310, get16(0x310) & ~INT_HI_IRQ1);
  }
}

static void updateInterruptStatus() {
  updateCradleIrq1Level();
  put16(0x30C, get16(0x310) & ~get16(0x304));
  put16(0x30E, get16(0x312) & ~get16(0x306));
}

bool palmHwHasWakeSource() {
  uint16_t hiPending = get16(0x310);
  uint16_t loPending = get16(0x312);
  uint16_t wakeHi = INT_HI_PEN | INT_HI_IRQ6 | INT_HI_IRQ1 | INT_HI_EMU;
  uint16_t wakeLo = INT_LO_TIMER | INT_LO_RTC | INT_LO_KBD | INT_LO_INT0 | INT_LO_INT1 |
                    INT_LO_INT2 | INT_LO_INT3;
  return ((hiPending & wakeHi) != 0) || ((loPending & wakeLo) != 0);
}

static void markLcdDirty() {
  if (!lcdDirty || lcdFrameReady) {
    lcdLastDirtyMillis = millis();
  }
  lcdDirty = true;
  lcdFrameReady = false;
  ++lcdDirtyGeneration;
}

static void updateLcdWriteRange() {
  uint32_t start = get32(0xA00) & 0x1ffffffeUL;
  uint16_t width = get16(0xA08);
  uint16_t height = get16(0xA0A) + 1;
  uint16_t bytesPerLine = static_cast<uint16_t>(dbRegs[0xA05]) * 2;
  uint8_t bpp = 1 << (dbRegs[0xA20] & 0x03);
  uint32_t frameBytes = static_cast<uint32_t>(bytesPerLine) * height;

  lcdWriteRangeValid = start != 0 && width > 0 && width <= 320 &&
                       height > 0 && height <= 320 &&
                       bytesPerLine > 0 && bpp <= 4 &&
                       frameBytes > 0 &&
                       start <= 0xffffffffUL - frameBytes;
  lcdWriteRangeStart = start;
  lcdWriteRangeEnd = start + frameBytes;
}

uint8_t palmHwGetInterruptLevel() {
  uint16_t hi = get16(0x30C);
  uint16_t lo = get16(0x30E);

  if (hi & INT_HI_EMU) return 7;
  if ((hi & INT_HI_IRQ6) || (lo & (INT_LO_TIMER | INT_LO_PWM))) return 6;
  if (hi & INT_HI_PEN) return 5;
  if ((lo & (INT_LO_SPIM | INT_LO_UART | INT_LO_WDT | INT_LO_RTC | INT_LO_KBD |
             INT_LO_INT3 | INT_LO_INT2 | INT_LO_INT1 | INT_LO_INT0)) ||
      (hi & INT_HI_SAMPLE_TIMER)) {
    return 4;
  }
  if (hi & INT_HI_IRQ3) return 3;
  if (hi & INT_HI_IRQ2) return 2;
  if (hi & INT_HI_IRQ1) return 1;
  return 0;
}

bool palmHwIsAsleep() {
  return (get16(0x200) & PLL_CONTROL_DISABLE) != 0;
}

static double systemClockFrequency() {
  return PALM_SYSTEM_CLOCK_HZ;
}

static void updateDisplayBrightnessLevel() {
#if PALM_HAS_SED1375
  // Palm IIIc brightness is not wired to the DragonBall contrast register.
  // Keep it fixed until the real ROM preference/hardware path is mapped.
  displayBrightnessLevel = 255;
#else
  uint16_t contrast = get16(0xA36);
  uint8_t lowByte = contrast & 0xff;
  uint8_t rawLevel = lowByte != 0 ? lowByte : ((contrast >> 8) & 0xff);
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_M100_EXPERIMENTAL
  if (rawLevel <= M100_CONTRAST_LEVEL_MIN) {
    displayBrightnessLevel = 0;
  } else if (rawLevel >= M100_CONTRAST_LEVEL_MAX) {
    displayBrightnessLevel = 255;
  } else {
    uint16_t span = M100_CONTRAST_LEVEL_MAX - M100_CONTRAST_LEVEL_MIN;
    displayBrightnessLevel =
        ((static_cast<uint16_t>(rawLevel - M100_CONTRAST_LEVEL_MIN) * 255U) + span / 2U) / span;
  }
#else
  displayBrightnessLevel = rawLevel;
#endif
#if PALM_CONTRAST_SERIAL_STATS
  if (contrast != lastContrastTraceValue) {
    lastContrastTraceValue = contrast;
    Serial.printf("A36 contrast=%04x raw=%u level=%u backlight=%u\n",
                  contrast,
                  rawLevel,
                  displayBrightnessLevel,
                  (dbRegs[0x429] & PORT_F_BACKLIGHT_ON) != 0 ? 1 : 0);
  }
#endif
#endif
}

static uint64_t hostMicrosToSystemCycles(uint64_t elapsedMicros) {
  return static_cast<uint64_t>((static_cast<double>(elapsedMicros) * systemClockFrequency()) / 1000000.0);
}

static void resetCyclePacer(uint64_t nowMicros) {
  cyclePaceHostMicros = nowMicros;
  cyclePaceBaseCycles = systemCycles;
}

static double timerTicksPerSecond() {
  uint16_t control = get16(0x600);
  uint8_t clockSource = static_cast<uint8_t>((control >> 1) & 0x7);
  double prescaler = static_cast<double>((get16(0x602) & 0x00ff) + 1);

  switch (clockSource) {
    case 0x1:
      return systemClockFrequency() / prescaler;
    case 0x2:
      return systemClockFrequency() / prescaler / 16.0;
    default:
      return (clockSource & 0x4) ? 32768.0 / prescaler : 0.0;
  }
}

static void applyTimerTicks(uint16_t control, uint32_t ticks) {
  if (ticks == 0) return;
  uint32_t updatedCounter = static_cast<uint32_t>(get16(0x608)) + ticks;
  uint16_t compare = get16(0x604);
  put16(0x608, static_cast<uint16_t>(updatedCounter));
  if (compare != 0 && updatedCounter >= compare) {
    put16(0x60A, get16(0x60A) | TMR_STATUS_COMPARE);
    if ((control & 0x0100) == 0) {
      put16(0x608, static_cast<uint16_t>(updatedCounter - compare));
    }
    if ((control & TMR_CONTROL_INT_ENABLE) != 0) {
      put16(0x312, get16(0x312) | INT_LO_TIMER);
      updateInterruptStatus();
    }
  }
}

static void resetTimerBaselines(uint64_t nowMicros) {
  timerLastCycles = static_cast<double>(systemCycles);
  timerLastHostMicros = nowMicros;
}

static void updateTimer() {
  uint16_t control = get16(0x600);
  uint64_t nowMicros = hostMonotonicMicros();
  if ((control & TMR_CONTROL_ENABLE) == 0) {
    resetTimerBaselines(nowMicros);
    return;
  }

  double timerHz = timerTicksPerSecond();
  if (timerHz <= 0.0) {
    resetTimerBaselines(nowMicros);
    return;
  }

  if (palmHwIsAsleep()) {
    if (timerLastHostMicros == 0) {
      resetTimerBaselines(nowMicros);
      return;
    }

    uint64_t elapsedMicros = nowMicros - timerLastHostMicros;
    uint32_t ticks = static_cast<uint32_t>((static_cast<double>(elapsedMicros) * timerHz) / 1000000.0);
    if (ticks == 0) return;

    timerLastHostMicros += static_cast<uint64_t>((static_cast<double>(ticks) * 1000000.0) / timerHz);
    timerLastCycles = static_cast<double>(systemCycles);
    applyTimerTicks(control, ticks);
    return;
  }

  double elapsedCycles = static_cast<double>(systemCycles) - timerLastCycles;
  if (elapsedCycles <= 0.0) {
    timerLastHostMicros = nowMicros;
    return;
  }

  uint32_t ticks = static_cast<uint32_t>((elapsedCycles / systemClockFrequency()) * timerHz);
  if (ticks == 0) {
    timerLastHostMicros = nowMicros;
    return;
  }

  timerLastCycles += (static_cast<double>(ticks) / timerHz) * systemClockFrequency();
  timerLastHostMicros = nowMicros;
  applyTimerTicks(control, ticks);
}

static bool keyRowF(uint8_t bit) {
  return (dbRegs[0x428] & bit) != 0 && (dbRegs[0x429] & bit) == 0;
}

static bool keyRowC(uint8_t bit) {
  return (dbRegs[0x410] & bit) != 0 && (dbRegs[0x411] & bit) == 0;
}

static bool keyRowB(uint8_t bit) {
  return (dbRegs[0x408] & bit) != 0 && (dbRegs[0x409] & bit) == 0;
}

static uint8_t portDKeyBits() {
  uint8_t bits = 0;
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_M100_EXPERIMENTAL
  bool row0 = keyRowB(0x01);
  bool row1 = keyRowB(0x08);
  bool row2 = keyRowB(0x40);
#elif PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
  bool row0 = keyRowC(0x01);
  bool row1 = keyRowC(0x02);
  bool row2 = keyRowC(0x04);
#else
  bool row0 = keyRowF(0x10) || keyRowC(0x01) || keyRowB(0x01);
  bool row1 = keyRowF(0x20) || keyRowC(0x02) || keyRowB(0x08);
  bool row2 = keyRowF(0x40) || keyRowC(0x04) || keyRowB(0x40);
#endif

  if (row0) {
      if (buttonBitsDown & KEY_BIT_HARD1) bits |= 0x01;
      if (buttonBitsDown & KEY_BIT_HARD2) bits |= 0x02;
      if (buttonBitsDown & KEY_BIT_HARD3) bits |= 0x04;
      if (buttonBitsDown & KEY_BIT_HARD4) bits |= 0x08;
  }
  if (row1) {
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_M100_EXPERIMENTAL
      if (buttonBitsDown & KEY_BIT_PAGE_DOWN) bits |= 0x02;
#else
      if (buttonBitsDown & KEY_BIT_PAGE_UP) bits |= 0x01;
      if (buttonBitsDown & KEY_BIT_PAGE_DOWN) bits |= 0x02;
#endif
  }
  if (row2) {
      if (buttonBitsDown & KEY_BIT_POWER) bits |= 0x01;
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_M100_EXPERIMENTAL
      if (buttonBitsDown & KEY_BIT_PAGE_UP) bits |= 0x02;
#elif PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
      if (buttonBitsDown & KEY_BIT_CONTRAST) bits |= 0x02;
#endif
      if (buttonBitsDown & KEY_BIT_HARD2) bits |= 0x04;
  }
  return bits;
}

static uint8_t sleepingKeyEdgeColumns(uint16_t bits) {
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
  uint8_t columns = 0;
  if (bits & (KEY_BIT_HARD1 | KEY_BIT_PAGE_UP | KEY_BIT_POWER)) columns |= 0x01;
  if (bits & (KEY_BIT_HARD2 | KEY_BIT_PAGE_DOWN | KEY_BIT_CONTRAST)) columns |= 0x02;
  if (bits & KEY_BIT_HARD3) columns |= 0x04;
  if (bits & KEY_BIT_HARD4) columns |= 0x08;
  return columns;
#else
  (void)bits;
  return 0;
#endif
}

static bool idDetectAsserted() {
  return (dbRegs[0x430] & PORT_G_ID_DETECT) != 0 &&
         (dbRegs[0x431] & PORT_G_ID_DETECT) == 0 &&
         (dbRegs[0x432] & PORT_G_ID_DETECT) == 0 &&
         (dbRegs[0x433] & PORT_G_ID_DETECT) != 0;
}

static uint8_t portInputValue(char port) {
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
  if (port == 'C') return 0x08;  // Not charging, active-low cradle input.
  if (port == 'F') return 0x82;  // FIXTRNL2 plus LCD-powered input.
#endif
  if (port == 'D') return idDetectAsserted() ? HARDWARE_ID_KEY_STATE : portDKeyBits();
  if (port == 'F') return penDown ? 0x00 : 0x02;  // Sumo/Brad PenIO is active low.
  if (port == 'E') return 0xff;  // Brad/Palm IIIx hardware sub-ID is zero.
  return 0x00;
}

static uint8_t portInternalValue(char port) {
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
  if (port == 'D') return PORT_D_POWER_FAIL | 0x40;  // Not in cradle.
#endif
  if (port == 'D') return PORT_D_POWER_FAIL;
  return 0x00;
}

static bool portDataOffsets(uint16_t offset, uint16_t &dir, uint16_t &data, uint16_t &select, char &port) {
  switch (offset) {
    case 0x401: dir = 0x400; data = 0x401; select = 0x402; port = 'A'; return true;
    case 0x409: dir = 0x408; data = 0x409; select = 0x40B; port = 'B'; return true;
    case 0x411: dir = 0x410; data = 0x411; select = 0x413; port = 'C'; return true;
    case 0x419: dir = 0x418; data = 0x419; select = 0x41B; port = 'D'; return true;
    case 0x421: dir = 0x420; data = 0x421; select = 0x423; port = 'E'; return true;
    case 0x429: dir = 0x428; data = 0x429; select = 0x42B; port = 'F'; return true;
    case 0x431: dir = 0x430; data = 0x431; select = 0x433; port = 'G'; return true;
    default: return false;
  }
}

static uint8_t readPortData(uint16_t offset) {
  uint16_t dirOffset, dataOffset, selectOffset;
  char port;
  if (!portDataOffsets(offset, dirOffset, dataOffset, selectOffset, port)) return dbRegs[offset];

  uint8_t sel = dbRegs[selectOffset];
  uint8_t dir = dbRegs[dirOffset];
  uint8_t output = dbRegs[dataOffset];
  uint8_t input = portInputValue(port);
  uint8_t internal = portInternalValue(port);
  if (port == 'D') sel |= 0x0f;

  internal &= ~sel;
  output &= sel & dir;
  input &= sel & ~dir;
  uint8_t value = output | input | internal;
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
  if (port == 'F') {
    // Austin waits for the SED1375 LCD-powered input during display wake.
    // Force it visible even if GPIO select/dir would otherwise hide it.
    value |= 0x81;
  }
#endif
  return value;
}

static void updatePortDInterrupts() {
  uint16_t pending = get16(0x312) & ~(INT_LO_ALL_KEYS | INT_LO_KBD);
  uint8_t dir = dbRegs[0x418];
  uint8_t data = portDKeyBits();
  uint8_t polarity = dbRegs[0x41C];
  uint8_t request = dbRegs[0x41D];
  uint8_t kbdEnable = dbRegs[0x41E];
  uint8_t edge = dbRegs[0x41F];
  uint8_t bits = 0;

  bits |= static_cast<uint8_t>(~edge) & data & polarity;
  bits |= static_cast<uint8_t>(~edge) & static_cast<uint8_t>(~data) & static_cast<uint8_t>(~polarity);
  if (!palmHwIsAsleep()) {
    bits |= edge & portDEdge & polarity;
  } else {
    bits |= edge & portDEdge & polarity;
  }
  bits &= request & static_cast<uint8_t>(~dir);

  if ((data & static_cast<uint8_t>(~dir) & kbdEnable) != 0) {
    pending |= INT_LO_KBD;
  }
  pending |= (static_cast<uint16_t>(bits) << 8) & INT_LO_ALL_KEYS;
  put16(0x312, pending);
  updateInterruptStatus();
}

static uint32_t rtcRegisterSecondsOfDay() {
  uint32_t value = get32(0xB00);
  uint32_t sec = value & 0x3fUL;
  uint32_t min = (value >> 16) & 0x3fUL;
  uint32_t hour = (value >> 24) & 0x1fUL;
  if (sec > 59) sec = 59;
  if (min > 59) min = 59;
  if (hour > 23) hour = 23;
  return hour * 3600UL + min * 60UL + sec;
}

static uint32_t rtcHostSeconds() {
#if defined(ESP32)
  static constexpr int64_t ESP32_RTC_BASE_EPOCH_SECONDS = 946684800LL;  // 2000-01-01 00:00:00 UTC.
  struct timeval tv = {};
  if (gettimeofday(&tv, nullptr) == 0 && tv.tv_sec >= ESP32_RTC_BASE_EPOCH_SECONDS) {
    return static_cast<uint32_t>(tv.tv_sec - ESP32_RTC_BASE_EPOCH_SECONDS);
  }
#endif
  return millis() / 1000UL;
}

static void seedRtcFromTimeRegister() {
  uint32_t seconds = rtcRegisterSecondsOfDay();
#if defined(ESP32)
  static constexpr int64_t ESP32_RTC_BASE_EPOCH_SECONDS = 946684800LL;  // 2000-01-01 00:00:00 UTC.
  struct timeval tv = {};
  tv.tv_sec = ESP32_RTC_BASE_EPOCH_SECONDS + seconds;
  tv.tv_usec = 0;
  settimeofday(&tv, nullptr);
#endif
  lastRtcSecond = 0xffffffffUL;
}

static void updateRtcInterrupts() {
  // RTC stopwatch interrupt behavior is not implemented yet. Apps that depend
  // on DragonBall RTC stopwatch ticks may pause while Palm OS is asleep.
  uint16_t pending = get16(0xB0E) & get16(0xB10) &
                     (RTC_INT_STOPWATCH | RTC_INT_MINUTE | RTC_INT_ALARM |
                      RTC_INT_24HR | RTC_INT_SECOND | RTC_INT_HOUR);
  if ((get16(0xB0C) & RTC_CONTROL_ENABLE) != 0 && pending != 0) {
    put16(0x312, get16(0x312) | INT_LO_RTC);
  } else {
    put16(0x312, get16(0x312) & ~INT_LO_RTC);
  }
  updateInterruptStatus();
}

static void updateRtcTime() {
  uint32_t seconds = rtcHostSeconds();
  uint32_t sec = seconds % 60UL;
  uint32_t min = (seconds / 60UL) % 60UL;
  uint32_t hour = (seconds / 3600UL) % 24UL;
  put32(0xB00, (hour << 24) | (min << 16) | sec);
  if (seconds == lastRtcSecond) return;

  bool firstTick = lastRtcSecond == 0xffffffffUL;
  lastRtcSecond = seconds;
  if (firstTick || (get16(0xB0C) & RTC_CONTROL_ENABLE) == 0) return;

  uint16_t enabled = get16(0xB10);
  uint16_t status = get16(0xB0E);
  if ((enabled & RTC_INT_SECOND) != 0) status |= RTC_INT_SECOND;
  if (sec == 0 && (enabled & RTC_INT_MINUTE) != 0) status |= RTC_INT_MINUTE;
  if (min == 0 && sec == 0 && (enabled & RTC_INT_HOUR) != 0) status |= RTC_INT_HOUR;
  if (hour == 0 && min == 0 && sec == 0 && (enabled & RTC_INT_24HR) != 0) status |= RTC_INT_24HR;

  uint32_t alarm = get32(0xB04);
  uint32_t alarmSec = alarm & 0x3fUL;
  uint32_t alarmMin = (alarm >> 16) & 0x3fUL;
  uint32_t alarmHour = (alarm >> 24) & 0x1fUL;
  if ((enabled & RTC_INT_ALARM) != 0 && alarmHour == hour && alarmMin == min && alarmSec == sec) {
    status |= RTC_INT_ALARM;
  }

  put16(0xB0E, status);
  updateRtcInterrupts();
}

static void completeSpiExchange() {
  uint16_t control = get16(0x802);
  if ((control & (SPIM_ENABLE | SPIM_EXCHANGE)) != (SPIM_ENABLE | SPIM_EXCHANGE)) return;
  uint16_t spiData = get16(0x800);
  uint16_t numBits = (control & 0x000f) + 1;

  if ((dbRegs[0x431] & 0x20) == 0) {
    uint32_t oldBitsMask = 0xffffffffUL << numBits;
    uint32_t newBitsMask = ~oldBitsMask;
    uint16_t result = 0;

    adsBitBufferIn = ((adsBitBufferIn << numBits) & oldBitsMask) | (spiData & newBitsMask);
    adsNumBitsIn += numBits;

    uint32_t mask = 1UL << (adsNumBitsIn - adsCommandBitsSeen - 1);
    while (mask) {
      result = (result << 1) | (adsBitBufferOut >> 15);
      adsBitBufferOut <<= 1;

      if (adsCommandBitsSeen == 0) {
        if ((mask & adsBitBufferIn) != 0) {
          ++adsCommandBitsSeen;
        } else {
          --adsNumBitsIn;
        }
        if (adsHavePending) {
          adsHavePending = false;
          adsBitBufferOut = adsPendingResult;
        }
      } else {
        ++adsCommandBitsSeen;
        if (adsCommandBitsSeen == 8) {
          adsNumBitsIn -= 8;
          adsCommandBitsSeen = 0;

          uint8_t command = adsBitBufferIn >> adsNumBitsIn;
          int channel = (command & 0x70) >> 4;
          uint16_t conversion = 0;
          switch (channel) {
            case 1:  // pen Y
              conversion = penDown ? penYRaw : 0x0000;
              break;
            case 5:  // pen X
              conversion = penDown ? penXRaw : 0x0000;
              break;
            case 6:  // dock serial: undocked
              conversion = 0x0000;
              break;
            case 2:  // battery
              conversion = 0x0fff;
              break;
            default:
              conversion = 0x0000;
              break;
          }

          adsPendingResult = conversion << 4;
          adsHavePending = true;
        }
      }

      mask >>= 1;
    }

    put16(0x800, result & newBitsMask);
  }

  control |= SPIM_INT_STATUS;
  control &= ~SPIM_EXCHANGE;
  put16(0x802, control);
  if ((control & SPIM_INT_ENABLE) != 0) {
    put16(0x312, get16(0x312) | INT_LO_SPIM);
    updateInterruptStatus();
  }
}

static void updateSpimInterruptPending() {
  if ((get16(0x802) & SPIM_INT_STATUS) == 0) {
    put16(0x312, get16(0x312) & ~INT_LO_SPIM);
    updateInterruptStatus();
  }
}

static void updateUartRegs() {
  uint16_t control = get16(0x900);
  uint16_t receive = get16(0x904) & 0x00ff;
  uint16_t transmit = (get16(0x906) & 0x00ff) | UART_TX_IGNORE_CTS;

  if (uartRxCount > 0) {
    receive = UART_RX_DATA_READY | uartRxFifo[uartRxTail];
    if (uartRxCount >= PALM_UART_FIFO_SIZE) receive |= UART_RX_FIFO_FULL;
    if (uartRxCount >= PALM_UART_FIFO_SIZE / 2) receive |= UART_RX_FIFO_HALF;
  }

  transmit |= UART_TX_AVAILABLE;
  if (uartTxCount == 0) transmit |= UART_TX_FIFO_EMPTY;
  if (uartTxCount < PALM_UART_FIFO_SIZE / 2) transmit |= UART_TX_FIFO_HALF;

  put16(0x904, receive);
  put16(0x906, transmit);

  bool pending = false;
  if ((control & UART_CONTROL_ENABLE) != 0) {
    if ((control & UART_CONTROL_RX_ENABLE) != 0) {
      if ((receive & UART_RX_DATA_READY) != 0 && (control & UART_CONTROL_RX_RDY_INT_ENABLE) != 0) pending = true;
      if ((receive & UART_RX_FIFO_HALF) != 0 && (control & UART_CONTROL_RX_HALF_INT_ENABLE) != 0) pending = true;
      if ((receive & UART_RX_FIFO_FULL) != 0 && (control & UART_CONTROL_RX_FULL_INT_ENABLE) != 0) pending = true;
    }
    if ((control & UART_CONTROL_TX_ENABLE) != 0) {
      if ((transmit & UART_TX_AVAILABLE) != 0 && (control & UART_CONTROL_TX_AVAIL_INT_ENABLE) != 0) pending = true;
      if ((transmit & UART_TX_FIFO_HALF) != 0 && (control & UART_CONTROL_TX_HALF_INT_ENABLE) != 0) pending = true;
      if ((transmit & UART_TX_FIFO_EMPTY) != 0 && (control & UART_CONTROL_TX_EMPTY_INT_ENABLE) != 0) pending = true;
    }
  }

  if (pending) {
    put16(0x312, get16(0x312) | INT_LO_UART);
  } else {
    put16(0x312, get16(0x312) & ~INT_LO_UART);
  }
  updateInterruptStatus();
}

static bool enqueueUartRxByte(uint8_t value) {
  if (uartRxCount >= PALM_UART_FIFO_SIZE) {
    ++uartRxOverrunCount;
    return false;
  }

  uartRxFifo[uartRxHead] = value;
  uartRxHead = (uartRxHead + 1) % PALM_UART_FIFO_SIZE;
  ++uartRxCount;
  return true;
}

static bool uartIrdaEnabled() {
  return (get16(0x908) & UART_MISC_IRDA_ENABLE) != 0;
}

static void updateUartIrdaProbeEchoState() {
#if PALM_UART_IRDA_PROBE_ECHO
  bool enabled = uartIrdaEnabled();
  if (enabled && !uartIrdaEnabledLast) {
    uartIrdaProbeEchoBudget = PALM_UART_IRDA_PROBE_ECHO_BYTES;
    uartIrdaProbeEchoUntilMs = millis() + PALM_UART_IRDA_PROBE_ECHO_MS;
  } else if (!enabled) {
    uartIrdaProbeEchoBudget = 0;
  }
  uartIrdaEnabledLast = enabled;
#else
  uartIrdaEnabledLast = uartIrdaEnabled();
  uartIrdaProbeEchoBudget = 0;
  uartIrdaProbeEchoUntilMs = 0;
#endif
}

static void enqueueUartIrdaEcho(uint8_t value) {
#if PALM_UART_IRDA_PROBE_ECHO
  if (!uartIrdaEnabled() || uartIrdaProbeEchoBudget == 0) return;
  if (static_cast<int32_t>(millis() - uartIrdaProbeEchoUntilMs) > 0) {
    uartIrdaProbeEchoBudget = 0;
    return;
  }
  if (value == IRDA_SIR_BOF || value == IRDA_SIR_EOF || value == IRDA_SIR_ESCAPE) {
    uartIrdaProbeEchoBudget = 0;
    return;
  }
  if (enqueueUartRxByte(value)) --uartIrdaProbeEchoBudget;
#else
  (void)value;
#endif
}

static void resetUartState() {
  uartRxHead = 0;
  uartRxTail = 0;
  uartRxCount = 0;
  uartTxHead = 0;
  uartTxTail = 0;
  uartTxCount = 0;
  uartRxOverrunCount = 0;
  uartTxOverrunCount = 0;
  uartIrdaEnabledLast = false;
  uartIrdaProbeEchoBudget = 0;
  uartIrdaProbeEchoUntilMs = 0;
  put16(0x906, UART_TX_FIFO_EMPTY | UART_TX_FIFO_HALF | UART_TX_AVAILABLE | UART_TX_IGNORE_CTS);
}

void palmHwInit() {
  memset(dbRegs, 0, PALM_DB_REG_SIZE);
  adsBitBufferIn = 0;
  adsBitBufferOut = 0;
  adsNumBitsIn = 0;
  adsPendingResult = 0;
  adsHavePending = false;
  adsCommandBitsSeen = 0;
  penDown = false;
  penXRaw = 0;
  penYRaw = 0;
  buttonBitsDown = 0;
  cradleButtonLineLow = false;
  portDEdge = 0;
  resetUartState();

  // DragonBall EZ defaults copied from Cloudpilot's EmRegsEZ reset image.
  dbRegs[0x000] = 0x1c;        // system control
  dbRegs[0x004] = 0x10;        // chip ID
  dbRegs[0x005] = 0x05;        // mask ID
  put16(0x100, 0x0000);        // group base address A
  put16(0x110, 0x00e0);        // CS A select
  put16(0x116, 0x1800);        // CS D select
  put16(0x118, 0x0060);        // EMU chip select
  put16(0x200, 0x2430);        // PLL control
  put16(0x202, 0x0123);        // PLL frequency select
  dbRegs[0x207] = 0x1f;        // power control
  put16(0x304, 0x00ff);        // interrupt mask high
  put16(0x306, 0xffff);        // interrupt mask low
  dbRegs[0x402] = 0xff;        // port A pullup
  dbRegs[0x40A] = 0xff;        // port B pullup
  dbRegs[0x40B] = 0xff;        // port B select
  dbRegs[0x412] = 0xff;        // port C pulldown
  dbRegs[0x413] = 0xff;        // port C select
  dbRegs[0x41A] = 0xff;        // port D pullup
  dbRegs[0x41B] = 0xf0;        // port D select
  dbRegs[0x422] = 0xff;        // port E pullup
  dbRegs[0x423] = 0xff;        // port E select
  dbRegs[0x42A] = 0xff;        // port F pullup/down
  dbRegs[0x432] = 0x3d;        // port G pullup
  dbRegs[0x433] = 0x08;        // port G select
  put16(0x500, 0x0020);        // PWM control
  dbRegs[0x504] = 0xfe;        // PWM period
  put16(0x604, 0xffff);        // timer compare
  put16(0xB0A, 0x0001);        // RTC watchdog
  put32(0xA00, 0x00000000UL);  // LCD start address
  dbRegs[0xA05] = 0xff;        // LCD page width, in words
  put16(0xA08, 0x03ff);        // LCD screen width register
  put16(0xA0A, 0x01ff);        // LCD screen height register
  put16(0xA18, 0x0000);        // cursor X
  put16(0xA1A, 0x0000);        // cursor Y
  put16(0xA1C, 0x0101);        // cursor width/height
  dbRegs[0xA1F] = 0x7f;        // blink control
  dbRegs[0xA20] = 0x00;        // panel control
  dbRegs[0xA21] = 0x00;        // polarity
  dbRegs[0xA23] = 0x00;        // ACD rate
  dbRegs[0xA25] = 0x00;        // pixel clock
  dbRegs[0xA27] = 0x40;        // clock control
  dbRegs[0xA29] = 0xff;        // refresh adjust
  dbRegs[0xA2D] = 0x00;        // panning offset
  dbRegs[0xA31] = 0xb9;        // frame rate
  dbRegs[0xA33] = 0x84;        // gray palette
  put16(0xA36, 0x0000);        // contrast PWM
  put16(0x906, UART_TX_FIFO_EMPTY | UART_TX_FIFO_HALF | UART_TX_AVAILABLE | UART_TX_IGNORE_CTS);
  displayBrightnessLevel = 255;
  updateDisplayBrightnessLevel();

  updateLcdWriteRange();
  lcdDirty = true;
  lcdFrameReady = true;
  lcdDirtyGeneration = 1;
  lcdLastDirtyMillis = millis();
  lastTimerStatus = 0;
  systemCycles = 0;
  timerLastCycles = 0.0;
  timerLastHostMicros = hostMonotonicMicros();
  resetCyclePacer(timerLastHostMicros);
  seedRtcFromTimeRegister();
  memset(&hwDebug, 0, sizeof(hwDebug));
}

bool palmHwLoadState(const uint8_t *regs, size_t regSize, const PalmHwSavedState &state) {
  if (regs == nullptr || regSize != PALM_DB_REG_SIZE) return false;

  memcpy(dbRegs, regs, PALM_DB_REG_SIZE);
  lastTimerStatus = state.lastTimerStatus;
  systemCycles = state.systemCycles;
  timerLastCycles = state.timerLastCycles;
  timerLastHostMicros = hostMonotonicMicros();
  resetCyclePacer(timerLastHostMicros);
  seedRtcFromTimeRegister();
  uartIrdaEnabledLast = uartIrdaEnabled();
  uartIrdaProbeEchoBudget = 0;
  uartIrdaProbeEchoUntilMs = 0;
  displayBrightnessLevel = 255;
  updateDisplayBrightnessLevel();

  adsBitBufferIn = state.adsBitBufferIn;
  adsBitBufferOut = state.adsBitBufferOut;
  adsNumBitsIn = state.adsNumBitsIn < 0 ? 0 : (state.adsNumBitsIn > 31 ? 31 : state.adsNumBitsIn);
  adsPendingResult = state.adsPendingResult;
  adsHavePending = state.adsHavePending != 0;
  adsCommandBitsSeen = state.adsCommandBitsSeen < 0 ? 0 :
                       (state.adsCommandBitsSeen > 8 ? 8 : state.adsCommandBitsSeen);
  penDown = state.penDown != 0;
  penXRaw = state.penXRaw > 0x0fff ? 0x0fff : state.penXRaw;
  penYRaw = state.penYRaw > 0x0fff ? 0x0fff : state.penYRaw;
  buttonBitsDown = state.buttonBitsDown;
  cradleButtonLineLow = false;
  portDEdge = state.portDEdge;

  updateLcdWriteRange();
  lcdDirty = true;
  lcdFrameReady = true;
  lcdLastDirtyMillis = millis();
  ++lcdDirtyGeneration;
  memset(&hwDebug, 0, sizeof(hwDebug));
  updatePortDInterrupts();
  updateRtcInterrupts();
  updateUartRegs();
  updateInterruptStatus();
  return true;
}

bool palmHwSaveState(uint8_t *regs, size_t regSize, PalmHwSavedState &state) {
  if (regs == nullptr || regSize != PALM_DB_REG_SIZE) return false;

  palmHwCycle();
  memcpy(regs, dbRegs, PALM_DB_REG_SIZE);
  state.lastTimerStatus = lastTimerStatus;
  state.adsBitBufferIn = adsBitBufferIn;
  state.adsBitBufferOut = adsBitBufferOut;
  state.adsNumBitsIn = adsNumBitsIn;
  state.adsPendingResult = adsPendingResult;
  state.adsHavePending = adsHavePending ? 1 : 0;
  state.adsCommandBitsSeen = adsCommandBitsSeen;
  state.penDown = penDown ? 1 : 0;
  state.penXRaw = penXRaw;
  state.penYRaw = penYRaw;
  state.buttonBitsDown = buttonBitsDown;
  state.portDEdge = portDEdge;
  state.systemCycles = systemCycles;
  state.timerLastCycles = timerLastCycles;
  state.lastRtcSecond = lastRtcSecond;
  state.lcdDirty = lcdDirty ? 1 : 0;
  state.lcdFrameReady = lcdFrameReady ? 1 : 0;
  return true;
}

void palmHwPrepareForSleepSnapshot() {
  penDown = false;
  penXRaw = 0;
  penYRaw = 0;
  buttonBitsDown = 0;
  cradleButtonLineLow = false;
  portDEdge = 0;
  put16(0x310, get16(0x310) & ~(INT_HI_PEN | INT_HI_IRQ1));
  put16(0x312, get16(0x312) & ~(INT_LO_KBD | INT_LO_ALL_KEYS));
  updatePortDInterrupts();
  updateInterruptStatus();
}

bool palmHwSleepSnapshotInputsQuiet() {
  uint16_t hiPending = get16(0x310);
  uint16_t loPending = get16(0x312);
  return !penDown &&
         buttonBitsDown == 0 &&
         !cradleButtonLineLow &&
         portDEdge == 0 &&
         (hiPending & (INT_HI_PEN | INT_HI_IRQ1)) == 0 &&
         (loPending & (INT_LO_KBD | INT_LO_ALL_KEYS)) == 0;
}

void palmHwSetPen(bool down, uint16_t x, uint16_t y) {
  penDown = down;
  if (x > PEN_DIGITIZER_MAX_X) x = PEN_DIGITIZER_MAX_X;
  if (y > PEN_DIGITIZER_MAX_Y) y = PEN_DIGITIZER_MAX_Y;
  penXRaw = map(x, 0, PEN_DIGITIZER_MAX_X, PEN_RAW_LEFT, PEN_RAW_RIGHT);
  penYRaw = map(y, 0, PEN_DIGITIZER_MAX_Y, PEN_RAW_TOP, PEN_RAW_BOTTOM);
  if (penDown) {
    put16(0x310, get16(0x310) | INT_HI_PEN);
  } else {
    put16(0x310, get16(0x310) & ~INT_HI_PEN);
  }
  updateInterruptStatus();
}

void palmHwSetPenRaw(bool down, uint16_t rawX, uint16_t rawY) {
  penDown = down;
  penXRaw = rawX > 0x0fff ? 0x0fff : rawX;
  penYRaw = rawY > 0x0fff ? 0x0fff : rawY;
  if (penDown) {
    put16(0x310, get16(0x310) | INT_HI_PEN);
  } else {
    put16(0x310, get16(0x310) & ~INT_HI_PEN);
  }
  updateInterruptStatus();
}

void palmHwSetPowerButton(bool down) {
  palmHwSetButtonBits(KEY_BIT_POWER, down);
}

void palmHwSetCradleButton(bool down) {
  bool oldAsserted = cradleButtonIrq1Asserted();
  cradleButtonLineLow = down;
  bool newAsserted = cradleButtonIrq1Asserted();

  if (irq1IsEdgeTriggered()) {
    if (!oldAsserted && newAsserted) {
      put16(0x310, get16(0x310) | INT_HI_IRQ1);
    }
  } else {
    updateCradleIrq1Level();
  }
  updateInterruptStatus();
}

void palmHwSetButtonBits(uint16_t bits, bool down) {
  uint8_t oldKeyBits = portDKeyBits();
  uint16_t before = buttonBitsDown;
  if (down) {
    buttonBitsDown |= bits;
  } else {
    buttonBitsDown &= ~bits;
  }
  if (buttonBitsDown == before) return;

  uint8_t newKeyBits = portDKeyBits();
  portDEdge |= newKeyBits & static_cast<uint8_t>(~oldKeyBits);
  if (down && palmHwIsAsleep()) {
    portDEdge |= sleepingKeyEdgeColumns(bits);
  }
  updatePortDInterrupts();
}

void palmHwAdvanceCycles(uint32_t cycles) {
  uint64_t requestedCycles = systemCycles + cycles;
  uint64_t nowMicros = hostMonotonicMicros();

  if (palmHwIsAsleep()) {
    systemCycles = requestedCycles;
    resetCyclePacer(nowMicros);
    return;
  }

  if (cyclePaceHostMicros == 0 || nowMicros < cyclePaceHostMicros) {
    resetCyclePacer(nowMicros);
  }

  uint64_t elapsedMicros = nowMicros - cyclePaceHostMicros;
  uint64_t maxCycles = cyclePaceBaseCycles + hostMicrosToSystemCycles(elapsedMicros);
  if (maxCycles < systemCycles) maxCycles = systemCycles;

  systemCycles = requestedCycles > maxCycles ? maxCycles : requestedCycles;
}

void palmHwCycle() {
  updateTimer();
  updateRtcTime();
  if (lcdDirty && !lcdFrameReady && millis() - lcdLastDirtyMillis >= 20) {
    lcdFrameReady = true;
  }
  updateInterruptStatus();
}

bool palmHwInRegisterSpace(uint32_t address) {
  if (address >= PALM_DB_REG_BASE && address - PALM_DB_REG_BASE < PALM_DB_REG_SIZE) {
    return true;
  }
#if PALM_ENABLE_24BIT_ALIASES
  if (address >= PALM_DB_REG_24BIT_BASE &&
      address - PALM_DB_REG_24BIT_BASE < PALM_DB_REG_SIZE) {
    return true;
  }
#endif
  return false;
}

static uint16_t regOffset(uint32_t address) {
#if PALM_ENABLE_24BIT_ALIASES
  if (address >= PALM_DB_REG_BASE) return address - PALM_DB_REG_BASE;
  return address - PALM_DB_REG_24BIT_BASE;
#else
  return address - PALM_DB_REG_BASE;
#endif
}

uint8_t palmHwRead8(uint32_t address) {
  if (!palmHwInRegisterSpace(address)) return 0xff;
  uint16_t offset = regOffset(address);
  ++hwDebug.regReadCount;
  hwDebug.lastRegReadOffset = offset;
  // Match POSE/Cloudpilot: toggle CLK32 on each PLL frequency read so
  // HwrPreRAMInit / PrvSetPLL polling loops can observe both edges.
  if (offset == 0x202) dbRegs[0x202] ^= 0x80;
  if (offset == 0x419 || offset == 0x401 || offset == 0x409 || offset == 0x411 ||
      offset == 0x421 || offset == 0x429 || offset == 0x431) {
    return readPortData(offset);
  }
  if (offset >= 0x30C && offset <= 0x313) updateInterruptStatus();
  if (offset >= 0x900 && offset <= 0x909) updateUartRegs();
  if (offset == 0x904 || offset == 0x905) {
    uint16_t receive = get16(0x904);
    uint8_t value = dbRegs[offset];
    if (offset == 0x905 && (receive & UART_RX_DATA_READY) != 0 && uartRxCount > 0) {
      value = uartRxFifo[uartRxTail];
      uartRxTail = (uartRxTail + 1) % PALM_UART_FIFO_SIZE;
      --uartRxCount;
      updateUartRegs();
    }
    return value;
  }
  if (offset >= 0x608 && offset <= 0x60B) updateTimer();
  if (offset >= 0xB00 && offset <= 0xB03) updateRtcTime();
  if (offset >= 0x60A && offset <= 0x60B) {
    updateTimer();
    lastTimerStatus |= get16(0x60A);
  }
  return dbRegs[offset];
}

void palmHwWrite8(uint32_t address, uint8_t value) {
  if (!palmHwInRegisterSpace(address)) return;

  uint16_t offset = regOffset(address);
  if (offset == 0xB0E || offset == 0xB0F) {
    uint16_t status = get16(0xB0E);
    uint16_t clearMask = offset == 0xB0E ? (static_cast<uint16_t>(value) << 8) : value;
    put16(0xB0E, status & ~clearMask);
    updateRtcInterrupts();
    ++hwDebug.regWriteCount;
    hwDebug.lastRegWriteOffset = offset;
    hwDebug.lastRegWriteValue = value;
    return;
  }

  if (offset == 0x30C || offset == 0x30D) {
    if (offset == 0x30D && (value & static_cast<uint8_t>(INT_HI_IRQ1)) != 0 && irq1IsEdgeTriggered()) {
      put16(0x310, get16(0x310) & ~INT_HI_IRQ1);
    }
    updateInterruptStatus();
    ++hwDebug.regWriteCount;
    hwDebug.lastRegWriteOffset = offset;
    hwDebug.lastRegWriteValue = value;
    return;
  }

  dbRegs[offset] = value;
  if (offset >= 0x600 && offset <= 0x609) {
    timerLastHostMicros = hostMonotonicMicros();
    timerLastCycles = static_cast<double>(systemCycles);
  }
  if (offset >= 0xB00 && offset <= 0xB03) {
    seedRtcFromTimeRegister();
  }
  if (offset == 0x419) {
    portDEdge &= static_cast<uint8_t>(~(value & dbRegs[0x41F]));
  }
  if (offset == 0x803) completeSpiExchange();
  if (offset == 0x802 || offset == 0x803) updateSpimInterruptPending();
  if (offset == 0x907) {
    if (uartTxCount < PALM_UART_FIFO_SIZE) {
      uartTxFifo[uartTxHead] = value;
      uartTxHead = (uartTxHead + 1) % PALM_UART_FIFO_SIZE;
      ++uartTxCount;
      enqueueUartIrdaEcho(value);
    } else {
      ++uartTxOverrunCount;
    }
    updateUartRegs();
  }
  if (offset == 0x908 || offset == 0x909) updateUartIrdaProbeEchoState();
  if (offset >= 0x900 && offset <= 0x909) updateUartRegs();
  if ((offset >= 0x500 && offset <= 0x504) ||
      offset == 0x410 || offset == 0x411 || offset == 0x413 ||
      offset == 0xA36 || offset == 0xA37) {
    updateDisplayBrightnessLevel();
  }
  if (offset == 0x60A || offset == 0x60B) {
    uint16_t status = get16(0x60A);
    status &= value | ~lastTimerStatus;
    put16(0x60A, status);
    lastTimerStatus = 0;
    if ((status & TMR_STATUS_COMPARE) == 0) {
      put16(0x312, get16(0x312) & ~INT_LO_TIMER);
      updateInterruptStatus();
    }
  }
  if (offset >= 0x302 && offset <= 0x313) updateInterruptStatus();
  if ((offset >= 0x418 && offset <= 0x41F) || offset == 0x408 || offset == 0x409 ||
      offset == 0x410 || offset == 0x411 || offset == 0x428 || offset == 0x429) {
    updatePortDInterrupts();
  }
  if (offset >= 0xB0C && offset <= 0xB11) updateRtcInterrupts();
  ++hwDebug.regWriteCount;
  hwDebug.lastRegWriteOffset = offset;
  hwDebug.lastRegWriteValue = value;

  if ((offset >= 0xA00 && offset <= 0xA0B) || offset == 0xA20 || offset == 0xA2D ||
      offset == 0xA33) {
    uint32_t start = get32(0xA00);
    start &= 0x1ffffffeUL;
    put32(0xA00, start);
    updateLcdWriteRange();
    markLcdDirty();
    ++hwDebug.lcdWriteCount;
    hwDebug.lastLcdWriteOffset = offset;
    hwDebug.lastLcdWriteValue = value;
  }
}

void palmHwNotifyMemoryWrite(uint32_t address) {
  if (lcdWriteRangeValid && address >= lcdWriteRangeStart && address < lcdWriteRangeEnd) {
    markLcdDirty();
  }
}

PalmLcdState palmHwGetLcdState() {
  PalmLcdState lcd;
  lcd.startAddr = get32(0xA00) & 0x1ffffffeUL;
  lcd.width = get16(0xA08);
  lcd.height = get16(0xA0A) + 1;
  lcd.bytesPerLine = static_cast<uint16_t>(dbRegs[0xA05]) * 2;
  lcd.panelControl = dbRegs[0xA20];
  lcd.dirtyGeneration = lcdDirtyGeneration;
#if PALM_FORCE_1BPP_LCD
  lcd.bpp = 1;
#else
  lcd.bpp = 1 << (lcd.panelControl & 0x03);
#endif
  lcd.margin = dbRegs[0xA2D];
  lcd.dirty = lcdDirty;
  lcd.frameReady = lcdFrameReady;

  lcd.valid = lcd.startAddr != 0 && lcd.width > 0 && lcd.width <= 320 && lcd.height > 0 &&
              lcd.height <= 320 && lcd.bytesPerLine > 0 && lcd.bpp <= 4;

  return lcd;
}

PalmHwDebug palmHwGetDebug() { return hwDebug; }

uint8_t palmHwDisplayBrightnessLevel() { return displayBrightnessLevel; }

bool palmHwLcdBacklightOn() {
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_M100_EXPERIMENTAL
  return (dbRegs[0x429] & PORT_F_BACKLIGHT_ON) != 0;
#else
  return false;
#endif
}

uint32_t palmHwUartWriteRx(const uint8_t *buffer, uint32_t count) {
  if (buffer == nullptr || count == 0) return 0;

  uint32_t written = 0;
  while (written < count) {
    if (!enqueueUartRxByte(buffer[written])) break;
    ++written;
  }
  if (written < count) uartRxOverrunCount += count - written - 1;
  updateUartRegs();
  return written;
}

uint32_t palmHwUartReadTx(uint8_t *buffer, uint32_t count) {
  if (buffer == nullptr || count == 0) return 0;

  uint32_t read = 0;
  while (read < count && uartTxCount > 0) {
    buffer[read++] = uartTxFifo[uartTxTail];
    uartTxTail = (uartTxTail + 1) % PALM_UART_FIFO_SIZE;
    --uartTxCount;
  }
  updateUartRegs();
  return read;
}

uint32_t palmHwUartRxCount() { return uartRxCount; }

uint32_t palmHwUartRxFree() {
  return uartRxCount >= PALM_UART_FIFO_SIZE ? 0 : PALM_UART_FIFO_SIZE - uartRxCount;
}

uint32_t palmHwUartTxCount() { return uartTxCount; }
uint32_t palmHwUartRxOverrunCount() { return uartRxOverrunCount; }
uint32_t palmHwUartTxOverrunCount() { return uartTxOverrunCount; }
uint16_t palmHwUartMisc() { return get16(0x908); }
bool palmHwUartIsIrda() { return (get16(0x908) & UART_MISC_IRDA_ENABLE) != 0; }

static uint32_t loadUartQueue(uint8_t *dst, const uint8_t *src, uint32_t sourceSize,
                              uint32_t sourceTail, uint32_t sourceCount) {
  memset(dst, 0, PALM_UART_FIFO_SIZE);
  if (src == nullptr || sourceSize == 0) return 0;
  const uint32_t copyCount =
      sourceCount > PALM_UART_FIFO_SIZE ? PALM_UART_FIFO_SIZE : sourceCount;
  for (uint32_t i = 0; i < copyCount; ++i) {
    dst[i] = src[(sourceTail + i) % sourceSize];
  }
  return copyCount;
}

static uint32_t saveUartQueue(uint8_t *dst, const uint8_t *src, uint32_t sourceTail,
                              uint32_t sourceCount) {
  if (dst == nullptr) return 0;
  const uint32_t copyCount =
      sourceCount > PALM_UART_FIFO_SIZE ? PALM_UART_FIFO_SIZE : sourceCount;
  for (uint32_t i = 0; i < copyCount; ++i) {
    dst[i] = src[(sourceTail + i) % PALM_UART_FIFO_SIZE];
  }
  return copyCount;
}

void palmHwLoadUartState(const uint8_t *rxFifo, const uint8_t *txFifo,
                         uint32_t sourceFifoSize,
                         uint32_t rxHead, uint32_t rxTail, uint32_t rxCount,
                         uint32_t txHead, uint32_t txTail, uint32_t txCount,
                         uint32_t rxOverrun, uint32_t txOverrun) {
  (void)rxHead;
  (void)txHead;
  uartRxTail = 0;
  uartRxCount = loadUartQueue(uartRxFifo, rxFifo, sourceFifoSize, rxTail, rxCount);
  uartRxHead = uartRxCount % PALM_UART_FIFO_SIZE;
  uartTxTail = 0;
  uartTxCount = loadUartQueue(uartTxFifo, txFifo, sourceFifoSize, txTail, txCount);
  uartTxHead = uartTxCount % PALM_UART_FIFO_SIZE;
  uartRxOverrunCount = rxOverrun;
  uartTxOverrunCount = txOverrun;
  updateUartRegs();
}

void palmHwSaveUartState(uint8_t *rxFifo, uint8_t *txFifo,
                         uint32_t &rxHead, uint32_t &rxTail, uint32_t &rxCount,
                         uint32_t &txHead, uint32_t &txTail, uint32_t &txCount,
                         uint32_t &rxOverrun, uint32_t &txOverrun) {
  updateUartRegs();
  rxCount = saveUartQueue(rxFifo, uartRxFifo, uartRxTail, uartRxCount);
  rxTail = 0;
  rxHead = rxCount;
  txCount = saveUartQueue(txFifo, uartTxFifo, uartTxTail, uartTxCount);
  txTail = 0;
  txHead = txCount;
  rxOverrun = uartRxOverrunCount;
  txOverrun = uartTxOverrunCount;
}

uint8_t palmHwPeekReg8(uint16_t offset) {
  return offset < PALM_DB_REG_SIZE ? dbRegs[offset] : 0xff;
}

uint16_t palmHwPeekReg16(uint16_t offset) {
  if (offset > PALM_DB_REG_SIZE - 2) return 0xffff;
  return (static_cast<uint16_t>(dbRegs[offset]) << 8) | dbRegs[offset + 1];
}

uint32_t palmHwPeekReg32(uint16_t offset) {
  if (offset > PALM_DB_REG_SIZE - 4) return 0xffffffffUL;
  return (static_cast<uint32_t>(palmHwPeekReg16(offset)) << 16) | palmHwPeekReg16(offset + 2);
}

void palmHwMarkLcdClean() {
  lcdDirty = false;
  lcdFrameReady = false;
}
