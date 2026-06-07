# ESP32-PALM Bring-Up Notes

This sketch is now a usable Palm m100 bring-up on the ESP32-4827S043C board.
It is still experimental, but Palm OS boots, touch works, LCD output is usable,
state snapshots work, low-power sleep is handled, and the emulated Palm UART is
bridged to the board's host serial port.

What is wired now:

- ESP32-4827S043C RGB LCD and GT911 touch pins copied from
  `cyd_ref/005638_005638_Jingcai_ESP32_4827S043C_simple_GT911_touch.ino`.
- Palm m100 ROM embedded from `Palm-m100-3.51-en.rom` with `.incbin`.
- Adaptive emulated Palm RAM at `0x00000000`. The ESP32-4827S043C profile tries
  to allocate the full hardware profile RAM in PSRAM.
- ROM mapped at `0x10c08000`; the supplied m100 Big ROM image maps reset
  vectors into the embedded flash image.
- The same file is also aliased at `0x10c00000` below the Big ROM base, because
  early boot checks the `FEEDBEEF` Palm card header token at `0x10c00008`.
- Musashi is patched for this Palm build to keep 32-bit addresses for the
  68000 core. Palm IIIx ROM vectors and DragonBall register addresses live above
  16 MB (`0x10c08000` and `0xfffff000`), so 24-bit masking causes false RAM and
  register alias collisions. Musashi's reset SP/PC are seeded from the ROM
  vector so execution starts in ROM.
- 160x160 Palm LCD renderer shown on the selected ESP32 LCD, with bilinear
  panel scaling and only the Palm LCD area updated dynamically.
- Static m100 silkscreen artwork is generated from `Silkscreen.png` into
  `silkscreen_asset.h` as a packed 4bpp PROGMEM asset, then drawn once into the
  static panel frame.
- A top-of-16MB RAM alias maps the upper emulated RAM window ending at
  `0x01000000` back onto the allocated Palm RAM. Early Palm OS code writes into
  this `0x00ffxxxx` area before the LCD controller is initialized.
- Musashi memory callback functions.
- Musashi CPU integration enabled in `palm_config.h`.
- Musashi core updated to the ESP32-friendly fork used by `likeablob/cydintosh`,
  configured for a 68000-only static decode table so the opcode table lives in
  flash instead of consuming about 256 KB of ESP32 DRAM.
- Musashi separate immediate/PC-relative read callbacks are enabled. Immediate
  reads use the Palm instruction-read helper so opcode extension words avoid
  part of the generic data-read path.
- `M68K_STRICT_68000_FASTPATH` trims the ESP32 build for the DragonBall's
  68EC000-class CPU. It also aliases static opcode-table entries that are not
  valid for plain 68000 back to Musashi's illegal-instruction handler, dropping
  about 14 KB of unused handler code in the current Arduino build.
- The direct RAM/ROM page cache uses a small XOR page-index hash. This does not
  add RAM, but reduces simple low-bit collisions between distant RAM, ROM, and
  alias pages.
- First DragonBall EZ LCD register bridge at `0xfffff000`, based on Cloudpilot's
  Palm IIIx/EZ register model. The ESP32 renderer now reads the LCD start
  address, width, height, page width, panel control, and panning registers.
- Direct 1-bit LCD rendering from emulated Palm memory. There is no extra local
  160x160 framebuffer copy; this saves 3200 bytes of ESP32 RAM and avoids touch
  input scribbling static into the Palm display area.
- The fixed-interval LCD renderer no longer keeps RAM-write dirty tracking or
  unchanged-frame hash checks; it simply redraws the Palm LCD area on its fixed
  schedule.
- `PALM_PANEL_INDEXED_FRAMEBUFFER` stores the two full-screen RGB-panel frames
  as 8-bit palette indices. This halves the PSRAM panel-frame footprint, while
  keeping the RGB565 palette/cache in internal DRAM and expanding each scanline
  back to RGB565 in the ESP RGB-panel bounce callback.
- Palm RAM is allocated before render-buffer and LCD initialization so the hot
  low RAM segment gets first claim on contiguous internal DRAM. The current
  target is controlled by `PALM_RAM_INTERNAL_LOW_SIZE`; allocation steps down in
  16 KB chunks if the requested internal block is not available, and keeps the
  rest in PSRAM.
