"""Behavioral tests for the HA ledger and triage flow."""

from unittest.mock import patch

from homeassistant.core import HomeAssistant
from pytest_homeassistant_custom_component.common import MockConfigEntry

from custom_components.gnomon.const import DOMAIN


async def _setup(hass: HomeAssistant):
    entry = MockConfigEntry(domain=DOMAIN, data={"kids": [{"id": "alex", "name": "Alex"}]})
    entry.add_to_hass(hass)
    with patch("custom_components.gnomon.coordinator.Store.async_load", return_value=None):
        assert await hass.config_entries.async_setup(entry.entry_id)
        await hass.async_block_till_done()
    return hass.data[DOMAIN][entry.entry_id]


async def test_usage_total_limit_event(hass: HomeAssistant):
    coordinator = await _setup(hass)
    await coordinator.async_set_limit("alex", "games", 10)
    events = []
    hass.bus.async_listen("gnomon_limit_reached", lambda event: events.append(event.data))
    await hass.services.async_call(DOMAIN, "report_usage", {
        "kid": "alex", "device": "pc", "category": "games", "minutes": 12
    }, blocking=True)
    await hass.async_block_till_done()
    assert coordinator.total("alex", "games") == 12
    assert events == [{"kid": "alex", "category": "games", "limit": 10, "used": 12}]


async def test_setup_dismisses_stale_notification(hass: HomeAssistant):
    with patch("custom_components.gnomon.coordinator.persistent_notification.async_dismiss") as dismiss:
        await _setup(hass)
    dismiss.assert_called_once_with(hass, "gnomon_unclassified")


async def test_unknown_assignment_bumps_rules(hass: HomeAssistant):
    coordinator = await _setup(hass)
    await hass.services.async_call(DOMAIN, "report_unknown", {
        "kid": "alex", "device": "pc", "kind": "process", "id": "newgame.exe", "hint": "New Game"
    }, blocking=True)
    key = "alex|process|newgame.exe"
    assert key in coordinator.unknowns
    version = coordinator.rules.version
    await coordinator.async_assign_unknown(key, "games")
    assert coordinator.rules.processes["newgame.exe"] == "games"
    assert coordinator.rules.version == version + 1
    assert key not in coordinator.unknowns


async def test_reset_and_rules_response(hass: HomeAssistant):
    coordinator = await _setup(hass)
    await coordinator.async_report_usage("alex", "pc", "video", 5)
    await hass.services.async_call(DOMAIN, "reset", {"kid": "alex", "category": "video"}, blocking=True)
    assert coordinator.total("alex", "video") == 0
    response = await hass.services.async_call(DOMAIN, "get_rules", {}, blocking=True, return_response=True)
    assert response["version"] >= 1
    assert any(item["id"] == "unclassified" for item in response["categories"])
