"""Service schemas and handlers."""

from __future__ import annotations

import voluptuous as vol

from homeassistant.core import HomeAssistant, ServiceCall, SupportsResponse
from homeassistant.helpers import config_validation as cv

from .const import DOMAIN
from .coordinator import GnomonCoordinator


async def async_register_services(hass: HomeAssistant, coordinator: GnomonCoordinator) -> None:
    async def report_usage(call: ServiceCall) -> None:
        await coordinator.async_report_usage(**call.data)

    async def report_unknown(call: ServiceCall) -> None:
        data = dict(call.data)
        data["item_id"] = data.pop("id")
        await coordinator.async_report_unknown(**data)

    async def get_rules(_call: ServiceCall):
        return coordinator.rules.response()

    async def heartbeat(call: ServiceCall) -> None:
        await coordinator.async_heartbeat(**call.data)

    async def reset(call: ServiceCall) -> None:
        await coordinator.async_reset(call.data["kid"], call.data.get("category"))

    slug = vol.Match(r"^[a-z0-9_]+$")
    hass.services.async_register(DOMAIN, "report_usage", report_usage, schema=vol.Schema({
        vol.Required("kid"): slug, vol.Required("device"): slug,
        vol.Required("category"): slug, vol.Required("minutes"): vol.Coerce(int),
        vol.Optional("app_id", default=""): cv.string,
    }))
    hass.services.async_register(DOMAIN, "report_unknown", report_unknown, schema=vol.Schema({
        vol.Required("kid"): slug, vol.Required("device"): slug,
        vol.Required("kind"): vol.In(("process", "domain")),
        vol.Required("id"): cv.string, vol.Optional("hint", default=""): cv.string,
    }))
    hass.services.async_register(
        DOMAIN, "get_rules", get_rules, schema=vol.Schema({}),
        supports_response=SupportsResponse.ONLY,
    )
    hass.services.async_register(DOMAIN, "heartbeat", heartbeat, schema=vol.Schema({
        vol.Required("kid"): slug, vol.Required("device"): slug,
        vol.Optional("agent_version", default=""): cv.string,
    }))
    hass.services.async_register(DOMAIN, "reset", reset, schema=vol.Schema({
        vol.Required("kid"): slug, vol.Optional("category"): slug,
    }))


async def async_unregister_services(hass: HomeAssistant) -> None:
    for service in ("report_usage", "report_unknown", "get_rules", "heartbeat", "reset"):
        hass.services.async_remove(DOMAIN, service)
