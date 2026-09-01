"""Stable Gnomon usage and rules sensors."""

from __future__ import annotations

from homeassistant.components.sensor import SensorEntity
from homeassistant.config_entries import ConfigEntry
from homeassistant.const import UnitOfTime
from homeassistant.core import HomeAssistant, callback
from homeassistant.helpers.dispatcher import async_dispatcher_connect
from homeassistant.helpers.entity_platform import AddEntitiesCallback
from homeassistant.helpers.restore_state import RestoreEntity

from .const import (
    DOMAIN, SIGNAL_STATE_CHANGED, usage_device_total_unique_id,
    usage_overall_unique_id, usage_total_unique_id,
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
        fixed = (GnomonRulesVersionSensor(coordinator, entry),)
        for entity in fixed:
            if entity.unique_id not in known:
                known.add(entity.unique_id)
                entities.append(entity)
        for kid in coordinator.kids:
            for category in coordinator.rules.categories:
                total = GnomonTotalSensor(coordinator, entry, kid, category)
                if total.unique_id not in known:
                    known.add(total.unique_id); entities.append(total)
            overall = GnomonOverallSensor(coordinator, entry, kid)
            if overall.unique_id not in known:
                known.add(overall.unique_id); entities.append(overall)
            for device in coordinator.devices.get(kid, set()):
                item = GnomonDeviceTotalSensor(coordinator, entry, kid, device)
                if item.unique_id not in known:
                    known.add(item.unique_id); entities.append(item)
        if entities:
            async_add_entities(entities)

    discover()
    entry.async_on_unload(async_dispatcher_connect(hass, f"{SIGNAL_STATE_CHANGED}_entities", discover))


class _UsageSensor(GnomonEntity, RestoreEntity, SensorEntity):
    _attr_native_unit_of_measurement = UnitOfTime.MINUTES
    _attr_icon = "mdi:timer-outline"


class GnomonDeviceTotalSensor(_UsageSensor):
    def __init__(self, coordinator, entry, kid: str, device: str) -> None:
        super().__init__(coordinator, entry)
        self.kid, self.device = kid, device
        self._attr_unique_id = usage_device_total_unique_id(kid, device)
        self._attr_suggested_object_id = self._attr_unique_id
        self._attr_name = f"Used {kid} {device} total"

    @property
    def native_value(self) -> int:
        return self.coordinator.device_total(self.kid, self.device)

    @property
    def device_info(self):
        return self.agent_device_info(self.kid, self.device)


class GnomonTotalSensor(_UsageSensor):
    def __init__(self, coordinator, entry, kid: str, category: str) -> None:
        super().__init__(coordinator, entry)
        self.kid, self.category = kid, category
        self._attr_unique_id = usage_total_unique_id(kid, category)
        self._attr_suggested_object_id = self._attr_unique_id
        self._attr_name = f"Used {kid} {category}"

    @property
    def native_value(self) -> int:
        return self.coordinator.total(self.kid, self.category)

    @property
    def device_info(self):
        return self.kid_device_info(self.kid)


class GnomonOverallSensor(_UsageSensor):
    def __init__(self, coordinator, entry, kid: str) -> None:
        super().__init__(coordinator, entry)
        self.kid = kid
        self._attr_unique_id = usage_overall_unique_id(kid)
        self._attr_suggested_object_id = self._attr_unique_id
        self._attr_name = f"Used {kid} total"

    @property
    def native_value(self) -> int:
        return self.coordinator.total_all(self.kid)

    @property
    def device_info(self):
        return self.kid_device_info(self.kid)


class GnomonRulesVersionSensor(GnomonEntity, SensorEntity):
    _attr_name = "Rules version"
    _attr_unique_id = "gnomon_rules_version"
    _attr_suggested_object_id = "gnomon_rules_version"
    _attr_icon = "mdi:file-tree"

    @property
    def native_value(self) -> int:
        return self.coordinator.rules.version
