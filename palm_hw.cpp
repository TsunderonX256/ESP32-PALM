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

static constexpr uint8_t PORT_D_POWER_FAIL = 0x80;
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
static constexpr uint8_t PORT_G_ID_DETECT = 0x04;
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_M100_EXPERIMENTAL
static constexpr uint8_t HARDWARE_ID_KEY_STATE = 0xfa;  // m100 ID1 and ID3 active-low.
#else
static constexpr uint8_t HARDWARE_ID_KEY_STATE = 0xff;
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

static void updateTimer() {
  uint16_t control = get16(0x600);
  uint64_t nowMicros = hostMonotonicMicros();
  if ((control & TMR_CONTROL_ENABLE) == 0) {
    timerLastCycles = static_cast<double>(systemCycles);
    timerLastHostMicros = nowMicros;
    return;
  }

  double timerHz = timerTicksPerSecond();
  if (timerHz <= 0.0) {
    timerLastCycles = static_cast<double>(systemCycles);
    timerLastHostMicros = nowMicros;
    return;
  }

  if (timerLastHostMicros == 0) {
    timerLastHostMicros = nowMicros;
    timerLastCycles = static_cast<double>(systemCycles);
    return;
  }

  uint64_t elapsedMicros = nowMicros - timerLastHostMicros;
  uint32_t ticks = static_cast<uint32_t>((static_cast<double>(elapsedMicros) * timerHz) / 1000000.0);
  if (ticks == 0) return;

  timerLastHostMicros += static_cast<uint64_t>((static_cast<double>(ticks) * 1000000.0) / timerHz);
  timerLastCycles += static_cast<double>(ticks) / timerHz * systemClockFrequency();

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
#endif
      if (buttonBitsDown & KEY_BIT_HARD2) bits |= 0x04;
  }
  return bits;
}

static bool idDetectAsserted() {
  return (dbRegs[0x430] & PORT_G_ID_DETECT) != 0 &&
         (dbRegs[0x431] & PORT_G_ID_DETECT) == 0 &&
         (dbRegs[0x432] & PORT_G_ID_DETECT) == 0 &&
         (dbRegs[0x433] & PORT_G_ID_DETECT) != 0;
}

static uint8_t portInputValue(char port) {
  if (port == 'D') return idDetectAsserted() ? HARDWARE_ID_KEY_STATE : portDKeyBits();
  if (port == 'F') return penDown ? 0x00 : 0x02;  // Sumo/Brad PenIO is active low.
  if (port == 'E') return 0xff;  // Brad/Palm IIIx hardware sub-ID is zero.
  return 0x00;
}

static uint8_t portInternalValue(char port) {
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
  return output | input | internal;
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

  if ((dbRegs[0x431] & 0x20) == 0) {
    uint16_t numBits = (control & 0x000f) + 1;
    uint32_t oldBitsMask = 0xffffffffUL << numBits;
    uint32_t newBitsMask = ~oldBitsMask;
    uint16_t spiData = get16(0x800);
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

  updateLcdWriteRange();
  lcdDirty = true;
  lcdFrameReady = true;
  lcdDirtyGeneration = 1;
  lcdLastDirtyMillis = millis();
  lastTimerStatus = 0;
  systemCycles = 0;
  timerLastCycles = 0.0;
  timerLastHostMicros = hostMonotonicMicros();
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
  seedRtcFromTimeRegister();

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
  updatePortDInterrupts();
}

void palmHwAdvanceCycles(uint32_t cycles) {
  systemCycles += cycles;
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
