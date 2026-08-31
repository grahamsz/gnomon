"""Shared entity helpers."""

from __future__ import annotations

from homeassistant.config_entries import ConfigEntry
from homeassistant.helpers.device_registry import DeviceInfo
from homeassistant.helpers.entity import Entity
from homeassistant.helpers.dispatcher import async_dispatcher_connect

from .const import DOMAIN, SIGNAL_STATE_CHANGED
from .coordinator import GnomonCoordinator


class GnomonEntity(Entity):
    """Entity backed by the central coordinator."""

    _attr_has_entity_name = True
    _attr_should_poll = False

    def __init__(self, coordinator: GnomonCoordinator, entry: ConfigEntry) -> None:
        self.coordinator = coordinator
        self.entry = entry

    async def async_added_to_hass(self) -> None:
        await super().async_added_to_hass()
        self.async_on_remove(async_dispatcher_connect(
            self.hass, SIGNAL_STATE_CHANGED, self.async_write_ha_state
        ))

    def kid_device_info(self, kid: str) -> DeviceInfo:
        return DeviceInfo(
            identifiers={(DOMAIN, f"kid_{kid}")},
            name=self.coordinator.kids[kid].name,
            manufacturer="Gnomon", model="Screen time account",
        )

    def agent_device_info(self, kid: str, device: str) -> DeviceInfo:
        return DeviceInfo(
            identifiers={(DOMAIN, f"agent_{kid}_{device}")},
            name=f"{self.coordinator.kids[kid].name} — {device}",
            manufacturer="Gnomon", model="Screen time agent",
            via_device=(DOMAIN, f"kid_{kid}"),
        )
