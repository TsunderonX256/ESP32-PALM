# Hardware Profiles

The emulator is built around one Palm hardware profile at a time. Runtime
switching is intentionally avoided so each build has one ROM mapping, RAM map,
LCD geometry, digitizer geometry, and device identity.

## Supported Profiles

- `IIIX`: current working Palm IIIx profile.
- `M100_EXPERIMENTAL`: current working Palm m100 profile for
  `Palm-m100-3.51-en.rom`.
- `IIIC_EXPERIMENTAL`: current working Palm IIIc/Austin desktop profile for
  `Palm-IIIc-4.1-en.rom`.

## Profile Notes

- `M100_EXPERIMENTAL`: uses the m100-class 160x160 LCD plus 60-pixel
  silkscreen geometry, Calvin/m100 hardware ID bits, the m100 key matrix, and
  DragonBall EZ LCD contrast PWM register `$A36`.
- The m100 key matrix follows the Cloudpilot/POSE layout: row 0 is the four
  app buttons, row 1 column 1 is Page Down, and row 2 contains Power, Page Up,
  and app button 2.
- The m100 ROM exposes **Brightness** in the Pen shortcut list. Contrast still
  exists as a lower-level LCD/PWM register path. On the tested m100 ROM the
  slider writes `$A36 = 0x0180` at minimum and `$A36 = 0x01aa` at maximum; the
  ESP32 target clamps that raw `0x80..0xaa` byte range into the full 10-50%
  physical backlight PWM range.
- Real m100 LCD backlight enable is DragonBall EZ Port F data bit `0x20`
  (`hwrEZPortFBacklightOn`, active high). The ESP32 renderer uses that bit to
  switch the DragonBall LCD palette: normal mode maps low pixels bright and high
  pixels dark, while backlit mode inverts the grayscale palette for all supported
  LCD bpp values.
- The IIIx profile keeps desktop contrast fixed at maximum because this ROM
  does not expose a software brightness/contrast control in normal use.
- The IIIc profile uses a 4 MB RAM map and the external Epson SED1375 color
  LCD controller at `$1F000000`. This is intentionally separate from the
  DragonBall EZ LCD path used by IIIx and m100.

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

For IIIc:

```powershell
cmake -S NativeMusashi -B NativeMusashi\build-release -DPALM_PROFILE=IIIC_EXPERIMENTAL
cmake --build NativeMusashi\build-release
```

and in `PalmDesktopHarness/PalmConfig.vb`:

```vb
#Const PALM_PROFILE_M100_EXPERIMENTAL = False
#Const PALM_PROFILE_IIIC_EXPERIMENTAL = True
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
- `IIIC_EXPERIMENTAL`

## VB Harness

The VB harness currently selects its profile with a conditional constant in
`PalmDesktopHarness/PalmConfig.vb`:

```vb
#Const PALM_PROFILE_M100_EXPERIMENTAL = False
#Const PALM_PROFILE_IIIC_EXPERIMENTAL = False
```

Keep both `False` for the IIIx build. Set only `PALM_PROFILE_M100_EXPERIMENTAL`
to `True` for the m100 build, or only `PALM_PROFILE_IIIC_EXPERIMENTAL` to
`True` for the IIIc build. Make sure the matching ROM file exists beside the
EXE.

Current desktop profile files:

- IIIx ROM: `Palm-IIIx-3.1.rom`
- IIIx state: `palm_iiix_state.bin`
- m100 ROM: `Palm-m100-3.51-en.rom`
- m100 state: `palm_m100_state.bin`
- IIIc ROM: `Palm-IIIc-4.1-en.rom`
- IIIc state: `palm_iiic_state.bin`

## Desktop LCD Palette

The desktop renderer keeps LCD colors in `PalmDesktopHarness/PalmConfig.vb` so
each hardware profile can tune its own panel appearance. Current IIIx and m100
profiles intentionally share the same passive LCD and green EL backlight
palette:

Profiles also choose whether the renderer follows the DragonBall EZ LCD
contrast register. m100 enables it for the ROM's **Brightness** shortcut; IIIx
uses a fixed maximum contrast value.

On ESP32, the m100 renderer also follows the hardware backlight GPIO. Port F
data bit `0x20` selects the inverted backlit DragonBall LCD palette, and the
observed `$A36` raw byte range `0x80..0xaa` is expanded to the configured
physical backlight PWM span.

| Purpose | RGB |
| --- | --- |
| Normal LCD background | `226, 230, 218` |
| Normal silkscreen fill | `150, 160, 130` |
| Sleep page background | `224, 228, 214` |
| Sleep LCD background | `204, 211, 194` |
| Sleep silkscreen fill | `134, 142, 116` |
| Sleep line/pixel color | `90, 95, 80` |
| Inverted backlight page background | `18, 38, 24` |
| Inverted backlight LCD background | `10, 28, 14` |
| Inverted backlight sleep LCD background | `10, 18, 12` |
| Inverted backlight low pixel | `14, 42, 18` |
| Inverted backlight high pixel | `172, 255, 146` |
| Inverted backlight silkscreen fill | `118, 152, 102` |
| Inverted backlight line/pixel color | `3, 14, 5` |

m100 Note Pad data:

- DB name: `npadDB`
- DB type: `DATA`
- DB creator: `npad`
- Current HotSync support exports records to 1-bit BMP files under
  `HotSync/NotePad`.

## External Keyboards

External keyboard emulation is intentionally disabled for the current OS 3.x
profiles. The serial/Stowaway experiment was removed from the normal source
surface because the tested driver handshakes share the cradle button/IRQ1 line
and can fight HotSync. The IR keyboard driver path appears to require Palm OS
4-era Serial/IrDA behavior, so it is not part of the normal profile surface.

The hardware UART still models the IIIx-style IR route: `uMisc.IRDAEn` (`$0020`)
selects IRDA mode and transmitted bytes echo back to receive, matching the
half-duplex IR circuit. That behavior belongs to the device profile even though
no external keyboard is currently exposed.

## Cradle Button / IRQ1

The cradle HotSync button is modeled as an active-low physical line feeding
DragonBall EZ IRQ1. The emulator honors the IRQ1 polarity and edge/level mode
bits in ICR (`$302`): level mode follows the line until release, while edge mode
latches IRQ1 on the active transition and lets Palm OS clear it by writing the
IRQ1 bit to ISR (`$30D`). The public desktop API name is
`palm_native_set_cradle_button`; the older HotSync-named entry point remains as a
compatibility alias.

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

or, for desktop/header parity only:

```c
#define PALM_HARDWARE_PROFILE PALM_PROFILE_IIIC_EXPERIMENTAL
```

The ESP32/CYD path does not currently implement the IIIc SED1375 color LCD
controller, so IIIc remains a desktop-oriented profile for now.
