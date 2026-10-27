# INPHIC / ATK Battery Tray

[中文](README.md) | [**English**](README.en.md)

A **taskbar battery indicator** for wireless mice. Supports INPHIC **IN10** (device pushes reports), **IN9** (host queries a Feature Report), and **ATK X1 Pro with its 1K receiver** (Output/Input Report queries).

No driver patching, DLL injection, or device configuration changes. IN10 listens for battery updates; IN9 and ATK send battery queries, then display valid readings in the notification area.

### ATK X1 Pro support in this branch

- Real battery readings, tray refresh, reopening details, second-instance exit and fresh readings after a rescan have been checked with an X1 Pro and its **1K receiver `373B:101A`**.
- Queries run about **2.5 seconds** after each valid reply. Report length, command, percentage range and checksum are validated. After 8 seconds without valid data, the icon greys out and retains the last reading.
- Only the 17-byte vendor battery collection, `Usage Page 0xFF02 / Usage 0x0002`, is accessed; no mouse movement or keystrokes are read.
- A second launch exits immediately to prevent duplicate polling. Manual rescans wake the supervisor and reopen interfaces after old reads finish; receivers of the same model keep separate states.
- The charging flag was checked on hardware while powered by a charger, after unplugging the charging cable, and after reconnecting it. It is read through the 1K receiver. The tooltip and details show charging; unplugging clears it. Unknown flags stay unknown, and 100% alone does not imply charging is complete.
- Idle timeouts have been checked: the icon greys out and keeps the last reading. The tooltip says "no response" rather than treating sleep as a confirmed power-off; stale charging data is marked as the last recorded state in details. USB wired data mode, 8K receivers, Bluetooth and other ATK models are not supported. Power-off, receiver unplug/replug, sleep recovery and coexistence with ATK HUB have not been checked on hardware.
- Build this branch and run `InphicBatteryTray.exe`; the upstream release binary does not include this adaptation.

Run checks without a test framework:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run.ps1
# Also query real hardware three times, with the receiver connected and mouse awake:
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run.ps1 -Probe
```

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
| Connection type | `USB` / `Bluetooth (BLE)` shown in details; ATK identifies the 2.4G receiver |
| Detail popup | Non-modal, never steals focus, auto-dismisses after 4 s |
| Autostart | Registry run key, toggleable from the tray menu |
| Multi-device | IN10 scans 9 VID/PID pairs; IN9 scans `248A` / `249A`; ATK matches `373B:101A` |
| Battery protocols | IN10 listens; IN9 polls Feature Reports (`0x0C`); ATK uses Output/Input Reports (`0x08` / `0x04`) |
| Zero dependencies | Single exe, no .NET SDK or runtime install required |

### Tray states

| State | Icon | Tooltip |
|-------|------|---------|
| Online | Fill proportional to charge, colored by threshold | `鼠标电量 87% (2000ms 刷新)` |
| Offline | Entirely greyed out | `鼠标：暂无回应（最后 87%，21:12:03）` |
| Not detected | Greyed out | `鼠标电量：未检测到设备` |

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
       0x03     device type   subcommand  status     payload
```

| Byte | Meaning |
|------|---------|
| `[0]` | Always `0x03` (Report ID) |
| `[1]` | Device type code: `0x95` / `0x90` / `0x93` / `0x99` / `0x98` / `0x73` |
| `[2]` | Subcommand: `0x10` connection/device#, `0x20` status, `0x40` / `0x41` **battery**, `0x50` charging |
| `[3]` | Battery-event status; the app interprets `1` as not charging, `2` as full and `3` as charging |
| `[4]` | Payload — battery percentage `0..100` |

Captured sample:

