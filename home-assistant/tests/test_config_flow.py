"""Configuration-flow tests for the single-hub, multiple-kid model."""

import json
from pathlib import Path

from homeassistant.config_entries import SOURCE_USER
from homeassistant.core import HomeAssistant
from homeassistant.data_entry_flow import FlowResultType
from pytest_homeassistant_custom_component.common import MockConfigEntry

from custom_components.gnomon.const import DOMAIN


MENU_OPTIONS = {
    "add_kid",
    "add_category",
    "rename_category",
    "remove_category",
    "add_rule",
    "remove_rule",
}


async def test_existing_hub_directs_user_to_options(hass: HomeAssistant):
    """A second Add Integration attempt should explain how to add a kid."""
    entry = MockConfigEntry(
        domain=DOMAIN,
        unique_id=DOMAIN,
        data={"kids": [{"id": "test", "name": "Test"}]},
    )
    entry.add_to_hass(hass)

    result = await hass.config_entries.flow.async_init(
        DOMAIN, context={"source": SOURCE_USER}
    )

    assert result["type"] is FlowResultType.ABORT
    assert result["reason"] == "already_configured"


async def test_options_menu_exposes_add_kid(hass: HomeAssistant):
    """The existing hub's Configure action should expose kid management."""
    entry = MockConfigEntry(
        domain=DOMAIN,
        unique_id=DOMAIN,
        data={"kids": [{"id": "test", "name": "Test"}]},
    )
    entry.add_to_hass(hass)

    result = await hass.config_entries.options.async_init(entry.entry_id)

    assert result["type"] is FlowResultType.MENU
    assert set(result["menu_options"]) == MENU_OPTIONS


def test_options_menu_has_labels():
    """Every options route must have a visible Home Assistant menu label."""
    root = Path(__file__).parents[2] / "custom_components" / DOMAIN
    for relative_path in ("strings.json", "translations/en.json"):
        content = json.loads((root / relative_path).read_text(encoding="utf-8"))
        labels = content["options"]["step"]["init"]["menu_options"]
        assert set(labels) == MENU_OPTIONS
        assert all(labels.values())
