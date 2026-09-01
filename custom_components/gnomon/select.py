"""Transient unknown-item triage selects."""

from __future__ import annotations

import re

from homeassistant.components.select import SelectEntity
from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant, callback
from homeassistant.helpers.dispatcher import async_dispatcher_connect
from homeassistant.helpers.entity_platform import AddEntitiesCallback

from .const import ASSIGN_PLACEHOLDER, DOMAIN, SIGNAL_STATE_CHANGED, SIGNAL_UNKNOWN_REMOVED
from .entity import GnomonEntity


async def async_setup_entry(
    hass: HomeAssistant, entry: ConfigEntry, async_add_entities: AddEntitiesCallback
) -> None:
    coordinator = hass.data[DOMAIN][entry.entry_id]
    known: set[str] = set()

    @callback
    def discover() -> None:
        entities = []
        for key in coordinator.unknowns:
            if key not in known:
                known.add(key); entities.append(GnomonTriageSelect(coordinator, entry, key))
        if entities:
            async_add_entities(entities)

    discover()
    entry.async_on_unload(async_dispatcher_connect(hass, f"{SIGNAL_STATE_CHANGED}_entities", discover))


class GnomonTriageSelect(GnomonEntity, SelectEntity):
    _attr_icon = "mdi:tag-question-outline"

    def __init__(self, coordinator, entry, key: str) -> None:
        super().__init__(coordinator, entry)
        self.key = key
        item = coordinator.unknowns[key]
        safe = re.sub(r"[^a-z0-9_]+", "_", item.id.lower()).strip("_")
        self._attr_unique_id = f"gnomon_classify_{item.kid}_{item.kind}_{safe}"
        self._attr_suggested_object_id = f"gnomon_classify_{item.kind}_{safe}"
        self._attr_name = f"Classify {item.kind} {item.id}"
        self._attr_current_option = ASSIGN_PLACEHOLDER

    @property
    def options(self) -> list[str]:
        return [ASSIGN_PLACEHOLDER, *self.coordinator.rules.categories]

    @property
    def available(self) -> bool:
        return self.key in self.coordinator.unknowns

    async def async_added_to_hass(self) -> None:
        await super().async_added_to_hass()

        async def removed(key: str) -> None:
            if key == self.key:
                await self.async_remove(force_remove=True)

        self.async_on_remove(async_dispatcher_connect(self.hass, SIGNAL_UNKNOWN_REMOVED, removed))

    async def async_select_option(self, option: str) -> None:
        if option != ASSIGN_PLACEHOLDER:
            await self.coordinator.async_assign_unknown(self.key, option)

    @property
    def device_info(self):
        item = self.coordinator.unknowns.get(self.key)
        return self.kid_device_info(item.kid) if item else None
