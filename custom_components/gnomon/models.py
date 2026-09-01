"""Serializable domain models for Gnomon."""
from __future__ import annotations

from dataclasses import asdict, dataclass, field
from typing import Any


@dataclass(slots=True)
class Category:
    id: str
    name: str
    idle_timeout_min: int = 3
    media_counts_as_active: bool = False

    @classmethod
    def from_dict(cls, value: dict[str, Any]) -> "Category":
        return cls(
            id=value["id"], name=value["name"],
            idle_timeout_min=int(value.get("idle_timeout_min", 3)),
            media_counts_as_active=bool(value.get("media_counts_as_active", False)),
        )


@dataclass(slots=True)
class Kid:
    id: str
    name: str


@dataclass(slots=True)
class RulesMap:
    version: int = 1
    categories: dict[str, Category] = field(default_factory=dict)
    processes: dict[str, str] = field(default_factory=dict)
    domains: dict[str, str] = field(default_factory=dict)
    overrides: dict[str, dict[str, dict[str, str]]] = field(default_factory=dict)

    def response(self) -> dict[str, Any]:
        return {
            "version": self.version,
            "categories": [asdict(category) for category in self.categories.values()],
            "processes": dict(self.processes), "domains": dict(self.domains),
            "overrides": self.overrides,
        }
