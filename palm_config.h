#pragma once

// Turn this on after Musashi-master/m68kops.c and Musashi-master/m68kops.h
// have been generated with Musashi's m68kmake tool.
#define PALM_ENABLE_MUSASHI 1

#include "palm_profile.h"

// ESP32 target: ESP32-4827S043C with 16 MB flash, 8 MB PSRAM,
// 480x272 RGB panel, and GT911 capacitive touch.
#define PALM_RAM_ALLOC_TARGET_SIZE PALM_RAM_LOGICAL_SIZE
#define PALM_RAM_ALLOC_MIN_SIZE PALM_RAM_LOGICAL_SIZE
#define PALM_PREFER_PSRAM 1

#define PALM_POST_RAM_INTERNAL_RESERVE 0
#define PALM_RAM_STATIC_BACKING 0
#define PALM_RAM_STATIC_FALLBACK_SIZE 0

// The supplied Palm IIIx image is the Big ROM card image. Its file offset 0
// maps to 0x10c08000; the reset vector 0x10c0822a therefore lands at file
// offset 0x022a, where the real boot jump and "boot" marker live.

#define PALM_CPU_SLICE_CYCLES 10000
#define PALM_CPU_BURST_MS 6
#define PALM_LCD_REDRAW_INTERVAL_MS 100
#define PALM_TOUCH_POLL_INTERVAL_MS 5
#define PALM_TOUCH_RELEASE_DEBOUNCE_MS 30

// Keep the ESP32 target as thin as possible. The VB harness is the place for
// heavy diagnostics; this build spends RAM on Palm state and simple LCD blits.
// Match the VB harness' known-good "ADS raw / raw wide" path. The GT911 gives
// calibrated panel coordinates, but Palm OS still expects ADS-style raw values.
#define PALM_TOUCH_USE_RAW_ADC 1
#define PALM_BOOT_SERIAL_STATS 1
#define PALM_RUNTIME_SERIAL_STATS 1
#define PALM_BOOT_STATUS_TEXT 0
#define PALM_DRAW_STATIC_SHELL 1
#define PALM_TRACE_UNMAPPED 0
#define PALM_FORCE_1BPP_LCD 1
#define PALM_BUS_ERROR_ON_RAM_LIMIT 1
#define PALM_MIRROR_LOGICAL_RAM 1
