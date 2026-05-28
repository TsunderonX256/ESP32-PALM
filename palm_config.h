#pragma once

// Turn this on after Musashi-master/m68kops.c and Musashi-master/m68kops.h
// have been generated with Musashi's m68kmake tool.
#define PALM_ENABLE_MUSASHI 1

#include "palm_profile.h"

#define PALM_RAM_ALLOC_TARGET_SIZE (256UL * 1024UL)
// The CYD build tries for the VB-proven 256K backing, but current ESP32
// heap pressure leaves a little less available. Keep the logical RAM at 4MB
// and allow testing with the largest physical backing we can grab.
#define PALM_RAM_ALLOC_MIN_SIZE (238UL * 1024UL)
#define PALM_POST_RAM_INTERNAL_RESERVE 0
#define PALM_RAM_STATIC_BACKING 0
#define PALM_RAM_STATIC_FALLBACK_SIZE 0

// The supplied Palm IIIx image is the Big ROM card image. Its file offset 0
// maps to 0x10c08000; the reset vector 0x10c0822a therefore lands at file
// offset 0x022a, where the real boot jump and "boot" marker live.

#define PALM_CPU_SLICE_CYCLES 2000
#define PALM_LCD_REDRAW_INTERVAL_MS 100

// Keep the ESP32 target as thin as possible. The VB harness is the place for
// heavy diagnostics; this build spends RAM on Palm state and simple LCD blits.
#define PALM_TOUCH_USE_CYD_RAW_ADC 1
#define PALM_SERIAL_STATS 1
#define PALM_BOOT_STATUS_TEXT 0
#define PALM_DRAW_STATIC_SHELL 1
#define PALM_TRACE_UNMAPPED 0
#define PALM_FORCE_1BPP_LCD 1
#define PALM_BUS_ERROR_ON_RAM_LIMIT 1
#define PALM_MIRROR_LOGICAL_RAM 1
