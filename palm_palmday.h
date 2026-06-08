#pragma once

#include <stdint.h>
#include "palm_profile.h"

#ifndef PALM_PALMDAY_PATCH_ENABLED
#define PALM_PALMDAY_PATCH_ENABLED 0
#endif

#ifndef PALM_PALMDAY_YEAR_OFFSET
#define PALM_PALMDAY_YEAR_OFFSET 56
#endif

static inline int32_t palmPalmDayInternalYear(int32_t displayYear) {
#if PALM_PALMDAY_PATCH_ENABLED
  return displayYear - PALM_PALMDAY_YEAR_OFFSET;
#else
  return displayYear;
#endif
}

static inline bool palmPalmDayIsLeapYear(int32_t year) {
  return ((year % 4) == 0 && (year % 100) != 0) || ((year % 400) == 0);
}

static inline uint8_t palmPalmDayMonthDays(int32_t year, uint8_t month) {
  static const uint8_t days[] = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
  if (month == 2 && palmPalmDayIsLeapYear(year)) return 29;
  return (month >= 1 && month <= 12) ? days[month - 1] : 31;
}

static inline uint32_t palmPalmDaySecondsFromDisplayDate(int32_t displayYear,
                                                         uint8_t month,
                                                         uint8_t day,
                                                         uint8_t hour,
                                                         uint8_t minute,
                                                         uint8_t second) {
  int32_t year = palmPalmDayInternalYear(displayYear);
  if (year < 1904) year = 1904;
  if (month < 1) month = 1;
  if (month > 12) month = 12;
  uint8_t maxDay = palmPalmDayMonthDays(year, month);
  if (day < 1) day = 1;
  if (day > maxDay) day = maxDay;
  if (hour > 23) hour = 23;
  if (minute > 59) minute = 59;
  if (second > 59) second = 59;

  uint32_t days = 0;
  for (int32_t y = 1904; y < year; ++y) {
    days += palmPalmDayIsLeapYear(y) ? 366UL : 365UL;
  }
  for (uint8_t m = 1; m < month; ++m) {
    days += palmPalmDayMonthDays(year, m);
  }
  days += static_cast<uint32_t>(day - 1);

  return days * 86400UL +
         static_cast<uint32_t>(hour) * 3600UL +
         static_cast<uint32_t>(minute) * 60UL +
         static_cast<uint32_t>(second);
}
