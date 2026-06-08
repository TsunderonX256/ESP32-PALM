# ESP32-PALM

ESP32-PALM is an experimental Palm OS emulator project. It includes a Windows
desktop harness that runs Palm OS well enough to use apps, HotSync files,
persist RAM state, and emulate basic Palm buzzer audio, plus an ESP32-S3+PSRAM
hardware target that now runs the Palm m100 profile directly on a small RGB
touchscreen board.

The current embedded target is the ESP32-4827S043C board with 16 MB flash,
8 MB PSRAM, 480x272 RGB LCD, GT911 touch, and a CH340 USB serial bridge.

## Current Status

- ESP32 Palm m100 profile is usable on the ESP32-4827S043C: Palm OS boots,
  touch works, the LCD is stable enough for normal use, snapshots can be saved
  and restored, sleep/wake is handled, and a host serial UART bridge is wired.
- Desktop Palm m100 profile boots and works well in normal use.
- Palm IIIx profile is still supported by the native core and harness.
- Palm IIIc support is experimental. It uses the Palm IIIc/Austin hardware
  profile, 4 MB RAM, and the SED1375 color LCD path.
- ESP32 performance is still below real hardware in CPU-heavy apps. The current
  focus is improving Musashi and memory-map speed while keeping touch and LCD
  timing stable.
- Native emulator core is exposed through `NativeMusashi/palm_core.h` for future
  hosts such as SDL, Android, Linux, or ESP32+PSRAM.

## Features

- Musashi 68000 CPU core.
- DragonBall EZ-style memory map, registers, interrupts, timers, RTC, LCD,
  GPIO/button input, pen/touch input, UART transport, and PWM buzzer.
- Experimental SED1375 color LCD controller path for Palm IIIc.
- 160x160 1-bit LCD rendering with bulk framebuffer transfer.
- Palm m100-style 160x220 digitizer area, including silkscreen region.
- Sleep/wake handling with timer/RTC wake support.
- RAM state save/restore.
- ESP32-S3 m100 target with bilinear LCD scaling, static PNG-derived
  silkscreen art, virtual hardware buttons, power/save/reset controls,
  low-power sleep polling, and a raw host serial bridge for UART/HotSync work.
- Desktop HotSync host for installing `.prc` and `.pdb` files.
- Memo Pad text sync to per-memo `.txt` files.
- m100 Note Pad export to raw `.bin` backup plus 1-bit `.bmp` preview.
- Basic Palm buzzer/audio output on Windows.

## Repository Layout

```text
ESP32-PALM.ino             Arduino sketch for the ESP32-4827S043C target
Silkscreen.png             Source art for the ESP32 m100 silkscreen strip
silkscreen_asset.h         Generated packed 4bpp firmware asset from Silkscreen.png
NativeMusashi/             Native C Palm hardware/CPU bridge
PalmDesktopHarness/        VB.NET WinForms desktop emulator
PalmRamProbe/              Desktop RAM limit test harness
PalmRomExtractor/          VB.NET ROM app lister and PRC exporter
SmallBasicBasEditor/       VB.NET SmallBASIC .bas text-chunk editor
Musashi-master/            Musashi 68000 CPU core used by the emulator
HARDWARE_PROFILES.md       Profile switching notes
README_BRINGUP.md          Detailed bring-up history and low-level notes
```

## ROM Files

ROM images are not included in this repository.

Place your own legally-dumped ROM files beside the executable or in the project
root before running:

- `Palm-m100-3.51-en.rom` for the m100 profile
- `Palm-IIIx-3.1.rom` for the IIIx profile
- `Palm-IIIc-4.1-en.rom` for the IIIc experimental profile

The `.gitignore` intentionally excludes ROMs, Palm application packages, RAM
state files, HotSync data, and build outputs.

## Build: Windows Desktop Harness

Requirements:

- Windows
- .NET 8 SDK
- Visual Studio 2022 C++ build tools
- CMake

Build the native DLL:

```bat
call "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64
cmake -S NativeMusashi -B NativeMusashi\build-release -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release -DPALM_PROFILE=M100_EXPERIMENTAL
cmake --build NativeMusashi\build-release
```

