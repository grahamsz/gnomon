"""Central, persisted Gnomon state. All mutations happen here."""

from __future__ import annotations

from dataclasses import asdict
from datetime import datetime, timedelta, timezone
import logging
from typing import Any

from homeassistant.core import HomeAssistant, callback
from homeassistant.helpers.dispatcher import async_dispatcher_send
from homeassistant.helpers.event import async_call_later, async_track_point_in_time
from homeassistant.helpers.storage import Store

from .const import (
    AGENT_STALE_MINUTES, DEFAULT_CATEGORIES, DOMAIN, SEED_DOMAINS, SEED_PROCESSES,
    SIGNAL_STATE_CHANGED, SIGNAL_UNKNOWN_REMOVED, STORAGE_KEY, STORAGE_VERSION,
    UNCLASSIFIED, UNKNOWN_NOTIFICATION_HOURS,
)
from .models import Category, Kid, RulesMap, UnknownItem, utc_now_iso

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
        })

    def total(self, kid: str, category: str) -> int:
        return sum(values.get(category, 0) for values in self.usage.get(kid, {}).values())

    def exhausted(self, kid: str, category: str) -> bool:
        limit = self.limits.get(kid, {}).get(category, 0)
        return limit > 0 and self.total(kid, category) >= limit

    async def async_report_usage(
        self, kid: str, device: str, category: str, minutes: int, app_id: str = ""
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
        if category == UNCLASSIFIED and app_id:
            for item in self.unknowns.values():
                if item.kid == kid and item.id == app_id.lower():
                    item.minutes_seen += clamped
                    item.last_seen = utc_now_iso()
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
            self.hass.components.persistent_notification.async_dismiss("gnomon_unclassified")
            return
        oldest = min(datetime.fromisoformat(item.first_seen) for item in self.unknowns.values())
        due = oldest + timedelta(hours=UNKNOWN_NOTIFICATION_HOURS)
        delay = max(0.0, (due - datetime.now(timezone.utc)).total_seconds())

        async def notify(_now: Any) -> None:
            if self.unknowns:
                self.hass.components.persistent_notification.async_create(
                    f"{len(self.unknowns)} unclassified item(s) have been waiting for review.",
                    title="Gnomon classification inbox", notification_id="gnomon_unclassified",
                )

        self._unknown_timer = async_call_later(self.hass, delay, notify)
