# Spec: Gnomon — Android Agent (Kotlin)

**Component:** Android tracking agent (foreground app usage, classification, reporting)
**Phase:** v1 — measurement and visibility only. **No enforcement of any kind: no overlays, no blocking, no package suspension.**
**Audience:** coding agent. This document is self-contained; implement exactly what is specified here.

---

## 1. Purpose

An Android app that observes which app is actively in use on the device, classifies it into a screen-time category using a rules map fetched from Home Assistant (HA), and reports usage in integer-minute deltas to the HA `gnomon` integration. One device belongs to one kid. The app shows the kid a live, read-only view of the same numbers the parent sees — it is a mirror, not a monitor.

The Home Assistant integration is specified in a companion document (`home-assistant-integration.md`); the binding contract is reproduced in §8 of this spec and takes precedence if anything here appears to conflict.

**Explicit non-goals for v1:** AccessibilityService blocking, overlay windows, `setPackagesSuspended`, device-owner provisioning automation, kiosk features. v1 *may* include device-owner **detection** (see §9) but must not act on it.

## 2. Tech constraints

- Kotlin, minSdk 26, targetSdk latest
- Jetpack Compose for UI; Material 3
- `androidx.work.WorkManager` for watchdog/scheduling
- OkHttp (WebSocket) + kotlinx.serialization for HA protocol
- Room (or DataStore-JSON, your call — justify in code) for the rules cache and pending-delta queue
- Foreground service with `specialUse`/`dataSync` type as appropriate; persistent notification
- No third-party analytics, no crash reporting SDKs, no network calls to anywhere except the configured HA instance

## 3. Onboarding & permissions

First-run flow must guide the user through, with explanations of *why* (this app is kid-visible; the copy matters):

1. **Usage Access** (`PACKAGE_USAGE_STATS`) — required for foreground detection; deep-link via `Settings.ACTION_USAGE_ACCESS_SETTINGS`
2. **Battery optimization exemption** — `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`; explain that aggressive OEM power management kills tracking
3. **Notification permission** (API 33+) — for the persistent foreground-service notification, which is also the kid's status glance
4. Configuration: HA URL, long-lived token, kid id, device id (suggest a default like `phone`; editable), with a "test connection" button that performs a `get_rules` round-trip

A status screen shows each permission green/red with a fix button — this screen doubles as the tamper-visibility surface in v1: if usage access is revoked, the app says so openly and reports degraded state on its next heartbeat (see §9).

## 4. Tracking core

### 4.1 Foreground detection
- Primary: `UsageStatsManager.queryEvents()` on a 15 s poll (WorkManager periodic + foreground-service loop while screen is on), diffing `MOVE_TO_FOREGROUND` / `MOVE_TO_BACKGROUND` (and `ACTIVITY_RESUMED`/`ACTIVITY_PAUSED` on API 29+) to maintain current-foreground package state
- Screen state: `ACTION_SCREEN_ON/OFF`, `ACTION_USER_PRESENT` receivers; screen off = never count
- Doze/standby: do not attempt to defeat Doze for tracking; on wake, replay `queryEvents()` across the gap so usage during brief doze windows is still attributed correctly. Gaps with no events (device truly off) produce no usage.

### 4.2 Activity signals
Android has no meaningful global input-idle API; use:
- **Screen on + app foreground** = base counting condition
- **Media signal:** `MediaSessionManager.getActiveSessions()` (requires notification listener permission — **request optionally, not required**) and `AudioManager.isMusicActive()` as a permission-free fallback. Rationale per category: `media_counts_as_active` governs whether an app that is foreground-but-possibly-idle (video apps) keeps counting. Without the media signal, count foreground-while-screen-on time for all categories (documented v1 simplification on Android).
- v1 Android counting rule, explicit: `counting = screen_on AND app_foreground AND app_mapped`

### 4.3 Delta accounting
Accumulate active seconds against the current category; flush whole minutes via `report_usage` on foreground change and every 60 s while counting. Keep fractional remainder. Queue offline (§6).

## 5. Classification

1. Package name → rules map lookup. The map's `processes` dict keys map naturally to Android package names (`com.google.android.youtube`, `com.netflix.mediaclient`, …). The HA integration's seed data includes common packages; agents should also handle exact match failures by trying the last two package segments progressively is **not** allowed — exact match only, unknowns go to `unclassified` (keeps the map honest and the triage loop fed).
2. `domains` map is unused on Android in v1 (no browser extension on Android in v1).
3. Unknown → category `unclassified` + one `report_unknown` per rules-map version, with `hint` = app label from `PackageManager` (e.g. "Brawl Stars"), kind `process`, id = package name.
4. Per-kid `overrides` take precedence over global mappings.

## 6. Reporting & HA connection