Build the WinForms harness:

```bat
dotnet build PalmDesktopHarness\PalmDesktopHarness.vbproj -c Release
```

Run:

```bat
PalmDesktopHarness\bin\Release\net8.0-windows\PalmDesktopHarness.exe
```

The desktop harness profile is selected in `PalmDesktopHarness/PalmConfig.vb`.
Keep that setting in sync with the native
`PALM_PROFILE` CMake option.

## ROM Application Export

`PalmRomExtractor` lists application databases inside a Palm ROM dump and can
export them as installable `.prc` files. It handles normal PRC/PDB relative
offsets, file-absolute offsets, and common Palm ROM-base absolute offsets.

Build:

```bat
dotnet build PalmRomExtractor\PalmRomExtractor.vbproj -c Release
```

List ROM applications:

```bat
dotnet run --project PalmRomExtractor -- list Palm-m100-3.51-en.rom
```

Export all ROM applications:

```bat
dotnet run --project PalmRomExtractor -- export Palm-m100-3.51-en.rom ExtractedApps --all
```

Export one app by display name or creator code:

```bat
dotnet run --project PalmRomExtractor -- export Palm-m100-3.51-en.rom ExtractedApps --name "Memo Pad"
dotnet run --project PalmRomExtractor -- export Palm-m100-3.51-en.rom ExtractedApps --creator memo
```

If a dump preserves a nonstandard ROM address base, pass it explicitly:

```bat
dotnet run --project PalmRomExtractor -- list MyPalm.rom --rom-base 0x10C00000
```

## SmallBASIC .bas Editor

`SmallBasicBasEditor` is a small WinForms editor for Palm SmallBASIC `.bas`
files. It opens `TEXT` / `SmBa` Palm database files, lists readable BASIC source
sections for editing, and saves the sections back into a minimal SmallBASIC Palm
database wrapper.

```bat
dotnet run --project SmallBasicBasEditor\SmallBasicBasEditor.vbproj
smallbasic_bas_editor.cmd "path\to\program.bas"
```

## Switching Hardware Profiles

See [HARDWARE_PROFILES.md](HARDWARE_PROFILES.md) for full profile notes.

Short version:

- Native CMake profile: `-DPALM_PROFILE=IIIX`,
  `-DPALM_PROFILE=M100_EXPERIMENTAL`, or
  `-DPALM_PROFILE=IIIC_EXPERIMENTAL`
- VB profile switch:
  set exactly one of `PALM_PROFILE_M100_EXPERIMENTAL` or
  `PALM_PROFILE_IIIC_EXPERIMENTAL` to `True` in
  `PalmDesktopHarness/PalmConfig.vb`; keep both `False` for IIIx.

Rebuild both the native DLL and the VB harness after switching.

The m100 profile includes the m100/Calvin hardware identity, 160x220 digitizer
geometry, m100 key matrix, LCD contrast PWM register, and Port F backlight
enable line. The m100 ROM shows Brightness rather than Contrast in the Pen
shortcut list; observed `$A36` values run from `0x0180` to `0x01aa` and the
ESP32 target maps that range onto a 10-50% physical backlight PWM span. Port F
data bit `0x20` switches the emulated LCD palette between normal grayscale and
inverted backlit grayscale. The IIIc profile adds the SED1375 color LCD path and
a 4 MB RAM map. On ESP32, IIIc brightness is Austin-specific: the ROM asserts
Port B bit `0x08` as the LCD brightness-controller sync line, sends a 16-bit
SPIM value through `$800..$803`, and the emulator maps the inverted
`spiData >> 4` range `0x020..0x0a0` onto the configured 10-50% physical
backlight span. Port C bits `0x10` and `0x40` gate the IIIc backlight/panel
power. The IIIx profile keeps desktop LCD contrast fixed at maximum because its
ROM does not normally expose that software control.

The IIIx, m100, and IIIc hardware interfaces are selected at compile time with
`PALM_HARDWARE_PROFILE`; runtime profile switching is intentionally avoided.
IIIx/m100 builds use the DragonBall LCD path, while IIIc builds enable the
SED1375 register/VRAM/CLUT path and Austin GPIO handling.

