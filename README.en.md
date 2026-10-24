# Inphic Battery Tray

[中文](README.md) | [**English**](README.en.md)

A **taskbar battery indicator** for INPHIC wireless mice. By [@Tan-TanZi](https://github.com/Tan-TanZi).

No driver patching, no DLL injection into the vendor software, no writes to the device — it only listens to the battery report the mouse already broadcasts every 2 seconds, and paints it in the notification area.

In the actual notification area:

![In the tray](assets/a1.png)

At different charge levels:

![Battery levels](assets/preview-levels.png)

---

## Why this exists

The vendor config tool `INPHIC HUB` shows battery only inside its own UI, and only after you open the app and navigate to the device page. The tray shows nothing, so once the app is closed you have no idea how much charge is left.

Windows' built-in "Bluetooth device battery" does not work for mice paired through a 2.4G USB receiver, so this had to be built by hand.

---

## Features

| Feature | Detail |
|---------|--------|
| Battery icon | Mouse silhouette filled from the bottom up |
| Colors | Teal `#19fac6` (>40%) / yellow (16–40%) / red (≤15%) |
| Offline detection | No report for 8 s → whole icon greys out, hover still shows the last known level |
| Connection type | `USB` / `Bluetooth (BLE)` shown in details |
| Detail popup | Non-modal, never steals focus, auto-dismisses after 4 s |
| Autostart | Registry run key, toggleable from the tray menu |
| Multi-device | Scans 9 VID/PID pairs, several mice can be shown at once |
| Zero dependencies | Single exe, no .NET SDK or runtime install required |

### Tray states

| State | Icon | Tooltip |
|-------|------|---------|
| Online | Teal fill proportional to charge | `INPHIC 87% (2000ms refresh)` |
| Offline | Entirely greyed out | `INPHIC: disconnected / powered off (last 87%, 21:12:03)` |
| Not detected | Greyed out | `INPHIC: no device detected` |

---

## Quick start

### Option 1: Prebuilt binary

1. Download `InphicBatteryTray.exe`
2. Put it somewhere permanent (e.g. `%LOCALAPPDATA%\InphicBatteryTray\`)
3. Double-click to run; the tray icon appears
4. Right-click the icon → tick **开机自动启动** (Start with Windows)

> Do not put it inside the vendor software's install folder — uninstalling or updating `INPHIC HUB` may delete it and break autostart.

### Option 2: Build from source

See [Build](#build).

---

## Usage

| Action | Effect |
|--------|--------|
| Hover | Shows charge level and refresh interval |
| Right-click | Menu: details / rescan devices / autostart / exit |
| Double-click | Shows the detail popup |
| Click popup | Closes immediately (otherwise it fades after 4 s; stays while the cursor is over it) |

---

## How it works

### Transport

The vendor tool is a plain user-mode program using **hidapi** over HID Output/Input reports — **no kernel driver**. This project talks to the device through Windows' own `hid.dll` + `setupapi.dll`, so even `hidapi.dll` is not required.

The important part: the device is opened with `FILE_SHARE_READ | FILE_SHARE_WRITE`. HID input reports are copied to every handle open on the same interface, so this tool and the vendor software **can run at the same time** without interfering.

### Battery report format (measured)

Battery lives on the `Usage Page 0x000A` interface (not the 64-byte vendor channel), fixed **5 bytes**:

```
offset 0        1             2           3          4
       0x03     device type   subcommand  device #   payload
```

| Byte | Meaning |
|------|---------|
| `[0]` | Always `0x03` (Report ID) |
| `[1]` | Device type code: `0x95` / `0x90` / `0x93` / `0x99` / `0x98` / `0x73` |
| `[2]` | Subcommand: `0x10` connection/device#, `0x20` status, `0x40` **battery**, `0x50` charging |
| `[3]` | Device number `1..7` |
| `[4]` | Payload — battery percentage `0..100` |

Captured sample:

```
03 98 40 01 64     →  device type 0x98, battery report, device #1, 0x64 = 100%
```

### Reporting behaviour (measured)

| Item | Measured |
|------|----------|
| Direction | **Device pushes on its own**; the host never polls |
| Interval | **≈2000 ms**, ±60 ms jitter |
| Resolution | Integer percent (1% step), **no decimals, no voltage/ADC field** |
| Power-off | **Goes silent immediately**, sends no "goodbye" notification |
| Other subcommands | `0x10/0x20/0x50` never fired in any scenario tested (power cycles, replug) |

The last two rows are why offline detection can only be a **timeout**: the device never announces that it left, so the only signal is silence.

### Why 8 seconds

The report interval is 2 s, so 8 s means four consecutive misses — long enough not to trip on a single hiccup, short enough that the icon greys out quickly after power-off.

### How connection type is determined

Not by asking the device — by inspecting the **HID interface path**: Bluetooth devices enumerate under `BTHENUM`, everything else is treated as USB. This mirrors what the vendor does (it distinguishes connection modes by VID).

### Icon rendering

The tray icon is rasterised at runtime with GDI+, not shipped as a fixed image:

1. Scale `MouseIcon.png` to the system's real icon size (usually 16×16)
2. Find the alpha edge and force the outermost ring to a solid dark stroke — at 16px a scaled stroke is only half a pixel wide and would otherwise smear into a blob
3. Detect "green body" pixels (`G - R > 45 && G - B > 45`) and replace them with the status colour or grey, based on the fill height
4. Outline, scroll wheel and top button keep their original colours

That is what keeps it recognisable as a mouse at 16×16 instead of a coloured blob.

---

## Build

Only the .NET Framework compiler that ships with Windows is needed — **no SDK install**:

```powershell
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /target:winexe /platform:x64 `
    /win32icon:assets\app.ico `
    /resource:assets\MouseIcon.png,MouseIcon.png `
    /out:InphicBatteryTray.exe `
    /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    src\InphicBatteryTray.cs src\AssemblyInfo.cs
```

Or simply:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

---

## Customisation

Edit these in `src/InphicBatteryTray.cs` and rebuild:

| What | Where |
|------|-------|
| Colour thresholds | `StatusColor()` |
| Offline timeout | `const int OfflineAfterMs` |
| Fill direction | `fillTop` / `y >= fillTop` in `DrawIcon()` |
| Icon artwork | replace `assets/MouseIcon.png` |
| Supported VID/PID | `Candidates` array |
| Reporting assumption | keep `OfflineAfterMs` ≥ 4× the report interval |

The full device-matching list lives in the `Candidates` array in the source (9 VID/PID pairs, covering the 7 models the vendor software enumerates plus 2 self-branded VIDs).

---

## Privacy

Read-only, and deliberately avoids user input:

- ✅ Reads: battery reports on the status channel (`Usage Page 0x000A`)
- ✅ Reads: HID metadata (VID/PID, product string)
- ❌ **Never reads** any `Usage Page 0x0001` (Generic Desktop) interface — that is the keyboard and mouse proper, where keystrokes and movement live
- ❌ Sends no reports, writes nothing to the device, changes no settings
- ❌ No network access, no telemetry

This was a hard constraint during development too: every diagnostic sniffer used while reverse-engineering explicitly filtered out keyboard/mouse interfaces.

---

## Known limitations

1. **Tested on exactly one device** (INPHIC **IN10**). The other VID/PID pairs come from the vendor's enumeration list and are **not hardware-verified** — they may not work.
2. **Battery resolution is 1% at the hardware level.** No decimals, no voltage, no health data. Showing `87.3%` would require fabricating it through interpolation, which this project deliberately does not do.
3. **Reporting rate cannot be configured.** The 2-second cadence is baked into firmware; the host-side protocol has no "set report interval" command.
4. **Offline is a guess via timeout** (8 s), because the device sends nothing on power-off. A mouse that merely dozed off could be misreported as offline.
5. Fine badge/detail is lost at 16×16 — that is the physical limit of tray icons.

---

## Credits

- [RuiShu1024/InphicMouse](https://github.com/RuiShu1024/InphicMouse) — a protocol reconstruction of the same vendor's IN9 series. The two generations are not wire-compatible (IN9 uses 33-byte checksummed frames; this device uses 5-byte reports), but it confirmed the vendor's overall design and the "connection mode is inferred from VID" approach.

---

## Disclaimer

Unofficial third-party tool, not affiliated with INPHIC.

Protocol details were obtained by statically reverse-engineering `INPHIC HUB 1.0.1.19` plus live capture, for interoperability purposes only. The program itself is read-only and modifies neither the device nor the vendor software. Use at your own risk.

---

## License

[MIT](LICENSE)
