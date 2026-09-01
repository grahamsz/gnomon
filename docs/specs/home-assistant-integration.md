# Spec: Gnomon — Home Assistant Custom Integration

**Product name:** Gnomon — after the sundial's shadow-casting blade (Greek *gnōmōn*, "the one who indicates"). The name encodes the product's stance: it doesn't block the sun, it makes the time plain to see.

**Component:** `gnomon` custom integration for Home Assistant (Python)
**Phase:** 0.1 — measurement and visibility only. **No enforcement of any kind.**
**Audience:** coding agent. This document is self-contained; implement exactly what is specified here.

---

## 1. Purpose

This integration is the **single source of truth** for a transparent, multi-kid screen time system. Device agents (Windows, Android — separate codebases, separate specs) track foreground app usage, classify it into categories using a rules map owned by this integration, and report minute deltas back. The integration owns:

- All accounting (usage totals per kid / device / category)
- The rules map (process-name and domain → category mappings)
- Stable category, child-wide, and device-wide usage/limit rollups
- All entities, services, and events that parents use in dashboards and automations

**Explicit non-goals for 0.1:** blocking signals that agents act on, lockdown/curfew switches, pause switches, per-kid UI panels, HACS packaging. Block-flag entities are created (computed state only) so parents can prototype automations, but no agent behavior depends on them in 0.1.

## 2. Tech constraints

- Python 3.12+, targets Home Assistant Core 2025.x+
- Standard HACS custom integration layout in `custom_components/gnomon/`
- Use `homeassistant.helpers.storage.Store` for persistence
- Use config entries (UI-based config flow); no YAML configuration
- Async throughout; no blocking I/O in the event loop
- Translations file (`strings.json` + `translations/en.json`) for config flow

### File layout

```
custom_components/gnomon/
├── __init__.py          # setup, service registration, storage load
├── manifest.json        # domain: "gnomon", iot_class: "calculated"
├── const.py             # all constants, entity ID helpers
├── config_flow.py       # UI config flow + options flow
├── coordinator.py       # central state: aggregate usage, limits, rules; all mutation here
├── models.py            # dataclasses: Kid, Category, RulesMap
├── sensor.py            # usage sensors, rules version sensor
├── number.py            # per-kid per-category limit numbers
├── binary_sensor.py     # exhausted flags (advisory), agent-connected flags
├── services.py          # service handlers incl. response support
├── strings.json
└── translations/en.json
```

## 3. Domain model

**Identifiers:** `kid`, `device`, `category` are lowercase slugs matching `^[a-z0-9_]+$`. Config flow validates and slugifies.

**Category** (defined globally, limits are per kid):
- `id`, display `name`
- `idle_timeout_min` (int, default 3) — agents stop counting after this much input inactivity
- `media_counts_as_active` (bool) — whether media playback overrides input idleness

The category `unclassified` **always exists** (created by the integration, cannot be deleted). Agents bill unknown apps/domains to it.

**Kid:** `id`, display `name`. Kids own category and overall limit numbers and usage totals.

**Device:** `id`, display `name`, belongs to exactly one kid. Devices are registered implicitly the first time an agent reports with a new `(kid, device)` pair (config flow pre-declares kids only).

**RulesMap:**
- `version`: int, starts at 1, increments on every mutation
- `processes`: dict of lowercase process name (no path, e.g. `fortniteclient-win64-shipping.exe`) → category id
- `domains`: dict of lowercase hostname (e.g. `youtube.com`) → category id; suffix matching is the agents' job, the map stores base domains only
- `overrides`: optional per-kid dicts of the same shape, which take precedence over the global map for that kid

Raw process/package/domain activity is deliberately absent from this domain model. Each agent keeps its own local activity catalog.

## 4. Entities

All entities are created dynamically per kid/device/category from coordinator state. `unique_id`s must be stable: `gnomon_{kind}_{kid}_{device}_{category}` etc.

