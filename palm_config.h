#pragma once

#include <stdint.h>

// Turn this on after Musashi-master/m68kops.c and Musashi-master/m68kops.h
// have been generated with Musashi's m68kmake tool.
#define PALM_ENABLE_MUSASHI 1
#define PALM_STATIC_DISPLAY_TEST 0

#define PALM_HARDWARE_PROFILE PALM_PROFILE_IIIC_EXPERIMENTAL
#include "palm_profile.h"

// ESP32 target: ESP32-4827S043C with 16 MB flash, 8 MB PSRAM,
// 480x272 RGB panel, and GT911 capacitive touch.
#define PALM_RAM_ALLOC_TARGET_SIZE PALM_RAM_LOGICAL_SIZE
#define PALM_RAM_ALLOC_MIN_SIZE PALM_RAM_LOGICAL_SIZE
#define PALM_PREFER_PSRAM 1
#define PALM_RAM_INTERNAL_LOW_SIZE (64UL * 1024UL)

#define PALM_POST_RAM_INTERNAL_RESERVE 0
#define PALM_RAM_STATIC_BACKING 0
#define PALM_RAM_STATIC_FALLBACK_SIZE 0

// The embedded ROM is supplied by the user and assembled into flash by
// palm_rom.S. Big ROM file offset 0 maps to PALM_ROM_BASE, with the low
// card-header alias handled by palm_memory.cpp.

#define PALM_CPU_SLICE_CYCLES 48000
#define PALM_CPU_BURST_MS 12
#define PALM_LCD_REDRAW_INTERVAL_MS 100
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
#define PALM_SED1375_DECODE_ON_RENDER_CORE 1
#else
#define PALM_SED1375_DECODE_ON_RENDER_CORE 0
#endif
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
#define PALM_PANEL_INDEXED_FRAMEBUFFER 0
#else
#define PALM_PANEL_INDEXED_FRAMEBUFFER 1
#endif
#if PALM_PANEL_INDEXED_FRAMEBUFFER
typedef uint8_t PanelPixel;
#else
typedef uint16_t PanelPixel;
#endif
#define PALM_TOUCH_POLL_INTERVAL_MS 5
#define PALM_SLEEP_TOUCH_POLL_INTERVAL_MS 250
#define PALM_DEEP_IDLE_LIGHT_SLEEP 1
#define PALM_DISABLE_UNUSED_RADIOS 0
#define PALM_TOUCH_RELEASE_DEBOUNCE_MS 10
#define PALM_UART_FIFO_SIZE 4096
#define PALM_UART_HOST_SERIAL_BRIDGE 1
#define PALM_UART_HOST_SERIAL_BAUD 115200
#define PALM_UART_BRIDGE_CHUNK 64
#define PALM_UART_IRDA_PROBE_ECHO 1
#define PALM_UART_IRDA_PROBE_ECHO_BYTES 8
#define PALM_UART_IRDA_PROBE_ECHO_MS 200

// Keep the ESP32 target as thin as possible. The VB harness is the place for
// heavy diagnostics; this build spends RAM on Palm state and simple LCD blits.
// Match the VB harness' known-good "ADS raw / raw wide" path. The GT911 gives
// calibrated panel coordinates, but Palm OS still expects ADS-style raw values.
#define PALM_TOUCH_USE_EVENT_QUEUE 0
#define PALM_TOUCH_USE_RAW_ADC 1
#define PALM_BOOT_SERIAL_STATS 1
#define PALM_DEBUG_SERIAL_BOOT_ONLY 1
#define PALM_STATE_SERIAL_STATS 0
#define PALM_RUNTIME_SERIAL_STATS 0
#define PALM_PERF_SERIAL_STATS 0
#define PALM_TOUCH_EDGE_SERIAL_STATS 0
#define PALM_BUTTON_HOLD_SERIAL_STATS 0
#define PALM_WAKE_SERIAL_STATS 0
#define PALM_CONTRAST_SERIAL_STATS 0
#define PALM_IIIC_BRIGHTNESS_SERIAL_STATS 0
#define PALM_DEBUG_SERIAL_ENABLED (PALM_BOOT_SERIAL_STATS || PALM_STATE_SERIAL_STATS || \
                                   PALM_RUNTIME_SERIAL_STATS || PALM_PERF_SERIAL_STATS || \
                                   PALM_TOUCH_EDGE_SERIAL_STATS || PALM_BUTTON_HOLD_SERIAL_STATS || \
                                   PALM_WAKE_SERIAL_STATS || PALM_CONTRAST_SERIAL_STATS || \
                                   PALM_IIIC_BRIGHTNESS_SERIAL_STATS)
#define PALM_AUTO_WAKE_RESTORED_STATE 1
#define PALM_BOOT_STATUS_TEXT 0
#define PALM_DRAW_STATIC_SHELL 1
#define PALM_TRACE_UNMAPPED 0
#define PALM_FORCE_1BPP_LCD 0
#define PALM_BUS_ERROR_ON_RAM_LIMIT 0
#define PALM_MIRROR_LOGICAL_RAM 1
#define PALM_MEMORY_COMPATIBLE_ACCESS 0