- If the ESP32 cannot allocate all logical Palm RAM, the missing logical range is
  mirrored into the real paged RAM backing store. This follows Cloudpilot's SRAM
  bank behavior more closely than sparse zeroes: reads and writes above the real
  allocation still round-trip through backing RAM. For speed, the ESP32 build
  currently compiles Musashi bus-error support out (`M68K_BUS_ERR_ENABLE =
  OPT_OFF`) and leaves `PALM_BUS_ERROR_ON_RAM_LIMIT` disabled. Re-enable both
  if an app compatibility issue appears to depend on RAM-limit or
  unmapped-memory bus errors.
- The serial status also prints DragonBall register read/write counters as
  `hw=reads/writes`, plus the last register offsets touched.
- Instruction fetches are stricter than data reads: sparse RAM can satisfy data
  probes, but executing from sparse or unmapped memory raises an instruction bus
  error. The serial status prints this as `ibus=count@address`.
- GT911 touch is mapped through the raw ADS/digitizer path used by Palm OS.
- Virtual hardware buttons are placed in the unused side panel areas. The left
  strip exposes the four app buttons plus up/down; the right strip exposes
  power, hold-to-save snapshot, and reset.
- m100 backlight behavior follows the real hardware split: Port F bit `0x20`
  switches the LCD render palette into inverted backlit mode, while the ROM's
  `$A36` contrast values (`0x0180..0x01aa` observed) are mapped to 10-50% ESP32
  backlight PWM.
- Snapshot save/restore uses `/palm_m100_state.bin` on the SD card. Restored
  sleep states are woken automatically.
- Palm sleep turns off display/backlight, lowers ESP32 CPU frequency, and uses
  light sleep between lower-rate touch polls. Touch anywhere wakes the device.
- The emulated DragonBall UART is bridged to the board's CH340 host serial port
  at 115200 baud after boot logging is released.
- RTC stopwatch interrupts are not implemented yet; apps that depend on
  stopwatch ticks may pause while Palm OS is asleep.

`m68kops.c` and `m68kops.h` have been generated with Ubuntu 24.04 under WSL.
If they need to be regenerated:

```sh
cd Musashi-master
make clean
make m68kops.c m68kops.h
```

Current CPU switch in `palm_config.h`:

```cpp
#define PALM_ENABLE_MUSASHI 1
```

The embedded ROM does not fit in the default 1.2 MB ESP32 app partition. The
command-line build for the ESP32-4827S043C target is:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\CompileEsp32Palm.ps1
```

That helper builds from a temporary folder named `ESP32-PALM`, matching
Arduino's sketch-folder rule, and expects `Palm-m100-3.51-en.rom` in the
project root. For a compile-only check of the source without a real ROM image:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\CompileEsp32Palm.ps1 -CodeCheckOnly
```

The raw Arduino CLI command is:

```sh
arduino-cli compile --fqbn "esp32:esp32:esp32s3:FlashSize=16M,PartitionScheme=custom,PSRAM=opi,CPUFreq=240,USBMode=hwcdc,UploadMode=default,CDCOnBoot=default" .
```

The helper copies `Tools/esp32_palm_16mb_partitions.csv` into the temporary
sketch as `partitions.csv`. That custom 16 MB layout uses two 4 MB app slots
and a 7.9 MB FATFS partition, giving the `-O2` ESP32-S3 build more room than
Arduino's standard `app3M_fat9M_16MB` layout.

Current next work is polish and compatibility: improve CPU speed, make
HotSync/serial more robust, emulate RTC stopwatch behavior, and add Beam/IR if
needed.

Desktop harness:

- `PalmDesktopHarness` is a VB.NET WinForms memory-map probe. It allocates the
  full 4 MB logical Palm IIIx RAM on desktop so RAM shortage does not hide the
  next hardware issue.
- `NativeMusashi` builds `PalmMusashi.dll`, a small Musashi wrapper used by the
  desktop harness. The harness has `Init CPU`, `Run 100`, and `Run 10k` buttons
  that execute ROM code and show PC/SP, RAM/ROM/register access counts, LCD
  writes, and bus errors.
- Run it with:

```sh
dotnet run --project PalmDesktopHarness/PalmDesktopHarness.vbproj
```

Build the native DLL first if needed:

```bat
call "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat" -arch=x64
cmake -S NativeMusashi -B NativeMusashi\build-release -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release
cmake --build NativeMusashi\build-release
```