```
03 98 40 01 64     →  device type 0x98, battery report, status 1, 0x64 = 100%
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

### IN9 series (a different protocol entirely)

IN9 and IN10 share nothing — not even the transport:

| Item | IN10 | IN9 |
|---|---|---|
| Transport | Input Report, device pushes | **Feature Report** (`HidD_SetFeature` / `HidD_GetFeature`) |
| Report ID | `0x03` (status) / `0x04` (config) | **`0x0C`** |
| Frame | 5 bytes | **33 bytes** |
| Battery source | passive, every 2 s | **host sends `0x20`** |
| Battery offset | 5th byte | response `buf[0x12]` |
| VID/PID | `1D57` / `045E`, 9 pairs | `248A:5B2E` (wired) / `248A:5B2F` (2.4G) |

Frame layout:

```
[0]     0x0C      report ID
[1]     0x01      fixed
[2]     command   0x20 = query battery, 0xF0 = handshake
[3]     0x00
[4]     sequence (increments)
[5..]   payload
```

- Handshake: `0C 01 F0 00 <seq> 01 01 FF` (write only)
- Query: `0C 01 20 00 <seq> 00 00` → wait 10 ms → read back 33 bytes

Response:

| Offset | Meaning |
|---|---|
| `[2]` | `0x20` command echo |
| `[6]` | `!= 0` means a valid response |
| `[8..0x0B]` | model magic (e.g. `"M909"`) |
| `[0x0F]` | IC type |
| `[0x12]` | **battery 0–100** |
| `[0x13]` | `2` = charging |
| `[0x15]` | online flag |

> The frame layout and response offsets come from the derivative project [InphicBatteryTray-IN9](https://github.com/MRXie-zf/InphicBatteryTray-IN9), reverse-engineered from the vendor's `Inphic IN9 GM.exe` and verified on real hardware.

Device matching: VID `248A` / `249A` with **Feature report length ≥ 33**, excluding Generic Desktop / Keyboard input collections rather than relying on an MI number. IN10 listens only on `Usage Page 0x000A`; ATK uses its dedicated vendor battery collection.

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
- ✅ Queries: IN9 Feature Reports and the ATK vendor battery collection (`08 04 … 49`), without changing settings
- ✅ Reads: HID metadata (VID/PID, product string)
- ❌ **Never reads** any `Usage Page 0x0001` (Generic Desktop) interface — that is the keyboard and mouse proper, where keystrokes and movement live
- ❌ Changes no device settings (DPI, lighting, key mappings or firmware)
- ❌ No network access, no telemetry

This was a hard constraint during development too: every diagnostic sniffer used while reverse-engineering explicitly filtered out keyboard/mouse interfaces.

---

## Known limitations

1. **Model coverage**: IN10 verified on `Inphic KP 8K`; IN9 implemented against the hardware-verified protocol of the IN9 refresh (`248A:5B2E` / `248A:5B2F`). Other INPHIC models (IN10 Pro / IN6 …) are unverified and may not read.
2. **Only the device's integer percentage is displayed.** No interpolation, voltage display or health data. INPHIC battery reports contain no voltage field; extra ATK report fields are not used for the tray display.
3. **Reporting rate cannot be configured.** IN10's 2 s cadence is baked into firmware; IN9's 2.5 s poll interval is chosen by the app. Neither generation exposes a "set interval" command.
4. **Offline is a guess via timeout** (8 s), because the device sends nothing on power-off. A mouse that merely dozed off could be misreported as offline.
5. Fine badge/detail is lost at 16×16 — that is the physical limit of tray icons.

### Battery not detected?

The app writes `tray.log` next to the executable. During normal operation, a log larger than 512 KB starts afresh with the next entry. It records enumerated interfaces, received bytes and parse results, and no longer exports icon images automatically. A sleeping ATK mouse may not answer queries; move it to wake it before checking again. Provide the log when asking for diagnosis.

---

## Credits

- [Fan4Metal/mouse_tray](https://github.com/Fan4Metal/mouse_tray/blob/master/mouse_tray/drivers/chipset/nordic52.py) — reference for the ATK / Compx 17-byte battery query protocol, additionally checked here on an X1 Pro 1K receiver.
- [**MRXie-zf/InphicBatteryTray-IN9**](https://github.com/MRXie-zf/InphicBatteryTray-IN9) — the IN9 derivative of this project, and **the sole basis for IN9 support in v1.1.0**. They reverse-engineered the real Feature Report protocol from the vendor's `Inphic IN9 GM.exe` (report ID `0x0C`, command `0x20`, battery at `buf[0x12]`) and verified it on hardware. If you want richer IN9 features (low-battery toast, `--probe` diagnostics), use their build directly.
- [RuiShu1024/InphicMouse](https://github.com/RuiShu1024/InphicMouse) — an earlier protocol reconstruction of the same vendor's IN9 series, useful for understanding the vendor's communication design (notably that connection mode is inferred from VID).

---

## Disclaimer

Unofficial third-party tool, not affiliated with INPHIC or ATK.

Protocol details were obtained by statically reverse-engineering `INPHIC HUB 1.0.1.19` plus live capture, for interoperability purposes only. The program itself is read-only and modifies neither the device nor the vendor software. Use at your own risk.

---

## License

[MIT](LICENSE)
