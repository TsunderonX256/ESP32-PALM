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
- ESP32 CYD build is experimental and likely needs PSRAM for practical use.
- Native emulator core is exposed through `NativeMusashi/palm_core.h` for future
  hosts such as SDL, Android, Linux, or ESP32+PSRAM.

## Features

- Musashi 68000 CPU core.
- DragonBall EZ-style memory map, registers, interrupts, timers, RTC, LCD,
  GPIO/button input, pen/touch input, UART transport, and PWM buzzer.
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

The desktop harness currently uses the m100 profile in
`PalmDesktopHarness/PalmConfig.vb`. Keep that setting in sync with the native
`PALM_PROFILE` CMake option.

## Switching Hardware Profiles

See [HARDWARE_PROFILES.md](HARDWARE_PROFILES.md) for full profile notes.

Short version:

- Native CMake profile: `-DPALM_PROFILE=IIIX` or
  `-DPALM_PROFILE=M100_EXPERIMENTAL`
- VB profile switch:
  `#Const PALM_PROFILE_M100_EXPERIMENTAL = True` or `False` in
  `PalmDesktopHarness/PalmConfig.vb`

Rebuild both the native DLL and the VB harness after switching.

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
