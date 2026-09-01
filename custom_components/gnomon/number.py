"""Per-kid category limit numbers."""

from __future__ import annotations

from homeassistant.components.number import NumberEntity, NumberMode
from homeassistant.config_entries import ConfigEntry
from homeassistant.const import UnitOfTime
from homeassistant.core import HomeAssistant, callback
from homeassistant.helpers.dispatcher import async_dispatcher_connect
from homeassistant.helpers.entity_platform import AddEntitiesCallback

from .const import (
    DOMAIN, SIGNAL_STATE_CHANGED, limit_device_unique_id,
    limit_overall_unique_id, limit_unique_id,
)
from .entity import GnomonEntity


async def async_setup_entry(
    hass: HomeAssistant, entry: ConfigEntry, async_add_entities: AddEntitiesCallback
) -> None:
    coordinator = hass.data[DOMAIN][entry.entry_id]
    known: set[str] = set()

    @callback
    def discover() -> None:
        entities = []
        for kid in coordinator.kids:
            for category in coordinator.rules.categories:
                entity = GnomonLimitNumber(coordinator, entry, kid, category)
                if entity.unique_id not in known:
                    known.add(entity.unique_id); entities.append(entity)
            overall = GnomonOverallLimitNumber(coordinator, entry, kid)
            if overall.unique_id not in known:
                known.add(overall.unique_id); entities.append(overall)
            for device in coordinator.devices.get(kid, set()):
                entity = GnomonDeviceLimitNumber(coordinator, entry, kid, device)
                if entity.unique_id not in known:
                    known.add(entity.unique_id); entities.append(entity)
        if entities:
            async_add_entities(entities)

    discover()
    entry.async_on_unload(async_dispatcher_connect(hass, f"{SIGNAL_STATE_CHANGED}_entities", discover))


class GnomonLimitNumber(GnomonEntity, NumberEntity):
    _attr_native_min_value = 0
    _attr_native_max_value = 720
    _attr_native_step = 5
    _attr_native_unit_of_measurement = UnitOfTime.MINUTES
    _attr_mode = NumberMode.BOX
    _attr_icon = "mdi:timer-sand"

    def __init__(self, coordinator, entry, kid: str, category: str) -> None:
        super().__init__(coordinator, entry)
        self.kid, self.category = kid, category
        self._attr_unique_id = limit_unique_id(kid, category)
        self._attr_suggested_object_id = self._attr_unique_id
        self._attr_name = f"Limit {kid} {category}"

    @property
    def native_value(self) -> int:
        return self.coordinator.limits[self.kid].get(self.category, 0)

    async def async_set_native_value(self, value: float) -> None:
        await self.coordinator.async_set_limit(self.kid, self.category, round(value))

    @property
    def device_info(self):
        return self.kid_device_info(self.kid)


class _OverallLimitNumber(GnomonEntity, NumberEntity):
    _attr_native_min_value = 0
    _attr_native_max_value = 1440
    _attr_native_step = 5
    _attr_native_unit_of_measurement = UnitOfTime.MINUTES
    _attr_mode = NumberMode.BOX
    _attr_icon = "mdi:timer-sand-complete"


class GnomonOverallLimitNumber(_OverallLimitNumber):
    def __init__(self, coordinator, entry, kid: str) -> None:
        super().__init__(coordinator, entry)
        self.kid = kid
        self._attr_unique_id = limit_overall_unique_id(kid)
        self._attr_suggested_object_id = self._attr_unique_id
        self._attr_name = f"Limit {kid} total"

    @property
    def native_value(self) -> int:
        return self.coordinator.overall_limits.get(self.kid, 0)

    async def async_set_native_value(self, value: float) -> None:
        await self.coordinator.async_set_overall_limit(self.kid, round(value))

    @property
    def device_info(self):
        return self.kid_device_info(self.kid)


class GnomonDeviceLimitNumber(_OverallLimitNumber):
    def __init__(self, coordinator, entry, kid: str, device: str) -> None:
        super().__init__(coordinator, entry)
        self.kid, self.device = kid, device
        self._attr_unique_id = limit_device_unique_id(kid, device)
        self._attr_suggested_object_id = self._attr_unique_id
        self._attr_name = f"Limit {kid} {device} total"

    @property
    def native_value(self) -> int:
        return self.coordinator.device_limits.get(self.kid, {}).get(self.device, 0)

    async def async_set_native_value(self, value: float) -> None:
        await self.coordinator.async_set_overall_limit(self.kid, round(value), self.device)

    @property
    def device_info(self):
        return self.agent_device_info(self.kid, self.device)
