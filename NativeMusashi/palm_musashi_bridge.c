#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#include "m68k.h"
#include "palm_core.h"
#include "palm_profile.h"

#define PALM_STATE_MAGIC 0x50414c4du
#define PALM_STATE_VERSION 2u
#define PALM_CALL_STUB_ADDRESS 0x003fff00u
#define PALM_CALL_RETURN_ADDRESS 0x003fff04u
#define SYS_TRAP_MEM_PTR_NEW 0xa013u
#define SYS_TRAP_MEM_PTR_FREE 0xa012u
#define SYS_TRAP_DM_CREATE_DATABASE_FROM_IMAGE 0xa07fu
#define SYS_TRAP_EVT_WAKEUP 0xa12fu
#define SYS_TRAP_UI_BRIGHTNESS_ADJUST 0xa3abu
#define UART_CONTROL_ENABLE 0x8000u
#define UART_CONTROL_RX_ENABLE 0x4000u
#define UART_CONTROL_TX_ENABLE 0x2000u
#define UART_CONTROL_RX_FULL_INT_ENABLE 0x0020u
#define UART_CONTROL_RX_HALF_INT_ENABLE 0x0010u
#define UART_CONTROL_RX_RDY_INT_ENABLE 0x0008u
#define UART_CONTROL_TX_EMPTY_INT_ENABLE 0x0004u
#define UART_CONTROL_TX_HALF_INT_ENABLE 0x0002u
#define UART_CONTROL_TX_AVAIL_INT_ENABLE 0x0001u
#define UART_RX_FIFO_FULL 0x8000u
#define UART_RX_FIFO_HALF 0x4000u
#define UART_RX_DATA_READY 0x2000u
#define UART_TX_FIFO_EMPTY 0x8000u
#define UART_TX_FIFO_HALF 0x4000u
#define UART_TX_AVAILABLE 0x2000u
#define UART_TX_IGNORE_CTS 0x0800u
#define UART_MISC_IRDA_ENABLE 0x0020u
#define UART_FIFO_SIZE 4096u
#define RAM_TRACK_PAGE_SIZE 4096u
#define RAM_TRACK_PAGE_COUNT (PALM_RAM_LOGICAL_SIZE / RAM_TRACK_PAGE_SIZE)

typedef struct PalmNativeStateHeader {
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
    PalmNativeDebug debug;
    uint16_t lastTimerStatus;
    uint32_t timerTicks;
    uint32_t adsBitBufferIn;
    uint16_t adsBitBufferOut;
    int32_t adsNumBitsIn;
    uint16_t adsPendingResult;
    int32_t adsHavePending;
    int32_t adsCommandBitsSeen;
    int32_t penDown;
    uint16_t penXRaw;
    uint16_t penYRaw;
    uint16_t buttonBitsDown;
    uint8_t portDEdge;
    uint8_t cpuInitialized;
    uint16_t reserved0;
    uint64_t systemCycles;
    double timerLastCycles;
    int64_t lastRtcSecond;
    int32_t lcdDirty;
    int32_t lcdFrameReady;
    uint64_t lcdReadyCycle;
    uint8_t uartRxFifo[UART_FIFO_SIZE];
    uint8_t uartTxFifo[UART_FIFO_SIZE];
    uint32_t uartRxHead;
    uint32_t uartRxTail;
    uint32_t uartRxCount;
    uint32_t uartTxHead;
    uint32_t uartTxTail;
    uint32_t uartTxCount;
    uint32_t uartRxOverrunCount;
    uint32_t uartTxOverrunCount;
} PalmNativeStateHeader;

static uint8_t *g_rom;
static uint32_t g_rom_size;
static uint8_t *g_ram;
static uint32_t g_ram_size;
static uint8_t g_ram_dirty_pages[RAM_TRACK_PAGE_COUNT];
static uint32_t g_ram_dirty_page_count;
static uint32_t g_ram_highest_written;
static uint8_t g_regs[PALM_DB_REG_SIZE];
static PalmNativeDebug g_debug;
static uint16_t g_last_timer_status;
static uint32_t g_timer_ticks;
static uint32_t g_ads_bit_buffer_in;
static uint16_t g_ads_bit_buffer_out;
static int g_ads_num_bits_in;
static uint16_t g_ads_pending_result;
static int g_ads_have_pending;
static int g_ads_command_bits_seen;
static int g_pen_down;
static uint16_t g_pen_x_raw;
static uint16_t g_pen_y_raw;
static uint16_t g_button_bits_down;
static uint8_t g_port_d_edge;
static int g_cpu_initialized;
static uint64_t g_system_cycles;
static double g_timer_last_cycles;
static time_t g_last_rtc_second;
static int g_lcd_dirty;
static int g_lcd_frame_ready;
static uint64_t g_lcd_ready_cycle;
static uint8_t g_uart_rx_fifo[UART_FIFO_SIZE];
static uint8_t g_uart_tx_fifo[UART_FIFO_SIZE];
static uint32_t g_uart_rx_head;
static uint32_t g_uart_rx_tail;
static uint32_t g_uart_rx_count;
static uint32_t g_uart_tx_head;
static uint32_t g_uart_tx_tail;
static uint32_t g_uart_tx_count;
static uint32_t g_uart_rx_overrun_count;
static uint32_t g_uart_tx_overrun_count;
static int g_pwm_sound_enabled;
static double g_pwm_sound_frequency;
static double g_pwm_sound_duty;
static int g_cradle_button_line_low;
static int g_iiic_in_cradle;
#if PALM_HAS_SED1375
static uint8_t g_sed1375_regs[PALM_SED1375_REG_SIZE];
static uint8_t g_sed1375_vram[PALM_SED1375_VRAM_SIZE];
static uint32_t g_sed1375_clut[256];
static uint8_t g_sed1375_lut_entry;
static uint8_t g_sed1375_lut_color;
static uint16_t g_iiic_lcd_brightness;
#endif

static void update_interrupt_status(void);
static void update_cradle_irq1_level(void);
static void update_timer(void);
static void update_rtc_time(void);
static void update_uart_regs(void);
static double system_clock_frequency(void);
static int enqueue_uart_rx_byte(uint8_t value);

#define PORT_D_POWER_FAIL 0x80u
#define INT_HI_PEN 0x0010u
#define INT_HI_IRQ6 0x0008u
#define INT_HI_IRQ3 0x0004u
#define INT_HI_IRQ2 0x0002u
#define INT_HI_IRQ1 0x0001u
#define INT_HI_EMU 0x0080u
#define INT_HI_SAMPLE_TIMER 0x0040u
#define ICR_POL1 0x8000u
#define ICR_ET1 0x0800u
#define INT_LO_SPIM 0x0001u
#define INT_LO_TIMER 0x0002u
#define INT_LO_UART 0x0004u
#define INT_LO_WDT 0x0008u
#define INT_LO_RTC 0x0010u
#define INT_LO_KBD 0x0040u
#define INT_LO_PWM 0x0080u
#define INT_LO_INT0 0x0100u
#define INT_LO_INT1 0x0200u
#define INT_LO_INT2 0x0400u
#define INT_LO_INT3 0x0800u
#define INT_LO_ALL_KEYS 0x0f00u
#define SPIM_ENABLE 0x0200u
#define SPIM_EXCHANGE 0x0100u
#define SPIM_INT_STATUS 0x0080u
#define SPIM_INT_ENABLE 0x0040u
#define TMR_STATUS_COMPARE 0x0001u
#define TMR_CONTROL_ENABLE 0x0001u
#define TMR_CONTROL_INT_ENABLE 0x0010u
#define PLL_CONTROL_DISABLE 0x0008u
#define RTC_CONTROL_ENABLE 0x0080u
#define RTC_INT_STOPWATCH 0x0001u
#define RTC_INT_MINUTE 0x0002u
#define RTC_INT_ALARM 0x0004u
#define RTC_INT_24HR 0x0008u
#define RTC_INT_SECOND 0x0010u
#define RTC_INT_HOUR 0x0020u
#define KEY_BIT_POWER 0x0001u
#define KEY_BIT_PAGE_UP 0x0002u
#define KEY_BIT_PAGE_DOWN 0x0004u
#define KEY_BIT_HARD1 0x0008u
#define KEY_BIT_HARD2 0x0010u
#define KEY_BIT_HARD3 0x0020u
#define KEY_BIT_HARD4 0x0040u
#define KEY_BIT_CONTRAST 0x0200u
#define PEN_RAW_BASE 500u
#define PEN_DIGITIZER_MAX_X 159u
#define PEN_DIGITIZER_MAX_Y 219u
#define PEN_ADC_MAX 4095u
#define LCD_FRAME_CYCLES 320000ull

static uint16_t read16(uint32_t address, int instruction_fetch);
static uint32_t read32(uint32_t address, int instruction_fetch);

static void mark_lcd_dirty(void) {
    if (!g_lcd_dirty || g_lcd_frame_ready) {
        g_lcd_ready_cycle = g_system_cycles + LCD_FRAME_CYCLES;
    }
    g_lcd_dirty = 1;
    g_lcd_frame_ready = 0;
}

static void update_lcd_frame(void) {
    if (g_lcd_dirty && !g_lcd_frame_ready && g_system_cycles >= g_lcd_ready_cycle) {
        g_lcd_frame_ready = 1;
    }
}

#define TRACE_LINES 64
#define TRACE_LINE_LEN 96
static char g_trace[TRACE_LINES][TRACE_LINE_LEN];
static uint32_t g_trace_pos;
static uint32_t g_trace_count;
static char g_pc_trace[TRACE_LINES][TRACE_LINE_LEN];
static uint32_t g_pc_trace_pos;
static uint32_t g_pc_trace_count;
static char g_first_pc_trace[TRACE_LINES][TRACE_LINE_LEN];
static uint32_t g_first_pc_trace_count;
static char g_jump_trace[TRACE_LINES][TRACE_LINE_LEN];
static uint32_t g_jump_trace_pos;
static uint32_t g_jump_trace_count;
static uint32_t g_previous_pc;
static int g_have_previous_pc;
static int g_trace_enabled;

static void trace_pc(uint32_t pc) {
    if (!g_trace_enabled) return;

    uint16_t opcode = read16(pc, 1);
    char *line = g_pc_trace[g_pc_trace_pos % TRACE_LINES];
    if (pc >= 0x10c08ae0u && pc <= 0x10c08b16u) {
        snprintf(line, TRACE_LINE_LEN, "PC=%08X opcode=%04X A2=%08X A4=%08X D5=%08X D6=%08X",
                 pc, opcode, m68k_get_reg(0, M68K_REG_A2), m68k_get_reg(0, M68K_REG_A4),
                 m68k_get_reg(0, M68K_REG_D5), m68k_get_reg(0, M68K_REG_D6));
    } else {
        snprintf(line, TRACE_LINE_LEN, "PC=%08X opcode=%04X", pc, opcode);
    }
    g_pc_trace_pos++;
    if (g_pc_trace_count < TRACE_LINES) g_pc_trace_count++;

    if (g_first_pc_trace_count < TRACE_LINES) {
        snprintf(g_first_pc_trace[g_first_pc_trace_count], TRACE_LINE_LEN, "PC=%08X opcode=%04X", pc, opcode);
        g_first_pc_trace_count++;
    }

    if (g_have_previous_pc) {
        uint32_t delta = pc - g_previous_pc;
        if (!(delta == 2 || delta == 4 || delta == 6 || delta == 8 || delta == 10)) {
            char *jump_line = g_jump_trace[g_jump_trace_pos % TRACE_LINES];
            snprintf(jump_line, TRACE_LINE_LEN, "from=%08X to=%08X opcode=%04X", g_previous_pc, pc, opcode);
            g_jump_trace_pos++;
            if (g_jump_trace_count < TRACE_LINES) g_jump_trace_count++;
        }
    }
    g_previous_pc = pc;
    g_have_previous_pc = 1;

    ++g_debug.instructionCount;
    g_debug.lastInstructionPc = pc;
    g_debug.lastOpcode = opcode;
}