The desktop LCD palette is also profile-owned in
`PalmDesktopHarness/PalmConfig.vb`; see
[HARDWARE_PROFILES.md](HARDWARE_PROFILES.md#desktop-lcd-palette) for the
current normal LCD and inverted green backlight RGB values.

## External Keyboards

External serial/IR keyboard emulation is disabled in normal builds. The tested
keyboard drivers either target newer Palm OS releases or depend on hardware
handshakes that conflict with HotSync behavior on the current profiles. Native
Palm input remains Graffiti, the on-screen keyboard, touch, and
the hardware application buttons.

## ESP32 Notes

The Arduino sketch targets the ESP32-4827S043C board, based on the
local `cyd_ref/005638_005638_Jingcai_ESP32_4827S043C_simple_GT911_touch.ino`
reference. That profile uses Arduino_GFX for the 480x272 RGB panel, GT911 touch,
an SD card for snapshots, and PSRAM for most Palm RAM.

PalmDay mitigation is scaffolded in `palm_config.h` and `palm_palmday.h`, but it
is disabled by default so the normal ESP32 build runs the unpatched ROM path used
for performance testing. When `PALM_PALMDAY_PATCH_ENABLED` is set,
`Tools/CompileEsp32Palm.ps1` runs `Tools/PatchPalmRom.ps1` against the temporary
`palm_current.rom` copy before Arduino embeds it. Patch entries live in
`Tools/PalmRomPatches.json` and must match the selected profile, ROM file name,
SHA-256, offset, and expected bytes; the original user ROM is never modified.
The current manifest includes a strict Palm IIIc 4.1 EN patch set for the local
ROM SHA-256. Cold-boot `TimSetSeconds` seeding is intentionally disabled so Palm
OS time is driven through emulated hardware/RTC behavior.

The ESP32 path keeps:

- a configurable low Palm RAM segment in internal DRAM via
  `PALM_RAM_INTERNAL_LOW_SIZE`, allocated before render buffers so the hot low
  RAM segment gets priority, with the rest in PSRAM
- CPU/register/timer hot state in internal DRAM
- ROM in flash
- 16 MB flash layout with two 4 MB app slots and a FATFS partition
- 10 FPS interval LCD updates, with only the 160x160 Palm LCD area refreshed
  dynamically
- optional indexed 8-bit RGB panel frame buffers with
  `PALM_PANEL_INDEXED_FRAMEBUFFER`. When enabled, the active and pending
  full-screen panel frames take about 261 KB instead of 522 KB; a small RGB565
  palette/cache stays in internal DRAM and the RGB bounce callback expands
  pixels back to RGB565 for the panel
- Musashi separate immediate/PC-relative read callbacks enabled so opcode
  extension reads can use the faster Palm instruction-read path where possible
- strict 68000 Musashi fast path enabled, including a generated static-table
  alias list for non-68000 opcode handlers
- a static PNG-derived 4bpp m100 silkscreen strip compiled into flash
- GT911 touch mapped into the ADS/digitizer emulation
- virtual app/up/down buttons on the left side of the panel
- virtual power, save snapshot, and reset controls on the right side of the
  panel
- Palm OS m100 brightness/contrast writes mapped from `$A36 = 0x0180..0x01aa`
  to a 10-50% ESP32 backlight PWM range
- m100 Port F bit `0x20` backlight state mapped to the LCD render palette, so
  backlit mode inverts all DragonBall LCD bpp modes through a per-frame palette
- Palm IIIc/Austin brightness mapped from Port B bit `0x08` brightness-controller
  sync plus 16-bit SPIM transfers; the inverted `spiData >> 4` range
  `0x020..0x0a0` controls ESP32 backlight duty while Port C bits `0x10` and
  `0x40` gate backlight/panel power
- automatic restore from `/palm_m100_state.bin` on the SD card, with wake from
  saved sleep state
- low-power Palm sleep mode that turns off the display/backlight, lowers CPU
  frequency, and uses light sleep between 250 ms touch polls
- SD snapshot access is short-lived: the firmware mounts the card only for
  save/restore, then calls `SD.end()`, stops the SPI bus, holds CS high, and
  releases the SD SPI pins to input
- the unused XPT2046 controller fitted on some ESP32-4827S043C boards is sent a
  startup power-down command; its CS is IO38 on the same SPI bus as the SD card
- raw Palm UART emulation bridged to the board's host serial port at 115200 baud
- room for future ESP32 LEDC output for Palm buzzer PWM

The ESP32 path intentionally keeps most runtime serial logging compiled out or
disabled after boot so the host serial port can be handed to the emulated Palm
UART.

Measured on the ESP32-4827S043C board, the current firmware is roughly 250 mA
while awake and roughly 40 mA while Palm OS is asleep. The remaining sleep draw
appears to be board/peripheral baseline rather than emulator activity: GT911 INT
is tied to ground on this PCB, so wake still needs low-rate polling, and deeper
savings would require hardware power gating for the LCD, touch, SD card, or
other always-powered board loads.

`Silkscreen.png` is the editable source for the side silkscreen artwork.
`silkscreen_asset.h` is the generated packed 4bpp PROGMEM copy used by the
firmware. Keeping this asset compiled into flash is faster and simpler than
loading it from FATFS; the visible static strip is drawn once into the static
panel frame.

Known ESP32 limitations:

- CPU-heavy apps still run slower than real m100 hardware.
- RTC stopwatch interrupt behavior is not implemented yet, so apps that depend
  on stopwatch ticks may pause while Palm OS is asleep.
- UART/HotSync is a raw serial bridge and still needs more compatibility work.
- Beam/IR is not implemented.

For ESP32 speed, Musashi bus-error support is compiled out by default:
`M68K_BUS_ERR_ENABLE` is `OPT_OFF` in `Musashi-master/m68kconf.h`, and
`PALM_BUS_ERROR_ON_RAM_LIMIT` is `0` in `palm_config.h`. Re-enable both if an
app or ROM path shows compatibility problems that look like missing RAM-limit
or unmapped-memory bus errors.

Known-good Arduino CLI compile target for the ESP32-4827S043C profile:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\CompileEsp32Palm.ps1
```

Build and upload to the board on `COM4`:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\CompileEsp32Palm.ps1 -Upload -Port COM4
```

The helper copies the checkout to a temporary folder named `ESP32-PALM`, because
Arduino CLI expects the sketch folder to match `ESP32-PALM.ino`. It also checks
that the user-supplied `Palm-m100-3.51-en.rom` exists before compiling. For a
source-only compile check without a real ROM image, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\CompileEsp32Palm.ps1 -CodeCheckOnly
```

The default helper build uses
[Tools/esp32_palm_16mb_partitions.csv](Tools/esp32_palm_16mb_partitions.csv),
a custom 16 MB layout with two 4 MB app slots and a 7.9 MB FATFS partition.
This gives the speed-focused ESP32-S3 build more headroom than Arduino's
standard `app3M_fat9M_16MB` layout.

The underlying Arduino CLI command is:

```bat
"%LOCALAPPDATA%\Programs\Arduino IDE\resources\app\lib\backend\resources\arduino-cli.exe" compile --fqbn "esp32:esp32:esp32s3:FlashSize=16M,PartitionScheme=custom,PSRAM=opi,CPUFreq=240,USBMode=hwcdc,UploadMode=default,CDCOnBoot=default" .
```

## Future Porting

The native API is now collected in `NativeMusashi/palm_core.h`. A future
cross-platform host should call that API and provide:

- ROM loading
- render surface
- touch/button input mapping
- audio output for PWM buzzer state
- app lifecycle state save/load
- optional HotSync/file install UI

Good next host targets would be SDL/Linux, native Android, or ESP32+PSRAM.

## License / Third-Party Code

This project includes the Musashi 68000 emulator source. See
`Musashi-master/readme.txt` and related files for Musashi's original license and
credits.

Palm OS ROMs and commercial Palm applications are not included and must be
provided by the user.
