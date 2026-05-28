#include "palm_memory.h"
#include "palm_hw.h"

#include <stdlib.h>
#include <string.h>
#if defined(ESP32)
#include <esp_heap_caps.h>
#endif

extern "C" {
extern const uint8_t palm_rom_start[];
extern const uint8_t palm_rom_end[];
void m68k_pulse_bus_error(void);
}

struct RamSegment {
  uint8_t *data;
  uint32_t base;
  uint32_t size;
};

static constexpr size_t RAM_MAX_SEGMENTS = 64;

#if PALM_RAM_STATIC_BACKING
static uint8_t palmRamStatic[PALM_RAM_ALLOC_TARGET_SIZE];
#else
static RamSegment palmRamSegments[RAM_MAX_SEGMENTS];
static size_t palmRamSegmentCount = 0;
#if PALM_RAM_STATIC_FALLBACK_SIZE > 0
static uint8_t palmRamStaticFallback[PALM_RAM_STATIC_FALLBACK_SIZE];
#endif
#endif
static size_t palmRamSizeBytes = 0;
static size_t palmRamLastAttemptBytes = 0;
static size_t palmRamLastAttemptSegments = 0;
static PalmMemoryDebug memoryDebug;

static void *palmAlloc(size_t size) {
#if defined(ESP32)
  void *ptr = heap_caps_malloc(size, MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
  if (ptr != nullptr) return ptr;
#if defined(MALLOC_CAP_SPIRAM)
  return heap_caps_malloc(size, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
#else
  return nullptr;
#endif
#else
  return malloc(size);
#endif
}

static void freeRamPages() {
#if !PALM_RAM_STATIC_BACKING
  for (size_t i = 0; i < palmRamSegmentCount; ++i) {
    bool staticSegment =
#if PALM_RAM_STATIC_FALLBACK_SIZE > 0
        palmRamSegments[i].data >= palmRamStaticFallback &&
        palmRamSegments[i].data < palmRamStaticFallback + sizeof(palmRamStaticFallback);
#else
        false;
#endif
    if (!staticSegment) {
#if defined(ESP32)
      heap_caps_free(palmRamSegments[i].data);
#else
      free(palmRamSegments[i].data);
#endif
    }
    palmRamSegments[i].data = nullptr;
    palmRamSegments[i].base = 0;
    palmRamSegments[i].size = 0;
  }
  palmRamSegmentCount = 0;
#endif
  palmRamSizeBytes = 0;
}

static uint8_t *ramPointer(uint32_t offset) {
#if PALM_RAM_STATIC_BACKING
  return palmRamStatic + offset;
#else
  for (size_t i = 0; i < palmRamSegmentCount; ++i) {
    RamSegment &segment = palmRamSegments[i];
    if (offset >= segment.base && offset < segment.base + segment.size) {
      return segment.data + (offset - segment.base);
    }
  }
  return nullptr;
#endif
}

static uint32_t mirroredRamOffset(uint32_t logicalOffset) {
  return palmRamSizeBytes == 0 ? 0 : logicalOffset % palmRamSizeBytes;
}

static bool inLogicalRam(uint32_t address, uint32_t &offset) {
  if (address >= PALM_RAM_BASE && address < PALM_RAM_BASE + PALM_RAM_LOGICAL_SIZE) {
    offset = address - PALM_RAM_BASE;
    return true;
  }

  uint32_t aliasBase = PALM_RAM_TOP_ALIAS_END - PALM_RAM_LOGICAL_SIZE;
  if (address >= aliasBase && address < PALM_RAM_TOP_ALIAS_END) {
    offset = address - aliasBase;
    return true;
  }

  return false;
}

static bool inRamBank(uint32_t address) {
  return (address >= PALM_RAM_BASE && address < PALM_RAM_TOP_ALIAS_END);
}

static bool ramSizeProbeAddress(uint32_t address) {
  return address >= PALM_RAM_BASE + PALM_RAM_LOGICAL_SIZE &&
         address < PALM_RAM_BASE + PALM_RAM_LOGICAL_SIZE + 4;
}

static void signalBusError(uint32_t address) {
#if PALM_TRACE_UNMAPPED
  ++memoryDebug.busErrorCount;
  memoryDebug.lastBusError = address;
#endif
#if PALM_BUS_ERROR_ON_RAM_LIMIT
  m68k_pulse_bus_error();
#endif
}

static void signalInstrBusError(uint32_t address) {
#if PALM_TRACE_UNMAPPED
  ++memoryDebug.instrBusErrorCount;
  memoryDebug.lastInstrBusError = address;
#endif
#if PALM_BUS_ERROR_ON_RAM_LIMIT
  m68k_pulse_bus_error();
#endif
}

bool palmMemoryInit() {
  if (palmRamSizeBytes != 0) return true;

#if PALM_RAM_STATIC_BACKING
  memset(palmRamStatic, 0, sizeof(palmRamStatic));
  palmRamSizeBytes = sizeof(palmRamStatic);
#else
  memset(palmRamSegments, 0, sizeof(palmRamSegments));
  static const size_t blockSizes[] = {65536, 32768, 16384, 8192, 4096, 2048, 1024, 512, 256, 128, 64};
  for (size_t b = 0; b < sizeof(blockSizes) / sizeof(blockSizes[0]) &&
                     palmRamSizeBytes < PALM_RAM_ALLOC_TARGET_SIZE &&
                     palmRamSegmentCount < RAM_MAX_SEGMENTS; ++b) {
    size_t blockSize = blockSizes[b];
    while (palmRamSizeBytes < PALM_RAM_ALLOC_TARGET_SIZE &&
           palmRamSegmentCount < RAM_MAX_SEGMENTS) {
      size_t remaining = PALM_RAM_ALLOC_TARGET_SIZE - palmRamSizeBytes;
      size_t request = remaining < blockSize ? remaining : blockSize;
#if defined(ESP32)
      if (PALM_POST_RAM_INTERNAL_RESERVE > 0) {
        size_t largestInternal =
            heap_caps_get_largest_free_block(MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
        if (largestInternal <= PALM_POST_RAM_INTERNAL_RESERVE + 64) break;
        size_t maxRequest = largestInternal - PALM_POST_RAM_INTERNAL_RESERVE;
        if (request > maxRequest) request = maxRequest;
        request &= ~static_cast<size_t>(63);
        if (request < 64) break;
      }
#endif
      uint8_t *segment = static_cast<uint8_t *>(palmAlloc(request));
      if (segment == nullptr) break;
      memset(segment, 0, request);
      palmRamSegments[palmRamSegmentCount].data = segment;
      palmRamSegments[palmRamSegmentCount].base = palmRamSizeBytes;
      palmRamSegments[palmRamSegmentCount].size = request;
      ++palmRamSegmentCount;
      palmRamSizeBytes += request;
    }
  }
#if PALM_RAM_STATIC_FALLBACK_SIZE > 0
  if (palmRamSizeBytes < PALM_RAM_ALLOC_TARGET_SIZE &&
      palmRamSegmentCount < RAM_MAX_SEGMENTS) {
    size_t remaining = PALM_RAM_ALLOC_TARGET_SIZE - palmRamSizeBytes;
    size_t fallbackSize = remaining < sizeof(palmRamStaticFallback) ?
                          remaining : sizeof(palmRamStaticFallback);
    memset(palmRamStaticFallback, 0, fallbackSize);
    palmRamSegments[palmRamSegmentCount].data = palmRamStaticFallback;
    palmRamSegments[palmRamSegmentCount].base = palmRamSizeBytes;
    palmRamSegments[palmRamSegmentCount].size = fallbackSize;
    ++palmRamSegmentCount;
    palmRamSizeBytes += fallbackSize;
  }
#endif
#endif

  if (palmRamSizeBytes < PALM_RAM_ALLOC_MIN_SIZE) {
    palmRamLastAttemptBytes = palmRamSizeBytes;
    palmRamLastAttemptSegments = palmRamSegmentCount;
    freeRamPages();
    return false;
  }

  palmRamLastAttemptBytes = palmRamSizeBytes;
  palmRamLastAttemptSegments = palmRamSegmentCount;
  memset(&memoryDebug, 0, sizeof(memoryDebug));
  palmHwInit();
  return true;
}

size_t palmRomSize() {
  return static_cast<size_t>(palm_rom_end - palm_rom_start);
}

size_t palmRamSize() { return palmRamSizeBytes; }
size_t palmRamLastAllocAttemptSize() { return palmRamLastAttemptBytes; }
size_t palmRamLastAllocAttemptSegments() { return palmRamLastAttemptSegments; }

static bool ramOffset(uint32_t address, uint32_t &offset) {
  if (palmRamSizeBytes == 0) return false;

  if (address >= PALM_RAM_BASE && address < PALM_RAM_BASE + palmRamSizeBytes) {
    offset = address - PALM_RAM_BASE;
    return true;
  }

  uint32_t aliasBase = PALM_RAM_TOP_ALIAS_END - palmRamSizeBytes;
  if (address >= aliasBase && address < PALM_RAM_TOP_ALIAS_END) {
    offset = address - aliasBase;
    return true;
  }

  return false;
}

static bool romOffset(uint32_t address, uint32_t &offset) {
  offset = address - PALM_ROM_BASE;
  if (address >= PALM_ROM_BASE && offset < palmRomSize()) {
    return true;
  }

  offset = address - PALM_ROM_LOW_ALIAS_BASE;
  if (address >= PALM_ROM_LOW_ALIAS_BASE && address < PALM_ROM_BASE && offset < palmRomSize()) {
    return true;
  }

  if (address >= PALM_ROM_LOW_ALIAS_BASE && address < PALM_ROM_LOW_ALIAS_BASE + PALM_ROM_CHIP_SELECT_SIZE) {
    offset = (address - PALM_ROM_LOW_ALIAS_BASE) % palmRomSize();
    return true;
  }

#if PALM_ENABLE_24BIT_ALIASES
  if (address >= PALM_ROM_LOW_24BIT_ALIAS_BASE && address < PALM_ROM_LOW_24BIT_ALIAS_BASE + PALM_ROM_CHIP_SELECT_SIZE) {
    offset = (address - PALM_ROM_LOW_24BIT_ALIAS_BASE) % palmRomSize();
    return true;
  }

  offset = address - PALM_ROM_LOW_24BIT_ALIAS_BASE;
  if (address >= PALM_ROM_LOW_24BIT_ALIAS_BASE && address < PALM_ROM_24BIT_BASE && offset < palmRomSize()) {
    return true;
  }

  offset = address - PALM_ROM_24BIT_BASE;
  return address >= PALM_ROM_24BIT_BASE && offset < palmRomSize();
#else
  return false;
#endif
}

uint8_t palmRead8(uint32_t address) {
  uint32_t offset;
  if (romOffset(address, offset)) return palm_rom_start[offset];
  if (palmHwInRegisterSpace(address)) return palmHwRead8(address);
  if (ramOffset(address, offset)) return *ramPointer(offset);
  if (ramSizeProbeAddress(address)) return 0x00;
#if PALM_MIRROR_LOGICAL_RAM
  if (inLogicalRam(address, offset)) {
#if PALM_TRACE_UNMAPPED
    ++memoryDebug.mirrorReadCount;
    memoryDebug.lastMirrorRead = address;
#endif
    return *ramPointer(mirroredRamOffset(offset));
  }
#else
  if (inRamBank(address)) {
    signalBusError(address);
    return 0xff;
  }
#endif
  if (inRamBank(address)) {
    signalBusError(address);
    return 0xff;
  }

#if PALM_TRACE_UNMAPPED
  ++memoryDebug.unmappedReadCount;
  memoryDebug.lastUnmappedRead = address;
#endif

  // Stub unmapped DragonBall/peripheral space as idle high for early probing.
  return 0xff;
}

uint16_t palmRead16(uint32_t address) {
  return (static_cast<uint16_t>(palmRead8(address)) << 8) |
         static_cast<uint16_t>(palmRead8(address + 1));
}

uint32_t palmRead32(uint32_t address) {
  return (static_cast<uint32_t>(palmRead16(address)) << 16) |
         static_cast<uint32_t>(palmRead16(address + 2));
}

void palmWrite8(uint32_t address, uint8_t value) {
  uint32_t offset;
  if (palmHwInRegisterSpace(address)) {
    palmHwWrite8(address, value);
    return;
  }

  if (romOffset(address, offset)) {
    return;
  }

  if (ramOffset(address, offset)) {
    *ramPointer(offset) = value;
    palmHwNotifyMemoryWrite(address);
    return;
  }

  if (ramSizeProbeAddress(address)) {
    return;
  }

#if PALM_MIRROR_LOGICAL_RAM
  if (inLogicalRam(address, offset)) {
#if PALM_TRACE_UNMAPPED
    ++memoryDebug.mirrorWriteCount;
    memoryDebug.lastMirrorWrite = address;
#endif
    *ramPointer(mirroredRamOffset(offset)) = value;
    palmHwNotifyMemoryWrite(address);
    return;
  }
#else
  if (inRamBank(address)) {
    signalBusError(address);
    return;
  }
#endif
  if (inRamBank(address)) {
    signalBusError(address);
    return;
  }

#if PALM_TRACE_UNMAPPED
  ++memoryDebug.unmappedWriteCount;
  memoryDebug.lastUnmappedWrite = address;
  memoryDebug.lastUnmappedWriteValue = value;
#endif
}

void palmWrite16(uint32_t address, uint16_t value) {
  palmWrite8(address, value >> 8);
  palmWrite8(address + 1, value & 0xff);
}

void palmWrite32(uint32_t address, uint32_t value) {
  palmWrite16(address, value >> 16);
  palmWrite16(address + 2, value & 0xffff);
}

extern "C" unsigned int m68k_read_memory_8(unsigned int address) {
  return palmRead8(address);
}

extern "C" unsigned int m68k_read_memory_16(unsigned int address) {
  return palmRead16(address);
}

extern "C" unsigned int m68k_read_memory_32(unsigned int address) {
  return palmRead32(address);
}

extern "C" unsigned int palm_read_instr_16(unsigned int address) {
  uint32_t offset;
  uint32_t nextOffset;
  if (romOffset(address, offset) && romOffset(address + 1, nextOffset)) {
    return (static_cast<unsigned int>(palm_rom_start[offset]) << 8) |
           static_cast<unsigned int>(palm_rom_start[nextOffset]);
  }
  if (palmHwInRegisterSpace(address)) return palmHwRead8(address) << 8 | palmHwRead8(address + 1);
  if (ramOffset(address, offset) && ramOffset(address + 1, nextOffset)) {
    return (static_cast<unsigned int>(*ramPointer(offset)) << 8) |
           static_cast<unsigned int>(*ramPointer(nextOffset));
  }
#if PALM_MIRROR_LOGICAL_RAM
  if (inLogicalRam(address, offset) && inLogicalRam(address + 1, nextOffset)) {
#if PALM_TRACE_UNMAPPED
    ++memoryDebug.mirrorReadCount;
    memoryDebug.lastMirrorRead = address;
#endif
    return (static_cast<unsigned int>(*ramPointer(mirroredRamOffset(offset))) << 8) |
           static_cast<unsigned int>(*ramPointer(mirroredRamOffset(nextOffset)));
  }
#endif

  signalInstrBusError(address);
  return 0xffff;
}

extern "C" void m68k_write_memory_8(unsigned int address, unsigned int value) {
  palmWrite8(address, value & 0xff);
}

extern "C" void m68k_write_memory_16(unsigned int address, unsigned int value) {
  palmWrite16(address, value & 0xffff);
}

extern "C" void m68k_write_memory_32(unsigned int address, unsigned int value) {
  palmWrite32(address, value);
}

PalmMemoryDebug palmMemoryGetDebug() { return memoryDebug; }