static void trace_reg_write(uint32_t pc, uint32_t address, uint16_t offset, uint8_t value) {
    if (!g_trace_enabled) return;

    char *line = g_trace[g_trace_pos % TRACE_LINES];
    snprintf(line, TRACE_LINE_LEN, "PC=%08X addr=%08X reg[$%03X] <- $%02X", pc, address, offset, value);
    g_trace_pos++;
    if (g_trace_count < TRACE_LINES) g_trace_count++;
}

static void trace_ram_write(uint32_t pc, uint32_t address, uint32_t offset, uint8_t value) {
    if (!g_trace_enabled) return;

    if (address >= 0x10000u && address < 0x00ff0000u) return;
    char *line = g_trace[g_trace_pos % TRACE_LINES];
    snprintf(line, TRACE_LINE_LEN, "PC=%08X addr=%08X ram[$%06X] <- $%02X", pc, address, offset, value);
    g_trace_pos++;
    if (g_trace_count < TRACE_LINES) g_trace_count++;
}

static void trace_bus_error(uint32_t pc, uint32_t address, int instruction_fetch) {
    if (!g_trace_enabled) return;

    char *line = g_trace[g_trace_pos % TRACE_LINES];
    snprintf(line, TRACE_LINE_LEN, "PC=%08X %s bus error @ %08X", pc, instruction_fetch ? "ibus" : "bus", address);
    g_trace_pos++;
    if (g_trace_count < TRACE_LINES) g_trace_count++;
}

static int rom_offset(uint32_t address, uint32_t *offset) {
    if (address >= PALM_ROM_BASE) {
        *offset = address - PALM_ROM_BASE;
        if (*offset < g_rom_size) return 1;
    }

    if (address >= PALM_ROM_LOW_ALIAS_BASE && address < PALM_ROM_BASE) {
        *offset = address - PALM_ROM_LOW_ALIAS_BASE;
        if (*offset < g_rom_size) return 1;
    }

    if (address >= PALM_ROM_LOW_ALIAS_BASE && address < PALM_ROM_LOW_ALIAS_BASE + PALM_ROM_CHIP_SELECT_SIZE) {
        *offset = (address - PALM_ROM_LOW_ALIAS_BASE) % g_rom_size;
        if (*offset < g_rom_size) return 1;
    }

#if PALM_ENABLE_24BIT_ALIASES
    if (address >= PALM_ROM_LOW_24BIT_ALIAS_BASE &&
        address < PALM_ROM_LOW_24BIT_ALIAS_BASE + PALM_ROM_CHIP_SELECT_SIZE) {
        *offset = (address - PALM_ROM_LOW_24BIT_ALIAS_BASE) % g_rom_size;
        if (*offset < g_rom_size) return 1;
    }

    if (address >= PALM_ROM_LOW_24BIT_ALIAS_BASE && address < PALM_ROM_24BIT_BASE) {
        *offset = address - PALM_ROM_LOW_24BIT_ALIAS_BASE;
        if (*offset < g_rom_size) return 1;
    }

    if (address >= PALM_ROM_24BIT_BASE) {
        *offset = address - PALM_ROM_24BIT_BASE;
        if (*offset < g_rom_size) return 1;
    }
#endif

    return 0;
}

static int reg_offset(uint32_t address, uint32_t *offset) {
    if (address >= PALM_DB_REG_BASE) {
        *offset = address - PALM_DB_REG_BASE;
        return *offset < PALM_DB_REG_SIZE;
    }

#if PALM_ENABLE_24BIT_ALIASES
    if (address >= PALM_DB_REG_24BIT_BASE && address < PALM_DB_REG_24BIT_BASE + PALM_DB_REG_SIZE) {
        *offset = address - PALM_DB_REG_24BIT_BASE;
        return 1;
    }
#endif

    return 0;
}

static int ram_offset(uint32_t address, uint32_t *offset) {
    if (address < PALM_RAM_LOGICAL_SIZE) {
        *offset = g_ram_size == 0 ? 0 : address % g_ram_size;
        return 1;
    }

    uint32_t alias_base = PALM_RAM_TOP_ALIAS_END - PALM_RAM_LOGICAL_SIZE;
    if (address >= alias_base && address < PALM_RAM_TOP_ALIAS_END) {
        *offset = g_ram_size == 0 ? 0 : (address - alias_base) % g_ram_size;
        return 1;
    }

    return 0;
}

static int ram_logical_offset(uint32_t address, uint32_t *offset) {
    if (address < PALM_RAM_LOGICAL_SIZE) {
        *offset = address;
        return 1;
    }

    uint32_t alias_base = PALM_RAM_TOP_ALIAS_END - PALM_RAM_LOGICAL_SIZE;
    if (address >= alias_base && address < PALM_RAM_TOP_ALIAS_END) {
        *offset = address - alias_base;
        return 1;
    }

    return 0;
}

#if PALM_HAS_SED1375
static int sed1375_reg_offset(uint32_t address, uint32_t *offset) {
    if (address >= PALM_SED1375_REG_BASE && address < PALM_SED1375_REG_BASE + PALM_SED1375_REG_SIZE) {
        *offset = address - PALM_SED1375_REG_BASE;
        return 1;
    }
    return 0;
}

static int sed1375_vram_offset(uint32_t address, uint32_t *offset) {
    if (address >= PALM_SED1375_BASE && address < PALM_SED1375_BASE + PALM_SED1375_VRAM_SIZE) {
        *offset = address - PALM_SED1375_BASE;
        return 1;
    }
    return 0;
}

static void init_sed1375(void) {
    memset(g_sed1375_regs, 0, sizeof(g_sed1375_regs));
    memset(g_sed1375_vram, 0, sizeof(g_sed1375_vram));
    g_sed1375_regs[0x00] = 0x24; /* Product code 6, revision 0. */
    g_sed1375_regs[0x04] = 19;   /* 160 pixels: (19 + 1) * 8. */
    g_sed1375_regs[0x05] = 159;  /* 160 lines: value + 1. */
    g_sed1375_regs[0x12] = 20;   /* Default byte pitch for 1bpp 160-wide. */
    g_sed1375_lut_entry = 0;
    g_sed1375_lut_color = 0;
    g_iiic_lcd_brightness = 0xffu;
    for (uint32_t i = 0; i < 256u; ++i) {
        uint8_t level = (uint8_t)i;
        g_sed1375_clut[i] = 0xff000000u | ((uint32_t)level << 16) | ((uint32_t)level << 8) | level;
    }
}

static void update_iiic_lcd_brightness(void) {
    uint16_t brightness = 0xffu;
    if ((g_regs[0x411] & 0x10u) == 0) brightness = 0x70u; /* Backlight disabled. */
    if ((g_sed1375_regs[0x03] & 0x04u) != 0) brightness = 0x40u; /* SED power-save. */
    g_iiic_lcd_brightness = brightness;
    mark_lcd_dirty();
}

static uint8_t sed1375_read_reg(uint32_t offset) {
    if (offset == 0x0a) return (uint8_t)(g_sed1375_regs[offset] | 0x80u);
    if (offset == 0x17) {
        uint32_t entry = g_sed1375_clut[g_sed1375_lut_entry];
        uint8_t value = 0;
        if (g_sed1375_lut_color == 0) value = (uint8_t)((entry >> 16) & 0xf0u);
        else if (g_sed1375_lut_color == 1) value = (uint8_t)((entry >> 8) & 0xf0u);
        else value = (uint8_t)((entry >> 0) & 0xf0u);
        g_sed1375_lut_color = (uint8_t)((g_sed1375_lut_color + 1u) % 3u);
        if (g_sed1375_lut_color == 0) g_sed1375_lut_entry++;
        return value;
    }
    return g_sed1375_regs[offset];
}

static void sed1375_write_reg(uint32_t offset, uint8_t value) {
    if (offset == 0x00) return;
    g_sed1375_regs[offset] = value;

    if (offset == 0x15) {
        g_sed1375_lut_entry = value;
        g_sed1375_lut_color = 0;
        return;
    }

    if (offset == 0x17) {
        uint32_t expanded = (uint32_t)(((value & 0xf0u) >> 4) * 0x11u);
        uint32_t *entry = &g_sed1375_clut[g_sed1375_lut_entry];
        if (g_sed1375_lut_color == 0) {
            *entry = (*entry & 0xff00ffffu) | (expanded << 16);
        } else if (g_sed1375_lut_color == 1) {
            *entry = (*entry & 0xffff00ffu) | (expanded << 8);
        } else {
            *entry = (*entry & 0xffffff00u) | expanded;
        }
        g_sed1375_lut_color = (uint8_t)((g_sed1375_lut_color + 1u) % 3u);
        if (g_sed1375_lut_color == 0) g_sed1375_lut_entry++;
        mark_lcd_dirty();
    }

    if (offset == 0x03) update_iiic_lcd_brightness();
    if (offset >= 0x01 && offset <= 0x1c) mark_lcd_dirty();
}
#endif

static void track_ram_write(uint32_t address) {
    uint32_t logical_offset;
    if (!ram_logical_offset(address, &logical_offset)) return;

    uint32_t page = logical_offset / RAM_TRACK_PAGE_SIZE;
    if (page < RAM_TRACK_PAGE_COUNT && g_ram_dirty_pages[page] == 0) {
        g_ram_dirty_pages[page] = 1;
        g_ram_dirty_page_count++;
    }
    if (g_ram_highest_written == 0xffffffffu || logical_offset > g_ram_highest_written) {
        g_ram_highest_written = logical_offset;
    }
}

static int ram_size_probe_address(uint32_t address) {
    return address >= PALM_RAM_BASE + PALM_RAM_LOGICAL_SIZE &&
           address < PALM_RAM_BASE + PALM_RAM_LOGICAL_SIZE + 4;
}

static int ram_put8(uint32_t address, uint8_t value) {
    uint32_t offset;
    if (!ram_offset(address, &offset)) return 0;
    g_ram[offset] = value;
    track_ram_write(address);
    return 1;
}

static int ram_put16(uint32_t address, uint16_t value) {
    return ram_put8(address, (uint8_t)(value >> 8)) &&
           ram_put8(address + 1, (uint8_t)(value & 0xff));
}

static int ram_put32(uint32_t address, uint32_t value) {
    return ram_put16(address, (uint16_t)(value >> 16)) &&
           ram_put16(address + 2, (uint16_t)(value & 0xffff));
}

static void put16(uint16_t offset, uint16_t value) {
    g_regs[offset] = (uint8_t)(value >> 8);
    g_regs[offset + 1] = (uint8_t)(value & 0xff);
}

static uint16_t get16(uint16_t offset) {
    return (uint16_t)((g_regs[offset] << 8) | g_regs[offset + 1]);
}

static uint32_t get32(uint16_t offset) {
    return ((uint32_t)get16(offset) << 16) | get16((uint16_t)(offset + 2));
}

static void put32(uint16_t offset, uint32_t value) {
    put16(offset, (uint16_t)(value >> 16));
    put16((uint16_t)(offset + 2), (uint16_t)value);
}

