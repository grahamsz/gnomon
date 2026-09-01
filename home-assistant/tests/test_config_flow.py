"""Configuration-flow tests for the single-hub, multiple-kid model."""

from homeassistant.config_entries import SOURCE_USER
from homeassistant.core import HomeAssistant
from homeassistant.data_entry_flow import FlowResultType
from pytest_homeassistant_custom_component.common import MockConfigEntry

from custom_components.gnomon.const import DOMAIN


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
