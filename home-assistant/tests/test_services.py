"""Behavioral tests for aggregate accounting and rule synchronization."""

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


async def test_raw_activity_reports_are_compatibility_noops(hass: HomeAssistant):
    coordinator = await _setup(hass)
    await hass.services.async_call(DOMAIN, "report_unknown", {
        "kid": "alex", "device": "pc", "kind": "process", "id": "newgame.exe", "hint": "New Game"
    }, blocking=True)
    assert not hasattr(coordinator, "unknowns")
    assert hass.states.get("sensor.gnomon_unclassified") is None


async def test_reset_and_rules_response(hass: HomeAssistant):
    coordinator = await _setup(hass)
    await coordinator.async_report_usage("alex", "pc", "video", 5)
    await hass.services.async_call(DOMAIN, "reset", {"kid": "alex", "category": "video"}, blocking=True)
    assert coordinator.total("alex", "video") == 0
    response = await hass.services.async_call(DOMAIN, "get_rules", {}, blocking=True, return_response=True)
    assert response["version"] >= 1
    assert any(item["id"] == "unclassified" for item in response["categories"])


async def test_classification_sync_stores_only_rule(hass: HomeAssistant):
    coordinator = await _setup(hass)
    await hass.services.async_call(DOMAIN, "report_usage", {
        "kid": "alex", "device": "pc", "category": "unclassified", "minutes": 7,
        "app_id": "news.example.com", "kind": "domain", "app_label": "Example News",
    }, blocking=True)
    assert not hasattr(coordinator, "usage_items")

    version = coordinator.rules.version
    updated = await hass.services.async_call(DOMAIN, "set_classification", {
        "kid": "alex", "kind": "domain", "id": "news.example.com",
        "category": "schoolwork",
    }, blocking=True, return_response=True)
    assert updated["version"] == version + 1
    assert "items" not in updated
    assert coordinator.rules.overrides["alex"]["domains"]["news.example.com"] == "schoolwork"


async def test_overall_and_device_limits_are_independent(hass: HomeAssistant):
    coordinator = await _setup(hass)
    await coordinator.async_set_overall_limit("alex", 10)
    await coordinator.async_set_overall_limit("alex", 6, "pc")
    events = []
    hass.bus.async_listen("gnomon_limit_reached", lambda event: events.append(event.data))
    await coordinator.async_report_usage("alex", "pc", "games", 7)
    await coordinator.async_report_usage("alex", "phone", "video", 4)
    assert coordinator.total_all("alex") == 11
    assert coordinator.device_total("alex", "pc") == 7
    assert {event["scope"] for event in events} == {"child", "device"}
    await hass.async_block_till_done()
    status = await hass.services.async_call(
        DOMAIN, "get_status", {"kid": "alex", "device": "pc"},
        blocking=True, return_response=True,
    )
    assert status["child"] == {"used": 11, "limit": 10}
    assert status["device"] == {"id": "pc", "used": 7, "limit": 6}
    assert next(item for item in status["categories"] if item["id"] == "games")["used"] == 7
    assert not any("pc_games" in entity_id for entity_id in hass.states.async_entity_ids())
    assert not any(entity_id.startswith("select.gnomon_") for entity_id in hass.states.async_entity_ids())
