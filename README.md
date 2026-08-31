# Gnomon

Gnomon is a transparent, measurement-only screen-time system. Home Assistant is
the source of truth; visible Windows and Android agents classify foreground use
and report integer-minute deltas. Version 1 deliberately contains no blocking,
locking, overlays, stealth mode, or process control.

## Repository layout

- `home-assistant/` — custom integration and its test suite
- `android/` — Kotlin/Compose Android agent (minSdk 26)
- `windows/` — .NET 8 agent, browser companion, and WiX installer
- `docs/specs/` — binding product specifications

Each component has its own README with setup and build instructions. The binding
product specifications live under `docs/specs/`.

## Local development environment

The required SDKs are installed under the ignored `.tools/` directory and the
Home Assistant test environment is in `.venv/`. Load them into a PowerShell
session before using the commands in the component READMEs:

```powershell
. .\dev-env.ps1
```

## Privacy and safety contract

Agents send only the configured kid/device identifiers, category, integer minute
deltas, process/package identifiers, browser hostnames, and optional app labels.
The browser extension never sends URLs, paths, page titles, or history. Gnomon v1
only measures and displays activity.
