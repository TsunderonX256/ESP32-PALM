#pragma once

#include <Arduino.h>
#include "palm_config.h"

bool palmMemoryInit();
size_t palmRomSize();
size_t palmRamSize();
size_t palmRamLastAllocAttemptSize();
size_t palmRamLastAllocAttemptSegments();
size_t palmRamSegmentSize(size_t index);
bool palmRamWriteBytes(uint32_t offset, const uint8_t *data, uint32_t count);
bool palmRamReadBytes(uint32_t offset, uint8_t *data, uint32_t count);

struct PalmLcdState;

uint8_t palmRead8(uint32_t address);
uint16_t palmRead16(uint32_t address);
uint32_t palmRead32(uint32_t address);

void palmWrite8(uint32_t address, uint8_t value);
void palmWrite16(uint32_t address, uint16_t value);
void palmWrite32(uint32_t address, uint32_t value);

struct PalmMemoryDebug {
  uint32_t unmappedReadCount;
  uint32_t unmappedWriteCount;
  uint32_t lastUnmappedRead;
  uint32_t lastUnmappedWrite;
  uint32_t lastUnmappedWriteValue;
  uint32_t sparseReadCount;
  uint32_t sparseWriteCount;
  uint32_t lastSparseRead;
  uint32_t lastSparseWrite;
  uint32_t mirrorReadCount;
  uint32_t mirrorWriteCount;
  uint32_t lastMirrorRead;
  uint32_t lastMirrorWrite;
  uint32_t busErrorCount;
  uint32_t lastBusError;
  uint32_t instrBusErrorCount;
  uint32_t lastInstrBusError;
};

PalmMemoryDebug palmMemoryGetDebug();

bool palmSed1375GetLcdState(PalmLcdState &lcd);
uint16_t palmSed1375PaletteColor565(uint8_t index);
uint32_t palmSed1375PaletteGeneration();
uint32_t palmSed1375VramGeneration();
const uint8_t *palmSed1375VramPointer(uint32_t address, uint32_t count);
size_t palmSed1375StateSize();
bool palmSed1375ReadStateBytes(uint32_t offset, uint8_t *dest, size_t count);
bool palmSed1375WriteStateBytes(uint32_t offset, const uint8_t *src, size_t count);

extern "C" {
unsigned int m68k_read_memory_8(unsigned int address);
unsigned int m68k_read_memory_16(unsigned int address);
unsigned int m68k_read_memory_32(unsigned int address);
unsigned int palm_read_instr_16(unsigned int address);
unsigned int m68k_read_immediate_16(unsigned int address);
unsigned int m68k_read_immediate_32(unsigned int address);
unsigned int m68k_read_pcrelative_8(unsigned int address);
unsigned int m68k_read_pcrelative_16(unsigned int address);
unsigned int m68k_read_pcrelative_32(unsigned int address);
void m68k_write_memory_8(unsigned int address, unsigned int value);
void m68k_write_memory_16(unsigned int address, unsigned int value);
void m68k_write_memory_32(unsigned int address, unsigned int value);
}
