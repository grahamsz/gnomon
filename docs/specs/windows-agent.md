# Spec: Gnomon — Windows Agent (C#)

**Component:** Windows background agent (tracking, classification, reporting)
**Phase:** 0.1 — measurement and visibility only. **No enforcement of any kind: no process killing, no overlays, no session locking.**
**Audience:** coding agent. This document is self-contained; implement exactly what is specified here.

---

## 1. Purpose

A Windows service that observes which application is actively being used, classifies it into a screen-time category using a rules map fetched from Home Assistant (HA), and reports usage in integer-minute deltas to the HA `gnomon` integration. It runs on a standard (non-admin) kid's PC, reports for a specific Windows user account mapped to a kid, and shows the kid a live, read-only view of the same numbers the parent sees.

The Home Assistant integration is specified in a companion document (`home-assistant-integration.md`); the binding contract is reproduced in §8 of this spec and takes precedence if anything here appears to conflict.

**Explicit non-goals for 0.1:** enforcement (killing, closing, overlaying, locking), anti-tamper policy lockdowns, browser-page blocking, commercial code signing (self-signed documented; see §11).

## 2. Tech constraints

- .NET Framework 4.8, compiled with C# 12
- Lightweight `ServiceBase` watchdog and ordinary cancellation-controlled tasks; no generic host
- Win32 interop via `CsWin32` (source-generated P/Invoke) — no hand-written signatures
- GSMTC media-session detection via the Windows SDK contracts plus a foreground audio-session fallback via the narrow NAudio WASAPI packages
- WebSocket client: `System.Net.WebSockets.ClientWebSocket` + `System.Text.Json`
- Tray/status UI: WinForms, small, read-only
- Serilog to rolling file under `%ProgramData%\Gnomon\logs`
- Installer: WiX Toolset v4+ producing a per-machine MSI (see §11)
- Config: `%ProgramData%\Gnomon\config.json`, documented schema, hot-reload not required; seeded by the installer, never overwritten on upgrade

### Architecture: session worker + watchdog service

**Technical constraint that dictates the design:** Windows services run in Session 0 and **cannot** hook a user session's foreground-window events, read its input-idle state, or enumerate its audio sessions. Tracking must live in the kid's session; the service is a watchdog, not the tracker.

- **Session worker:** `Gnomon.Agent.exe` (default mode, runs as the logged-in user, no console). Owns: tracking core, classifier, activity state machine, extension HTTP listener, HA connection, delta queue, and the WinForms tray UI (§7). Autostarted via HKLM Run key (§11); no-ops unless the session user matches `windowsUser`.
- **Watchdog service:** `Gnomon.Agent.exe --service`, runs as **LocalSystem** via `ServiceBase`. One job: ensure the session worker is running in the configured user's session (60 s check; relaunch via `WTSGetActiveConsoleSessionId` + `CreateProcessAsUser`). No tracking, no HA connection.
- Killing the worker stops tracking, but the watchdog relaunches it within 60 s — sufficient for 0.1 (hard anti-tamper is explicitly out of scope).
- One framework-dependent executable serves both modes. The MSI requires .NET Framework 4.8 or later, which is included on current Windows versions.

```json
{
  "haUrl": "ws://homeassistant.local:8123/api/websocket",
  "haToken": "<long-lived access token>",
  "kid": "alex",
  "device": "pc",
  "windowsUser": "Alex",
  "extensionPort": 45981
}
```

## 3. Tracking core

### 3.1 Foreground detection
- Use `SetWinEventHook` (`EVENT_SYSTEM_FOREGROUND`, out-of-context) — **no polling loop**
- On event: `GetWindowThreadProcessId` → `Process.GetProcessById` → process name, lowercased, no path, e.g. `msedge.exe`
- Also subscribe to `SystemEvents.SessionSwitch` (lock/unlock) and listen for `SC_MONITORPOWER` (display sleep) via a hidden message window

### 3.2 Activity signals
The agent counts time only when the foreground app is **actively used**, per category config:

