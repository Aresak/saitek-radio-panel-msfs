# CustomRadioPanel

A free, open-source app dedicated to the **Logitech / Saitek Pro Flight Radio Panel** with
**Microsoft Flight Simulator 2020**. Reads the panel's selectors, knobs and ACT/STBY buttons, drives
the two 5-digit displays, and talks to the sim over SimConnect — no paid software, no subscription.

> Built with .NET 10 + MAUI Blazor Hybrid. Windows only (that's where MSFS, SimConnect and the panel live).

![build](https://github.com/Aresak/saitek-radio-panel-msfs/actions/workflows/build.yml/badge.svg)

> ❤️ **Made with love for the flight-sim community — free, forever.**
> If it earned a spot on your desk and you'd like to say thanks, you can
> [**buy me a coffee**](https://buymeacoffee.com/aresak). Totally optional, always appreciated — happy landings! ✈️

---

## Features

- **Both rows** (upper / lower), full selector: `COM1 COM2 NAV1 NAV2 ADF DME XPDR`.
- **Tuning** with the concentric knobs — outer = coarse (1 MHz), inner = fine (COM 25 kHz **or 8.33 kHz**, NAV 50 kHz).
- **ACT/STBY** button swaps active/standby; long-press on XPDR sets standard pressure.
- **Transponder** (octal squawk) and **altimeter** (QNH / STD) on the XPDR position.
- **Configurable knob feel** — the thing the old profile got wrong. Acceleration makes a fast spin
  cover a big range while slow turns stay precise. All thresholds live in **Settings**.
- **VATSIM-friendly display** — drops the always-1 leading digit and shows 3 decimals (`123.405 → 23.405`).
- **Aircraft-agnostic** — standard SimConnect variables/events, not tied to any specific add-on.
- **Live UI** — a dashboard styled like the real panel: live displays and selector positions, and you
  can click any display to type a frequency straight into the sim.

## What each selector position does

| Selector | Left display | Right display | Outer knob | Inner knob | Button |
|---|---|---|---|---|---|
| **COM1 / COM2** | active | standby | ±1 MHz | ±25 / 8.33 kHz | swap |
| **NAV1 / NAV2** | active | standby | ±1 MHz | ±50 kHz | swap |
| **ADF** | ADF1 (kHz) | ADF2 (kHz) | ±10 kHz | ±1 kHz | — |
| **DME** | DME1 (NM) | DME2 (NM) | read-only | read-only | — |
| **XPDR** | QNH (hPa) | squawk | ±1 hPa | ±1 (octal) | long-press = STD |

## Download & run (release)

1. Grab the latest `CustomRadioPanel-win-x64.zip` from the [Releases](../../releases) page.
2. Unzip anywhere and run **`CustomRadioPanel.App.exe`**.
3. Plug in the Radio Panel and start MSFS.

Requirements:
- **Windows 10/11 x64.**
- **[WebView2 runtime](https://developer.microsoft.com/microsoft-edge/webview2/)** — preinstalled on
  current Windows; installed automatically on most systems.
- **MSFS 2020** for the sim link (see below). The panel + displays work without the sim too.

### About SimConnect.dll

The app needs the native **`SimConnect.dll`**, which is a proprietary Microsoft library — so it is
**not** bundled here. The app finds your own copy automatically, in this order:

1. `SIMCONNECT_DLL` environment variable (full path or folder), if set.
2. Next to `CustomRadioPanel.App.exe`.
3. Your **Steam** MSFS install (`…\steamapps\common\MicrosoftFlightSimulator\SimConnect.dll`).
4. `C:\MSFS SDK\SimConnect SDK\lib\SimConnect.dll`.

If MSFS is installed via Steam this is fully automatic. Otherwise, copy `SimConnect.dll` (the 64-bit
one from your sim install) next to the exe, or point `SIMCONNECT_DLL` at it. A 32-bit SimConnect.dll
(e.g. the one bundled with vPilot) will not work.

## Settings

Open **Settings** in the app. Values are saved to
`%AppData%\CustomRadioPanel\appsettings.json`.

| Setting | Meaning |
|---|---|
| Acceleration on/off | Enable fast-spin acceleration |
| Fast / Medium threshold (ms) | How quick a spin counts as fast / medium |
| Fast / Medium multiplier | Step multiplier at those speeds |
| Global step scale | Scales every step up/down |
| Long-press (ms) | ACT/STBY long-press threshold |
| 8.33 kHz COM spacing | Inner COM step = 5 kHz instead of 25 kHz |
| Hide leading digit | `123.405 → 23.405` (recommended for VATSIM) |

## Build from source

Requires the **.NET 10 SDK** and the `maui-windows` workload (`dotnet workload install maui-windows`).

```bash
dotnet test                                    # unit tests
dotnet build  CustomRadioPanel.App             # build
dotnet run    --project CustomRadioPanel.App   # run
```

Produce a self-contained release build:

```bash
dotnet publish CustomRadioPanel.App -c Release -r win-x64 --self-contained true \
  -p:WindowsPackageType=None -o publish
```

The output folder contains `CustomRadioPanel.App.exe` and everything it needs (the .NET runtime is
bundled). Nothing from `libs/` is required or shipped.

## How it works

| Project | Role |
|---|---|
| `CustomRadioPanel.Core` | HID layer (HidSharp), SimConnect (direct P/Invoke), bridge logic + encoder acceleration, config. |
| `CustomRadioPanel.App`  | MAUI Blazor Hybrid desktop app; hosts the always-on bridge. |
| `CustomRadioPanel.Tests`| xUnit tests for the display encoder, input decoder and formatting. |

- **HID**: 3-byte input reports decode to selector / knob / button events; the two displays are a
  22-byte HID feature report. Protocol per [bjanders/fpanels](https://github.com/bjanders/fpanels).
- **SimConnect**: called via P/Invoke against the native DLL (the SDK's managed wrapper is a
  .NET-Framework mixed-mode assembly .NET 10 can't load). Frequencies are set with the `*_SET_HZ`
  key events (SimVar writes are ignored by MSFS).

## Reporting issues

Found a bug? Please [open an issue](../../issues) and **attach the log file** — it makes diagnosis far
easier. The app writes rolling logs to:

```
%AppData%\CustomRadioPanel\logs\radiopanel-<date>.log
```

The quickest way there: **Settings → Logs → Open logs folder** in the app. Attach the newest
`radiopanel-*.log` and say what you were doing, which aircraft, and whether the panel and MSFS were connected.

## License

MIT — see [LICENSE](LICENSE). Not affiliated with Microsoft, Logitech or Saitek. "SimConnect" is a
Microsoft component and is not distributed with this project.
