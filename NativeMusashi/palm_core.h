#ifndef PALM_CORE_H
#define PALM_CORE_H

#include <stdint.h>

#if defined(_WIN32)
#define PALM_EXPORT __declspec(dllexport)
#else
#define PALM_EXPORT
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct PalmNativeDebug {
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
    uint32_t lcdWriteCount;
    uint16_t lastLcdWriteOffset;
    uint8_t lastLcdWriteValue;
    uint32_t instructionCount;
    uint32_t lastInstructionPc;
    uint16_t lastOpcode;
    uint32_t adsCommandCount;
    uint16_t penXRaw;
    uint16_t penYRaw;
    uint16_t penDown;
    uint16_t lastAdsCommand;
    uint16_t lastAdsChannel;
    uint16_t lastAdsConversion;
    uint16_t lastAdsResponse;
    uint16_t lastAdsChannelConversion[8];
    uint32_t adsChannelCounts[8];
} PalmNativeDebug;

PALM_EXPORT int palm_native_init(const uint8_t *rom, uint32_t rom_size, uint32_t ram_size);
PALM_EXPORT void palm_native_warm_reset(void);
PALM_EXPORT int palm_native_execute(int cycles);
PALM_EXPORT int palm_native_advance_time_ms(uint32_t elapsed_ms);
PALM_EXPORT int palm_native_service_wake(int cycles);
PALM_EXPORT int palm_native_is_asleep(void);
PALM_EXPORT void palm_native_set_trace_enabled(int enabled);

PALM_EXPORT uint32_t palm_native_state_size(void);
PALM_EXPORT int palm_native_save_state(uint8_t *buffer, uint32_t buffer_size);
PALM_EXPORT int palm_native_load_state(const uint8_t *buffer, uint32_t buffer_size);

PALM_EXPORT uint32_t palm_native_get_pc(void);
PALM_EXPORT uint32_t palm_native_get_sp(void);
PALM_EXPORT uint32_t palm_native_get_sr(void);
PALM_EXPORT uint32_t palm_native_get_d0(void);
PALM_EXPORT uint32_t palm_native_get_a0(void);
PALM_EXPORT uint32_t palm_native_get_d(int index);
PALM_EXPORT uint32_t palm_native_get_a(int index);
PALM_EXPORT void palm_native_get_debug(PalmNativeDebug *debug);

PALM_EXPORT uint32_t palm_native_lcd_start(void);
PALM_EXPORT uint16_t palm_native_lcd_width(void);
PALM_EXPORT uint16_t palm_native_lcd_height(void);
PALM_EXPORT uint16_t palm_native_lcd_pitch(void);
PALM_EXPORT uint8_t palm_native_lcd_panel(void);
PALM_EXPORT uint8_t palm_native_lcd_pan(void);
PALM_EXPORT uint16_t palm_native_lcd_contrast(void);
PALM_EXPORT int palm_native_lcd_dirty(void);
PALM_EXPORT int palm_native_lcd_frame_ready(void);
PALM_EXPORT void palm_native_lcd_mark_clean(void);
PALM_EXPORT uint32_t palm_native_copy_memory(uint32_t address, uint8_t *buffer, uint32_t count);

PALM_EXPORT int palm_native_sound_enabled(void);
PALM_EXPORT double palm_native_sound_frequency(void);
PALM_EXPORT double palm_native_sound_duty(void);

PALM_EXPORT void palm_native_set_pen(int down, uint16_t x, uint16_t y);
PALM_EXPORT void palm_native_set_pen_raw(int down, uint16_t raw_x, uint16_t raw_y);
PALM_EXPORT void palm_native_set_button_bits(uint16_t bits, int down);
PALM_EXPORT void palm_native_set_power_button(int down);
PALM_EXPORT void palm_native_set_cradle_button(int down);
PALM_EXPORT void palm_native_set_in_cradle(int in_cradle);
PALM_EXPORT int palm_native_get_in_cradle(void);
PALM_EXPORT void palm_native_set_hotsync_button(int down);
PALM_EXPORT void palm_native_pulse_irq1(void);

PALM_EXPORT uint32_t palm_native_uart_write_rx(const uint8_t *buffer, uint32_t count);
PALM_EXPORT uint32_t palm_native_uart_read_tx(uint8_t *buffer, uint32_t count);
PALM_EXPORT uint32_t palm_native_uart_rx_count(void);
PALM_EXPORT uint32_t palm_native_uart_tx_count(void);
PALM_EXPORT uint32_t palm_native_uart_rx_overrun_count(void);
PALM_EXPORT uint32_t palm_native_uart_tx_overrun_count(void);
PALM_EXPORT uint16_t palm_native_uart_misc(void);
PALM_EXPORT uint32_t palm_native_uart_is_irda(void);
PALM_EXPORT uint32_t palm_native_event_wakeup(void);
PALM_EXPORT uint32_t palm_native_show_brightness_adjust(void);

PALM_EXPORT uint16_t palm_native_ads_channel_conversion(int channel);
PALM_EXPORT uint32_t palm_native_ads_channel_count(int channel);

PALM_EXPORT uint32_t palm_native_probe32(uint32_t address);
PALM_EXPORT uint8_t palm_native_peek8(uint32_t address);
PALM_EXPORT uint32_t palm_native_ram_physical_size(void);
PALM_EXPORT uint32_t palm_native_ram_dirty_pages(void);
PALM_EXPORT uint32_t palm_native_ram_highest_written(void);
PALM_EXPORT uint32_t palm_native_get_build_id(void);
PALM_EXPORT uint32_t palm_native_hardware_profile(void);
PALM_EXPORT const char *palm_native_profile_name(void);
PALM_EXPORT int palm_native_install_prc_image(const uint8_t *buffer, uint32_t buffer_size, uint32_t *result_out);
PALM_EXPORT uint32_t palm_native_copy_trace(char *buffer, uint32_t buffer_size);
PALM_EXPORT uint32_t palm_native_copy_pc_trace(char *buffer, uint32_t buffer_size);
PALM_EXPORT uint32_t palm_native_copy_first_pc_trace(char *buffer, uint32_t buffer_size);
PALM_EXPORT uint32_t palm_native_copy_jump_trace(char *buffer, uint32_t buffer_size);

#ifdef __cplusplus
}
#endif

#endif
