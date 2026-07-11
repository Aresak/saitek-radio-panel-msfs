# Native SimConnect.dll

To enable the live MSFS connection, this folder needs **one** file:

- `SimConnect.dll` — the **native, 64-bit** SimConnect library.

The build detects it automatically (`HAS_SIMCONNECT` is defined), compiles the real
`SimConnectService` (which P/Invokes it directly), and copies it next to the app. Without it the app
still builds and runs — the HID panel works and the sim link is a no-op (`NullSimConnectService`).

## Why only the native DLL?

We do **not** use the SDK's managed `Microsoft.FlightSimulator.SimConnect.dll`: it is a
.NET-Framework mixed-mode (C++/CLI) assembly and .NET 10 refuses to load it
("assembly architecture is not compatible"). Talking to the native C API directly avoids that
entirely and is fully forward-compatible.

## Where to get the x64 SimConnect.dll

It ships **with MSFS 2020 itself** (no SDK install needed). For a Steam install it is at:

```
<Steam>\steamapps\common\MicrosoftFlightSimulator\SimConnect.dll
```

Copy that file here. **Must be the 64-bit build** — a 32-bit `SimConnect.dll` (e.g. the one bundled
with vPilot) will fail to load in this 64-bit app.
