# Gnomon

Gnomon is a transparent, measurement-only screen-time system. Home Assistant is
the source of truth; visible Windows and Android agents classify foreground use
and report integer-minute deltas. The 0.1 series deliberately contains no blocking,
locking, overlays, stealth mode, or process control.

Parents can classify usage from either agent: Windows exposes an elevated
classification workbench in **Configure**, while Android keeps connection and
classification controls behind a local parent PIN. Each workbench lists only
activity observed locally. Home Assistant receives and distributes the selected
kid-specific rules, never the device's app or browsing catalog.

Daily category budgets are independent from two overall allowances: one across
all of a child's devices and one for each device. The visible agent view leads
with the tighter remaining overall allowance, then shows every category's time left.

## Repository layout

- `custom_components/gnomon/` — HACS-compliant Home Assistant integration source
- `home-assistant/` — Home Assistant tests and development documentation
- `android/` — Kotlin/Compose Android agent (minSdk 26)
- `windows/` — .NET Framework 4.8 agent, browser companion, and WiX installer
- `docs/specs/` — binding product specifications

Each component has its own README with setup and build instructions. The binding
product specifications live under `docs/specs/`.

## Downloads and release channels

GitHub Actions builds Home Assistant, Windows, and Android together. Every push
to `main` refreshes the **dev** prerelease; tags such as `v0.1.0` create a
versioned **production** release. Windows artifacts include an MSI, portable ZIP,
and browser companion. Android releases include an APK and, for signed builds,
an AAB.

Development binaries are attached to the
[`dev` GitHub prerelease](https://github.com/grahamsz/gnomon/releases/tag/dev),
not committed into the source tree:

- [Windows installer](https://github.com/grahamsz/gnomon/releases/download/dev/GnomonAgent-dev-win-x64.msi)
- [Windows portable ZIP](https://github.com/grahamsz/gnomon/releases/download/dev/GnomonAgent-dev-win-x64.zip)
- [Android APK](https://github.com/grahamsz/gnomon/releases/download/dev/Gnomon-dev-android.apk)
- [Browser companion](https://github.com/grahamsz/gnomon/releases/download/dev/GnomonBrowserCompanion-dev.zip)
- [Home Assistant package (`gnomon.zip` for HACS)](https://github.com/grahamsz/gnomon/releases/download/dev/gnomon.zip)

See [all releases](https://github.com/grahamsz/gnomon/releases) for immutable
production versions after the first `vX.Y.Z` tag is published.

Production Android releases require repository signing secrets. See
[`docs/releasing.md`](docs/releasing.md) for signing, tagging, and release details.

## Install in Home Assistant with HACS

Add `https://github.com/grahamsz/gnomon` to HACS as a custom **Integration**
repository, download Gnomon from `main` or a production release, restart Home
Assistant, then add **Gnomon** from **Settings → Devices & services**. The complete setup sequence is in
[`docs/releasing.md`](docs/releasing.md#add-gnomon-to-hacs-and-home-assistant).

## Local development environment

The required SDKs are installed under the ignored `.tools/` directory and the
Home Assistant test environment is in `.venv/`. Load them into a PowerShell
session before using the commands in the component READMEs:

```powershell
. .\dev-env.ps1
```

## Privacy and safety contract

Agents send only the configured kid/device identifiers, category, integer-minute
deltas, heartbeats, and parent-selected classification rules. Process/package
identifiers, browser hostnames, app labels, URLs, paths, titles, and history remain
local to the device. Gnomon 0.1 only measures and displays activity.
