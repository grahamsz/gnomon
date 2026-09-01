# Gnomon Windows agent

The same .NET Framework 4.8 WinForms executable has two modes:

- no arguments: visible per-user tracker, extension listener, HA connection, and tray UI;
- `--service`: LocalSystem watchdog which ensures the worker exists in the active console session.

The tracker uses an out-of-context foreground WinEvent hook; the one-second timer
only evaluates activity state and never polls the foreground window. It cannot
block apps or hide itself.

The per-process Core Audio fallback uses the narrow `NAudio.Wasapi` package because
NAudio 2.2.1 places `MMDeviceEnumerator` and audio-session APIs there; the broader
`NAudio` package is not referenced.

## Build and test

Install the .NET 8 SDK (used as the compiler), the .NET Framework 4.8 targeting pack,
and WiX v4+, then run:

```powershell
dotnet test windows/Gnomon.sln
dotnet publish windows/src/Gnomon.Agent/Gnomon.Agent.csproj -c Release -p:DebugType=None -p:DebugSymbols=false
dotnet build windows/installer/Gnomon.Installer.wixproj -c Release
```

Configuration lives at `%ProgramData%\Gnomon\config.json`; cached rules and logs
remain under the same directory. The worker exits immediately in any session
whose user does not match `windowsUser`.

Run `Gnomon.Agent.exe --configure` to open the guided setup window. A bare
`homeassistant.local` address is normalized to
`ws://homeassistant.local:8123/api/websocket`.

Because configuration is elevated, its **Classifications** button is the Windows
admin boundary. The workbench shows only this PC's usage-ranked apps and websites.
Home Assistant persists and syncs the selected rule, not the local activity list.

The MSI also installs the Chrome companion files under `Browser Companion`.
Choose **Set up Chrome companion** from the Gnomon tray menu to open Chrome's
extensions page and the exact folder to select with **Load unpacked**.
