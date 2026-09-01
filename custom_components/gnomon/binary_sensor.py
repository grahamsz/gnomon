"""Exhausted and agent-connectivity binary sensors."""

from __future__ import annotations

from homeassistant.components.binary_sensor import BinarySensorEntity, BinarySensorDeviceClass
from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant, callback
from homeassistant.helpers.dispatcher import async_dispatcher_connect
from homeassistant.helpers.entity_platform import AddEntitiesCallback

from .const import DOMAIN, SIGNAL_STATE_CHANGED
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
                item = GnomonExhaustedSensor(coordinator, entry, kid, category)
                if item.unique_id not in known:
                    known.add(item.unique_id); entities.append(item)
            for device in coordinator.devices.get(kid, set()):
                item = GnomonAgentSensor(coordinator, entry, kid, device)
                if item.unique_id not in known:
                    known.add(item.unique_id); entities.append(item)
        if entities:
            async_add_entities(entities)

    discover()
    entry.async_on_unload(async_dispatcher_connect(hass, f"{SIGNAL_STATE_CHANGED}_entities", discover))


class GnomonExhaustedSensor(GnomonEntity, BinarySensorEntity):
    _attr_icon = "mdi:timer-alert-outline"

    def __init__(self, coordinator, entry, kid: str, category: str) -> None:
        super().__init__(coordinator, entry)
        self.kid, self.category = kid, category
        self._attr_unique_id = f"gnomon_exhausted_{kid}_{category}"
        self._attr_suggested_object_id = self._attr_unique_id
        self._attr_name = f"Exhausted {kid} {category}"

    @property
    def is_on(self) -> bool:
        return self.coordinator.exhausted(self.kid, self.category)

    @property
    def device_info(self):
        return self.kid_device_info(self.kid)


class GnomonAgentSensor(GnomonEntity, BinarySensorEntity):
    _attr_device_class = BinarySensorDeviceClass.CONNECTIVITY

    def __init__(self, coordinator, entry, kid: str, device: str) -> None:
        super().__init__(coordinator, entry)
        self.kid, self.device = kid, device
        self._attr_unique_id = f"gnomon_agent_{kid}_{device}"
        self._attr_suggested_object_id = self._attr_unique_id
        self._attr_name = f"Agent {kid} {device}"

    @property
    def is_on(self) -> bool:
        return (self.kid, self.device) in self.coordinator.agent_online

    @property
    def extra_state_attributes(self):
        return {"agent_version": self.coordinator.agent_versions.get((self.kid, self.device), "")}

    @property
    def device_info(self):
        return self.agent_device_info(self.kid, self.device)
