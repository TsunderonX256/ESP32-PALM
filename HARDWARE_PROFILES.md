# Hardware Profiles

The emulator is built around one Palm hardware profile at a time. Runtime
switching is intentionally avoided so each build has one ROM mapping, RAM map,
LCD geometry, digitizer geometry, and device identity.

## Supported Profile

- `IIIX`: current working Palm IIIx profile.
- `M100_EXPERIMENTAL`: boots and works well in early testing with
  `Palm-m100-3.51-en.rom`, but keep it marked experimental until more apps,
  sleep/wake, HotSync, and edge-case hardware behavior are checked.

## Experimental Profile

- `M100_EXPERIMENTAL`: uses the m100-class 160x160 LCD plus 60-pixel
  silkscreen geometry. It currently shares the DragonBall EZ hardware behavior
  used by the IIIx-oriented core.

## Switching Profiles

Always switch both the native DLL and the VB harness together.

For IIIx:

```powershell
cmake -S NativeMusashi -B NativeMusashi\build-release -DPALM_PROFILE=IIIX
cmake --build NativeMusashi\build-release
```

and in `PalmDesktopHarness/PalmConfig.vb`:

```vb
#Const PALM_PROFILE_M100_EXPERIMENTAL = False
```

For m100:

```powershell
cmake -S NativeMusashi -B NativeMusashi\build-release -DPALM_PROFILE=M100_EXPERIMENTAL
cmake --build NativeMusashi\build-release
```

and in `PalmDesktopHarness/PalmConfig.vb`:

```vb
#Const PALM_PROFILE_M100_EXPERIMENTAL = True
```

After switching either profile, rebuild the VB harness so the current
`PalmMusashi.dll` and ROM are copied into the app output folder.

## Desktop Native Core

The native DLL defaults to `IIIX` when configured from scratch. To build a
specific profile:

```powershell
cmake -S NativeMusashi -B NativeMusashi\build-release -DPALM_PROFILE=IIIX
cmake --build NativeMusashi\build-release
```

Accepted `PALM_PROFILE` values:

- `IIIX`
- `M100_EXPERIMENTAL`

## VB Harness

The VB harness currently selects its profile with a conditional constant in
`PalmDesktopHarness/PalmConfig.vb`:

```vb
#Const PALM_PROFILE_M100_EXPERIMENTAL = False
```

Keep this `False` for the working IIIx build. Set it to `True` only for the
experimental m100 build, and make sure the matching ROM file exists beside the
EXE.

Current desktop profile files:

- IIIx ROM: `Palm-IIIx-3.1.rom`
- IIIx state: `palm_iiix_state.bin`
- m100 ROM: `Palm-m100-3.51-en.rom`
- m100 state: `palm_m100_state.bin`

m100 Note Pad data:

- DB name: `npadDB`
- DB type: `DATA`
- DB creator: `npad`
- Current HotSync support exports records to 1-bit BMP files under
  `HotSync/NotePad`.

## ESP32 Build

The Arduino build defaults to `IIIX` through `palm_profile.h`. To compile a
different profile, define `PALM_HARDWARE_PROFILE` before `palm_config.h` is
included or add a compiler define:

```c
#define PALM_HARDWARE_PROFILE PALM_PROFILE_IIIX
```

or:

```c
#define PALM_HARDWARE_PROFILE PALM_PROFILE_M100_EXPERIMENTAL
```