static uint32_t call_palm_trap(uint16_t trap, const uint32_t *params, int param_count, uint32_t *a0_out) {
    if (!g_ram || !g_cpu_initialized) {
        if (a0_out) *a0_out = 0;
        return 0;
    }

    uint32_t stub_offset;
    if (!ram_offset(PALM_CALL_STUB_ADDRESS, &stub_offset) || stub_offset + 6u > g_ram_size) {
        if (a0_out) *a0_out = 0;
        return 0;
    }

    uint8_t saved_stub[6];
    memcpy(saved_stub, g_ram + stub_offset, sizeof(saved_stub));

    uint32_t old_pc = m68k_get_reg(0, M68K_REG_PC);
    uint32_t old_sp = m68k_get_reg(0, M68K_REG_SP);
    uint32_t old_sr = m68k_get_reg(0, M68K_REG_SR);
    uint32_t old_usp = m68k_get_reg(0, M68K_REG_USP);
    uint32_t old_isp = m68k_get_reg(0, M68K_REG_ISP);
    uint32_t old_d[8];
    uint32_t old_a[8];
    for (int i = 0; i < 8; ++i) {
        old_d[i] = m68k_get_reg(0, M68K_REG_D0 + i);
        old_a[i] = m68k_get_reg(0, M68K_REG_A0 + i);
    }

    if (!ram_put16(PALM_CALL_STUB_ADDRESS, 0x4e4fu) ||
        !ram_put16(PALM_CALL_STUB_ADDRESS + 2, trap) ||
        !ram_put16(PALM_CALL_STUB_ADDRESS + 4, 0x60feu)) {
        memcpy(g_ram + stub_offset, saved_stub, sizeof(saved_stub));
        if (a0_out) *a0_out = 0;
        return 0;
    }

    uint32_t param_bytes = (uint32_t)param_count * 4u;
    uint32_t call_sp = old_sp - param_bytes;
    for (int i = 0; i < param_count; ++i) {
        ram_put32(call_sp + (uint32_t)i * 4u, params[i]);
    }

    m68k_set_reg(M68K_REG_SP, call_sp);
    m68k_set_reg(M68K_REG_PC, PALM_CALL_STUB_ADDRESS);

    for (int i = 0; i < 2000; ++i) {
        m68k_execute(2000);
        update_timer();
        update_rtc_time();
        update_interrupt_status();
        uint32_t pc = m68k_get_reg(0, M68K_REG_PC);
        if (pc == PALM_CALL_STUB_ADDRESS + 4 || pc == PALM_CALL_STUB_ADDRESS + 6) break;
    }

    uint32_t d0 = m68k_get_reg(0, M68K_REG_D0);
    uint32_t a0 = m68k_get_reg(0, M68K_REG_A0);

    memcpy(g_ram + stub_offset, saved_stub, sizeof(saved_stub));
    for (int i = 0; i < 8; ++i) {
        m68k_set_reg(M68K_REG_D0 + i, old_d[i]);
        m68k_set_reg(M68K_REG_A0 + i, old_a[i]);
    }
    m68k_set_reg(M68K_REG_SR, old_sr);
    m68k_set_reg(M68K_REG_USP, old_usp);
    m68k_set_reg(M68K_REG_ISP, old_isp);
    m68k_set_reg(M68K_REG_SP, old_sp);
    m68k_set_reg(M68K_REG_PC, old_pc);

    if (a0_out) *a0_out = a0;
    return d0;
}

static uint32_t call_palm_trap_stack(uint16_t trap, const uint8_t *stack_bytes,
                                     uint32_t stack_byte_count, uint32_t *a0_out) {
    if (!g_ram || !g_cpu_initialized) {
        if (a0_out) *a0_out = 0;
        return 0;
    }

    uint32_t stub_offset;
    if (!ram_offset(PALM_CALL_STUB_ADDRESS, &stub_offset) || stub_offset + 6u > g_ram_size) {
        if (a0_out) *a0_out = 0;
        return 0;
    }

    uint8_t saved_stub[6];
    memcpy(saved_stub, g_ram + stub_offset, sizeof(saved_stub));

    uint32_t old_pc = m68k_get_reg(0, M68K_REG_PC);
    uint32_t old_sp = m68k_get_reg(0, M68K_REG_SP);
    uint32_t old_sr = m68k_get_reg(0, M68K_REG_SR);
    uint32_t old_usp = m68k_get_reg(0, M68K_REG_USP);
    uint32_t old_isp = m68k_get_reg(0, M68K_REG_ISP);
    uint32_t old_d[8];
    uint32_t old_a[8];
    for (int i = 0; i < 8; ++i) {
        old_d[i] = m68k_get_reg(0, M68K_REG_D0 + i);
        old_a[i] = m68k_get_reg(0, M68K_REG_A0 + i);
    }

    uint32_t call_sp = old_sp - stack_byte_count;
    uint32_t stack_offset;
    if (!ram_offset(call_sp, &stack_offset) || stack_offset + stack_byte_count > g_ram_size ||
        !ram_put16(PALM_CALL_STUB_ADDRESS, 0x4e4fu) ||
        !ram_put16(PALM_CALL_STUB_ADDRESS + 2, trap) ||
        !ram_put16(PALM_CALL_STUB_ADDRESS + 4, 0x60feu)) {
        memcpy(g_ram + stub_offset, saved_stub, sizeof(saved_stub));
        if (a0_out) *a0_out = 0;
        return 0;
    }

    if (stack_byte_count > 0 && stack_bytes) {
        memcpy(g_ram + stack_offset, stack_bytes, stack_byte_count);
    }

    m68k_set_reg(M68K_REG_SP, call_sp);
    m68k_set_reg(M68K_REG_PC, PALM_CALL_STUB_ADDRESS);

    for (int i = 0; i < 2000; ++i) {
        m68k_execute(2000);
        update_timer();
        update_rtc_time();
        update_interrupt_status();
        uint32_t pc = m68k_get_reg(0, M68K_REG_PC);
        if (pc == PALM_CALL_STUB_ADDRESS + 4 || pc == PALM_CALL_STUB_ADDRESS + 6) break;
    }

    uint32_t d0 = m68k_get_reg(0, M68K_REG_D0);
    uint32_t a0 = m68k_get_reg(0, M68K_REG_A0);

    memcpy(g_ram + stub_offset, saved_stub, sizeof(saved_stub));
    for (int i = 0; i < 8; ++i) {
        m68k_set_reg(M68K_REG_D0 + i, old_d[i]);
        m68k_set_reg(M68K_REG_A0 + i, old_a[i]);
    }
    m68k_set_reg(M68K_REG_SR, old_sr);
    m68k_set_reg(M68K_REG_USP, old_usp);
    m68k_set_reg(M68K_REG_ISP, old_isp);
    m68k_set_reg(M68K_REG_SP, old_sp);
    m68k_set_reg(M68K_REG_PC, old_pc);

    if (a0_out) *a0_out = a0;
    return d0;
}