- **Input signal:** `GetLastInputInfo`; input-idle = `now − lastInput > category.idle_timeout_min` (default 3 min)
- **Media signal (two probes, OR'd):** GSMTC playback state, then NAudio `AudioSessionManager2` for an audio session belonging to the foreground PID with state `Active`. Poll every 5 s.
- **Hard stops:** session locked, display asleep, or screensaver running → never count.

### 3.3 Activity state machine (per tick, 1 s)
```
counting = foreground_app_mapped
           AND session_active
           AND ( NOT input_idle
                 OR (media_playing AND category.media_counts_as_active) )
```
Run a 1 s dispatcher timer that evaluates this and accumulates active seconds against the current (kid, category). On foreground change or 60 s of accumulated active time (whichever first), flush whole minutes as a delta report (see §6). Keep fractional remainder.

## 4. Classification

1. Resolve foreground process name against the rules map `processes` dict (exact, case-insensitive).
2. **Browser exception:** if the process is a known browser (`msedge.exe`, `chrome.exe`, `firefox.exe`, `brave.exe`, `vivaldi.exe`), defer to the domain reported by the browser extension (§5). Resolve hostname against `domains` by suffix match (map stores base domains; `www.youtube.com` matches `youtube.com`).
3. If no mapping (or browser with no extension heartbeat in the last 60 s): category = `unclassified`, and report the item once per rules-map version via `gnomon.report_unknown` (see §8) with a `hint` from the exe's `FileDescription`/`CompanyName` metadata.
4. Per-kid `overrides` in the rules map take precedence over global mappings.

Unknowns already reported are cached in a local set keyed `(kind, id, rulesVersion)`; a rules version bump clears the set so newly-classified items that re-appear unknown (e.g. map rollback) are re-reported.

## 5. Browser extension companion

Ship a minimal Manifest V3 extension (Chrome/Edge) in `windows/browser-extension/` plus install docs.

**Agent side:** host an HTTP listener on `127.0.0.1:{extensionPort}`:
- `POST /active-domain` with JSON `{"domain": "youtube.com"}` — update current-domain state
- `GET /status` → `{"agent":"up","rulesVersion":7}` — extension health check

**Extension side:**
- On tab activation, tab URL change, window focus change, and a 15 s heartbeat: send active-tab **hostname only** to the agent. Never send full URLs, paths, titles, or history. This is a hard privacy requirement, test it.
- Extension popup shows: currently reported domain, its category, and agent reachability.
- Track `lastSeen` timestamp of extension traffic; browser with stale extension (>60 s) falls back to `unclassified` per §4.3.

## 6. Reporting & HA connection

- Single persistent WebSocket to HA (see §8 for the protocol). Reconnect with exponential backoff (5 s → 5 min cap, jittered).
- On (re)connect: fetch all states is **not** needed; instead call `gnomon.get_rules` if cached version ≠ current `sensor.gnomon_rules_version` (fetch that entity via `get_states` once), then `subscribe_events` for `state_changed` of `sensor.gnomon_rules_version`.
- Report minute deltas via `call_service` → `gnomon.report_usage`. Queue deltas in memory while disconnected (cap 720 min); on reconnect, flush in order.
- Heartbeat: `gnomon.heartbeat` every 5 min and on reconnect.
- Rules map cache: persist to `%ProgramData%\Gnomon\rules-cache.json`; agent is fully functional offline with a stale map.
- All timestamps UTC; no local midnight logic anywhere — reset is HA's job.

## 7. Kid-visible status UI

WinForms tray UI hosted in the session worker (§2):

- Tray icon tooltip: `Games 47/90 min · Video 12/30 min`
- Window (open from tray): table of categories with used/limit/remaining, current foreground app and its live classification, extension status, HA connection status, and a read-only view of the local unclassified list ("these currently count as unclassified")
- Data source: local state only — the UI must not add load to HA
- **No hidden mode.** The app is visible by design; do not implement stealth options.

## 8. Shared protocol reference (binding)

- **Transport:** HA WebSocket API `{haUrl}` (default `/api/websocket`). Flow: read `auth_required` → send `{"type":"auth","access_token": haToken}` → expect `auth_ok` → send commands with incrementing integer `id`.
- **Report usage:**
```json
{"id":1,"type":"call_service","domain":"gnomon","service":"report_usage",
 "service_data":{"kid":"alex","device":"pc","category":"games","minutes":3,"app_id":"fortniteclient-win64-shipping.exe"}}
```
- **Report unknown:**
```json
{"id":2,"type":"call_service","domain":"gnomon","service":"report_unknown",
 "service_data":{"kid":"alex","device":"pc","kind":"process","id":"newgame.exe","hint":"New Game by Studio"}}
```
- **Get rules (needs response):**
```json
{"id":3,"type":"call_service","domain":"gnomon","service":"get_rules","return_response":true}
```
Response schema:
```json
{"version":7,
 "categories":[{"id":"games","name":"Games","idle_timeout_min":3,"media_counts_as_active":false}],
 "processes":{"fortniteclient-win64-shipping.exe":"games"},
 "domains":{"youtube.com":"video"},
 "overrides":{"alex":{"processes":{},"domains":{"khanacademy.org":"schoolwork"}}}}
```
- **Heartbeat:** `gnomon.heartbeat` with `{kid, device, agent_version}`.
- **Invalidate:** `subscribe_events` on `state_changed`; client-filter `entity_id == "sensor.gnomon_rules_version"`; on change → refetch rules.
- Deltas are integer minutes; the integration owns all accumulation. Never send cumulative totals.

## 9. Error handling & edge cases

- HA unreachable at startup → run on cached rules, queue deltas, retry per backoff
- Token rejected (`auth_invalid`) → log loudly, show red state in tray, retry hourly (do not tight-loop)
- Browser with no extension → `unclassified` billing, visible in UI
- User switches Windows account → only track when `Environment.UserName` matches `windowsUser`; otherwise idle
- Handle process access failures (protected processes) by falling back to window title for display only — never parse titles for classification
- Machine sleep/shutdown: flush pending deltas best-effort on `SessionEnding`

## 10. Acceptance criteria

1. Foreground changes are event-driven (verify: <1% CPU idle, no polling)
2. Fortnite lobby with no input for 3 min stops counting; a foreground player with an active audio session keeps counting when its category enables media activity; both verifiable in logs
3. `youtube.com` in Edge bills `video`; `docs.google.com` bills `schoolwork`; killing the extension bills the browser as `unclassified` within 60 s
4. Unknown exe appears once in HA triage, with file-description hint
5. Kill network for 10 min with active usage → deltas queued and flushed on restore; no loss, no duplication
6. Rules edited in HA → agent picks up new map within ~5 s without restart
7. Full day soak: HA totals within ±2 min of a manual stopwatch log
8. `dotnet test` suite covers classifier, state machine, delta quantizer, WS protocol codec (recorded fixtures)
9. MSI installs silently on a clean Windows 11 VM: service present and running, worker autostarts at user logon, `config.json` seeded, .NET Framework 4.8 prerequisite detected, no SmartScreen-blocking errors beyond the documented unsigned-build warning
10. Upgrade over an existing install preserves `config.json` and `rules-cache.json` and restarts the service; downgrade is blocked with a clear message
11. Uninstall removes service, binaries, and Run key, leaves `%ProgramData%\Gnomon\` intact; reinstall picks the prior config up unchanged
12. Kill the session worker as the kid's standard user → watchdog relaunches it within 60 s; attempts to stop the `GnomonAgent` service as that user are denied by the OS

## 11. Packaging: MSI installer (WiX)

A proper MSI is a 0.1 deliverable. Use **WiX Toolset v4+** with the `WixToolset.Sdk` MSBuild project at `windows/installer/Gnomon.Installer.wixproj`; a Release build produces `GnomonAgent-{version}-x64.msi`.

### 11.1 Package identity
- Per-machine, x64: `InstallScope="perMachine"`, `InstallPrivileges="elevated"` (admin required — the parent installs it)
- Product name "Gnomon Agent", stable `UpgradeCode` GUID, version from CI (`Major.Minor.Build`)
- `MajorUpgrade` with a `DowngradeErrorMessage`; same-version reinstall allowed for repair

### 11.2 Input & layout
- Input is the framework-dependent .NET Framework 4.8 publish: `dotnet publish -c Release -p:DebugType=None -p:DebugSymbols=false`. CI enforces a 10 MiB maximum for both the publish directory and MSI.
- `ProgramFiles64Folder\Gnomon\Gnomon.Agent.exe` plus its small managed dependency set
- `%CommonAppDataFolder%\Gnomon\` seeded with a template `config.json` (placeholders, commented) as a component with `NeverOverwrite="yes"` — **upgrades must never clobber configuration**
- `%CommonAppDataFolder%\Gnomon\logs\` created with an ACL granting `BUILTIN\Users` Modify — the session worker runs as the kid (standard user) and must write logs

### 11.3 Service & autostart components
- Watchdog service via `ServiceInstall`: Name `GnomonAgent`, DisplayName "Gnomon Screen Time Agent", Start `auto`, Account `LocalSystem`; `ServiceConfig` failure actions restart at 5 s / 5 s / 30 s with 24 h reset; `ServiceControl` starts it on install, stops+removes on uninstall
- Session worker autostart via registry: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`, value `GnomonAgent` = `"[INSTALLDIR]Gnomon.Agent.exe"` (starts for every interactive user; the worker itself no-ops unless the session user matches `windowsUser`)

### 11.4 Install/uninstall behavior
- Silent install must work: `msiexec /i GnomonAgent-x.y.z-x64.msi /qn /l*v install.log`
- Interactive install ends by launching an elevated, visible configuration window
  that collects HA address, token, kid, device, and Windows user, writes
  `config.json`, and restarts the service. Default the address field to
  `homeassistant.local` and normalize it to
  `ws://homeassistant.local:8123/api/websocket`. Silent deployment installs the
  template without launching UI; the parent then runs `Gnomon.Agent.exe --configure`.
- Uninstall: stop and remove the service, remove binaries and the Run key; **leave** `%ProgramData%\Gnomon\` (config, cache, logs) intact; never touch HA-side data
- Code signing: optional `signtool` post-build step gated on a `$(SignCertificate)` property; document the self-signed dev flow and the expected SmartScreen warning for unsigned builds

### 11.5 Out of the MSI's scope
- The MSI installs the browser companion files and exposes a guided setup from the tray menu. Chrome and Edge still require the user to approve the unpacked extension in Developer mode for 0.1; Chrome Web Store / Edge Add-ons publishing is the 0.2 path (which would then allow managed installation via browser policy).

## 12. 0.2 seams (design for, do not build)

- An `IEnforcementAdapter` interface (`OnCategoryExhausted(category)`, `OnLockdown(state)`) with a no-op 0.1 implementation; wire the subscription to `binary_sensor.gnomon_exhausted_*` events behind a config flag that defaults off. 0.2 enforcement (process kills, `LockWorkStation`) can run in the session worker — same-user process control needs no elevation.
- Toast notification helper (warning path) — may exist as dead code behind the same flag
- Policy lockdowns for the kid account (DisableTaskMgr, AppLocker rules) as an optional second MSI feature/components group
