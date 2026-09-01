"""Central, persisted Gnomon state. All mutations happen here."""

from __future__ import annotations

from dataclasses import asdict
from datetime import datetime, timedelta, timezone
import logging
from typing import Any

from homeassistant.components import persistent_notification
from homeassistant.core import HomeAssistant, callback
from homeassistant.helpers.dispatcher import async_dispatcher_send
from homeassistant.helpers.event import async_call_later, async_track_point_in_time
from homeassistant.helpers.storage import Store

from .const import (
    AGENT_STALE_MINUTES, DEFAULT_CATEGORIES, DOMAIN, SEED_DOMAINS, SEED_PROCESSES,
    SIGNAL_STATE_CHANGED, SIGNAL_UNKNOWN_REMOVED, STORAGE_KEY, STORAGE_VERSION,
    UNCLASSIFIED, UNKNOWN_NOTIFICATION_HOURS,
)
from .models import Category, Kid, RulesMap, UnknownItem, UsageItem, utc_now_iso

_LOGGER = logging.getLogger(__name__)


class GnomonCoordinator:
    """Own the accounting ledger, rules, limits, devices, and unknown inbox."""

    def __init__(self, hass: HomeAssistant) -> None:
        self.hass = hass
        self.store: Store[dict[str, Any]] = Store(hass, STORAGE_VERSION, STORAGE_KEY)
        self.kids: dict[str, Kid] = {}
        self.rules = RulesMap()
        self.limits: dict[str, dict[str, int]] = {}
        self.usage: dict[str, dict[str, dict[str, int]]] = {}
        self.devices: dict[str, set[str]] = {}
        self.unknowns: dict[str, UnknownItem] = {}
        self.usage_items: dict[str, UsageItem] = {}
        self.agent_versions: dict[tuple[str, str], str] = {}
        self.agent_online: set[tuple[str, str]] = set()
        self._agent_timers: dict[tuple[str, str], Any] = {}
        self._unknown_timer: Any = None

    async def async_load(self, configured_kids: list[dict[str, str]]) -> None:
        stored = await self.store.async_load()
        if stored:
            self.kids = {key: Kid(**value) for key, value in stored.get("kids", {}).items()}
            rule_data = stored.get("rules", {})
            self.rules = RulesMap(
                version=int(rule_data.get("version", 1)),
                categories={k: Category.from_dict(v) for k, v in rule_data.get("categories", {}).items()},
                processes=rule_data.get("processes", {}), domains=rule_data.get("domains", {}),
                overrides=rule_data.get("overrides", {}),
            )
            self.limits = stored.get("limits", {})
            self.usage = stored.get("usage", {})
            self.devices = {k: set(v) for k, v in stored.get("devices", {}).items()}
            self.unknowns = {k: UnknownItem.from_dict(v) for k, v in stored.get("unknowns", {}).items()}
            self.usage_items = {
                key: UsageItem.from_dict(value)
                for key, value in stored.get("usage_items", {}).items()
            }
        else:
            self.rules = RulesMap(
                categories={item["id"]: Category.from_dict(item) for item in DEFAULT_CATEGORIES},
                processes=dict(SEED_PROCESSES), domains=dict(SEED_DOMAINS),
            )
        for value in configured_kids:
            self.kids[value["id"]] = Kid(**value)
        if UNCLASSIFIED not in self.rules.categories:
            item = next(x for x in DEFAULT_CATEGORIES if x["id"] == UNCLASSIFIED)
            self.rules.categories[UNCLASSIFIED] = Category.from_dict(item)
        for kid in self.kids:
            self._ensure_kid(kid)
        await self.async_save()
        self._arm_unknown_notification()

    def _ensure_kid(self, kid: str) -> None:
        self.devices.setdefault(kid, set())
        self.usage.setdefault(kid, {})
        self.limits.setdefault(kid, {})
        for category in self.rules.categories:
            self.limits[kid].setdefault(category, 0)

    async def async_save(self) -> None:
        await self.store.async_save({
            "kids": {k: asdict(v) for k, v in self.kids.items()},
            "rules": {
                "version": self.rules.version,
                "categories": {k: asdict(v) for k, v in self.rules.categories.items()},
                "processes": self.rules.processes, "domains": self.rules.domains,
                "overrides": self.rules.overrides,
            },
            "limits": self.limits, "usage": self.usage,
            "devices": {k: sorted(v) for k, v in self.devices.items()},
            "unknowns": {k: asdict(v) for k, v in self.unknowns.items()},
            "usage_items": {k: asdict(v) for k, v in self.usage_items.items()},
        })

    def total(self, kid: str, category: str) -> int:
        return sum(values.get(category, 0) for values in self.usage.get(kid, {}).values())

    def exhausted(self, kid: str, category: str) -> bool:
        limit = self.limits.get(kid, {}).get(category, 0)
        return limit > 0 and self.total(kid, category) >= limit

    async def async_report_usage(
        self, kid: str, device: str, category: str, minutes: int, app_id: str = "",
        kind: str = "process", app_label: str = "",
    ) -> None:
        if kid not in self.kids:
            _LOGGER.error("Ignoring usage for unknown kid %s", kid)
            return
        if category not in self.rules.categories:
            _LOGGER.warning("Unknown category %s; billing to unclassified", category)
            category = UNCLASSIFIED
        clamped = max(1, min(30, int(minutes)))
        if clamped != minutes:
            _LOGGER.warning("Clamped usage delta from %s to %s", minutes, clamped)
        was_exhausted = self.exhausted(kid, category)
        is_new_device = device not in self.devices[kid]
        self.devices[kid].add(device)
        device_usage = self.usage[kid].setdefault(device, {})
        device_usage[category] = device_usage.get(category, 0) + clamped
        app_id = app_id.lower().strip()
        if app_id:
            key = f"{kid}|{kind}|{app_id}"
            item = self.usage_items.get(key)
            if item is None:
                item = UsageItem(kind=kind, id=app_id, kid=kid)
                self.usage_items[key] = item
            item.minutes += clamped
            item.last_category = category
            item.last_seen = utc_now_iso()
            if app_label.strip():
                item.label = app_label.strip()[:120]
            if device not in item.devices:
                item.devices.append(device)
        if category == UNCLASSIFIED and app_id:
            unknown = self.unknowns.get(f"{kid}|{kind}|{app_id}")
            if unknown:
                unknown.minutes_seen += clamped
                unknown.last_seen = utc_now_iso()
        await self.async_heartbeat(kid, device, "")
        await self.async_save()
        async_dispatcher_send(self.hass, SIGNAL_STATE_CHANGED)
        if is_new_device:
            async_dispatcher_send(self.hass, f"{SIGNAL_STATE_CHANGED}_entities")
        if not was_exhausted and self.exhausted(kid, category):
            self.hass.bus.async_fire("gnomon_limit_reached", {
                "kid": kid, "category": category,
                "limit": self.limits[kid][category], "used": self.total(kid, category),
            })

    async def async_report_unknown(
        self, kid: str, device: str, kind: str, item_id: str, hint: str = ""
    ) -> None:
        if kid not in self.kids:
            _LOGGER.error("Ignoring unknown item for unknown kid %s", kid)
            return
        item_id = item_id.lower().strip()
        key = f"{kid}|{kind}|{item_id}"
        if key in self.unknowns:
            self.unknowns[key].last_seen = utc_now_iso()
        else:
            self.unknowns[key] = UnknownItem(
                kind=kind, id=item_id, kid=kid, device=device, hint=hint[:120]
            )
            self.hass.bus.async_fire("gnomon_unknown_seen", {
                "kid": kid, "device": device, "kind": kind, "id": item_id, "hint": hint[:120]
            })
            async_dispatcher_send(self.hass, f"{SIGNAL_STATE_CHANGED}_entities")
        usage_item = self.usage_items.get(key)
        if usage_item is None:
            self.usage_items[key] = UsageItem(
                kind=kind, id=item_id, kid=kid, label=hint[:120], devices=[device]
            )
        else:
            usage_item.last_seen = utc_now_iso()
            if hint.strip():
                usage_item.label = hint.strip()[:120]
            if device not in usage_item.devices:
                usage_item.devices.append(device)
        await self.async_save()
        async_dispatcher_send(self.hass, SIGNAL_STATE_CHANGED)
        self._arm_unknown_notification()

    async def async_assign_unknown(self, key: str, category: str) -> None:
        item = self.unknowns.get(key)
        if item is None or category not in self.rules.categories:
            return
        target = self.rules.processes if item.kind == "process" else self.rules.domains
        target[item.id] = category
        del self.unknowns[key]
        await self.async_rules_mutated()
        async_dispatcher_send(self.hass, SIGNAL_UNKNOWN_REMOVED, key)

    def classification_category(self, kid: str, kind: str, item_id: str) -> str:
        """Resolve the current category with the same precedence agents use."""
        plural = "processes" if kind == "process" else "domains"
        override = self.rules.overrides.get(kid, {}).get(plural, {})
        global_rules = self.rules.processes if kind == "process" else self.rules.domains
        if kind == "process":
            return override.get(item_id, global_rules.get(item_id, UNCLASSIFIED))
        candidates = {**global_rules, **override}
        matches = [
            (base, value) for base, value in candidates.items()
            if item_id == base or item_id.endswith(f".{base}")
        ]
        return max(matches, key=lambda value: len(value[0]))[1] if matches else UNCLASSIFIED

    def classification_catalog(self, kid: str) -> dict[str, Any]:
        """Return a usage-ranked catalog suitable for agent admin interfaces."""
        items: dict[str, dict[str, Any]] = {}
        for key, value in self.usage_items.items():
            if value.kid != kid:
                continue
            items[key] = {
                "kind": value.kind, "id": value.id,
                "label": value.label or value.id,
                "category": self.classification_category(kid, value.kind, value.id),
                "minutes": value.minutes, "devices": sorted(value.devices),
                "last_seen": value.last_seen,
                "unclassified": key in self.unknowns,
            }
        for key, value in self.unknowns.items():
            if value.kid == kid and key not in items:
                items[key] = {
                    "kind": value.kind, "id": value.id,
                    "label": value.hint or value.id, "category": UNCLASSIFIED,
                    "minutes": value.minutes_seen, "devices": [value.device],
                    "last_seen": value.last_seen, "unclassified": True,
                }
        return {
            "version": self.rules.version,
            "categories": [
                {"id": value.id, "name": value.name}
                for value in self.rules.categories.values()
            ],
            "items": sorted(
                items.values(), key=lambda value: (-value["minutes"], value["label"].lower())
            ),
        }

    async def async_set_classification(
        self, kid: str, kind: str, item_id: str, category: str
    ) -> dict[str, Any]:
        """Set a kid-specific mapping and return the refreshed catalog."""
        if kid not in self.kids or category not in self.rules.categories:
            return self.classification_catalog(kid)
        item_id = item_id.lower().strip()
        override = self.rules.overrides.setdefault(
            kid, {"processes": {}, "domains": {}}
        )
        override.setdefault("processes", {})
        override.setdefault("domains", {})
        override["processes" if kind == "process" else "domains"][item_id] = category
        await self.async_rules_mutated()
        return self.classification_catalog(kid)

    async def async_rules_mutated(self) -> None:
        self.rules.version += 1
        removed = []
        for key, item in self.unknowns.items():
            mapping = self.rules.processes if item.kind == "process" else self.rules.domains
            override = self.rules.overrides.get(item.kid, {}).get(
                "processes" if item.kind == "process" else "domains", {}
            )
            candidates = {**mapping, **override}
            covered = item.id in candidates
            if item.kind == "domain":
                covered = covered or any(item.id.endswith(f".{base}") for base in candidates)
            if covered:
                removed.append(key)
        for key in removed:
            del self.unknowns[key]
            async_dispatcher_send(self.hass, SIGNAL_UNKNOWN_REMOVED, key)
        for kid in self.kids:
            self._ensure_kid(kid)
        await self.async_save()
        async_dispatcher_send(self.hass, SIGNAL_STATE_CHANGED)
        async_dispatcher_send(self.hass, f"{SIGNAL_STATE_CHANGED}_entities")
        self._arm_unknown_notification()

    async def async_set_limit(self, kid: str, category: str, value: int) -> None:
        self.limits[kid][category] = max(0, min(720, int(value)))
        await self.async_save()
        async_dispatcher_send(self.hass, SIGNAL_STATE_CHANGED)

    async def async_reset(self, kid: str, category: str | None) -> None:
        if kid not in self.kids:
            _LOGGER.error("Ignoring reset for unknown kid %s", kid)
            return
        categories = [category] if category else list(self.rules.categories)
        for values in self.usage[kid].values():
            for category_id in categories:
                values[category_id] = 0
        for item in self.usage_items.values():
            if item.kid == kid and (
                category is None
                or self.classification_category(kid, item.kind, item.id) == category
            ):
                item.minutes = 0
        await self.async_save()
        async_dispatcher_send(self.hass, SIGNAL_STATE_CHANGED)
        self.hass.bus.async_fire("gnomon_reset", {"kid": kid, "category": category})

    async def async_heartbeat(self, kid: str, device: str, agent_version: str) -> None:
        if kid not in self.kids:
            _LOGGER.error("Ignoring heartbeat for unknown kid %s", kid)
            return
        self._ensure_kid(kid)
        is_new = device not in self.devices[kid]
        self.devices[kid].add(device)
        key = (kid, device)
        self.agent_online.add(key)
        if agent_version:
            self.agent_versions[key] = agent_version
        old_timer = self._agent_timers.pop(key, None)
        if old_timer:
            old_timer()

        async def mark_stale(_now: datetime) -> None:
            self.agent_online.discard(key)
            self._agent_timers.pop(key, None)
            async_dispatcher_send(self.hass, SIGNAL_STATE_CHANGED)

        self._agent_timers[key] = async_track_point_in_time(
            self.hass, mark_stale,
            datetime.now(timezone.utc) + timedelta(minutes=AGENT_STALE_MINUTES),
        )
        async_dispatcher_send(self.hass, SIGNAL_STATE_CHANGED)
        if is_new:
            await self.async_save()
            async_dispatcher_send(self.hass, f"{SIGNAL_STATE_CHANGED}_entities")

    def unknown_attributes(self) -> list[dict[str, Any]]:
        return [asdict(value) for value in self.unknowns.values()]

    @callback
    def shutdown(self, _event: Any = None) -> None:
        """Cancel timers owned by the coordinator."""
        for cancel in self._agent_timers.values():
            cancel()
        self._agent_timers.clear()
        if self._unknown_timer:
            self._unknown_timer()
            self._unknown_timer = None

    def _arm_unknown_notification(self) -> None:
        if self._unknown_timer:
            self._unknown_timer()
            self._unknown_timer = None
        if not self.unknowns:
            persistent_notification.async_dismiss(self.hass, "gnomon_unclassified")
            return
        oldest = min(datetime.fromisoformat(item.first_seen) for item in self.unknowns.values())
        due = oldest + timedelta(hours=UNKNOWN_NOTIFICATION_HOURS)
        delay = max(0.0, (due - datetime.now(timezone.utc)).total_seconds())

        async def notify(_now: Any) -> None:
            if self.unknowns:
                persistent_notification.async_create(
                    self.hass,
                    f"{len(self.unknowns)} unclassified item(s) have been waiting for review.",
                    title="Gnomon classification inbox", notification_id="gnomon_unclassified",
                )

        self._unknown_timer = async_call_later(self.hass, delay, notify)