- Persistent WebSocket via OkHttp while screen is on or while a flush is pending; allowed to disconnect during long screen-off periods (battery wins; reconnect on wake and flush).
- Reconnect with exponential backoff (5 s → 5 min, jittered).
- On (re)connect: compare cached rules version against `sensor.gnomon_rules_version` (via `get_states`), refetch `gnomon.get_rules` if stale, then `subscribe_events` for its `state_changed`.
- **Offline queue:** Room table of pending deltas; flush FIFO on reconnect; cap 720 rows (drop oldest with a log + status-screen notice).
- Heartbeat: `gnomon.heartbeat` on reconnect and every 5 min while connected, with `{kid, device, agent_version}`. Permission-degraded state is conveyed in v1 only by the absence of usage deltas plus the local status screen — do not extend the heartbeat schema.
- WorkManager watchdog: a periodic (15 min, flex) worker that verifies the foreground service is alive and restarts it if killed — the standard OSS pattern for surviving OEM task killers.

## 7. Kid-visible UI (transparency surface)

Single-activity Compose app:

- **Today:** per-category progress (used / limit / remaining), matching HA's numbers; pull-to-refresh fetches current totals from HA (`GET /api/states/sensor.gnomon_used_{kid}_{category}` — read-only REST is fine for this)
- **Now:** current foreground app, its category, whether it's counting, and why ("screen on, foreground")
- **Unclassified:** the local list of apps currently billing to `unclassified`, with their app labels — the kid can see exactly what's unmapped
- **Status:** HA connection, permission health, rules version, pending-queue depth
- Persistent notification: compact per-category summary (`Games 47/90 · Video 12/30`)
- No stealth mode, no disguised icon, no hidden reporting. Do not add any.

## 8. Shared protocol reference (binding)

- **Transport:** HA WebSocket API `{haUrl}/api/websocket`. Flow: read `auth_required` → send `{"type":"auth","access_token": ...}` → expect `auth_ok` → commands with incrementing integer `id`.
- **Report usage:**
```json
{"id":1,"type":"call_service","domain":"gnomon","service":"report_usage",
 "service_data":{"kid":"alex","device":"phone","category":"games","minutes":2,"app_id":"com.supercell.brawlstars"}}
```
- **Report unknown:**
```json
{"id":2,"type":"call_service","domain":"gnomon","service":"report_unknown",
 "service_data":{"kid":"alex","device":"phone","kind":"process","id":"com.supercell.brawlstars","hint":"Brawl Stars"}}
```
- **Get rules (needs response):**
```json
{"id":3,"type":"call_service","domain":"gnomon","service":"get_rules","return_response":true}
```
Response schema:
```json
{"version":7,
 "categories":[{"id":"games","name":"Games","idle_timeout_min":3,"media_counts_as_active":false}],
 "processes":{"com.supercell.brawlstars":"games"},
 "domains":{"youtube.com":"video"},
 "overrides":{"alex":{"processes":{},"domains":{}}}}
```
- **Heartbeat:** `gnomon.heartbeat` with `{kid, device, agent_version}`.
- **Invalidate:** `subscribe_events` on `state_changed`; client-filter `entity_id == "sensor.gnomon_rules_version"`; on change → refetch rules.
- Deltas are integer minutes; the integration owns all accumulation. Never send cumulative totals. All timestamps UTC; no local midnight logic — reset is HA's job.

## 9. Error handling & edge cases

- HA unreachable → queue deltas, run on cached rules map, surface state in UI
- Token rejected → status screen red, exponential retry (hourly cap), no crash loop
- Usage access revoked mid-run → `queryEvents` returns empty; detect via `UsageStatsManager` configuration check on each poll; show red permission state; keep heartbeating
- OEM task-kill (service destroyed) → WorkManager watchdog restarts; log restart count visibly on status screen
- Device reboot → `BOOT_COMPLETED` receiver restarts the service
- Package uninstalled while it had pending unclassified state → no special handling needed
- Multi-user devices: v1 assumes the app runs in the kid's profile only; document that Android guest/secondary profiles bypass tracking entirely (parent setup responsibility)

## 10. Acceptance criteria

1. Foreground app attribution matches manual observation across 20 app switches including rapid switching
2. Screen off stops counting within one poll interval; wake replays the gap correctly
3. Unknown app appears once in HA triage with its human-readable label
4. Airplane mode 30 min with active usage → zero lost minutes after reconnect; totals in HA match on-device totals
5. Rules edited in HA → app applies new map within seconds while connected
6. Force-stop the app → WorkManager restarts tracking within one periodic window; restart is visible on status screen
7. 24 h soak: HA totals within ±3 min of Android's own Digital Wellbeing numbers for the mapped apps
8. Unit tests (JUnit) for classifier, delta quantizer, offline queue FIFO/cap, protocol codec; instrumented test for usage-stats diffing

## 11. v1.1 seams (design for, do not build)

- `EnforcementController` interface (`onCategoryExhausted(category)`, `onLockdown(state)`) with a no-op v1 implementation, subscription to `binary_sensor.gnomon_exhausted_*`/`switch` states behind a default-off flag
- AccessibilityService manifest scaffolding (declared but disabled)
- Device-owner provisioning documentation (`adb shell dpm set-device-owner`) as a markdown file, not code
- `setPackagesSuspended` enforcement path behind the same controller
