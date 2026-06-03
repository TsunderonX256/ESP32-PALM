/* Minimal ESP32-PALM Musashi configuration.
 *
 * Based on the ESP32/CYD-friendly Musashi fork used by likeablob/cydintosh.
 * Keep this 68000-only: Palm IIIx uses a DragonBall EZ with a 68EC000 core.
 */

#ifndef M68KCONF__HEADER
#define M68KCONF__HEADER

#define OPT_OFF             0
#define OPT_ON              1
#define OPT_SPECIFY_HANDLER 2

#ifndef M68K_COMPILE_FOR_MAME
#define M68K_COMPILE_FOR_MAME OPT_OFF
#endif

#if M68K_COMPILE_FOR_MAME == OPT_OFF

#define M68K_EMULATE_010   OPT_OFF
#define M68K_EMULATE_EC020 OPT_OFF
#define M68K_EMULATE_020   OPT_OFF
#define M68K_EMULATE_040   OPT_OFF

#define M68K_SEPARATE_READS       OPT_OFF
#define M68K_SIMULATE_PD_WRITES   OPT_OFF
#define M68K_EMULATE_INT_ACK      OPT_OFF
#define M68K_EMULATE_BKPT_ACK     OPT_OFF
#define M68K_EMULATE_TRACE        OPT_OFF
#define M68K_EMULATE_RESET        OPT_OFF
#define M68K_CMPILD_HAS_CALLBACK  OPT_OFF
#define M68K_RTE_HAS_CALLBACK     OPT_OFF
#define M68K_TAS_HAS_CALLBACK     OPT_OFF
#define M68K_ILLG_HAS_CALLBACK    OPT_OFF
#define M68K_EMULATE_FC           OPT_OFF
#define M68K_MONITOR_PC           OPT_OFF
#define M68K_INSTRUCTION_HOOK     OPT_OFF
#define M68K_EMULATE_PREFETCH     OPT_OFF
#define M68K_EMULATE_ADDRESS_ERROR OPT_OFF
#define M68K_LOG_ENABLE           OPT_OFF
#define M68K_LOG_1010_1111        OPT_OFF

#ifndef M68K_DASM_ENABLE
#define M68K_DASM_ENABLE          OPT_OFF
#endif

/* Static decode table: consumes flash, not scarce ESP32 DRAM. */
#define M68K_DYNAMIC_INSTR_TABLES OPT_OFF
#define M68K_CYCLE_COUNTING       OPT_OFF
#define M68K_FIXED_CPU_TYPE       CPU_TYPE_000
#define M68K_BUS_ERR_ENABLE       OPT_OFF
#define M68K_USE_64_BIT           OPT_ON

unsigned int m68k_read_memory_8(unsigned int address);
unsigned int m68k_read_memory_16(unsigned int address);
unsigned int m68k_read_memory_32(unsigned int address);
unsigned int palm_read_instr_16(unsigned int address);
void m68k_write_memory_8(unsigned int address, unsigned int value);
void m68k_write_memory_16(unsigned int address, unsigned int value);
void m68k_write_memory_32(unsigned int address, unsigned int value);

#define m68k_read_instr_16(A) palm_read_instr_16(A)

#if defined(ESP32) || defined(ESP32_S3)
#include "esp_attr.h"
#define M68K_FAST_FUNC(x) IRAM_ATTR x
#endif

#endif

#endif
