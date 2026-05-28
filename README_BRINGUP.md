# ESP32-PALM Bring-Up Notes

This sketch is currently a hardware and memory scaffold for a minimal Palm OS
emulator on the ESP32 CYD.

What is wired now:

- CYD TFT/backlight/touch pins copied from `cyd_ref/ESP32_xAlarm.cpp`.
- Sketch-local `User_Setup.h` for the common CYD ILI9341 TFT_eSPI pinout.
- Palm IIIx ROM embedded from `Palm-IIIx-3.1.rom` with `.incbin`.
- Adaptive emulated Palm RAM at `0x00000000`. The sketch tries to back up to
  256 KB with real 1 KB ESP32 heap pages, then rounds that real allocation down
  to a stable bank size. Separately, the logical Palm RAM size is currently
  faked as 4 MB because Cloudpilot marks Palm IIIx as a 4096 KB device.
- ROM mapped at `0x10c08000`; the supplied file is the Palm IIIx Big ROM image,
  so reset PC `0x10c0822a` maps to file offset `0x022a`.
- The same file is also aliased at `0x10c00000` below the Big ROM base, because
  early boot checks the `FEEDBEEF` Palm card header token at `0x10c00008`.
- Musashi is patched for this Palm build to keep 32-bit addresses for the
  68000 core. Palm IIIx ROM vectors and DragonBall register addresses live above
  16 MB (`0x10c08000` and `0xfffff000`), so 24-bit masking causes false RAM and
  register alias collisions. Musashi's reset SP/PC are seeded from the ROM
  vector so execution starts in ROM.
- 160x160 1-bit Palm LCD renderer shown on the CYD LCD.
- A top-of-16MB RAM alias maps the upper emulated RAM window ending at
  `0x01000000` back onto the allocated Palm RAM. Early Palm OS code writes into
  this `0x00ffxxxx` area before the LCD controller is initialized.
- Musashi memory callback functions.
- Musashi CPU integration enabled in `palm_config.h`.
- Musashi core updated to the CYD-friendly fork used by `likeablob/cydintosh`,
  configured for a 68000-only static decode table so the opcode table lives in
  flash instead of consuming about 256 KB of ESP32 DRAM.
- First DragonBall EZ LCD register bridge at `0xfffff000`, based on Cloudpilot's
  Palm IIIx/EZ register model. The CYD renderer now reads the LCD start address,
  width, height, page width, panel control, and panning registers.
- Direct 1-bit LCD rendering from emulated Palm memory. There is no extra local
  160x160 framebuffer copy; this saves 3200 bytes of ESP32 RAM and avoids touch
  input scribbling static into the Palm display area.
- Palm RAM is allocated before TFT initialization to reduce heap fragmentation.
- If the ESP32 cannot allocate all logical Palm RAM, the missing logical range is
  mirrored into the real paged RAM backing store. This follows Cloudpilot's SRAM
  bank behavior more closely than sparse zeroes: reads and writes above the real
  allocation still round-trip through backing RAM. Reads or writes beyond the
  logical limit raise a Musashi bus error. The serial status prints mirror
  access counts plus the last bus-error address.
- The serial status also prints DragonBall register read/write counters as
  `hw=reads/writes`, plus the last register offsets touched.
- Instruction fetches are stricter than data reads: sparse RAM can satisfy data
  probes, but executing from sparse or unmapped memory raises an instruction bus
  error. The serial status prints this as `ibus=count@address`.

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

The embedded ROM does not fit in the default 1.2 MB ESP32 app partition. In
Arduino IDE, select:

```text
Tools > Partition Scheme > Huge APP (3MB No OTA/1MB SPIFFS)
```

The command-line build equivalent is:

```sh
arduino-cli compile --fqbn "esp32:esp32:esp32:PartitionScheme=huge_app" .
```

Expected first milestone:

1. Upload with `PALM_ENABLE_MUSASHI` set to `1`.
2. Confirm the screen shows ROM/RAM/SP/PC diagnostics.
3. Confirm serial output prints the Musashi PC, LCD state, LCD register writes,
   and last unmapped read/write once per second.
4. If the status stays `LCD=off`, the ROM has not configured the DragonBall LCD
   registers yet. Check `lcdWr`, `unmR`, and `unmW` in the same serial line.
5. If `LCD=on`, the screen should render directly from Palm RAM.
6. Start decoding the next unmapped DragonBall registers: timers, interrupts,
   GPIO, RTC, and pen input.

The hard work after this scaffold is the Palm IIIx hardware model: DragonBall
memory control, LCD controller registers, timers, interrupt controller, RTC,
and enough storage/card behavior for Palm OS 3.1 to finish booting.

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