| Entity | Pattern | Notes |
|---|---|---|
| Usage (total) | `sensor.gnomon_used_{kid}_{category}` | sum across the kid's devices; computed in coordinator |
| Limit | `number.gnomon_limit_{kid}_{category}` | 0–720 min, step 5; stored per kid/category |
| Exhausted flag | `binary_sensor.gnomon_exhausted_{kid}_{category}` | `on` when total usage ≥ limit. **Advisory only in 0.1.** |
| Child usage / limit | `sensor.gnomon_used_{kid}_total`, `number.gnomon_limit_{kid}_total` | Overall daily allowance across every device; independent of category budgets |
| Device usage / limit | `sensor.gnomon_used_{kid}_{device}_total`, `number.gnomon_limit_{kid}_{device}_total` | Overall daily allowance for one device |
| Overall exhausted flags | `binary_sensor.gnomon_exhausted_{kid}_total`, `binary_sensor.gnomon_exhausted_{kid}_{device}_total` | Advisory child/device allowance signals |
| Rules version | `sensor.gnomon_rules_version` | int; agents watch this to invalidate their cache |
| Agent connected | `binary_sensor.gnomon_agent_{kid}_{device}` | `on` when a heartbeat was seen within 15 min; use `async_track_point_in_time`, no polling |

Entity `device_info`: group entities under a HA device per kid (`name`: kid display name) and per agent device (child device, `via_device` the kid). This gives clean Areas/labels usage.

## 5. Services

Register under domain `gnomon`. Agents authenticate with long-lived access tokens and call these over the WebSocket API (primary) or REST (fallback for fire-and-forget calls).

### `gnomon.report_usage`
```yaml
kid: string (required)
device: string (required)
category: string (required, must exist; unknown category → log warning, bill to "unclassified")
minutes: int (required, 1–30; delta since last report)
```
No response. Increments only aggregate usage, totals, and exhausted flags, then refreshes agent-connected state. It never receives an app, package, or domain identifier. Category, child, and device limit transitions fire `gnomon_limit_reached` with their scope and aggregate values.

### `gnomon.report_unknown`
```yaml
kid: string (required)
device: string (required)
kind: string (required: "process" | "domain")
id: string (required; process name or hostname, lowercase)
hint: string (optional, ≤120 chars)
```
Compatibility no-op for pre-local-catalog agents. It creates no state, entity, event, or notification.

### `gnomon.get_rules`
No parameters. **Supports response** (`supports_response = SupportsResponse.ONLY`). Response:
```json
{
  "version": 7,
  "categories": [
    {"id": "games", "name": "Games", "idle_timeout_min": 3, "media_counts_as_active": false}
  ],
  "processes": {"fortniteclient-win64-shipping.exe": "games"},
  "domains": {"youtube.com": "video"},
  "overrides": {"alex": {"processes": {}, "domains": {"khanacademy.org": "schoolwork"}}}
}
```

### `gnomon.get_status`

Accepts `{kid, device}` and returns the stable aggregate categories plus child-wide and requested-device allowances. Agents use this response instead of guessing editable HA entity IDs.

### `gnomon.heartbeat`
```yaml
kid: string (required)
device: string (required)
agent_version: string (optional)
```
No response. Marks `binary_sensor.gnomon_agent_{kid}_{device}` on and (re)arms the 15-minute staleness timer.

### `gnomon.get_classifications` / `gnomon.set_classification`

`get_classifications` is a compatibility response with categories and an empty item list. `set_classification` accepts `{kid, kind, id, category}`, writes a kid-specific override, bumps the rules version, and returns the refreshed rules document. Agent workbenches supply their own local activity lists.

### `gnomon.reset`
```yaml
kid: string (required)
category: string (optional; omit = all categories)
```
No response. Zeroes the matching usage sensors (all devices) and re-evaluates exhausted flags. Fires event `gnomon_reset` with `{kid, category|null}`. **Reset scheduling is the parent's job via a normal HA time automation** — ship an example in `README.md`:

```yaml
automation:
  - alias: "Reset Alex screen time"
    trigger: [{platform: time, at: "06:00:00"}]
    action:
      - action: gnomon.reset
        data: {kid: alex}
```

## 6. Rules map management (options flow)

The options flow provides structural editing (rare operations):

- **Categories:** add/rename/remove (removing a category with usage refuses with an error; removing requires no active mappings)
- **Limits:** editable here too, but the primary surface is the `number` entities
- **Rules:** list/add/remove process and domain mappings, global and per-kid override; validate category references; lowercase everything
- **Agent workbenches:** Windows and Android edit kid-specific mappings through the authenticated classification service; HA remains the rule source of truth
- Every mutation: `version += 1`, persist, update `sensor.gnomon_rules_version`

Seed data: ship a built-in seed map (constant, ~40 entries) covering common games (`robloxplayerbeta.exe`, `javaw.exe`, `fortniteclient-win64-shipping.exe`, `minecraft*.exe`…), office apps (`winword.exe`, `excel.exe`, `powerpnt.exe` → `schoolwork`), and major domains (`youtube.com`, `netflix.com`, `tiktok.com`, `discord.com`, `roblox.com`, `classroom.google.com`, `docs.google.com`…). Applied on first setup only.

## 7. Local classification flow

1. Each agent records only the apps/domains it observes in private local storage.
2. The parent opens that device's admin-locked workbench and selects a bucket.
3. The agent sends only that explicit mapping to `set_classification`; HA bumps and distributes the rules document.
4. Minutes already accrued stay in their original bucket; classification is not retroactive.

## 8. Shared protocol reference (binding for all agents)

- **Transport:** HA WebSocket API `/api/websocket`, long-lived access token auth. Message flow: `auth_required` → `{"type":"auth","access_token":...}` → `auth_ok` → commands with incrementing `id`.
- **Report:** `{"id":N,"type":"call_service","domain":"gnomon","service":"report_usage","service_data":{...}}`
- **Fetch rules:** `call_service` on `gnomon.get_rules` with `"return_response": true`; cache the response keyed by `version`.
- **Invalidate:** subscribe to the custom `gnomon_changed` event. `kind=rules` refetches rules; `kind=status` refetches the compact aggregate status document. This never depends on user-editable entity IDs.
- **REST:** `POST /api/services/gnomon/{service}?return_response` with `Authorization: Bearer` supports the Android admin workbench; tracking continues to use WebSocket.
- Agents send integer minute **deltas**, not cumulative totals. Integration owns all accumulation.

## 9. Error handling & edge cases

- Unknown kid in any service call → log error, ignore (no implicit kid creation; kids are config-flow only)
- Unknown device → create implicitly under the kid (see §3)
- `minutes` outside 1–30 → clamp with warning
- Restart: aggregate usage, rules, and limits are restored from storage; agent-connected flags start `off`
- Clock crossing midnight does **nothing** implicitly — reset is automation-driven

## 10. Acceptance criteria

1. Config flow creates a kid; entities appear grouped under a device for that kid
2. `report_usage` over WS increments the kid category total, child total, and reporting-device total
3. Category, child, and device exhausted sensors flip at their limits and fire `gnomon_limit_reached`
4. `get_rules` returns valid schema with monotonically increasing `version` after any edit
5. No process/package/domain creates an HA entity; a local assignment updates the map and bumps its version
6. Heartbeat keeps agent flag on; 15 min silence flips it off
7. Full state survives HA restart
8. Example reset automation from README works unmodified
9. `pytest` + `pytest-homeassistant-custom-component` tests cover services, aggregate limits, and restore

## 11. 0.2 seams (design for, do not build)

- Lockdown/pause `switch` entities per kid
- Enforcement events the agents subscribe to (`binary_sensor` state is already there)
- `gnomon.add_bonus_time` service
