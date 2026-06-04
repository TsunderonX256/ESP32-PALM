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

static inline const uint8_t *PALM_MEM_FAST fillReadPage(uint32_t tag) {
  uint32_t pageBase = tag << PALM_MEM_PAGE_SHIFT;
  const uint8_t *base = romPointerForAddress(pageBase, PALM_MEM_PAGE_SIZE);
  if (base == nullptr) {
    base = ramPointerForAddress(pageBase, PALM_MEM_PAGE_SIZE);
  }

  DirectReadPage &entry = readPageCache[tag & (PALM_MEM_PAGE_CACHE_ENTRIES - 1)];
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

  DirectWritePage &entry = writePageCache[tag & (PALM_MEM_PAGE_CACHE_ENTRIES - 1)];
  entry.tag = tag;
  entry.base = base;
  return base;
}

static inline const uint8_t *PALM_MEM_FAST cachedReadPointer(uint32_t address, uint32_t size) {
  if (!pageContainsSpan(address, size)) return nullptr;

  uint32_t tag = address >> PALM_MEM_PAGE_SHIFT;
  DirectReadPage &entry = readPageCache[tag & (PALM_MEM_PAGE_CACHE_ENTRIES - 1)];
  const uint8_t *base = entry.tag == tag ? entry.base : fillReadPage(tag);
  return base != nullptr ? base + (address & PALM_MEM_PAGE_MASK) : nullptr;
}

static inline uint8_t *PALM_MEM_FAST cachedWritePointer(uint32_t address, uint32_t size) {
  if (!pageContainsSpan(address, size)) return nullptr;

  uint32_t tag = address >> PALM_MEM_PAGE_SHIFT;
  DirectWritePage &entry = writePageCache[tag & (PALM_MEM_PAGE_CACHE_ENTRIES - 1)];
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
       lowRequest >>= 1) {
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
  palmHwInit();
  return true;
}

size_t palmRomSize() {
  return static_cast<size_t>(palm_rom_end - palm_rom_start);
}

size_t palmRamSize() { return palmRamSizeBytes; }
size_t palmRamLastAllocAttemptSize() { return palmRamLastAttemptBytes; }
size_t palmRamLastAllocAttemptSegments() { return palmRamLastAttemptSegments; }

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
    palmHwNotifyMemoryWrite(address);
    return;
  }
#endif
  if (palmHwInRegisterSpace(address)) {
    palmHwWrite8(address, value);
    return;
  }

  if (romOffset(address, offset)) {
    return;
  }

  uint8_t *ram = ramPointerForAddress(address, 1);
  if (ram != nullptr) {
    ram[0] = value;
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
    palmHwNotifyMemoryWrite(address);
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
    palmHwNotifyMemoryWrite(address);
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
    palmHwNotifyMemoryWrite(address);
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
    palmHwNotifyMemoryWrite(address);
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
    palmHwNotifyMemoryWrite(address);
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
    palmHwNotifyMemoryWrite(address);
    return;
  }
  if (ramOffset(address, offset) && ramOffset(address + 3, lastOffset) && lastOffset == offset + 3) {
    uint8_t *ptr = ramPointerSpan(offset, 4);
    if (ptr != nullptr) {
      writeBe32(ptr, value);
      palmHwNotifyMemoryWrite(address);
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
        palmHwNotifyMemoryWrite(address);
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