static int interrupt_level(void) {
    uint16_t hi = (uint16_t)(get16(0x310) & ~get16(0x304));
    uint16_t lo = (uint16_t)(get16(0x312) & ~get16(0x306));

    if (hi & INT_HI_EMU) {
        return 7;
    }
    if ((hi & INT_HI_IRQ6) || (lo & (INT_LO_TIMER | INT_LO_PWM))) {
        return 6;
    }
    if (hi & INT_HI_PEN) {
        return 5;
    }
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

static int is_asleep(void) {
    return (get16(0x200) & PLL_CONTROL_DISABLE) != 0;
}

static int has_wake_source(void) {
    uint16_t hi_pending = get16(0x310);
    uint16_t lo_pending = get16(0x312);
    uint16_t wake_hi = INT_HI_PEN | INT_HI_IRQ6 | INT_HI_IRQ1 | INT_HI_EMU;
    uint16_t wake_lo = INT_LO_TIMER | INT_LO_RTC | INT_LO_KBD | INT_LO_INT0 | INT_LO_INT1 |
                       INT_LO_INT2 | INT_LO_INT3;
    return ((hi_pending & wake_hi) != 0) || ((lo_pending & wake_lo) != 0);
}

static int irq1_is_edge_triggered(void) {
    return (get16(0x302) & ICR_ET1) != 0;
}

static int cradle_button_irq1_asserted(void) {
    int active_high = (get16(0x302) & ICR_POL1) != 0;
    return active_high ? !g_cradle_button_line_low : g_cradle_button_line_low;
}

static void update_cradle_irq1_level(void) {
    if (irq1_is_edge_triggered()) return;
    if (cradle_button_irq1_asserted()) {
        put16(0x310, (uint16_t)(get16(0x310) | INT_HI_IRQ1));
    } else {
        put16(0x310, (uint16_t)(get16(0x310) & (uint16_t)~INT_HI_IRQ1));
    }
}

static void set_cradle_button_line(int down) {
    int old_asserted = cradle_button_irq1_asserted();
    g_cradle_button_line_low = down != 0;
    int new_asserted = cradle_button_irq1_asserted();

    if (irq1_is_edge_triggered()) {
        if (!old_asserted && new_asserted) {
            put16(0x310, (uint16_t)(get16(0x310) | INT_HI_IRQ1));
        }
    } else {
        update_cradle_irq1_level();
    }
    update_interrupt_status();
}

static void update_interrupt_status(void) {
    update_cradle_irq1_level();
    put16(0x30c, (uint16_t)(get16(0x310) & ~get16(0x304)));
    put16(0x30e, (uint16_t)(get16(0x312) & ~get16(0x306)));

    if (g_cpu_initialized) m68k_set_irq((unsigned int)interrupt_level());
}

static double system_clock_frequency(void) {
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
    return PALM_SYSTEM_CLOCK_HZ;
#else
    uint16_t pll_control = get16(0x200);
    uint16_t pll_freq_sel = get16(0x202);
    uint16_t pc = (uint16_t)(pll_freq_sel & 0x00ff);
    uint16_t qc = (uint16_t)((pll_freq_sel & 0x0f00) >> 8);
    double result = 32768.0 * (14.0 * (pc + 1) + qc + 1);

    if (pll_control & 0x0020) result /= 2.0;

    switch (pll_control & 0x0f00) {
        case 0x0000: result /= 2.0; break;
        case 0x0100: result /= 4.0; break;
        case 0x0200: result /= 8.0; break;
        case 0x0300: result /= 16.0; break;
        default: break;
    }

    return result;
#endif
}

static void update_pwm_sound(void) {
    uint16_t control = get16(0x500);
    uint8_t sample = g_regs[0x503];
    uint8_t period = g_regs[0x504];

    if ((control & 0x0010u) == 0) {
        g_pwm_sound_enabled = 0;
        g_pwm_sound_frequency = 0.0;
        g_pwm_sound_duty = 0.0;
        return;
    }

    uint32_t prescaler = ((uint32_t)(control >> 8) & 0x7fu) + 1u;
    uint32_t clock_divider = 2u << (control & 0x0003u);
    uint32_t period_divider = (uint32_t)period + 2u;
    if (period_divider > 256u) period_divider = 256u;

    double base_frequency = (control & 0x8000u) != 0 ? 32768.0 : system_clock_frequency();
    double frequency = base_frequency / (double)prescaler / (double)clock_divider / (double)period_divider;
    double duty = period == 0 ? 0.5 : (double)sample / (double)period;

    if (frequency <= 0.0 || frequency >= 20000.0) {
        g_pwm_sound_enabled = 0;
        g_pwm_sound_frequency = 0.0;
        g_pwm_sound_duty = 0.0;
        return;
    }

    if (duty < 0.02) duty = 0.02;
    if (duty > 0.98) duty = 0.98;
    g_pwm_sound_enabled = 1;
    g_pwm_sound_frequency = frequency;
    g_pwm_sound_duty = duty;
}

static double timer_ticks_per_second(void) {
    uint16_t control = get16(0x600);
    uint8_t clock_source = (uint8_t)((control >> 1) & 0x7);
    double prescaler = (double)((get16(0x602) & 0x00ff) + 1);

    switch (clock_source) {
        case 0x1:
            return system_clock_frequency() / prescaler;
        case 0x2:
            return system_clock_frequency() / prescaler / 16.0;
        default:
            return (clock_source & 0x4) ? 32768.0 / prescaler : 0.0;
    }
}

static void update_timer(void) {
    uint16_t control = get16(0x600);
    double timer_hz;
    double elapsed_cycles;
    uint32_t ticks;

    if ((control & TMR_CONTROL_ENABLE) == 0) {
        g_timer_last_cycles = (double)g_system_cycles;
        return;
    }

    timer_hz = timer_ticks_per_second();
    if (timer_hz <= 0.0) {
        g_timer_last_cycles = (double)g_system_cycles;
        return;
    }

    elapsed_cycles = (double)g_system_cycles - g_timer_last_cycles;
    ticks = (uint32_t)(elapsed_cycles / system_clock_frequency() * timer_hz);
    if (ticks == 0) return;

    g_timer_last_cycles += (double)ticks / timer_hz * system_clock_frequency();

    uint32_t updated_counter = (uint32_t)get16(0x608) + ticks;
    uint16_t compare = get16(0x604);
    put16(0x608, (uint16_t)updated_counter);
    if (compare != 0 && updated_counter >= compare) {
        put16(0x60a, (uint16_t)(get16(0x60a) | TMR_STATUS_COMPARE));
        if ((control & 0x0100u) == 0) {
            put16(0x608, (uint16_t)(updated_counter - compare));
        }
        if ((control & TMR_CONTROL_INT_ENABLE) != 0) {
            put16(0x312, (uint16_t)(get16(0x312) | INT_LO_TIMER));
            update_interrupt_status();
        }
    }
    g_timer_ticks += ticks;
}

static int key_row_f(uint8_t bit) {
    return (g_regs[0x428] & bit) != 0 && (g_regs[0x429] & bit) == 0;
}

static int key_row_c(uint8_t bit) {
    return (g_regs[0x410] & bit) != 0 && (g_regs[0x411] & bit) == 0;
}

static int key_row_b(uint8_t bit) {
    return (g_regs[0x408] & bit) != 0 && (g_regs[0x409] & bit) == 0;
}

static int id_detect_asserted(void) {
    const uint8_t mask = 0x04u;
    return (g_regs[0x430] & mask) == mask &&
           (g_regs[0x431] & mask) == 0 &&
           (g_regs[0x432] & mask) == 0 &&
           (g_regs[0x433] & mask) == mask;
}

static uint8_t hardware_id_key_state(void) {
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_M100_EXPERIMENTAL
    /* Cloudpilot/POSE identify the Palm m100 as ID1 + ID3 asserted. */
    return 0xfau;
#elif PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
    /* Palm IIIc/Austin identifies as ID1 + ID4 asserted. */
    return 0xf6u;
#else
    /* Palm IIIx/Brad identifies as ID1 + ID3 + ID4 asserted. */
    return 0xf2u;
#endif
}

static uint8_t port_d_key_bits(void) {
    uint8_t bits = 0;
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_M100_EXPERIMENTAL
    int row0 = key_row_b(0x01u);
    int row1 = key_row_b(0x08u);
    int row2 = key_row_b(0x40u);
#elif PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
    int row0 = key_row_c(0x01u);
    int row1 = key_row_c(0x02u);
    int row2 = key_row_c(0x04u);
#else
    int row0 = key_row_f(0x10u) || key_row_c(0x01u) || key_row_b(0x01u);
    int row1 = key_row_f(0x20u) || key_row_c(0x02u) || key_row_b(0x08u);
    int row2 = key_row_f(0x40u) || key_row_c(0x04u) || key_row_b(0x40u);
#endif

    if (row0) {
            if (g_button_bits_down & KEY_BIT_HARD1) bits |= 0x01u;
            if (g_button_bits_down & KEY_BIT_HARD2) bits |= 0x02u;
            if (g_button_bits_down & KEY_BIT_HARD3) bits |= 0x04u;
            if (g_button_bits_down & KEY_BIT_HARD4) bits |= 0x08u;
    }
    if (row1) {
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_M100_EXPERIMENTAL
            if (g_button_bits_down & KEY_BIT_PAGE_DOWN) bits |= 0x02u;
#else
            if (g_button_bits_down & KEY_BIT_PAGE_UP) bits |= 0x01u;
            if (g_button_bits_down & KEY_BIT_PAGE_DOWN) bits |= 0x02u;
#endif
    }
    if (row2) {
            if (g_button_bits_down & KEY_BIT_POWER) bits |= 0x01u;
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_M100_EXPERIMENTAL
            if (g_button_bits_down & KEY_BIT_PAGE_UP) bits |= 0x02u;
#elif PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
            if (g_button_bits_down & KEY_BIT_CONTRAST) bits |= 0x02u;
#endif
            if (g_button_bits_down & KEY_BIT_HARD2) bits |= 0x04u;
    }
    return bits;
}

static uint8_t sleeping_key_edge_columns(uint16_t bits) {
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
    uint8_t columns = 0;
    if (bits & (KEY_BIT_HARD1 | KEY_BIT_PAGE_UP | KEY_BIT_POWER)) columns |= 0x01u;
    if (bits & (KEY_BIT_HARD2 | KEY_BIT_PAGE_DOWN | KEY_BIT_CONTRAST)) columns |= 0x02u;
    if (bits & KEY_BIT_HARD3) columns |= 0x04u;
    if (bits & KEY_BIT_HARD4) columns |= 0x08u;
    return columns;
#else
    (void)bits;
    return 0;
#endif
}

static uint8_t port_input_value(char port) {
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
    if (port == 'C') return g_iiic_in_cradle ? 0x00u : 0x08u; /* Charging is active-low. */
    if (port == 'F') return 0x82u; /* FIXTRNL2 + LCD powered. */
#endif
    if (port == 'D') return id_detect_asserted() ? hardware_id_key_state() : port_d_key_bits();
    if (port == 'F') return g_pen_down ? 0x00 : 0x02;  /* Sumo/Brad PenIO is active low. */
    return port == 'E' ? 0xff : 0x00;
}

static uint8_t port_internal_value(char port) {
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
    if (port == 'D') return (uint8_t)(PORT_D_POWER_FAIL | (g_iiic_in_cradle ? 0x00u : 0x40u));
#endif
    return port == 'D' ? PORT_D_POWER_FAIL : 0x00;
}

static int port_data_offsets(uint16_t offset, uint16_t *dir, uint16_t *data, uint16_t *select, char *port) {
    switch (offset) {
        case 0x401: *dir = 0x400; *data = 0x401; *select = 0x402; *port = 'A'; return 1;
        case 0x409: *dir = 0x408; *data = 0x409; *select = 0x40b; *port = 'B'; return 1;
        case 0x411: *dir = 0x410; *data = 0x411; *select = 0x413; *port = 'C'; return 1;
        case 0x419: *dir = 0x418; *data = 0x419; *select = 0x41b; *port = 'D'; return 1;
        case 0x421: *dir = 0x420; *data = 0x421; *select = 0x423; *port = 'E'; return 1;
        case 0x429: *dir = 0x428; *data = 0x429; *select = 0x42b; *port = 'F'; return 1;
        case 0x431: *dir = 0x430; *data = 0x431; *select = 0x433; *port = 'G'; return 1;
        default: return 0;
    }
}

static uint8_t read_port_data(uint16_t offset) {
    uint16_t dir_offset, data_offset, select_offset;
    char port;
    if (!port_data_offsets(offset, &dir_offset, &data_offset, &select_offset, &port)) return g_regs[offset];

    uint8_t sel = g_regs[select_offset];
    uint8_t dir = g_regs[dir_offset];
    uint8_t output = g_regs[data_offset];
    uint8_t input = port_input_value(port);
    uint8_t internal = port_internal_value(port);
    if (port == 'D') sel |= 0x0f;

    internal &= (uint8_t)~sel;
    output &= (uint8_t)(sel & dir);
    input &= (uint8_t)(sel & ~dir);
    uint8_t value = (uint8_t)(output | input | internal);
#if PALM_HARDWARE_PROFILE == PALM_PROFILE_IIIC_EXPERIMENTAL
    if (port == 'F') {
        /*
         * Austin's ROM waits for the SED1375 LCD-powered input during display wake.
         * POSE/Cloudpilot force this bit high; if the GPIO select/dir state hides it,
         * HwrDisplayWake can spin forever with the panel half-powered.
         */
        value |= 0x81u;
    }
#endif
    return value;
}

static void update_port_d_interrupts(void) {
    uint16_t pending = (uint16_t)(get16(0x312) & (uint16_t)~(INT_LO_ALL_KEYS | INT_LO_KBD));
    uint8_t dir = g_regs[0x418];
    uint8_t data = port_d_key_bits();
    uint8_t polarity = g_regs[0x41c];
    uint8_t request = g_regs[0x41d];
    uint8_t kbd_enable = g_regs[0x41e];
    uint8_t edge = g_regs[0x41f];
    uint8_t bits = 0;

    bits |= (uint8_t)(~edge) & data & polarity;
    bits |= (uint8_t)(~edge) & (uint8_t)(~data) & (uint8_t)(~polarity);
    bits |= edge & g_port_d_edge & polarity;
    bits &= request & (uint8_t)(~dir);

    if ((data & (uint8_t)(~dir) & kbd_enable) != 0) {
        pending |= INT_LO_KBD;
    }
    pending |= ((uint16_t)bits << 8) & INT_LO_ALL_KEYS;
    put16(0x312, pending);
    update_interrupt_status();
}

static void update_rtc_interrupts(void) {
    uint16_t pending = (uint16_t)(get16(0xb0e) & get16(0xb10) &
        (RTC_INT_STOPWATCH | RTC_INT_MINUTE | RTC_INT_ALARM |
         RTC_INT_24HR | RTC_INT_SECOND | RTC_INT_HOUR));
    if ((get16(0xb0c) & RTC_CONTROL_ENABLE) != 0 && pending != 0) {
        put16(0x312, (uint16_t)(get16(0x312) | INT_LO_RTC));
    } else {
        put16(0x312, (uint16_t)(get16(0x312) & (uint16_t)~INT_LO_RTC));
    }
    update_interrupt_status();
}

static void update_rtc_time(void) {
    time_t now = time(NULL);
    struct tm *tm_now = localtime(&now);
    uint32_t hour = tm_now ? (uint32_t)tm_now->tm_hour : 0;
    uint32_t min = tm_now ? (uint32_t)tm_now->tm_min : 0;
    uint32_t sec = tm_now ? (uint32_t)tm_now->tm_sec : 0;
    put32(0xb00, (hour << 24) | (min << 16) | sec);
    if (now == g_last_rtc_second) return;

    int first_tick = g_last_rtc_second == (time_t)-1;
    g_last_rtc_second = now;
    if (first_tick || (get16(0xb0c) & RTC_CONTROL_ENABLE) == 0) return;

    uint16_t enabled = get16(0xb10);
    uint16_t status = get16(0xb0e);
    if ((enabled & RTC_INT_SECOND) != 0) status |= RTC_INT_SECOND;
    if (sec == 0 && (enabled & RTC_INT_MINUTE) != 0) status |= RTC_INT_MINUTE;
    if (min == 0 && sec == 0 && (enabled & RTC_INT_HOUR) != 0) status |= RTC_INT_HOUR;
    if (hour == 0 && min == 0 && sec == 0 && (enabled & RTC_INT_24HR) != 0) status |= RTC_INT_24HR;

    uint32_t alarm = get32(0xb04);
    uint32_t alarm_sec = alarm & 0x3fu;
    uint32_t alarm_min = (alarm >> 16) & 0x3fu;
    uint32_t alarm_hour = (alarm >> 24) & 0x1fu;
    if ((enabled & RTC_INT_ALARM) != 0 && alarm_hour == hour && alarm_min == min && alarm_sec == sec) {
        status |= RTC_INT_ALARM;
    }

    put16(0xb0e, status);
    update_rtc_interrupts();
}

static void update_uart_regs(void) {
    uint16_t control = get16(0x900);
    uint16_t receive = (uint16_t)(get16(0x904) & 0x00ffu);
    uint16_t transmit = (uint16_t)((get16(0x906) & 0x00ffu) | UART_TX_IGNORE_CTS);

    if (g_uart_rx_count > 0) {
        receive = (uint16_t)(UART_RX_DATA_READY | g_uart_rx_fifo[g_uart_rx_tail]);
        if (g_uart_rx_count >= UART_FIFO_SIZE) receive |= UART_RX_FIFO_FULL;
        if (g_uart_rx_count >= UART_FIFO_SIZE / 2u) receive |= UART_RX_FIFO_HALF;
    }

    transmit |= UART_TX_AVAILABLE;
    if (g_uart_tx_count == 0) transmit |= UART_TX_FIFO_EMPTY;
    if (g_uart_tx_count < UART_FIFO_SIZE / 2u) transmit |= UART_TX_FIFO_HALF;

    put16(0x904, receive);
    put16(0x906, transmit);

    uint16_t pending = 0;
    if ((control & UART_CONTROL_ENABLE) != 0) {
        if ((control & UART_CONTROL_RX_ENABLE) != 0) {
            if ((receive & UART_RX_DATA_READY) && (control & UART_CONTROL_RX_RDY_INT_ENABLE)) pending = 1;
            if ((receive & UART_RX_FIFO_HALF) && (control & UART_CONTROL_RX_HALF_INT_ENABLE)) pending = 1;
            if ((receive & UART_RX_FIFO_FULL) && (control & UART_CONTROL_RX_FULL_INT_ENABLE)) pending = 1;
        }
        if ((control & UART_CONTROL_TX_ENABLE) != 0) {
            if ((transmit & UART_TX_AVAILABLE) && (control & UART_CONTROL_TX_AVAIL_INT_ENABLE)) pending = 1;
            if ((transmit & UART_TX_FIFO_HALF) && (control & UART_CONTROL_TX_HALF_INT_ENABLE)) pending = 1;
            if ((transmit & UART_TX_FIFO_EMPTY) && (control & UART_CONTROL_TX_EMPTY_INT_ENABLE)) pending = 1;
        }
    }

    if (pending) {
        put16(0x312, (uint16_t)(get16(0x312) | INT_LO_UART));
    } else {
        put16(0x312, (uint16_t)(get16(0x312) & (uint16_t)~INT_LO_UART));
    }
    update_interrupt_status();
}

static int enqueue_uart_rx_byte(uint8_t value) {
    if (g_uart_rx_count >= UART_FIFO_SIZE) {
        g_uart_rx_overrun_count++;
        return 0;
    }

    g_uart_rx_fifo[g_uart_rx_head] = value;
    g_uart_rx_head = (g_uart_rx_head + 1u) % UART_FIFO_SIZE;
    g_uart_rx_count++;
    return 1;
}

static void complete_spi_exchange(void) {
    uint16_t control = get16(0x802);
    if ((control & (SPIM_ENABLE | SPIM_EXCHANGE)) != (SPIM_ENABLE | SPIM_EXCHANGE)) return;

    if ((g_regs[0x431] & 0x20) == 0) {
        uint16_t num_bits = (uint16_t)((control & 0x000f) + 1);
        uint32_t old_bits_mask = 0xffffffffu << num_bits;
        uint32_t new_bits_mask = ~old_bits_mask;
        uint16_t spi_data = get16(0x800);
        uint16_t result = 0;
        uint32_t mask;

        g_ads_bit_buffer_in = ((g_ads_bit_buffer_in << num_bits) & old_bits_mask) |
                              (spi_data & new_bits_mask);
        g_ads_num_bits_in += num_bits;

        mask = 1u << (g_ads_num_bits_in - g_ads_command_bits_seen - 1);
        while (mask) {
            result = (uint16_t)((result << 1) | (g_ads_bit_buffer_out >> 15));
            g_ads_bit_buffer_out <<= 1;

            if (g_ads_command_bits_seen == 0) {
                if ((mask & g_ads_bit_buffer_in) != 0) {
                    g_ads_command_bits_seen++;
                } else {
                    g_ads_num_bits_in--;
                }
                if (g_ads_have_pending) {
                    g_ads_have_pending = 0;
                    g_ads_bit_buffer_out = g_ads_pending_result;
                }
            } else {
                g_ads_command_bits_seen++;
                if (g_ads_command_bits_seen == 8) {
                    uint8_t command;
                    uint16_t conversion = 0;
                    int channel;

                    g_ads_num_bits_in -= 8;
                    g_ads_command_bits_seen = 0;
                    command = (uint8_t)(g_ads_bit_buffer_in >> g_ads_num_bits_in);
                    channel = (command & 0x70) >> 4;

                    switch (channel) {
                        case 1: /* pen Y */
                            conversion = g_pen_down ? g_pen_y_raw : 0x0000;
                            break;
                        case 5: /* pen X */
                            conversion = g_pen_down ? g_pen_x_raw : 0x0000;
                            break;
                        case 6: /* dock serial: undocked */
                            conversion = 0x0000;
                            break;
                        case 2: /* battery */
                            conversion = 0x0fff;
                            break;
                        default:
                            conversion = 0x0000;
                            break;
                    }

                    g_ads_pending_result = (uint16_t)(conversion << 4);
                    g_ads_have_pending = 1;
                    g_debug.adsCommandCount++;
                    g_debug.lastAdsCommand = command;
                    g_debug.lastAdsChannel = (uint16_t)channel;
                    g_debug.lastAdsConversion = conversion;
                    g_debug.lastAdsChannelConversion[channel] = conversion;
                    g_debug.adsChannelCounts[channel]++;
                }
            }

            mask >>= 1;
        }

        put16(0x800, (uint16_t)(result & new_bits_mask));
        g_debug.lastAdsResponse = (uint16_t)(result & new_bits_mask);
    }

    control |= SPIM_INT_STATUS;
    control &= (uint16_t)~SPIM_EXCHANGE;
    put16(0x802, control);
    if ((control & SPIM_INT_ENABLE) != 0) {
        put16(0x312, (uint16_t)(get16(0x312) | INT_LO_SPIM));
        update_interrupt_status();
    }
}

static void update_spim_interrupt_pending(void) {
    if ((get16(0x802) & SPIM_INT_STATUS) == 0) {
        put16(0x312, (uint16_t)(get16(0x312) & (uint16_t)~INT_LO_SPIM));
        update_interrupt_status();
    }
}

static void init_regs(void) {
    memset(g_regs, 0, sizeof(g_regs));
    g_regs[0x000] = 0x1c;
    g_regs[0x004] = 0x10;
    g_regs[0x005] = 0x05;
    put16(0x100, 0x0000);
    put16(0x110, 0x00e0);
    put16(0x118, 0x0060);
    put16(0x200, 0x2430);
    put16(0x116, 0x1800);
    put16(0x200, 0x2430);
    put16(0x202, 0x0123);
    g_regs[0x207] = 0x1f;
    put16(0x304, 0x00ff);
    put16(0x306, 0xffff);
    g_regs[0x402] = 0xff;
    g_regs[0x40a] = 0xff;
    g_regs[0x40b] = 0xff;
    g_regs[0x412] = 0xff;
    g_regs[0x413] = 0xff;
    g_regs[0x41a] = 0xff;
    g_regs[0x41b] = 0xf0;
    g_regs[0x422] = 0xff;
    g_regs[0x423] = 0xff;
    g_regs[0x42a] = 0xff;
    g_regs[0x432] = 0x3d;
    g_regs[0x433] = 0x08;
    put16(0x500, 0x0020);
    g_regs[0x504] = 0xfe;
    update_pwm_sound();
    put16(0x604, 0xffff);
    put16(0xb0a, 0x0001);
    g_regs[0xa05] = 0xff;
    put16(0xa08, 0x03ff);
    put16(0xa0a, 0x01ff);
    g_regs[0xa1f] = 0x7f;
    g_regs[0xa27] = 0x40;
    g_regs[0xa29] = 0xff;
    g_regs[0xa31] = 0xb9;
    g_regs[0xa33] = 0x84;
    put16(0xa36, 0x0000);
    g_last_timer_status = 0;
    g_timer_ticks = 0;
    g_ads_bit_buffer_in = 0;
    g_ads_bit_buffer_out = 0;
    g_ads_num_bits_in = 0;
    g_ads_pending_result = 0;
    g_ads_have_pending = 0;
    g_ads_command_bits_seen = 0;
    g_pen_down = 0;
    g_pen_x_raw = 0;
    g_pen_y_raw = 0;
    g_button_bits_down = 0;
    g_cradle_button_line_low = 0;
    g_iiic_in_cradle = 0;
    g_port_d_edge = 0;
    g_system_cycles = 0;
    g_timer_last_cycles = 0;
    g_last_rtc_second = (time_t)-1;
    g_lcd_dirty = 1;
    g_lcd_frame_ready = 1;
    g_lcd_ready_cycle = 0;
    g_uart_rx_head = 0;
    g_uart_rx_tail = 0;
    g_uart_rx_count = 0;
    g_uart_tx_head = 0;
    g_uart_tx_tail = 0;
    g_uart_tx_count = 0;
    g_uart_rx_overrun_count = 0;
    g_uart_tx_overrun_count = 0;
    put16(0x906, (uint16_t)(UART_TX_FIFO_EMPTY | UART_TX_FIFO_HALF | UART_TX_AVAILABLE | UART_TX_IGNORE_CTS));
#if PALM_HAS_SED1375
    init_sed1375();
#endif
}

static uint8_t read8(uint32_t address, int instruction_fetch) {
    uint32_t offset;
    g_debug.lastReadAddress = address;

    if (rom_offset(address, &offset)) {
        ++g_debug.romReadCount;
        return g_rom[offset];
    }

#if PALM_HAS_SED1375
    if (sed1375_reg_offset(address, &offset)) {
        ++g_debug.regReadCount;
        return sed1375_read_reg(offset);
    }

    if (sed1375_vram_offset(address, &offset)) {
        ++g_debug.ramReadCount;
        return g_sed1375_vram[offset];
    }
#endif

    if (reg_offset(address, &offset)) {
        ++g_debug.regReadCount;
        g_debug.lastRegReadOffset = (uint16_t)offset;
        if (offset >= 0x900 && offset <= 0x909) update_uart_regs();
        if (offset == 0x904 || offset == 0x905) {
            uint16_t receive = get16(0x904);
            uint8_t value = g_regs[offset];
            if (offset == 0x905 && (receive & UART_RX_DATA_READY) != 0 && g_uart_rx_count > 0) {
                value = g_uart_rx_fifo[g_uart_rx_tail];
                g_uart_rx_tail = (g_uart_rx_tail + 1u) % UART_FIFO_SIZE;
                g_uart_rx_count--;
                update_uart_regs();
            }
            return value;
        }
        if (offset == 0x202) g_regs[0x202] ^= 0x80;
        if (offset == 0x401 || offset == 0x409 || offset == 0x411 || offset == 0x419 ||
            offset == 0x421 || offset == 0x429 || offset == 0x431) {
            return read_port_data((uint16_t)offset);
        }
        if (offset >= 0x30c && offset <= 0x313) update_interrupt_status();
        if (offset >= 0x608 && offset <= 0x60b) update_timer();
        if (offset >= 0xb00 && offset <= 0xb03) update_rtc_time();
        if (offset >= 0x60a && offset <= 0x60b) {
            update_timer();
            g_last_timer_status |= get16(0x60a);
        }
        uint8_t value = g_regs[offset];
        return value;
    }

    if (ram_offset(address, &offset)) {
        ++g_debug.ramReadCount;
        return g_ram[offset];
    }

    if (ram_size_probe_address(address)) {
        ++g_debug.ramReadCount;
        return 0;
    }

    if (instruction_fetch) {
        ++g_debug.instrBusErrorCount;
    } else {
        ++g_debug.busErrorCount;
    }
    g_debug.lastBusErrorAddress = address;
    trace_bus_error(m68k_get_reg(0, M68K_REG_PC), address, instruction_fetch);
    m68k_pulse_bus_error();
    return 0xff;
}

static void write8(uint32_t address, uint8_t value) {
    uint32_t offset;
    g_debug.lastWriteAddress = address;

#if PALM_HAS_SED1375
    if (sed1375_reg_offset(address, &offset)) {
        sed1375_write_reg(offset, value);
        ++g_debug.regWriteCount;
        g_debug.lastRegWriteOffset = (uint16_t)offset;
        g_debug.lastRegWriteValue = value;
        if (offset >= 0x01 && offset <= 0x1c) {
            ++g_debug.lcdWriteCount;
            g_debug.lastLcdWriteOffset = (uint16_t)offset;
            g_debug.lastLcdWriteValue = value;
        }
        return;
    }

    if (sed1375_vram_offset(address, &offset)) {
        g_sed1375_vram[offset] = value;
        mark_lcd_dirty();
        ++g_debug.ramWriteCount;
        return;
    }
#endif

    if (reg_offset(address, &offset)) {
        if (offset == 0xb0e || offset == 0xb0f) {
            uint16_t status = get16(0xb0e);
            uint16_t clear_mask = offset == 0xb0e ? (uint16_t)(value << 8) : value;
            put16(0xb0e, (uint16_t)(status & ~clear_mask));
            update_rtc_interrupts();
            ++g_debug.regWriteCount;
            g_debug.lastRegWriteOffset = (uint16_t)offset;
            g_debug.lastRegWriteValue = value;
            trace_reg_write(m68k_get_reg(0, M68K_REG_PC), address, (uint16_t)offset, value);
            return;
        }

        if (offset == 0x30c || offset == 0x30d) {
            if (offset == 0x30d && (value & (uint8_t)INT_HI_IRQ1) != 0 && irq1_is_edge_triggered()) {
                put16(0x310, (uint16_t)(get16(0x310) & (uint16_t)~INT_HI_IRQ1));
            }
            update_interrupt_status();
            ++g_debug.regWriteCount;
            g_debug.lastRegWriteOffset = (uint16_t)offset;
            g_debug.lastRegWriteValue = value;
            trace_reg_write(m68k_get_reg(0, M68K_REG_PC), address, (uint16_t)offset, value);
            return;
        }

        g_regs[offset] = value;
        if (offset == 0x419) {
            g_port_d_edge &= (uint8_t)~(value & g_regs[0x41f]);
        }
        if (offset == 0x803) complete_spi_exchange();
        if (offset == 0x802 || offset == 0x803) update_spim_interrupt_pending();
        if (offset == 0x907) {
            if (g_uart_tx_count < UART_FIFO_SIZE) {
                g_uart_tx_fifo[g_uart_tx_head] = value;
                g_uart_tx_head = (g_uart_tx_head + 1u) % UART_FIFO_SIZE;
                g_uart_tx_count++;
            } else {
                g_uart_tx_overrun_count++;
            }
            update_uart_regs();
        }
        if (offset >= 0x900 && offset <= 0x909) update_uart_regs();
        if (offset >= 0x500 && offset <= 0x504) update_pwm_sound();
        if (offset == 0x60a || offset == 0x60b) {
            uint16_t status = get16(0x60a);
            status &= (uint16_t)(value | ~g_last_timer_status);
            put16(0x60a, status);
            g_last_timer_status = 0;
            if ((status & TMR_STATUS_COMPARE) == 0) {
                put16(0x312, (uint16_t)(get16(0x312) & ~INT_LO_TIMER));
                update_interrupt_status();
            }
        }
        if ((offset >= 0x302 && offset <= 0x313)) update_interrupt_status();
        if ((offset >= 0x418 && offset <= 0x41f) || offset == 0x408 || offset == 0x409 ||
            offset == 0x410 || offset == 0x411 || offset == 0x428 || offset == 0x429) {
            update_port_d_interrupts();
        }
#if PALM_HAS_SED1375
        if (offset == 0x411) update_iiic_lcd_brightness();
#endif
        if (offset >= 0xb0c && offset <= 0xb11) update_rtc_interrupts();
        ++g_debug.regWriteCount;
        g_debug.lastRegWriteOffset = (uint16_t)offset;
        g_debug.lastRegWriteValue = value;
        trace_reg_write(m68k_get_reg(0, M68K_REG_PC), address, (uint16_t)offset, value);

        if ((offset >= 0xa00 && offset <= 0xa0b) || offset == 0xa20 || offset == 0xa2d ||
            offset == 0xa33 || offset == 0xa36 || offset == 0xa37) {
            mark_lcd_dirty();
            ++g_debug.lcdWriteCount;
            g_debug.lastLcdWriteOffset = (uint16_t)offset;
            g_debug.lastLcdWriteValue = value;
        }
        return;
    }

    if (rom_offset(address, &offset)) return;

    if (ram_offset(address, &offset)) {
        g_ram[offset] = value;
        track_ram_write(address);
        ++g_debug.ramWriteCount;
        {
            uint32_t lcd_base = get32(0xa00) & 0x1ffffffeu;
            uint16_t lcd_height = (uint16_t)(get16(0xa0a) + 1);
            uint32_t lcd_bytes = (uint32_t)g_regs[0xa05] * 2u * lcd_height;
            if (lcd_base != 0 && address >= lcd_base && address < lcd_base + lcd_bytes) {
                mark_lcd_dirty();
            }
        }
        trace_ram_write(m68k_get_reg(0, M68K_REG_PC), address, offset, value);
        return;
    }

    if (ram_size_probe_address(address)) {
        ++g_debug.ramWriteCount;
        return;
    }

    ++g_debug.busErrorCount;
    g_debug.lastBusErrorAddress = address;
    trace_bus_error(m68k_get_reg(0, M68K_REG_PC), address, 0);
    m68k_pulse_bus_error();
}

static uint16_t read16(uint32_t address, int instruction_fetch) {
    return (uint16_t)((read8(address, instruction_fetch) << 8) | read8(address + 1, instruction_fetch));
}

static uint32_t read32(uint32_t address, int instruction_fetch) {
    return ((uint32_t)read16(address, instruction_fetch) << 16) | read16(address + 2, instruction_fetch);
}

unsigned int m68k_read_memory_8(unsigned int address) { return read8(address, 0); }
unsigned int m68k_read_memory_16(unsigned int address) { return read16(address, 0); }
unsigned int m68k_read_memory_32(unsigned int address) { return read32(address, 0); }
unsigned int palm_read_instr_16(unsigned int address) { return read16(address, 1); }

void m68k_write_memory_8(unsigned int address, unsigned int value) { write8(address, (uint8_t)value); }
void m68k_write_memory_16(unsigned int address, unsigned int value) {
    write8(address, (uint8_t)(value >> 8));
    write8(address + 1, (uint8_t)value);
}
void m68k_write_memory_32(unsigned int address, unsigned int value) {
    m68k_write_memory_16(address, value >> 16);
    m68k_write_memory_16(address + 2, value);
}

PALM_EXPORT int palm_native_init(const uint8_t *rom, uint32_t rom_size, uint32_t ram_size) {
    free(g_rom);
    free(g_ram);
    g_rom = (uint8_t *)malloc(rom_size);
    g_ram = (uint8_t *)calloc(1, ram_size);
    if (!g_rom || !g_ram) return 0;

    memcpy(g_rom, rom, rom_size);
    g_rom_size = rom_size;
    g_ram_size = ram_size;
    memset(g_ram_dirty_pages, 0, sizeof(g_ram_dirty_pages));
    g_ram_dirty_page_count = 0;
    g_ram_highest_written = 0xffffffffu;
    memset(&g_debug, 0, sizeof(g_debug));
    memset(g_trace, 0, sizeof(g_trace));
    memset(g_pc_trace, 0, sizeof(g_pc_trace));
    memset(g_first_pc_trace, 0, sizeof(g_first_pc_trace));
    memset(g_jump_trace, 0, sizeof(g_jump_trace));
    g_trace_pos = 0;
    g_trace_count = 0;
    g_pc_trace_pos = 0;
    g_pc_trace_count = 0;
    g_first_pc_trace_count = 0;
    g_jump_trace_pos = 0;
    g_jump_trace_count = 0;
    g_have_previous_pc = 0;
    g_trace_enabled = 0;
    g_cpu_initialized = 0;
    init_regs();

    m68k_init();
    m68k_set_cpu_type(M68K_CPU_TYPE_68000);
    m68k_set_instr_hook_callback(trace_pc);
    g_cpu_initialized = 1;
    uint32_t reset_sp = read32(PALM_ROM_BASE, 0);
    m68k_pulse_reset();
    m68k_set_reg(M68K_REG_SR, 0x2700);
    m68k_set_reg(M68K_REG_SP, reset_sp);
    m68k_set_reg(M68K_REG_ISP, reset_sp);
    m68k_set_reg(M68K_REG_USP, reset_sp);
    m68k_set_reg(M68K_REG_PC, read32(PALM_ROM_BASE + 4, 0));
    memset(&g_debug, 0, sizeof(g_debug));
    memset(g_trace, 0, sizeof(g_trace));
    memset(g_pc_trace, 0, sizeof(g_pc_trace));
    memset(g_first_pc_trace, 0, sizeof(g_first_pc_trace));
    memset(g_jump_trace, 0, sizeof(g_jump_trace));
    g_trace_pos = 0;
    g_trace_count = 0;
    g_pc_trace_pos = 0;
    g_pc_trace_count = 0;
    g_first_pc_trace_count = 0;
    g_jump_trace_pos = 0;
    g_jump_trace_count = 0;
    g_have_previous_pc = 0;
    return 1;
}

PALM_EXPORT void palm_native_set_trace_enabled(int enabled) { g_trace_enabled = enabled != 0; }

PALM_EXPORT void palm_native_warm_reset(void) {
    if (!g_cpu_initialized || !g_ram || !g_rom) return;

    m68k_pulse_reset();
    init_regs();
    uint32_t reset_sp = read32(PALM_ROM_BASE, 0);
    m68k_set_reg(M68K_REG_SR, 0x2700);
    m68k_set_reg(M68K_REG_SP, reset_sp);
    m68k_set_reg(M68K_REG_ISP, reset_sp);
    m68k_set_reg(M68K_REG_USP, reset_sp);
    m68k_set_reg(M68K_REG_PC, read32(PALM_ROM_BASE + 4, 0));

    memset(&g_debug, 0, sizeof(g_debug));
    memset(g_trace, 0, sizeof(g_trace));
    memset(g_pc_trace, 0, sizeof(g_pc_trace));
    memset(g_first_pc_trace, 0, sizeof(g_first_pc_trace));
    memset(g_jump_trace, 0, sizeof(g_jump_trace));
    g_trace_pos = 0;
    g_trace_count = 0;
    g_pc_trace_pos = 0;
    g_pc_trace_count = 0;
    g_first_pc_trace_count = 0;
    g_jump_trace_pos = 0;
    g_jump_trace_count = 0;
    g_have_previous_pc = 0;
}

PALM_EXPORT uint32_t palm_native_state_size(void) {
    uint32_t size = (uint32_t)(sizeof(PalmNativeStateHeader) + PALM_DB_REG_SIZE + g_ram_size);
#if PALM_HAS_SED1375
    size += PALM_SED1375_REG_SIZE + PALM_SED1375_VRAM_SIZE +
            (uint32_t)sizeof(g_sed1375_clut) + 4u;
#endif
    return size;
}

PALM_EXPORT int palm_native_save_state(uint8_t *buffer, uint32_t buffer_size) {
    uint32_t state_size = palm_native_state_size();
    if (!buffer || !g_ram || buffer_size < state_size) return 0;

    PalmNativeStateHeader header;
    memset(&header, 0, sizeof(header));
    header.magic = PALM_STATE_MAGIC;
    header.version = PALM_STATE_VERSION;
    header.totalSize = state_size;
    header.ramSize = g_ram_size;
    header.regSize = PALM_DB_REG_SIZE;
    header.romSize = g_rom_size;
    for (int i = 0; i < 8; ++i) {
        header.d[i] = m68k_get_reg(0, M68K_REG_D0 + i);
        header.a[i] = m68k_get_reg(0, M68K_REG_A0 + i);
    }
    header.pc = m68k_get_reg(0, M68K_REG_PC);
    header.sr = m68k_get_reg(0, M68K_REG_SR);
    header.sp = m68k_get_reg(0, M68K_REG_SP);
    header.usp = m68k_get_reg(0, M68K_REG_USP);
    header.isp = m68k_get_reg(0, M68K_REG_ISP);
    header.debug = g_debug;
    header.lastTimerStatus = g_last_timer_status;
    header.timerTicks = g_timer_ticks;
    header.adsBitBufferIn = g_ads_bit_buffer_in;
    header.adsBitBufferOut = g_ads_bit_buffer_out;
    header.adsNumBitsIn = g_ads_num_bits_in;
    header.adsPendingResult = g_ads_pending_result;
    header.adsHavePending = g_ads_have_pending;
    header.adsCommandBitsSeen = g_ads_command_bits_seen;
    header.penDown = g_pen_down;
    header.penXRaw = g_pen_x_raw;
    header.penYRaw = g_pen_y_raw;
    header.buttonBitsDown = g_button_bits_down;
    header.portDEdge = g_port_d_edge;
    header.cpuInitialized = (uint8_t)g_cpu_initialized;
    header.systemCycles = g_system_cycles;
    header.timerLastCycles = g_timer_last_cycles;
    header.lastRtcSecond = (int64_t)g_last_rtc_second;
    header.lcdDirty = g_lcd_dirty;
    header.lcdFrameReady = g_lcd_frame_ready;
    header.lcdReadyCycle = g_lcd_ready_cycle;
    memcpy(header.uartRxFifo, g_uart_rx_fifo, sizeof(header.uartRxFifo));
    memcpy(header.uartTxFifo, g_uart_tx_fifo, sizeof(header.uartTxFifo));
    header.uartRxHead = g_uart_rx_head;
    header.uartRxTail = g_uart_rx_tail;
    header.uartRxCount = g_uart_rx_count;
    header.uartTxHead = g_uart_tx_head;
    header.uartTxTail = g_uart_tx_tail;
    header.uartTxCount = g_uart_tx_count;
    header.uartRxOverrunCount = g_uart_rx_overrun_count;
    header.uartTxOverrunCount = g_uart_tx_overrun_count;

    uint8_t *cursor = buffer;
    memcpy(cursor, &header, sizeof(header));
    cursor += sizeof(header);
    memcpy(cursor, g_regs, PALM_DB_REG_SIZE);
    cursor += PALM_DB_REG_SIZE;
    memcpy(cursor, g_ram, g_ram_size);
    cursor += g_ram_size;
#if PALM_HAS_SED1375
    memcpy(cursor, g_sed1375_regs, PALM_SED1375_REG_SIZE);
    cursor += PALM_SED1375_REG_SIZE;
    memcpy(cursor, g_sed1375_vram, PALM_SED1375_VRAM_SIZE);
    cursor += PALM_SED1375_VRAM_SIZE;
    memcpy(cursor, g_sed1375_clut, sizeof(g_sed1375_clut));
    cursor += sizeof(g_sed1375_clut);
    cursor[0] = g_sed1375_lut_entry;
    cursor[1] = g_sed1375_lut_color;
    cursor[2] = (uint8_t)(g_iiic_lcd_brightness >> 8);
    cursor[3] = (uint8_t)(g_iiic_lcd_brightness & 0xffu);
#endif
    return 1;
}

PALM_EXPORT int palm_native_load_state(const uint8_t *buffer, uint32_t buffer_size) {
    if (!buffer || !g_ram || buffer_size < sizeof(PalmNativeStateHeader)) return 0;

    PalmNativeStateHeader header;
    memcpy(&header, buffer, sizeof(header));
    if (header.magic != PALM_STATE_MAGIC || header.version != PALM_STATE_VERSION) return 0;
    if (header.ramSize != g_ram_size || header.regSize != PALM_DB_REG_SIZE || header.romSize != g_rom_size) return 0;
    if (header.totalSize != palm_native_state_size()) return 0;
    if (buffer_size < header.totalSize) return 0;

    const uint8_t *cursor = buffer + sizeof(header);
    memcpy(g_regs, cursor, PALM_DB_REG_SIZE);
    cursor += PALM_DB_REG_SIZE;
    memcpy(g_ram, cursor, g_ram_size);
    cursor += g_ram_size;
#if PALM_HAS_SED1375
    memcpy(g_sed1375_regs, cursor, PALM_SED1375_REG_SIZE);
    cursor += PALM_SED1375_REG_SIZE;
    memcpy(g_sed1375_vram, cursor, PALM_SED1375_VRAM_SIZE);
    cursor += PALM_SED1375_VRAM_SIZE;
    memcpy(g_sed1375_clut, cursor, sizeof(g_sed1375_clut));
    cursor += sizeof(g_sed1375_clut);
    g_sed1375_lut_entry = cursor[0];
    g_sed1375_lut_color = cursor[1] % 3u;
    g_iiic_lcd_brightness = (uint16_t)(((uint16_t)cursor[2] << 8) | cursor[3]);
#endif

    g_debug = header.debug;
    g_last_timer_status = header.lastTimerStatus;
    g_timer_ticks = header.timerTicks;
    g_ads_bit_buffer_in = header.adsBitBufferIn;
    g_ads_bit_buffer_out = header.adsBitBufferOut;
    g_ads_num_bits_in = header.adsNumBitsIn;
    g_ads_pending_result = header.adsPendingResult;
    g_ads_have_pending = header.adsHavePending;
    g_ads_command_bits_seen = header.adsCommandBitsSeen;
    g_pen_down = header.penDown;
    g_pen_x_raw = header.penXRaw;
    g_pen_y_raw = header.penYRaw;
    g_button_bits_down = header.buttonBitsDown;
    g_port_d_edge = header.portDEdge;
    g_cpu_initialized = header.cpuInitialized != 0;
    g_system_cycles = header.systemCycles;
    g_timer_last_cycles = header.timerLastCycles;
    g_last_rtc_second = (time_t)header.lastRtcSecond;
    g_lcd_dirty = header.lcdDirty;
    g_lcd_frame_ready = header.lcdFrameReady;
    g_lcd_ready_cycle = header.lcdReadyCycle;
    memcpy(g_uart_rx_fifo, header.uartRxFifo, sizeof(g_uart_rx_fifo));
    memcpy(g_uart_tx_fifo, header.uartTxFifo, sizeof(g_uart_tx_fifo));
    g_uart_rx_head = header.uartRxHead % UART_FIFO_SIZE;
    g_uart_rx_tail = header.uartRxTail % UART_FIFO_SIZE;
    g_uart_rx_count = header.uartRxCount > UART_FIFO_SIZE ? UART_FIFO_SIZE : header.uartRxCount;
    g_uart_tx_head = header.uartTxHead % UART_FIFO_SIZE;
    g_uart_tx_tail = header.uartTxTail % UART_FIFO_SIZE;
    g_uart_tx_count = header.uartTxCount > UART_FIFO_SIZE ? UART_FIFO_SIZE : header.uartTxCount;
    g_uart_rx_overrun_count = header.uartRxOverrunCount;
    g_uart_tx_overrun_count = header.uartTxOverrunCount;

    for (int i = 0; i < 8; ++i) {
        m68k_set_reg(M68K_REG_D0 + i, header.d[i]);
        m68k_set_reg(M68K_REG_A0 + i, header.a[i]);
    }
    m68k_set_reg(M68K_REG_SR, header.sr);
    m68k_set_reg(M68K_REG_USP, header.usp);
    m68k_set_reg(M68K_REG_ISP, header.isp);
    m68k_set_reg(M68K_REG_SP, header.sp);
    m68k_set_reg(M68K_REG_PC, header.pc);

    update_interrupt_status();
    update_uart_regs();
    update_pwm_sound();
    update_lcd_frame();
    return 1;
}

PALM_EXPORT int palm_native_install_prc_image(const uint8_t *buffer, uint32_t buffer_size, uint32_t *result_out) {
    if (result_out) *result_out = 0xffffffffu;
    if (!buffer || buffer_size < 78 || !g_ram || !g_cpu_initialized) return 0;

    uint32_t a0 = 0;
    uint32_t params[1];
    params[0] = buffer_size;
    call_palm_trap(SYS_TRAP_MEM_PTR_NEW, params, 1, &a0);
    uint32_t image_ptr = a0;
    if (image_ptr == 0 || image_ptr >= PALM_RAM_LOGICAL_SIZE) {
        if (result_out) *result_out = 0xffff0000u | (a0 & 0xffffu);
        return 0;
    }

    for (uint32_t i = 0; i < buffer_size; ++i) {
        if (!ram_put8(image_ptr + i, buffer[i])) {
            params[0] = image_ptr;
            call_palm_trap(SYS_TRAP_MEM_PTR_FREE, params, 1, NULL);
            if (result_out) *result_out = 0xffff0002u;
            return 0;
        }
    }

    params[0] = image_ptr;
    uint32_t err = call_palm_trap(SYS_TRAP_DM_CREATE_DATABASE_FROM_IMAGE, params, 1, NULL);

    params[0] = image_ptr;
    call_palm_trap(SYS_TRAP_MEM_PTR_FREE, params, 1, NULL);

    if (result_out) *result_out = err;
    return err == 0;
}

PALM_EXPORT uint32_t palm_native_uart_write_rx(const uint8_t *buffer, uint32_t count) {
    if (!buffer || count == 0) return 0;

    uint32_t written = 0;
    while (written < count) {
        if (!enqueue_uart_rx_byte(buffer[written])) break;
        written++;
    }
    if (written < count) g_uart_rx_overrun_count += count - written - 1u;
    update_uart_regs();
    return written;
}

PALM_EXPORT uint32_t palm_native_uart_read_tx(uint8_t *buffer, uint32_t count) {
    if (!buffer || count == 0) return 0;

    uint32_t read = 0;
    while (read < count && g_uart_tx_count > 0) {
        buffer[read++] = g_uart_tx_fifo[g_uart_tx_tail];
        g_uart_tx_tail = (g_uart_tx_tail + 1u) % UART_FIFO_SIZE;
        g_uart_tx_count--;
    }
    update_uart_regs();
    return read;
}

PALM_EXPORT uint32_t palm_native_uart_rx_count(void) { return g_uart_rx_count; }
PALM_EXPORT uint32_t palm_native_uart_tx_count(void) { return g_uart_tx_count; }
PALM_EXPORT uint32_t palm_native_uart_rx_overrun_count(void) { return g_uart_rx_overrun_count; }
PALM_EXPORT uint32_t palm_native_uart_tx_overrun_count(void) { return g_uart_tx_overrun_count; }
PALM_EXPORT uint16_t palm_native_uart_misc(void) { return get16(0x908); }
PALM_EXPORT uint32_t palm_native_uart_is_irda(void) { return (get16(0x908) & UART_MISC_IRDA_ENABLE) != 0u ? 1u : 0u; }

PALM_EXPORT uint32_t palm_native_event_wakeup(void) {
    return call_palm_trap_stack(SYS_TRAP_EVT_WAKEUP, NULL, 0, NULL);
}

PALM_EXPORT uint32_t palm_native_show_brightness_adjust(void) {
    return call_palm_trap_stack(SYS_TRAP_UI_BRIGHTNESS_ADJUST, NULL, 0, NULL);
}

PALM_EXPORT int palm_native_execute(int cycles) {
    if (cycles > 0) g_system_cycles += (uint64_t)cycles;
    update_timer();
    update_rtc_time();
    update_lcd_frame();
    update_interrupt_status();
    if (is_asleep() && interrupt_level() == 0 && !has_wake_source()) return 0;
    int ran = m68k_execute(cycles);
    update_timer();
    update_lcd_frame();
    update_interrupt_status();
    return ran;
}

PALM_EXPORT int palm_native_advance_time_ms(uint32_t elapsed_ms) {
    if (elapsed_ms == 0) return 0;
    double cycles = system_clock_frequency() * ((double)elapsed_ms / 1000.0);
    if (cycles < 1.0) cycles = 1.0;
    g_system_cycles += (uint64_t)cycles;
    update_timer();
    update_rtc_time();
    update_lcd_frame();
    update_interrupt_status();
    return !(is_asleep() && interrupt_level() == 0 && !has_wake_source());
}

PALM_EXPORT int palm_native_service_wake(int cycles) {
    if (cycles <= 0) return 0;
    update_interrupt_status();
    if (is_asleep() && interrupt_level() == 0 && !has_wake_source()) return 0;
    int ran = m68k_execute(cycles);
    update_timer();
    update_lcd_frame();
    update_interrupt_status();
    return ran;
}

PALM_EXPORT uint32_t palm_native_get_pc(void) { return m68k_get_reg(0, M68K_REG_PC); }
PALM_EXPORT uint32_t palm_native_get_sp(void) { return m68k_get_reg(0, M68K_REG_SP); }
PALM_EXPORT uint32_t palm_native_get_sr(void) { return m68k_get_reg(0, M68K_REG_SR); }
PALM_EXPORT uint32_t palm_native_get_d0(void) { return m68k_get_reg(0, M68K_REG_D0); }
PALM_EXPORT uint32_t palm_native_get_a0(void) { return m68k_get_reg(0, M68K_REG_A0); }
PALM_EXPORT uint32_t palm_native_get_d(int index) {
    if (index < 0 || index > 7) return 0;
    return m68k_get_reg(0, M68K_REG_D0 + index);
}
PALM_EXPORT uint32_t palm_native_get_a(int index) {
    if (index < 0 || index > 7) return 0;
    return m68k_get_reg(0, M68K_REG_A0 + index);
}

PALM_EXPORT void palm_native_get_debug(PalmNativeDebug *debug) {
    if (debug) *debug = g_debug;
}

PALM_EXPORT uint16_t palm_native_ads_channel_conversion(int channel) {
    if (channel < 0 || channel > 7) return 0;
    return g_debug.lastAdsChannelConversion[channel];
}

PALM_EXPORT uint32_t palm_native_ads_channel_count(int channel) {
    if (channel < 0 || channel > 7) return 0;
    return g_debug.adsChannelCounts[channel];
}

PALM_EXPORT uint32_t palm_native_lcd_start(void) {
#if PALM_HAS_SED1375
    uint32_t offset = ((uint32_t)(g_sed1375_regs[0x11] & 0x03u) << 17) |
                      ((uint32_t)g_sed1375_regs[0x0d] << 9) |
                      ((uint32_t)g_sed1375_regs[0x0c] << 1);
    if (offset >= PALM_SED1375_VRAM_SIZE) offset = 0;
    return PALM_SED1375_BASE + offset;
#else
    return get32(0xa00) & 0x1ffffffeu;
#endif
}
PALM_EXPORT uint16_t palm_native_lcd_width(void) {
#if PALM_HAS_SED1375
    uint16_t width = (uint16_t)((g_sed1375_regs[0x04] + 1u) * 8u);
    return width == 0 ? PALM_LCD_WIDTH : width;
#else
    return get16(0xa08);
#endif
}
PALM_EXPORT uint16_t palm_native_lcd_height(void) {
#if PALM_HAS_SED1375
    uint16_t height = (uint16_t)(((uint16_t)g_sed1375_regs[0x06] << 8) | g_sed1375_regs[0x05]);
    return (uint16_t)(height + 1u);
#else
    return (uint16_t)(get16(0xa0a) + 1);
#endif
}
PALM_EXPORT uint16_t palm_native_lcd_pitch(void) {
#if PALM_HAS_SED1375
    uint16_t width = palm_native_lcd_width();
    uint8_t bpp = (uint8_t)(1u << ((g_sed1375_regs[0x02] & 0xc0u) >> 6));
    return (uint16_t)((width * bpp + 7u) / 8u);
#else
    return (uint16_t)(g_regs[0xa05] * 2);
#endif
}
PALM_EXPORT uint8_t palm_native_lcd_panel(void) {
#if PALM_HAS_SED1375
    return (uint8_t)((g_sed1375_regs[0x02] & 0xc0u) >> 6);
#else
    return g_regs[0xa20];
#endif
}
PALM_EXPORT uint8_t palm_native_lcd_pan(void) {
#if PALM_HAS_SED1375
    return 0;
#else
    return g_regs[0xa2d];
#endif
}
PALM_EXPORT uint16_t palm_native_lcd_contrast(void) {
#if PALM_HAS_SED1375
    return g_iiic_lcd_brightness;
#else
    return get16(0xa36);
#endif
}
PALM_EXPORT uint32_t palm_native_lcd_palette(uint8_t index) {
#if PALM_HAS_SED1375
    return g_sed1375_clut[index];
#else
    uint32_t level = index;
    return 0xff000000u | (level << 16) | (level << 8) | level;
#endif
}
PALM_EXPORT int palm_native_lcd_dirty(void) { return g_lcd_dirty; }
PALM_EXPORT int palm_native_lcd_frame_ready(void) { return g_lcd_dirty && g_lcd_frame_ready; }
PALM_EXPORT void palm_native_lcd_mark_clean(void) {
    g_lcd_dirty = 0;
    g_lcd_frame_ready = 0;
}
PALM_EXPORT uint32_t palm_native_probe32(uint32_t address) {
    uint32_t value = read32(address, 0);
    return value;
}

PALM_EXPORT uint8_t palm_native_peek8(uint32_t address) {
    uint8_t value = read8(address, 0);
    return value;
}
PALM_EXPORT uint32_t palm_native_copy_memory(uint32_t address, uint8_t *buffer, uint32_t count) {
    if (!buffer || count == 0) return 0;

    uint32_t offset;
    if (ram_offset(address, &offset)) {
        uint32_t available = g_ram_size - offset;
        uint32_t copied = count < available ? count : available;
        memcpy(buffer, g_ram + offset, copied);
        return copied;
    }

    if (rom_offset(address, &offset)) {
        uint32_t available = g_rom_size - offset;
        uint32_t copied = count < available ? count : available;
        memcpy(buffer, g_rom + offset, copied);
        return copied;
    }

#if PALM_HAS_SED1375
    if (sed1375_vram_offset(address, &offset)) {
        uint32_t available = PALM_SED1375_VRAM_SIZE - offset;
        uint32_t copied = count < available ? count : available;
        memcpy(buffer, g_sed1375_vram + offset, copied);
        return copied;
    }
#endif

    for (uint32_t i = 0; i < count; ++i) {
        buffer[i] = read8(address + i, 0);
    }
    return count;
}
PALM_EXPORT uint32_t palm_native_ram_physical_size(void) { return g_ram_size; }
PALM_EXPORT uint32_t palm_native_ram_dirty_pages(void) { return g_ram_dirty_page_count; }
PALM_EXPORT uint32_t palm_native_ram_highest_written(void) {
    return g_ram_highest_written == 0xffffffffu ? 0 : g_ram_highest_written;
}
PALM_EXPORT uint32_t palm_native_get_build_id(void) { return 0x20260525u; }
PALM_EXPORT int palm_native_sound_enabled(void) { return g_pwm_sound_enabled; }
PALM_EXPORT double palm_native_sound_frequency(void) { return g_pwm_sound_frequency; }
PALM_EXPORT double palm_native_sound_duty(void) { return g_pwm_sound_duty; }
PALM_EXPORT int palm_native_is_asleep(void) { return is_asleep(); }

PALM_EXPORT void palm_native_set_button_bits(uint16_t bits, int down) {
    uint8_t old_key_bits = port_d_key_bits();
    uint16_t before = g_button_bits_down;
    if (down != 0) {
        g_button_bits_down = (uint16_t)(g_button_bits_down | bits);
    } else {
        g_button_bits_down = (uint16_t)(g_button_bits_down & (uint16_t)~bits);
    }
    if (g_button_bits_down == before) return;

    uint8_t new_key_bits = port_d_key_bits();
    g_port_d_edge |= new_key_bits & (uint8_t)~old_key_bits;
    if (down != 0 && is_asleep()) {
        g_port_d_edge |= sleeping_key_edge_columns(bits);
    }
    update_port_d_interrupts();
}

PALM_EXPORT void palm_native_set_power_button(int down) {
    palm_native_set_button_bits(KEY_BIT_POWER, down);
}

PALM_EXPORT void palm_native_set_cradle_button(int down) {
    set_cradle_button_line(down);
}

PALM_EXPORT void palm_native_set_in_cradle(int in_cradle) {
    g_iiic_in_cradle = in_cradle != 0;
    update_interrupt_status();
}

PALM_EXPORT int palm_native_get_in_cradle(void) {
    return g_iiic_in_cradle;
}

PALM_EXPORT void palm_native_set_hotsync_button(int down) {
    palm_native_set_cradle_button(down);
}

PALM_EXPORT void palm_native_pulse_irq1(void) {
    put16(0x310, (uint16_t)(get16(0x310) | INT_HI_IRQ1));
    update_interrupt_status();
}

PALM_EXPORT void palm_native_set_pen(int down, uint16_t x, uint16_t y) {
    g_pen_down = down != 0;
    if (x > PEN_DIGITIZER_MAX_X) x = PEN_DIGITIZER_MAX_X;
    if (y > PEN_DIGITIZER_MAX_Y) y = PEN_DIGITIZER_MAX_Y;
    g_pen_x_raw = (uint16_t)(PEN_RAW_BASE - x);
    g_pen_y_raw = (uint16_t)(PEN_RAW_BASE - y);
    g_debug.penDown = (uint16_t)g_pen_down;
    g_debug.penXRaw = g_pen_x_raw;
    g_debug.penYRaw = g_pen_y_raw;
    if (g_pen_down) {
        put16(0x310, (uint16_t)(get16(0x310) | INT_HI_PEN));
    } else {
        put16(0x310, (uint16_t)(get16(0x310) & (uint16_t)~INT_HI_PEN));
    }
    update_interrupt_status();
}

PALM_EXPORT void palm_native_set_pen_raw(int down, uint16_t raw_x, uint16_t raw_y) {
    g_pen_down = down != 0;
    if (raw_x > PEN_ADC_MAX) raw_x = PEN_ADC_MAX;
    if (raw_y > PEN_ADC_MAX) raw_y = PEN_ADC_MAX;
    g_pen_x_raw = raw_x;
    g_pen_y_raw = raw_y;
    g_debug.penDown = (uint16_t)g_pen_down;
    g_debug.penXRaw = g_pen_x_raw;
    g_debug.penYRaw = g_pen_y_raw;
    if (g_pen_down) {
        put16(0x310, (uint16_t)(get16(0x310) | INT_HI_PEN));
    } else {
        put16(0x310, (uint16_t)(get16(0x310) & (uint16_t)~INT_HI_PEN));
    }
    update_interrupt_status();
}

PALM_EXPORT uint32_t palm_native_copy_trace(char *buffer, uint32_t buffer_size) {
    if (!buffer || buffer_size == 0) return 0;

    uint32_t written = 0;
    uint32_t start = g_trace_pos > g_trace_count ? g_trace_pos - g_trace_count : 0;
    for (uint32_t i = 0; i < g_trace_count; ++i) {
        const char *line = g_trace[(start + i) % TRACE_LINES];
        int n = snprintf(buffer + written, buffer_size - written, "%s\n", line);
        if (n < 0) break;
        if ((uint32_t)n >= buffer_size - written) {
            written = buffer_size - 1;
            break;
        }
        written += (uint32_t)n;
    }

    buffer[written] = 0;
    return written;
}

PALM_EXPORT uint32_t palm_native_copy_pc_trace(char *buffer, uint32_t buffer_size) {
    if (!buffer || buffer_size == 0) return 0;

    uint32_t written = 0;
    uint32_t start = g_pc_trace_pos > g_pc_trace_count ? g_pc_trace_pos - g_pc_trace_count : 0;
    for (uint32_t i = 0; i < g_pc_trace_count; ++i) {
        const char *line = g_pc_trace[(start + i) % TRACE_LINES];
        int n = snprintf(buffer + written, buffer_size - written, "%s\n", line);
        if (n < 0) break;
        if ((uint32_t)n >= buffer_size - written) {
            written = buffer_size - 1;
            break;
        }
        written += (uint32_t)n;
    }

    buffer[written] = 0;
    return written;
}

PALM_EXPORT uint32_t palm_native_copy_first_pc_trace(char *buffer, uint32_t buffer_size) {
    if (!buffer || buffer_size == 0) return 0;

    uint32_t written = 0;
    for (uint32_t i = 0; i < g_first_pc_trace_count; ++i) {
        const char *line = g_first_pc_trace[i];
        int n = snprintf(buffer + written, buffer_size - written, "%s\n", line);
        if (n < 0) break;
        if ((uint32_t)n >= buffer_size - written) {
            written = buffer_size - 1;
            break;
        }
        written += (uint32_t)n;
    }

    buffer[written] = 0;
    return written;
}

PALM_EXPORT uint32_t palm_native_copy_jump_trace(char *buffer, uint32_t buffer_size) {
    if (!buffer || buffer_size == 0) return 0;

    uint32_t written = 0;
    uint32_t start = g_jump_trace_pos > g_jump_trace_count ? g_jump_trace_pos - g_jump_trace_count : 0;
    for (uint32_t i = 0; i < g_jump_trace_count; ++i) {
        const char *line = g_jump_trace[(start + i) % TRACE_LINES];
        int n = snprintf(buffer + written, buffer_size - written, "%s\n", line);
        if (n < 0) break;
        if ((uint32_t)n >= buffer_size - written) {
            written = buffer_size - 1;
            break;
        }
        written += (uint32_t)n;
    }

    buffer[written] = 0;
    return written;
}
