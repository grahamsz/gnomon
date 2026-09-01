"""Central, persisted Gnomon state. All mutations happen here."""

from __future__ import annotations

from dataclasses import asdict
from datetime import datetime, timedelta, timezone
import logging
from typing import Any

from homeassistant.core import HomeAssistant, callback
from homeassistant.helpers.dispatcher import async_dispatcher_send
from homeassistant.helpers.event import async_track_point_in_time
from homeassistant.helpers.storage import Store

from .const import (
    AGENT_STALE_MINUTES, DEFAULT_CATEGORIES, SEED_DOMAINS, SEED_PROCESSES,
    SIGNAL_STATE_CHANGED, STORAGE_KEY, STORAGE_VERSION, UNCLASSIFIED,
)
from .models import Category, Kid, RulesMap

_LOGGER = logging.getLogger(__name__)


class GnomonCoordinator:
    """Own aggregate accounting, shared rules, limits, and registered devices."""

    def __init__(self, hass: HomeAssistant) -> None:
        self.hass = hass
        self.store: Store[dict[str, Any]] = Store(hass, STORAGE_VERSION, STORAGE_KEY)
        self.kids: dict[str, Kid] = {}
        self.rules = RulesMap()
        self.limits: dict[str, dict[str, int]] = {}
        self.overall_limits: dict[str, int] = {}
        self.device_limits: dict[str, dict[str, int]] = {}
        self.usage: dict[str, dict[str, dict[str, int]]] = {}
        self.devices: dict[str, set[str]] = {}
        self.agent_versions: dict[tuple[str, str], str] = {}
        self.agent_online: set[tuple[str, str]] = set()
        self._agent_timers: dict[tuple[str, str], Any] = {}

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
            self.overall_limits = {
                key: int(value) for key, value in stored.get("overall_limits", {}).items()
            }
            self.device_limits = {
                kid: {device: int(value) for device, value in limits.items()}
                for kid, limits in stored.get("device_limits", {}).items()
            }
            self.usage = stored.get("usage", {})
            self.devices = {k: set(v) for k, v in stored.get("devices", {}).items()}
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

    def _ensure_kid(self, kid: str) -> None:
        self.devices.setdefault(kid, set())
        self.usage.setdefault(kid, {})
        self.limits.setdefault(kid, {})
        self.overall_limits.setdefault(kid, 0)
        self.device_limits.setdefault(kid, {})
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
            "limits": self.limits,
            "overall_limits": self.overall_limits,
            "device_limits": self.device_limits,
            "usage": self.usage,
            "devices": {k: sorted(v) for k, v in self.devices.items()},
        })

    def total(self, kid: str, category: str) -> int:
        return sum(values.get(category, 0) for values in self.usage.get(kid, {}).values())

    def exhausted(self, kid: str, category: str) -> bool:
        limit = self.limits.get(kid, {}).get(category, 0)
        return limit > 0 and self.total(kid, category) >= limit

    def total_all(self, kid: str) -> int:
        return sum(sum(categories.values()) for categories in self.usage.get(kid, {}).values())

    def device_total(self, kid: str, device: str) -> int:
        return sum(self.usage.get(kid, {}).get(device, {}).values())

    def overall_exhausted(self, kid: str) -> bool:
        limit = self.overall_limits.get(kid, 0)
        return limit > 0 and self.total_all(kid) >= limit

    def device_exhausted(self, kid: str, device: str) -> bool:
        limit = self.device_limits.get(kid, {}).get(device, 0)
        return limit > 0 and self.device_total(kid, device) >= limit

    def status_response(self, kid: str, device: str) -> dict[str, Any]:
        """Return stable aggregate status without relying on editable entity IDs."""
        return {
            "categories": [
                {
                    "id": category.id, "name": category.name,
                    "used": self.total(kid, category.id),
                    "limit": self.limits.get(kid, {}).get(category.id, 0),
                }
                for category in self.rules.categories.values()
            ],
            "child": {
                "used": self.total_all(kid), "limit": self.overall_limits.get(kid, 0),
            },
            "device": {
                "id": device, "used": self.device_total(kid, device),
                "limit": self.device_limits.get(kid, {}).get(device, 0),
            },
        }

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
        was_overall_exhausted = self.overall_exhausted(kid)
        was_device_exhausted = self.device_exhausted(kid, device)
        is_new_device = device not in self.devices[kid]
        self.devices[kid].add(device)
        device_usage = self.usage[kid].setdefault(device, {})
        device_usage[category] = device_usage.get(category, 0) + clamped
        await self.async_heartbeat(kid, device, "")
        await self.async_save()
        async_dispatcher_send(self.hass, SIGNAL_STATE_CHANGED)
        self.hass.bus.async_fire("gnomon_changed", {
            "kind": "status", "kid": kid, "device": device,
        })
        if is_new_device:
            async_dispatcher_send(self.hass, f"{SIGNAL_STATE_CHANGED}_entities")
        if not was_exhausted and self.exhausted(kid, category):
            self.hass.bus.async_fire("gnomon_limit_reached", {
                "kid": kid, "category": category,
                "limit": self.limits[kid][category], "used": self.total(kid, category),
            })
        if not was_overall_exhausted and self.overall_exhausted(kid):
            self.hass.bus.async_fire("gnomon_limit_reached", {
                "kid": kid, "scope": "child", "limit": self.overall_limits[kid],
                "used": self.total_all(kid),
            })
        if not was_device_exhausted and self.device_exhausted(kid, device):
            self.hass.bus.async_fire("gnomon_limit_reached", {
                "kid": kid, "device": device, "scope": "device",
                "limit": self.device_limits[kid][device],
                "used": self.device_total(kid, device),
            })

    async def async_report_unknown(
        self, kid: str, device: str, kind: str, item_id: str, hint: str = ""
    ) -> None:
        # Kept as a compatibility no-op for older agents. Raw activity is local-only.
        return

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

    async def async_set_classification(
        self, kid: str, kind: str, item_id: str, category: str
    ) -> dict[str, Any]:
        """Set a kid-specific mapping and return the distributed rule document."""
        if kid not in self.kids or category not in self.rules.categories:
            return self.rules.response()
        item_id = item_id.lower().strip()
        override = self.rules.overrides.setdefault(
            kid, {"processes": {}, "domains": {}}
        )
        override.setdefault("processes", {})
        override.setdefault("domains", {})
        override["processes" if kind == "process" else "domains"][item_id] = category
        await self.async_rules_mutated()
        return self.rules.response()

    async def async_rules_mutated(self) -> None:
        self.rules.version += 1
        for kid in self.kids:
            self._ensure_kid(kid)
        await self.async_save()
        async_dispatcher_send(self.hass, SIGNAL_STATE_CHANGED)
        async_dispatcher_send(self.hass, f"{SIGNAL_STATE_CHANGED}_entities")
        self.hass.bus.async_fire("gnomon_changed", {
            "kind": "rules", "version": self.rules.version,
        })

    async def async_set_limit(self, kid: str, category: str, value: int) -> None:
        self.limits[kid][category] = max(0, min(720, int(value)))
        await self.async_save()
        async_dispatcher_send(self.hass, SIGNAL_STATE_CHANGED)
        self.hass.bus.async_fire("gnomon_changed", {"kind": "status", "kid": kid})

    async def async_set_overall_limit(
        self, kid: str, value: int, device: str | None = None
    ) -> None:
        value = max(0, min(1440, int(value)))
        if device:
            self.device_limits.setdefault(kid, {})[device] = value
        else:
            self.overall_limits[kid] = value
        await self.async_save()
        async_dispatcher_send(self.hass, SIGNAL_STATE_CHANGED)
        self.hass.bus.async_fire("gnomon_changed", {
            "kind": "status", "kid": kid, "device": device,
        })

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
        self.hass.bus.async_fire("gnomon_changed", {"kind": "status", "kid": kid})

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
            self.hass.bus.async_fire("gnomon_changed", {
                "kind": "status", "kid": kid, "device": device,
            })
        if is_new:
            await self.async_save()
            async_dispatcher_send(self.hass, f"{SIGNAL_STATE_CHANGED}_entities")

    @callback
    def shutdown(self, _event: Any = None) -> None:
        """Cancel timers owned by the coordinator."""
        for cancel in self._agent_timers.values():
            cancel()
        self._agent_timers.clear()
