"""Serializable domain models for Gnomon."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from typing import Any


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


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


@dataclass(slots=True)
class UnknownItem:
    kind: str
    id: str
    kid: str
    device: str
    hint: str = ""
    first_seen: str = field(default_factory=utc_now_iso)
    last_seen: str = field(default_factory=utc_now_iso)
    minutes_seen: int = 0

    @property
    def key(self) -> str:
        return f"{self.kid}|{self.kind}|{self.id}"

    @classmethod
    def from_dict(cls, value: dict[str, Any]) -> "UnknownItem":
        return cls(**value)


@dataclass(slots=True)
class UsageItem:
    """Persisted per-app/domain usage used by the classification workbench."""

    kind: str
    id: str
    kid: str
    label: str = ""
    minutes: int = 0
    last_category: str = "unclassified"
    first_seen: str = field(default_factory=utc_now_iso)
    last_seen: str = field(default_factory=utc_now_iso)
    devices: list[str] = field(default_factory=list)

    @property
    def key(self) -> str:
        return f"{self.kid}|{self.kind}|{self.id}"

    @classmethod
    def from_dict(cls, value: dict[str, Any]) -> "UsageItem":
        return cls(
            kind=value["kind"], id=value["id"], kid=value["kid"],
            label=value.get("label", ""), minutes=int(value.get("minutes", 0)),
            last_category=value.get("last_category", "unclassified"),
            first_seen=value.get("first_seen", utc_now_iso()),
            last_seen=value.get("last_seen", utc_now_iso()),
            devices=list(value.get("devices", [])),
        )
