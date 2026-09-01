# Gnomon Windows agent

The same WPF executable has two modes:

- no arguments: visible per-user tracker, extension listener, HA connection, and tray UI;
- `--service`: LocalSystem watchdog which ensures the worker exists in the active console session.

The tracker uses an out-of-context foreground WinEvent hook; the one-second timer
only evaluates activity state and never polls the foreground window. It cannot
block apps or hide itself.

The per-process Core Audio fallback uses the narrow `NAudio.Wasapi` package because
NAudio 2.2.1 places `MMDeviceEnumerator` and audio-session APIs there; the broader
`NAudio` package is not referenced.

## Build and test

Install the .NET 8 SDK and WiX v4, then run:

```powershell
dotnet test windows/Gnomon.sln
dotnet publish windows/src/Gnomon.Agent/Gnomon.Agent.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
dotnet build windows/installer/Gnomon.Installer.wixproj -c Release
```

Configuration lives at `%ProgramData%\Gnomon\config.json`; cached rules and logs
remain under the same directory. The worker exits immediately in any session
whose user does not match `windowsUser`.

Run `Gnomon.Agent.exe --configure` to open the guided setup window. A bare
`homeassistant.local` address is normalized to
`ws://homeassistant.local:8123/api/websocket`.
