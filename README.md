# ESP32-PALM

ESP32-PALM is an experimental Palm OS emulator project. It started as a
bring-up attempt for the ESP32 CYD, then grew a desktop harness that now runs
Palm OS well enough to use apps, HotSync files, persist RAM state, and emulate
basic Palm buzzer audio.

The current most usable target is the Windows desktop harness. The ESP32 CYD
target is kept in the tree, but a non-PSRAM CYD does not have enough practical
RAM headroom for the current core.

## Current Status

- Desktop Palm m100 profile boots and works well in normal use.
- Palm IIIx profile is still supported by the native core and harness.
- Palm IIIc support is experimental. It uses the Palm IIIc/Austin hardware
  profile, 8 MB RAM, and the SED1375 color LCD path.
- ESP32 CYD build is experimental and likely needs PSRAM for practical use.
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
- Desktop HotSync host for installing `.prc` and `.pdb` files.
- Memo Pad text sync to per-memo `.txt` files.
- m100 Note Pad export to raw `.bin` backup plus 1-bit `.bmp` preview.
- Basic Palm buzzer/audio output on Windows.

## Repository Layout

```text
ESP32-PALM.ino             Arduino sketch for ESP32/CYD experiments
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
geometry, m100 key matrix, and LCD contrast PWM register. The m100 ROM shows
Brightness rather than Contrast in the Pen shortcut list, but contrast writes
are still modeled for display rendering. The IIIc profile adds the SED1375
color LCD path and 8 MB RAM map. The IIIx profile keeps desktop LCD contrast
fixed at maximum because its ROM does not normally expose that software
control.

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

## ESP32 / CYD Notes

The Arduino sketch targets ESP32 CYD-style hardware with TFT and touch setup
borrowed from the local CYD reference project. The non-PSRAM CYD is too tight
for a comfortable Palm RAM allocation.

For a future ESP32+PSRAM pass, the likely direction is:

- Palm RAM in PSRAM
- CPU/register/timer hot state in internal DRAM
- ROM in flash
- dirty/interval LCD updates only
- real touch ADC mapped into the ADS/digitizer emulation
- ESP32 LEDC output for Palm buzzer PWM

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
