#pragma once

#include <Arduino.h>
#include "palm_config.h"

#define PALM_DB_REG_BASE 0xfffff000UL
#define PALM_DB_REG_24BIT_BASE 0x00fff000UL
#define PALM_DB_REG_SIZE 0x1000UL

struct PalmLcdState {
  uint32_t startAddr;
  uint16_t width;
  uint16_t height;
  uint16_t bytesPerLine;
  uint8_t bpp;
  uint8_t margin;
  uint8_t panelControl;
  uint32_t dirtyGeneration;
  bool valid;
  bool dirty;
  bool frameReady;
};

struct PalmHwDebug {
  uint32_t regReadCount;
  uint32_t regWriteCount;
  uint16_t lastRegReadOffset;
  uint16_t lastRegWriteOffset;
  uint8_t lastRegWriteValue;
  uint32_t lcdWriteCount;
  uint16_t lastLcdWriteOffset;
  uint8_t lastLcdWriteValue;
};

void palmHwInit();
bool palmHwInRegisterSpace(uint32_t address);
uint8_t palmHwRead8(uint32_t address);
void palmHwWrite8(uint32_t address, uint8_t value);
void palmHwSetPen(bool down, uint16_t x, uint16_t y);
void palmHwSetPenRaw(bool down, uint16_t rawX, uint16_t rawY);
void palmHwSetButtonBits(uint16_t bits, bool down);
void palmHwSetPowerButton(bool down);
void palmHwSetCradleButton(bool down);
void palmHwCycle();
uint8_t palmHwGetInterruptLevel();
bool palmHwIsAsleep();
bool palmHwHasWakeSource();
void palmHwNotifyMemoryWrite(uint32_t address);
PalmLcdState palmHwGetLcdState();
PalmHwDebug palmHwGetDebug();
void palmHwMarkLcdClean();
