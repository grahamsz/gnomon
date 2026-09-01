"""Gnomon Home Assistant integration."""

from __future__ import annotations

from homeassistant.config_entries import ConfigEntry
from homeassistant.const import EVENT_HOMEASSISTANT_STOP
from homeassistant.core import HomeAssistant
from homeassistant.helpers import entity_registry as er

from .const import DOMAIN, PLATFORMS
from .coordinator import GnomonCoordinator
from .services import async_register_services, async_unregister_services


async def async_setup_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    coordinator = GnomonCoordinator(hass)
    kids = list(entry.options.get("kids", entry.data.get("kids", [])))
    await coordinator.async_load(kids)
    _remove_obsolete_activity_entities(hass, entry, coordinator)
    entry.async_on_unload(coordinator.shutdown)
    entry.async_on_unload(
        hass.bus.async_listen_once(EVENT_HOMEASSISTANT_STOP, coordinator.shutdown)
    )
    hass.data.setdefault(DOMAIN, {})[entry.entry_id] = coordinator
    if len(hass.data[DOMAIN]) == 1:
        await async_register_services(hass, coordinator)
    await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)
    entry.async_on_unload(entry.add_update_listener(_async_reload_entry))
    return True


async def async_unload_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    unloaded = await hass.config_entries.async_unload_platforms(entry, PLATFORMS)
    if unloaded:
        hass.data[DOMAIN].pop(entry.entry_id)
        if not hass.data[DOMAIN]:
            await async_unregister_services(hass)
    return unloaded


async def _async_reload_entry(hass: HomeAssistant, entry: ConfigEntry) -> None:
    await hass.config_entries.async_reload(entry.entry_id)


def _remove_obsolete_activity_entities(
    hass: HomeAssistant, entry: ConfigEntry, coordinator: GnomonCoordinator
) -> None:
    """Remove the old per-item and per-device/category entity-registry clutter."""
    registry = er.async_get(hass)
    obsolete = {"gnomon_unclassified"}
    obsolete_device_prefixes = [
        f"gnomon_used_{kid}_{device}_"
        for kid, devices in coordinator.devices.items()
        for device in devices
    ]
    for entity in list(registry.entities.values()):
        if entity.config_entry_id != entry.entry_id:
            continue
        if (
            entity.domain == "select"
            or entity.unique_id.startswith("gnomon_classify_")
            or entity.unique_id in obsolete
            or any(
                entity.unique_id.startswith(prefix) and entity.unique_id != f"{prefix}total"
                for prefix in obsolete_device_prefixes
            )
        ):
            registry.async_remove(entity.entity_id)
