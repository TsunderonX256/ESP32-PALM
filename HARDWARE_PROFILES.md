# Hardware Profiles

The emulator is built around one Palm hardware profile at a time. Runtime
switching is intentionally avoided so each build has one ROM mapping, RAM map,
LCD geometry, digitizer geometry, and device identity.

## Supported Profiles

- `IIIX`: current working Palm IIIx profile.
- `M100_EXPERIMENTAL`: current working Palm m100 profile for
  `Palm-m100-3.51-en.rom`.
- `IIIC_EXPERIMENTAL`: current working Palm IIIc/Austin profile for
  `Palm-IIIc-4.1-en.rom`, including the experimental ESP32 SED1375 path.

## Profile Notes

- `IIIX`: uses the IIIx/Brad 160x160 DragonBall EZ LCD path, 4 MB RAM map,
  Brad hardware ID bits, and the shared III-series key/serial/IR register
  behavior. This profile does not expose a normal Palm OS brightness slider in
  the tested ROM, so display contrast stays fixed at maximum.
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
- `IIIC_EXPERIMENTAL`: uses the Palm IIIc/Austin profile with a 4 MB RAM map,
  20 MHz DragonBall EZ timing, Austin hardware ID bits, and the external Epson
  SED1375 color LCD controller at `$1F000000`. The SED1375 register aperture is
  `$1F01FFE0..$1F01FFFF`, and the emulated SED1375 VRAM window is 80 KB.
- IIIc rendering is intentionally separate from the DragonBall EZ LCD path used
  by IIIx and m100. IIIc uses the SED1375 indexed-color VRAM and CLUT path;
  DragonBall LCD registers such as `$A00..$A3F` are not the active panel
  interface for IIIc.

## Compile-Time Profile Boundaries

Profiles are selected at compile time with `PALM_HARDWARE_PROFILE`, never by a
runtime switch. `palm_config.h` defines the selected ESP32 profile before
including `palm_profile.h`; desktop native builds pass the equivalent profile
through CMake's `-DPALM_PROFILE=...` option.

The profile boundary is intentionally hard:

- `palm_profile.h` owns RAM size, CPU clock, device name, and whether SED1375 is
  present. Unsupported profile values fail compilation.
- `PALM_PROFILE_IIIX` and `PALM_PROFILE_M100_EXPERIMENTAL` set
  `PALM_HAS_SED1375=0` and use the DragonBall EZ LCD path.
- `PALM_PROFILE_IIIC_EXPERIMENTAL` sets `PALM_HAS_SED1375=1` and enables the
  SED1375 color LCD path.
- `palm_hw.cpp` gates hardware ID, key matrix, GPIO inputs/outputs, LCD
  backlight behavior, and IIIc brightness-controller handling with
  `#if PALM_HARDWARE_PROFILE == ...`.
- `palm_memory.cpp` gates the SED1375 register/VRAM/CLUT implementation with
  `#if PALM_HAS_SED1375`.
- ESP32 panel buffering is also profile-owned:
  `PALM_PANEL_INDEXED_FRAMEBUFFER` is enabled for IIIx/m100 to save PSRAM, and
  disabled for IIIc so the scaled 256-color SED1375 output is kept in RGB565.

When adding a new hardware behavior, keep it inside the matching profile block
unless the real IIIx, m100, and IIIc hardware all share that behavior.

## IIIc/Austin Hardware Controls

The IIIc profile models Austin-specific LCD and brightness control separately
from the m100 backlight/contrast path.

Known Austin display control lines:

| Function | Register/bit | Direction | Notes |
| --- | --- | --- | --- |
| SED1375 VRAM | `$1F000000` | memory | 80 KB indexed-color VRAM window |
| SED1375 registers | `$1F01FFE0..$1F01FFFF` | memory | Epson SED1375 control/CLUT aperture |
| LCD brightness controller sync | Port B data `$409`, bit `0x08` | active-low output | `hwrEZPortBLCDBright`; brackets brightness SPI transfers |
| SED1375 backlight enable | Port C data `$411`, bit `0x10` | active-high output | `hwrEZPortCBacklightEnable`; gates ESP32 backlight duty |
| Screen 5V enable | Port C data `$411`, bit `0x40` | active-high output | `hwrEZPortCEnable5V`; also gates ESP32 backlight duty |
| LCD powered input | Port F data `$429`, bit `0x01` | active-high input | held visible so Austin display wake polling completes |
| Pen IO input | Port F data `$429`, bit `0x02` | input | active-high Austin pen line |
| Video clock enable | Port F data `$429`, bit `0x20` | active-high output | Austin video-clock control line |
| SED1375 power save | SED1375 register `0x03`, bit `0x04` | register bit | observed as a power-save/dim state, not the normal brightness slider |

The Palm IIIc brightness slider does not write the m100 `$A36` contrast PWM
register. It asserts the Port B brightness-controller sync line and sends
16-bit DragonBall SPIM transfers through `$800..$803`. On the tested ROM, the
useful raw brightness value is `spiData >> 4`; observed slider endpoints are
approximately `0x020..0x0a0`, and the value is inverted. The ESP32 target maps:

```text
raw 0x020 -> brightness level 255 -> configured 50% physical backlight
raw 0x0a0 -> brightness level   0 -> configured 10% physical backlight
```

Port C backlight-enable and 5V-enable must both be high; otherwise ESP32
backlight duty is forced to zero. The temporary serial probe
`PALM_IIIC_BRIGHTNESS_SERIAL_STATS` can be enabled in `palm_config.h` to print
`IIIC BRI ...` lines while investigating this path, but it should stay disabled
in normal UART/HotSync builds.

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

The Arduino/ESP32 build is also compile-time selected. `palm_config.h` currently
defines the active profile before including `palm_profile.h`:

```c
#define PALM_HARDWARE_PROFILE PALM_PROFILE_IIIX
```

or:

```c
#define PALM_HARDWARE_PROFILE PALM_PROFILE_M100_EXPERIMENTAL
```

or:

```c
#define PALM_HARDWARE_PROFILE PALM_PROFILE_IIIC_EXPERIMENTAL
```

`Tools/CompileEsp32Palm.ps1` reads this setting and embeds the matching ROM by
default:

- IIIx: `Palm-IIIx-3.1.rom`
- m100: `Palm-m100-3.51-en.rom`
- IIIc: `Palm-IIIc-4.1-en.rom`

On ESP32, IIIx/m100 builds use the DragonBall LCD decoder and the indexed panel
framebuffer option. IIIc builds use the SED1375 color decoder, core-0 SED1375
VRAM snapshot/render assist, RGB565 panel frames, Austin GPIO handling, and the
IIIc brightness-controller SPI mapping described above.
