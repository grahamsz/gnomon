"""UI config and structural options flow for Gnomon."""

from __future__ import annotations

import re
from typing import Any

import voluptuous as vol

from homeassistant import config_entries
from homeassistant.core import callback
from homeassistant.helpers import selector

from .const import DOMAIN, SLUG_PATTERN, UNCLASSIFIED


def slugify(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", value.lower()).strip("_")


class GnomonConfigFlow(config_entries.ConfigFlow, domain=DOMAIN):
    VERSION = 1

    async def async_step_user(self, user_input: dict[str, Any] | None = None):
        # Gnomon is one hub with multiple kids. Stop before presenting the
        # first-kid form again and direct the user to the existing entry's
        # options flow, where additional kids belong.
        if self._async_current_entries():
            return self.async_abort(reason="already_configured")

        errors = {}
        if user_input is not None:
            kid_id = slugify(user_input["kid_id"])
            if not kid_id or not re.fullmatch(SLUG_PATTERN, kid_id):
                errors["base"] = "invalid_slug"
            else:
                await self.async_set_unique_id(DOMAIN)
                self._abort_if_unique_id_configured()
                return self.async_create_entry(
                    title="Gnomon", data={"kids": [{"id": kid_id, "name": user_input["kid_name"].strip()}]}
                )
        return self.async_show_form(
            step_id="user",
            data_schema=vol.Schema({vol.Required("kid_name"): str, vol.Required("kid_id"): str}),
            errors=errors,
        )

    @staticmethod
    @callback
    def async_get_options_flow(config_entry):
        return GnomonOptionsFlow(config_entry)


class GnomonOptionsFlow(config_entries.OptionsFlow):
    """Add kids/categories/rules without YAML.

    Existing limits remain primarily editable through number entities. Destructive
    category removal is intentionally conservative and performed in its own step.
    """

    def __init__(self, entry) -> None:
        self.entry = entry

    @property
    def coordinator(self):
        return self.hass.data[DOMAIN][self.entry.entry_id]

    async def async_step_init(self, user_input=None):
        return self.async_show_menu(
            step_id="init", menu_options=("add_kid", "add_category", "rename_category", "remove_category", "add_rule", "remove_rule")
        )

    async def async_step_add_kid(self, user_input=None):
        errors = {}
        if user_input:
            kid_id = slugify(user_input["kid_id"])
            if not kid_id or kid_id in self.coordinator.kids:
                errors["base"] = "duplicate_or_invalid"
            else:
                kids = [
                    {"id": value.id, "name": value.name} for value in self.coordinator.kids.values()
                ] + [{"id": kid_id, "name": user_input["kid_name"].strip()}]
                return self.async_create_entry(title="", data={"kids": kids})
        return self.async_show_form(
            step_id="add_kid", data_schema=vol.Schema({vol.Required("kid_name"): str, vol.Required("kid_id"): str}),
            errors=errors,
        )

    async def async_step_add_category(self, user_input=None):
        errors = {}
        if user_input:
            category_id = slugify(user_input["id"])
            if not category_id or category_id in self.coordinator.rules.categories:
                errors["base"] = "duplicate_or_invalid"
            else:
                from .models import Category
                self.coordinator.rules.categories[category_id] = Category(
                    id=category_id, name=user_input["name"].strip(),
                    idle_timeout_min=user_input["idle_timeout_min"],
                    media_counts_as_active=user_input["media_counts_as_active"],
                )
                await self.coordinator.async_rules_mutated()
                return self.async_create_entry(title="", data=dict(self.entry.options))
        return self.async_show_form(step_id="add_category", data_schema=vol.Schema({
            vol.Required("id"): str, vol.Required("name"): str,
            vol.Required("idle_timeout_min", default=3): vol.All(vol.Coerce(int), vol.Range(min=1, max=60)),
            vol.Required("media_counts_as_active", default=False): bool,
        }), errors=errors)

    async def async_step_remove_category(self, user_input=None):
        removable = [key for key in self.coordinator.rules.categories if key != UNCLASSIFIED]
        errors = {}
        if user_input:
            category = user_input["category"]
            in_use = any(values.get(category, 0) > 0 for devices in self.coordinator.usage.values() for values in devices.values())
            mapped = category in self.coordinator.rules.processes.values() or category in self.coordinator.rules.domains.values()
            if in_use or mapped:
                errors["base"] = "category_in_use"
            else:
                del self.coordinator.rules.categories[category]
                await self.coordinator.async_rules_mutated()
                return self.async_create_entry(title="", data=dict(self.entry.options))
        return self.async_show_form(step_id="remove_category", data_schema=vol.Schema({
            vol.Required("category"): vol.In(removable)
        }), errors=errors)

    async def async_step_rename_category(self, user_input=None):
        if user_input:
            self.coordinator.rules.categories[user_input["category"]].name = user_input["name"].strip()
            await self.coordinator.async_rules_mutated()
            return self.async_create_entry(title="", data=dict(self.entry.options))
        return self.async_show_form(step_id="rename_category", data_schema=vol.Schema({
            vol.Required("category"): vol.In(list(self.coordinator.rules.categories)),
            vol.Required("name"): str,
        }))

    async def async_step_add_rule(self, user_input=None):
        errors = {}
        if user_input:
            rule_id = user_input["id"].lower().strip()
            target = (self.coordinator.rules.processes if user_input["kind"] == "process"
                      else self.coordinator.rules.domains)
            if not rule_id:
                errors["base"] = "invalid_slug"
            else:
                if user_input.get("kid"):
                    override = self.coordinator.rules.overrides.setdefault(
                        user_input["kid"], {"processes": {}, "domains": {}}
                    )
                    override["processes" if user_input["kind"] == "process" else "domains"][rule_id] = user_input["category"]
                else:
                    target[rule_id] = user_input["category"]
                await self.coordinator.async_rules_mutated()
                return self.async_create_entry(title="", data=dict(self.entry.options))
        return self.async_show_form(step_id="add_rule", data_schema=vol.Schema({
            vol.Required("kind"): vol.In(("process", "domain")), vol.Required("id"): str,
            vol.Required("category"): vol.In(list(self.coordinator.rules.categories)),
            vol.Optional("kid"): vol.In(["", *self.coordinator.kids]),
        }), errors=errors)

    async def async_step_remove_rule(self, user_input=None):
        choices = [f"process:{key}" for key in self.coordinator.rules.processes]
        choices += [f"domain:{key}" for key in self.coordinator.rules.domains]
        if user_input:
            kind, item_id = user_input["rule"].split(":", 1)
            target = self.coordinator.rules.processes if kind == "process" else self.coordinator.rules.domains
            target.pop(item_id, None)
            await self.coordinator.async_rules_mutated()
            return self.async_create_entry(title="", data=dict(self.entry.options))
        return self.async_show_form(step_id="remove_rule", data_schema=vol.Schema({
            vol.Required("rule"): vol.In(choices)
        }))
