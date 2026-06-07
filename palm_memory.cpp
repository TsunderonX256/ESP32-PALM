#include "palm_memory.h"
#include "palm_hw.h"

#include <stdlib.h>
#include <string.h>
#if defined(ESP32)
#include <esp_attr.h>
#include <esp_heap_caps.h>
#define PALM_MEM_FAST IRAM_ATTR
#else
#define PALM_MEM_FAST
#endif

#ifndef PALM_MEMORY_COMPATIBLE_ACCESS
#define PALM_MEMORY_COMPATIBLE_ACCESS 0
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
static constexpr uint32_t PALM_MEM_PAGE_SHIFT = 12;
static constexpr uint32_t PALM_MEM_PAGE_SIZE = 1UL << PALM_MEM_PAGE_SHIFT;
static constexpr uint32_t PALM_MEM_PAGE_MASK = PALM_MEM_PAGE_SIZE - 1;
static constexpr size_t PALM_MEM_PAGE_CACHE_ENTRIES = 256;

struct DirectReadPage {
  uint32_t tag;
  const uint8_t *base;
};

struct DirectWritePage {
  uint32_t tag;
  uint8_t *base;
};

static DirectReadPage readPageCache[PALM_MEM_PAGE_CACHE_ENTRIES];
static DirectWritePage writePageCache[PALM_MEM_PAGE_CACHE_ENTRIES];

static void resetPageCache() {
  for (size_t i = 0; i < PALM_MEM_PAGE_CACHE_ENTRIES; ++i) {
    readPageCache[i].tag = 0xffffffffUL;
    readPageCache[i].base = nullptr;
    writePageCache[i].tag = 0xffffffffUL;
    writePageCache[i].base = nullptr;
  }
}

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

#if PALM_HAS_SED1375
static uint8_t sed1375Regs[PALM_SED1375_REG_SIZE];
static uint8_t *sed1375Vram = nullptr;
static uint32_t sed1375Clut[256];
static uint16_t sed1375Clut565[256];
static uint32_t sed1375PaletteGeneration = 1;
static uint32_t sed1375VramGeneration = 1;
static uint8_t sed1375LutEntry = 0;
static uint8_t sed1375LutColor = 0;
static constexpr uint32_t SED1375_STATE_REGS_OFFSET = 0;
static constexpr uint32_t SED1375_STATE_LUT_OFFSET = SED1375_STATE_REGS_OFFSET + PALM_SED1375_REG_SIZE;
static constexpr uint32_t SED1375_STATE_CLUT_OFFSET = SED1375_STATE_LUT_OFFSET + 4;
static constexpr uint32_t SED1375_STATE_VRAM_OFFSET = SED1375_STATE_CLUT_OFFSET + 256UL * sizeof(uint32_t);
static constexpr uint32_t SED1375_STATE_SIZE = SED1375_STATE_VRAM_OFFSET + PALM_SED1375_VRAM_SIZE;
#endif

static inline size_t PALM_MEM_FAST currentRomSize() {
  return static_cast<size_t>(palm_rom_end - palm_rom_start);
}

static void *palmAlloc(size_t size) {
#if defined(ESP32)
#if PALM_PREFER_PSRAM && defined(MALLOC_CAP_SPIRAM)
  void *ptr = heap_caps_malloc(size, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
  if (ptr != nullptr) return ptr;
  return heap_caps_malloc(size, MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
#else
  void *ptr = heap_caps_malloc(size, MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
  if (ptr != nullptr) return ptr;
#if defined(MALLOC_CAP_SPIRAM)
  return heap_caps_malloc(size, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
#else
  return nullptr;
#endif
#endif
#else
  return malloc(size);
#endif
}

static void *palmAllocInternal(size_t size) {
#if defined(ESP32)
  return heap_caps_malloc(size, MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
#else
  return malloc(size);
#endif
}

static void freeRamPages() {
#if PALM_HAS_SED1375
  if (sed1375Vram != nullptr) {
#if defined(ESP32)
    heap_caps_free(sed1375Vram);
#else
    free(sed1375Vram);
#endif
    sed1375Vram = nullptr;
  }
#endif
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
  resetPageCache();
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

static uint8_t *ramPointerSpan(uint32_t offset, uint32_t size) {
#if PALM_RAM_STATIC_BACKING
  return (offset + size <= sizeof(palmRamStatic)) ? palmRamStatic + offset : nullptr;
#else
  for (size_t i = 0; i < palmRamSegmentCount; ++i) {
    RamSegment &segment = palmRamSegments[i];
    if (offset >= segment.base && offset + size <= segment.base + segment.size) {
      return segment.data + (offset - segment.base);
    }
  }
  return nullptr;
#endif
}

static uint32_t mirroredRamOffset(uint32_t logicalOffset);

static inline uint8_t *PALM_MEM_FAST ramPointerFastSpan(uint32_t offset, uint32_t size) {
  if (palmRamSizeBytes == 0 || size == 0 || offset > palmRamSizeBytes - size) return nullptr;
#if PALM_RAM_STATIC_BACKING
  return palmRamStatic + offset;
#else
  if (palmRamSegmentCount > 0) {
    RamSegment &s0 = palmRamSegments[0];
    if (s0.size >= size && offset >= s0.base && offset <= s0.base + s0.size - size) {
      return s0.data + (offset - s0.base);
    }
  }
  if (palmRamSegmentCount > 1) {
    RamSegment &s1 = palmRamSegments[1];
    if (s1.size >= size && offset >= s1.base && offset <= s1.base + s1.size - size) {
      return s1.data + (offset - s1.base);
    }
  }
  return ramPointerSpan(offset, size);
#endif
}

static inline uint8_t *PALM_MEM_FAST ramPointerForAddress(uint32_t address, uint32_t size) {
  if (palmRamSizeBytes == 0 || size == 0 || palmRamSizeBytes < size) return nullptr;

  uint32_t ramStart = PALM_RAM_BASE;
  uint32_t ramEnd = PALM_RAM_BASE + palmRamSizeBytes;
  if (address >= ramStart && address <= ramEnd - size) {
    return ramPointerFastSpan(address - PALM_RAM_BASE, size);
  }

  uint32_t aliasBase = PALM_RAM_TOP_ALIAS_END - palmRamSizeBytes;
  if (address >= aliasBase && address <= PALM_RAM_TOP_ALIAS_END - size) {
    return ramPointerFastSpan(address - aliasBase, size);
  }

#if PALM_MIRROR_LOGICAL_RAM
  if (address >= PALM_RAM_BASE && address <= PALM_RAM_BASE + PALM_RAM_LOGICAL_SIZE - size) {
    uint32_t offset = mirroredRamOffset(address - PALM_RAM_BASE);
    if (offset <= palmRamSizeBytes - size) return ramPointerFastSpan(offset, size);
  }

  uint32_t logicalAliasBase = PALM_RAM_TOP_ALIAS_END - PALM_RAM_LOGICAL_SIZE;
  if (address >= logicalAliasBase && address <= PALM_RAM_TOP_ALIAS_END - size) {
    uint32_t offset = mirroredRamOffset(address - logicalAliasBase);
    if (offset <= palmRamSizeBytes - size) return ramPointerFastSpan(offset, size);
  }
#endif

  return nullptr;
}

static inline const uint8_t *PALM_MEM_FAST romPointerForAddress(uint32_t address, uint32_t size) {
  size_t romSize = currentRomSize();
  if (romSize == 0 || size == 0 || size > romSize) return nullptr;

  uint32_t offset = address - PALM_ROM_BASE;
  if (address >= PALM_ROM_BASE && offset <= romSize - size) {
    return palm_rom_start + offset;
  }

  offset = address - PALM_ROM_LOW_ALIAS_BASE;
  if (address >= PALM_ROM_LOW_ALIAS_BASE && address < PALM_ROM_BASE && offset <= romSize - size) {
    return palm_rom_start + offset;
  }

#if PALM_ENABLE_24BIT_ALIASES
  offset = address - PALM_ROM_LOW_24BIT_ALIAS_BASE;
  if (address >= PALM_ROM_LOW_24BIT_ALIAS_BASE && address < PALM_ROM_24BIT_BASE && offset <= romSize - size) {
    return palm_rom_start + offset;
  }

  offset = address - PALM_ROM_24BIT_BASE;
  if (address >= PALM_ROM_24BIT_BASE && offset <= romSize - size) {
    return palm_rom_start + offset;
  }
#endif

  if (address >= PALM_ROM_LOW_ALIAS_BASE &&
      address < PALM_ROM_LOW_ALIAS_BASE + PALM_ROM_CHIP_SELECT_SIZE) {
    offset = (address - PALM_ROM_LOW_ALIAS_BASE) % romSize;
    if (offset <= romSize - size) return palm_rom_start + offset;
  }

#if PALM_ENABLE_24BIT_ALIASES
  if (address >= PALM_ROM_LOW_24BIT_ALIAS_BASE &&
      address < PALM_ROM_LOW_24BIT_ALIAS_BASE + PALM_ROM_CHIP_SELECT_SIZE) {
    offset = (address - PALM_ROM_LOW_24BIT_ALIAS_BASE) % romSize;
    if (offset <= romSize - size) return palm_rom_start + offset;
  }
#endif

  return nullptr;
}

static inline bool PALM_MEM_FAST pageContainsSpan(uint32_t address, uint32_t size) {
  return size > 0 && size <= PALM_MEM_PAGE_SIZE &&
         (address & PALM_MEM_PAGE_MASK) <= PALM_MEM_PAGE_SIZE - size;
}

static inline size_t PALM_MEM_FAST pageCacheIndex(uint32_t tag) {
  return (tag ^ (tag >> 8) ^ (tag >> 16)) & (PALM_MEM_PAGE_CACHE_ENTRIES - 1);
}

static inline const uint8_t *PALM_MEM_FAST fillReadPage(uint32_t tag) {
  uint32_t pageBase = tag << PALM_MEM_PAGE_SHIFT;
  const uint8_t *base = romPointerForAddress(pageBase, PALM_MEM_PAGE_SIZE);
  if (base == nullptr) {
    base = ramPointerForAddress(pageBase, PALM_MEM_PAGE_SIZE);
  }

  DirectReadPage &entry = readPageCache[pageCacheIndex(tag)];
  entry.tag = tag;
  entry.base = base;
  return base;
}

static inline uint8_t *PALM_MEM_FAST fillWritePage(uint32_t tag) {
  uint32_t pageBase = tag << PALM_MEM_PAGE_SHIFT;
  uint8_t *base = nullptr;
  if (romPointerForAddress(pageBase, PALM_MEM_PAGE_SIZE) == nullptr) {
    base = ramPointerForAddress(pageBase, PALM_MEM_PAGE_SIZE);
  }

  DirectWritePage &entry = writePageCache[pageCacheIndex(tag)];
  entry.tag = tag;
  entry.base = base;
  return base;
}

static inline const uint8_t *PALM_MEM_FAST cachedReadPointer(uint32_t address, uint32_t size) {
  if (!pageContainsSpan(address, size)) return nullptr;

  uint32_t tag = address >> PALM_MEM_PAGE_SHIFT;
  DirectReadPage &entry = readPageCache[pageCacheIndex(tag)];
  const uint8_t *base = entry.tag == tag ? entry.base : fillReadPage(tag);
  return base != nullptr ? base + (address & PALM_MEM_PAGE_MASK) : nullptr;
}

static inline uint8_t *PALM_MEM_FAST cachedWritePointer(uint32_t address, uint32_t size) {
  if (!pageContainsSpan(address, size)) return nullptr;

  uint32_t tag = address >> PALM_MEM_PAGE_SHIFT;
  DirectWritePage &entry = writePageCache[pageCacheIndex(tag)];
  uint8_t *base = entry.tag == tag ? entry.base : fillWritePage(tag);
  return base != nullptr ? base + (address & PALM_MEM_PAGE_MASK) : nullptr;
}

static inline uint16_t PALM_MEM_FAST readBe16(const uint8_t *ptr) {
  return (static_cast<uint16_t>(ptr[0]) << 8) | ptr[1];
}

static inline uint32_t PALM_MEM_FAST readBe32(const uint8_t *ptr) {
  return (static_cast<uint32_t>(ptr[0]) << 24) |
         (static_cast<uint32_t>(ptr[1]) << 16) |
         (static_cast<uint32_t>(ptr[2]) << 8) |
         static_cast<uint32_t>(ptr[3]);
}

static inline void PALM_MEM_FAST writeBe16(uint8_t *ptr, uint16_t value) {
  ptr[0] = value >> 8;
  ptr[1] = value & 0xff;
}

static inline void PALM_MEM_FAST writeBe32(uint8_t *ptr, uint32_t value) {
  ptr[0] = value >> 24;
  ptr[1] = (value >> 16) & 0xff;
  ptr[2] = (value >> 8) & 0xff;
  ptr[3] = value & 0xff;
}

static uint32_t mirroredRamOffset(uint32_t logicalOffset) {
  return palmRamSizeBytes == 0 ? 0 : logicalOffset % palmRamSizeBytes;
}

#if PALM_HAS_SED1375
static bool sed1375RegOffset(uint32_t address, uint32_t &offset) {
  if (address >= PALM_SED1375_REG_BASE &&
      address < PALM_SED1375_REG_BASE + PALM_SED1375_REG_SIZE) {
    offset = address - PALM_SED1375_REG_BASE;
    return true;
  }
  return false;
}

static bool sed1375VramOffset(uint32_t address, uint32_t &offset) {
  if (address >= PALM_SED1375_BASE &&
      address < PALM_SED1375_BASE + PALM_SED1375_VRAM_SIZE) {
    offset = address - PALM_SED1375_BASE;
    return true;
  }
  return false;
}

static uint16_t argbTo565(uint32_t argb) {
  uint8_t r = static_cast<uint8_t>((argb >> 16) & 0xff);
  uint8_t g = static_cast<uint8_t>((argb >> 8) & 0xff);
  uint8_t b = static_cast<uint8_t>(argb & 0xff);
  return static_cast<uint16_t>(((r & 0xf8) << 8) | ((g & 0xfc) << 3) | (b >> 3));
}

static void updateSed1375Clut565(uint8_t index) {
  sed1375Clut565[index] = argbTo565(sed1375Clut[index]);
}

static bool ensureSed1375Allocated() {
  if (sed1375Vram != nullptr) return true;
#if defined(ESP32) && defined(MALLOC_CAP_SPIRAM)
  sed1375Vram = static_cast<uint8_t *>(
      heap_caps_malloc(PALM_SED1375_VRAM_SIZE, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT));
  if (sed1375Vram == nullptr) {
    sed1375Vram = static_cast<uint8_t *>(
        heap_caps_malloc(PALM_SED1375_VRAM_SIZE, MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT));
  }
#else
  sed1375Vram = static_cast<uint8_t *>(malloc(PALM_SED1375_VRAM_SIZE));
#endif
  return sed1375Vram != nullptr;
}

static bool initSed1375() {
  if (!ensureSed1375Allocated()) return false;
  memset(sed1375Regs, 0, sizeof(sed1375Regs));
  memset(sed1375Vram, 0, PALM_SED1375_VRAM_SIZE);
  sed1375Regs[0x00] = 0x24;
  sed1375Regs[0x04] = 19;
  sed1375Regs[0x05] = 159;
  sed1375Regs[0x12] = 20;
  sed1375LutEntry = 0;
  sed1375LutColor = 0;
  sed1375PaletteGeneration = 1;
  sed1375VramGeneration = 1;
  for (uint32_t i = 0; i < 256; ++i) {
    sed1375Clut[i] = 0xff000000UL | (i << 16) | (i << 8) | i;
    updateSed1375Clut565(static_cast<uint8_t>(i));
  }
  return true;
}

static uint8_t sed1375ReadReg(uint32_t offset) {
  if (offset == 0x0a) return sed1375Regs[offset] | 0x80;
  if (offset == 0x17) {
    uint32_t entry = sed1375Clut[sed1375LutEntry];
    uint8_t value = 0;
    if (sed1375LutColor == 0) value = static_cast<uint8_t>((entry >> 16) & 0xf0);
    else if (sed1375LutColor == 1) value = static_cast<uint8_t>((entry >> 8) & 0xf0);
    else value = static_cast<uint8_t>(entry & 0xf0);
    sed1375LutColor = (sed1375LutColor + 1) % 3;
    if (sed1375LutColor == 0) ++sed1375LutEntry;
    return value;
  }
  return offset < PALM_SED1375_REG_SIZE ? sed1375Regs[offset] : 0xff;
}

static void sed1375WriteReg(uint32_t offset, uint8_t value) {
  if (offset == 0x00 || offset >= PALM_SED1375_REG_SIZE) return;
  sed1375Regs[offset] = value;
  palmHwNotifySed1375RegWrite(static_cast<uint8_t>(offset), value);

  if (offset == 0x15) {
    sed1375LutEntry = value;
    sed1375LutColor = 0;
    return;
  }

  if (offset == 0x17) {
    uint32_t expanded = static_cast<uint32_t>(((value & 0xf0) >> 4) * 0x11);
    uint32_t &entry = sed1375Clut[sed1375LutEntry];
    if (sed1375LutColor == 0) {
      entry = (entry & 0xff00ffffUL) | (expanded << 16);
    } else if (sed1375LutColor == 1) {
      entry = (entry & 0xffff00ffUL) | (expanded << 8);
    } else {
      entry = (entry & 0xffffff00UL) | expanded;
    }
    updateSed1375Clut565(sed1375LutEntry);
    ++sed1375PaletteGeneration;
    sed1375LutColor = (sed1375LutColor + 1) % 3;
    if (sed1375LutColor == 0) ++sed1375LutEntry;
  }

}
#endif

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
  resetPageCache();

#if PALM_RAM_STATIC_BACKING
  memset(palmRamStatic, 0, sizeof(palmRamStatic));
  palmRamSizeBytes = sizeof(palmRamStatic);
#else
  memset(palmRamSegments, 0, sizeof(palmRamSegments));
#if PALM_RAM_INTERNAL_LOW_SIZE > 0
  for (size_t lowRequest = PALM_RAM_INTERNAL_LOW_SIZE;
       lowRequest >= 16384 &&
       palmRamSizeBytes < PALM_RAM_ALLOC_TARGET_SIZE &&
       palmRamSegmentCount < RAM_MAX_SEGMENTS;
       lowRequest -= (16UL * 1024UL)) {
    size_t remaining = PALM_RAM_ALLOC_TARGET_SIZE - palmRamSizeBytes;
    size_t request = lowRequest < remaining ? lowRequest : remaining;
    uint8_t *segment = static_cast<uint8_t *>(palmAllocInternal(request));
    if (segment == nullptr) continue;
    memset(segment, 0, request);
    palmRamSegments[palmRamSegmentCount].data = segment;
    palmRamSegments[palmRamSegmentCount].base = palmRamSizeBytes;
    palmRamSegments[palmRamSegmentCount].size = request;
    ++palmRamSegmentCount;
    palmRamSizeBytes += request;
    break;
  }
#endif
  static const size_t blockSizes[] = {
#if PALM_PREFER_PSRAM
      1048576, 524288, 262144, 131072,
#endif
      65536, 32768, 16384, 8192, 4096, 2048, 1024, 512, 256, 128, 64};
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
  resetPageCache();
#if PALM_HAS_SED1375
  if (!initSed1375()) {
    freeRamPages();
    return false;
  }
#endif
  palmHwInit();
  return true;
}

size_t palmRomSize() {
  return static_cast<size_t>(palm_rom_end - palm_rom_start);
}

size_t palmRamSize() { return palmRamSizeBytes; }
size_t palmRamLastAllocAttemptSize() { return palmRamLastAttemptBytes; }
size_t palmRamLastAllocAttemptSegments() { return palmRamLastAttemptSegments; }
size_t palmRamSegmentSize(size_t index) {
#if PALM_RAM_STATIC_BACKING
  return index == 0 ? palmRamSizeBytes : 0;
#else
  return index < palmRamSegmentCount ? palmRamSegments[index].size : 0;
#endif
}

bool palmRamWriteBytes(uint32_t offset, const uint8_t *data, uint32_t count) {
  if (count == 0) return true;
  if (data == nullptr || palmRamSizeBytes == 0 || offset > palmRamSizeBytes - count) return false;

  uint32_t remaining = count;
  uint32_t cursor = offset;
  const uint8_t *src = data;
  while (remaining > 0) {
    uint32_t chunk = remaining > 4096 ? 4096 : remaining;
    uint8_t *dst = ramPointerFastSpan(cursor, chunk);
    while (dst == nullptr && chunk > 1) {
      chunk >>= 1;
      dst = ramPointerFastSpan(cursor, chunk);
    }
    if (dst == nullptr) return false;
    memcpy(dst, src, chunk);
    cursor += chunk;
    src += chunk;
    remaining -= chunk;
  }
  return true;
}

bool palmRamReadBytes(uint32_t offset, uint8_t *data, uint32_t count) {
  if (count == 0) return true;
  if (data == nullptr || palmRamSizeBytes == 0 || offset > palmRamSizeBytes - count) return false;

  uint32_t remaining = count;
  uint32_t cursor = offset;
  uint8_t *dst = data;
  while (remaining > 0) {
    uint32_t chunk = remaining > 4096 ? 4096 : remaining;
    const uint8_t *src = ramPointerFastSpan(cursor, chunk);
    while (src == nullptr && chunk > 1) {
      chunk >>= 1;
      src = ramPointerFastSpan(cursor, chunk);
    }
    if (src == nullptr) return false;
    memcpy(dst, src, chunk);
    cursor += chunk;
    dst += chunk;
    remaining -= chunk;
  }
  return true;
}

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

uint8_t PALM_MEM_FAST palmRead8(uint32_t address) {
  uint32_t offset;
#if !PALM_MEMORY_COMPATIBLE_ACCESS
  const uint8_t *direct = cachedReadPointer(address, 1);
  if (direct != nullptr) return direct[0];
  const uint8_t *rom = romPointerForAddress(address, 1);
  if (rom != nullptr) return rom[0];
  uint8_t *ram = ramPointerForAddress(address, 1);
  if (ram != nullptr) return ram[0];
#else
  if (romOffset(address, offset)) return palm_rom_start[offset];
  if (palmHwInRegisterSpace(address)) return palmHwRead8(address);
  if (ramOffset(address, offset)) return *ramPointer(offset);
#endif
  if (palmHwInRegisterSpace(address)) return palmHwRead8(address);
#if PALM_HAS_SED1375
  if (sed1375RegOffset(address, offset)) return sed1375ReadReg(offset);
  if (sed1375VramOffset(address, offset) && sed1375Vram != nullptr) {
    return sed1375Vram[offset];
  }
#endif
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

uint16_t PALM_MEM_FAST palmRead16(uint32_t address) {
#if PALM_MEMORY_COMPATIBLE_ACCESS
  return (static_cast<uint16_t>(palmRead8(address)) << 8) |
         static_cast<uint16_t>(palmRead8(address + 1));
#else
  uint32_t offset;
  uint32_t nextOffset;
  const uint8_t *direct = cachedReadPointer(address, 2);
  if (direct != nullptr) return readBe16(direct);
  const uint8_t *rom = romPointerForAddress(address, 2);
  if (rom != nullptr) {
    return readBe16(rom);
  }
  uint8_t *ram = ramPointerForAddress(address, 2);
  if (ram != nullptr) {
    return readBe16(ram);
  }
  if (palmHwInRegisterSpace(address)) {
    return (static_cast<uint16_t>(palmHwRead8(address)) << 8) |
           static_cast<uint16_t>(palmHwRead8(address + 1));
  }
  if (ramOffset(address, offset) && ramOffset(address + 1, nextOffset)) {
    uint8_t *ptr = (nextOffset == offset + 1) ? ramPointerSpan(offset, 2) : nullptr;
    if (ptr != nullptr) {
      return readBe16(ptr);
    }
    return (static_cast<uint16_t>(*ramPointer(offset)) << 8) |
           static_cast<uint16_t>(*ramPointer(nextOffset));
  }
#if PALM_MIRROR_LOGICAL_RAM
  if (inLogicalRam(address, offset) && inLogicalRam(address + 1, nextOffset)) {
#if PALM_TRACE_UNMAPPED
    ++memoryDebug.mirrorReadCount;
    memoryDebug.lastMirrorRead = address;
#endif
    offset = mirroredRamOffset(offset);
    nextOffset = mirroredRamOffset(nextOffset);
    uint8_t *ptr = (nextOffset == offset + 1) ? ramPointerSpan(offset, 2) : nullptr;
    if (ptr != nullptr) {
      return readBe16(ptr);
    }
    return (static_cast<uint16_t>(*ramPointer(offset)) << 8) |
           static_cast<uint16_t>(*ramPointer(nextOffset));
  }
#endif
  return (static_cast<uint16_t>(palmRead8(address)) << 8) |
         static_cast<uint16_t>(palmRead8(address + 1));
#endif
}

uint32_t PALM_MEM_FAST palmRead32(uint32_t address) {
#if PALM_MEMORY_COMPATIBLE_ACCESS
  return (static_cast<uint32_t>(palmRead16(address)) << 16) |
         static_cast<uint32_t>(palmRead16(address + 2));
#else
  uint32_t offset;
  uint32_t lastOffset;
  const uint8_t *direct = cachedReadPointer(address, 4);
  if (direct != nullptr) return readBe32(direct);
  const uint8_t *rom = romPointerForAddress(address, 4);
  if (rom != nullptr) {
    return readBe32(rom);
  }
  uint8_t *ram = ramPointerForAddress(address, 4);
  if (ram != nullptr) {
    return readBe32(ram);
  }
#if PALM_MIRROR_LOGICAL_RAM
  if (inLogicalRam(address, offset) && inLogicalRam(address + 3, lastOffset)) {
    offset = mirroredRamOffset(offset);
    lastOffset = mirroredRamOffset(lastOffset);
    if (lastOffset == offset + 3) {
      uint8_t *ptr = ramPointerSpan(offset, 4);
      if (ptr != nullptr) {
        return readBe32(ptr);
      }
    }
  }
#endif
  return (static_cast<uint32_t>(palmRead16(address)) << 16) |
         static_cast<uint32_t>(palmRead16(address + 2));
#endif
}

void PALM_MEM_FAST palmWrite8(uint32_t address, uint8_t value) {
  uint32_t offset;
#if !PALM_MEMORY_COMPATIBLE_ACCESS
  uint8_t *direct = cachedWritePointer(address, 1);
  if (direct != nullptr) {
    direct[0] = value;
    return;
  }
#endif
  if (palmHwInRegisterSpace(address)) {
    palmHwWrite8(address, value);
    return;
  }

#if PALM_HAS_SED1375
  if (sed1375RegOffset(address, offset)) {
    sed1375WriteReg(offset, value);
    return;
  }

  if (sed1375VramOffset(address, offset) && sed1375Vram != nullptr) {
    sed1375Vram[offset] = value;
    ++sed1375VramGeneration;
    return;
  }
#endif

  if (romOffset(address, offset)) {
    return;
  }

  uint8_t *ram = ramPointerForAddress(address, 1);
  if (ram != nullptr) {
    ram[0] = value;
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

void PALM_MEM_FAST palmWrite16(uint32_t address, uint16_t value) {
#if PALM_MEMORY_COMPATIBLE_ACCESS
  palmWrite8(address, value >> 8);
  palmWrite8(address + 1, value & 0xff);
#else
  uint32_t offset;
  uint32_t nextOffset;
  uint8_t *direct = cachedWritePointer(address, 2);
  if (direct != nullptr) {
    writeBe16(direct, value);
    return;
  }
  if (palmHwInRegisterSpace(address)) {
    palmHwWrite8(address, value >> 8);
    palmHwWrite8(address + 1, value & 0xff);
    return;
  }
  if (romOffset(address, offset)) {
    return;
  }
  uint8_t *ram = ramPointerForAddress(address, 2);
  if (ram != nullptr) {
    writeBe16(ram, value);
    return;
  }
  if (ramOffset(address, offset) && ramOffset(address + 1, nextOffset)) {
    uint8_t *ptr = (nextOffset == offset + 1) ? ramPointerSpan(offset, 2) : nullptr;
    if (ptr != nullptr) {
      writeBe16(ptr, value);
    } else {
      *ramPointer(offset) = value >> 8;
      *ramPointer(nextOffset) = value & 0xff;
    }
    return;
  }
#if PALM_MIRROR_LOGICAL_RAM
  if (inLogicalRam(address, offset) && inLogicalRam(address + 1, nextOffset)) {
#if PALM_TRACE_UNMAPPED
    ++memoryDebug.mirrorWriteCount;
    memoryDebug.lastMirrorWrite = address;
#endif
    offset = mirroredRamOffset(offset);
    nextOffset = mirroredRamOffset(nextOffset);
    uint8_t *ptr = (nextOffset == offset + 1) ? ramPointerSpan(offset, 2) : nullptr;
    if (ptr != nullptr) {
      writeBe16(ptr, value);
    } else {
      *ramPointer(offset) = value >> 8;
      *ramPointer(nextOffset) = value & 0xff;
    }
    return;
  }
#endif
  palmWrite8(address, value >> 8);
  palmWrite8(address + 1, value & 0xff);
#endif
}

void PALM_MEM_FAST palmWrite32(uint32_t address, uint32_t value) {
#if PALM_MEMORY_COMPATIBLE_ACCESS
  palmWrite16(address, value >> 16);
  palmWrite16(address + 2, value & 0xffff);
#else
  uint32_t offset;
  uint32_t lastOffset;
  uint8_t *direct = cachedWritePointer(address, 4);
  if (direct != nullptr) {
    writeBe32(direct, value);
    return;
  }
  if (palmHwInRegisterSpace(address)) {
    palmHwWrite8(address, value >> 24);
    palmHwWrite8(address + 1, (value >> 16) & 0xff);
    palmHwWrite8(address + 2, (value >> 8) & 0xff);
    palmHwWrite8(address + 3, value & 0xff);
    return;
  }
  if (romOffset(address, offset)) {
    return;
  }
  uint8_t *ram = ramPointerForAddress(address, 4);
  if (ram != nullptr) {
    writeBe32(ram, value);
    return;
  }
  if (ramOffset(address, offset) && ramOffset(address + 3, lastOffset) && lastOffset == offset + 3) {
    uint8_t *ptr = ramPointerSpan(offset, 4);
    if (ptr != nullptr) {
      writeBe32(ptr, value);
      return;
    }
  }
#if PALM_MIRROR_LOGICAL_RAM
  if (inLogicalRam(address, offset) && inLogicalRam(address + 3, lastOffset)) {
    offset = mirroredRamOffset(offset);
    lastOffset = mirroredRamOffset(lastOffset);
    if (lastOffset == offset + 3) {
      uint8_t *ptr = ramPointerSpan(offset, 4);
      if (ptr != nullptr) {
        writeBe32(ptr, value);
        return;
      }
    }
  }
#endif
  palmWrite16(address, value >> 16);
  palmWrite16(address + 2, value & 0xffff);
#endif
}

extern "C" unsigned int PALM_MEM_FAST m68k_read_memory_8(unsigned int address) {
  return palmRead8(address);
}

extern "C" unsigned int PALM_MEM_FAST m68k_read_memory_16(unsigned int address) {
  return palmRead16(address);
}

extern "C" unsigned int PALM_MEM_FAST m68k_read_memory_32(unsigned int address) {
  return palmRead32(address);
}

extern "C" unsigned int PALM_MEM_FAST palm_read_instr_16(unsigned int address) {
  uint32_t offset;
  uint32_t nextOffset;
#if !PALM_MEMORY_COMPATIBLE_ACCESS
  const uint8_t *direct = cachedReadPointer(address, 2);
  if (direct != nullptr) return readBe16(direct);
#endif
  const uint8_t *rom = romPointerForAddress(address, 2);
  if (rom != nullptr) {
    return readBe16(rom);
  }
  if (palmHwInRegisterSpace(address)) return palmHwRead8(address) << 8 | palmHwRead8(address + 1);
#if PALM_HAS_SED1375
  if (sed1375RegOffset(address, offset)) return sed1375ReadReg(offset) << 8 | sed1375ReadReg(offset + 1);
#endif
  uint8_t *ram = ramPointerForAddress(address, 2);
  if (ram != nullptr) {
    return readBe16(ram);
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

extern "C" unsigned int PALM_MEM_FAST m68k_read_immediate_16(unsigned int address) {
  return palm_read_instr_16(address);
}

extern "C" unsigned int PALM_MEM_FAST m68k_read_immediate_32(unsigned int address) {
  return (palm_read_instr_16(address) << 16) | palm_read_instr_16(address + 2);
}

extern "C" unsigned int PALM_MEM_FAST m68k_read_pcrelative_8(unsigned int address) {
  return palmRead8(address);
}

extern "C" unsigned int PALM_MEM_FAST m68k_read_pcrelative_16(unsigned int address) {
  return palmRead16(address);
}

extern "C" unsigned int PALM_MEM_FAST m68k_read_pcrelative_32(unsigned int address) {
  return palmRead32(address);
}

extern "C" void PALM_MEM_FAST m68k_write_memory_8(unsigned int address, unsigned int value) {
  palmWrite8(address, value & 0xff);
}

extern "C" void PALM_MEM_FAST m68k_write_memory_16(unsigned int address, unsigned int value) {
  palmWrite16(address, value & 0xffff);
}

extern "C" void PALM_MEM_FAST m68k_write_memory_32(unsigned int address, unsigned int value) {
  palmWrite32(address, value);
}

PalmMemoryDebug palmMemoryGetDebug() { return memoryDebug; }

bool palmSed1375GetLcdState(PalmLcdState &lcd) {
#if PALM_HAS_SED1375
  uint32_t startOffset = ((static_cast<uint32_t>(sed1375Regs[0x11] & 0x03) << 17) |
                          (static_cast<uint32_t>(sed1375Regs[0x0d]) << 9) |
                          (static_cast<uint32_t>(sed1375Regs[0x0c]) << 1));
  if (startOffset >= PALM_SED1375_VRAM_SIZE) startOffset = 0;

  uint8_t bpp = static_cast<uint8_t>(1U << ((sed1375Regs[0x02] & 0xc0) >> 6));
  uint16_t width = static_cast<uint16_t>((sed1375Regs[0x04] + 1U) * 8U);
  uint16_t height = static_cast<uint16_t>((static_cast<uint16_t>(sed1375Regs[0x06]) << 8) |
                                          sed1375Regs[0x05]);
  lcd.startAddr = PALM_SED1375_BASE + startOffset;
  lcd.width = width == 0 ? PALM_LCD_W : width;
  lcd.height = static_cast<uint16_t>(height + 1U);
  lcd.bytesPerLine = static_cast<uint16_t>((static_cast<uint32_t>(lcd.width) * bpp + 7U) / 8U);
  lcd.bpp = bpp;
  lcd.margin = 0;
  lcd.panelControl = sed1375Regs[0x02];
  lcd.valid = sed1375Vram != nullptr && lcd.width > 0 && lcd.width <= 320 &&
              lcd.height > 0 && lcd.height <= 320 &&
              lcd.bytesPerLine > 0 && (bpp == 1 || bpp == 2 || bpp == 4 || bpp == 8);
  return lcd.valid;
#else
  (void)lcd;
  return false;
#endif
}

uint16_t palmSed1375PaletteColor565(uint8_t index) {
#if PALM_HAS_SED1375
  return sed1375Clut565[index];
#else
  (void)index;
  return 0;
#endif
}

uint32_t palmSed1375PaletteGeneration() {
#if PALM_HAS_SED1375
  return sed1375PaletteGeneration;
#else
  return 0;
#endif
}

uint32_t palmSed1375VramGeneration() {
#if PALM_HAS_SED1375
  return sed1375VramGeneration;
#else
  return 0;
#endif
}

const uint8_t *palmSed1375VramPointer(uint32_t address, uint32_t count) {
#if PALM_HAS_SED1375
  uint32_t offset = 0;
  if (count == 0 || sed1375Vram == nullptr || !sed1375VramOffset(address, offset) ||
      offset >= PALM_SED1375_VRAM_SIZE || count > PALM_SED1375_VRAM_SIZE - offset) {
    return nullptr;
  }
  return sed1375Vram + offset;
#else
  (void)address;
  (void)count;
  return nullptr;
#endif
}

size_t palmSed1375StateSize() {
#if PALM_HAS_SED1375
  return SED1375_STATE_SIZE;
#else
  return 0;
#endif
}

#if PALM_HAS_SED1375
static uint8_t sed1375StateByte(uint32_t offset) {
  if (offset < PALM_SED1375_REG_SIZE) return sed1375Regs[offset];
  if (offset == SED1375_STATE_LUT_OFFSET) return sed1375LutEntry;
  if (offset == SED1375_STATE_LUT_OFFSET + 1) return sed1375LutColor;
  if (offset < SED1375_STATE_CLUT_OFFSET) return 0;
  if (offset < SED1375_STATE_VRAM_OFFSET) {
    uint32_t clutOffset = offset - SED1375_STATE_CLUT_OFFSET;
    uint32_t entry = sed1375Clut[clutOffset / sizeof(uint32_t)];
    uint8_t shift = static_cast<uint8_t>((3U - (clutOffset & 3U)) * 8U);
    return static_cast<uint8_t>((entry >> shift) & 0xffU);
  }
  if (offset < SED1375_STATE_SIZE && sed1375Vram != nullptr) {
    return sed1375Vram[offset - SED1375_STATE_VRAM_OFFSET];
  }
  return 0;
}

static void sed1375SetStateByte(uint32_t offset, uint8_t value) {
  if (offset < PALM_SED1375_REG_SIZE) {
    sed1375Regs[offset] = value;
    return;
  }
  if (offset == SED1375_STATE_LUT_OFFSET) {
    sed1375LutEntry = value;
    return;
  }
  if (offset == SED1375_STATE_LUT_OFFSET + 1) {
    sed1375LutColor = value % 3U;
    return;
  }
  if (offset < SED1375_STATE_CLUT_OFFSET) return;
  if (offset < SED1375_STATE_VRAM_OFFSET) {
    uint32_t clutOffset = offset - SED1375_STATE_CLUT_OFFSET;
    uint32_t &entry = sed1375Clut[clutOffset / sizeof(uint32_t)];
    uint8_t shift = static_cast<uint8_t>((3U - (clutOffset & 3U)) * 8U);
    entry = (entry & ~(0xffUL << shift)) | (static_cast<uint32_t>(value) << shift);
    updateSed1375Clut565(static_cast<uint8_t>(clutOffset / sizeof(uint32_t)));
    ++sed1375PaletteGeneration;
    return;
  }
  if (offset < SED1375_STATE_SIZE && sed1375Vram != nullptr) {
    sed1375Vram[offset - SED1375_STATE_VRAM_OFFSET] = value;
    ++sed1375VramGeneration;
  }
}
#endif

bool palmSed1375ReadStateBytes(uint32_t offset, uint8_t *dest, size_t count) {
#if PALM_HAS_SED1375
  if (dest == nullptr || offset > SED1375_STATE_SIZE ||
      count > SED1375_STATE_SIZE - offset) {
    return false;
  }
  if (!ensureSed1375Allocated()) return false;
  for (size_t i = 0; i < count; ++i) {
    dest[i] = sed1375StateByte(offset + static_cast<uint32_t>(i));
  }
  return true;
#else
  (void)offset;
  (void)dest;
  return count == 0;
#endif
}

bool palmSed1375WriteStateBytes(uint32_t offset, const uint8_t *src, size_t count) {
#if PALM_HAS_SED1375
  if (src == nullptr || offset > SED1375_STATE_SIZE ||
      count > SED1375_STATE_SIZE - offset) {
    return false;
  }
  if (!ensureSed1375Allocated()) return false;
  for (size_t i = 0; i < count; ++i) {
    sed1375SetStateByte(offset + static_cast<uint32_t>(i), src[i]);
  }
  return true;
#else
  (void)offset;
  (void)src;
  return count == 0;
#endif
}
