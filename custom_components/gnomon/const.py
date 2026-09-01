"""Constants for Gnomon."""

from __future__ import annotations

from typing import Final

DOMAIN: Final = "gnomon"
PLATFORMS: Final = ("sensor", "number", "binary_sensor")
STORAGE_KEY: Final = "gnomon.state"
STORAGE_VERSION: Final = 1
UNCLASSIFIED: Final = "unclassified"
SLUG_PATTERN: Final = r"^[a-z0-9_]+$"
AGENT_STALE_MINUTES: Final = 15
SIGNAL_STATE_CHANGED: Final = f"{DOMAIN}_state_changed"

DEFAULT_CATEGORIES: Final = (
    {"id": "games", "name": "Games", "idle_timeout_min": 3, "media_counts_as_active": False},
    {"id": "video", "name": "Video", "idle_timeout_min": 3, "media_counts_as_active": True},
    {"id": "social", "name": "Social", "idle_timeout_min": 3, "media_counts_as_active": False},
    {"id": "schoolwork", "name": "Schoolwork", "idle_timeout_min": 10, "media_counts_as_active": True},
    {"id": UNCLASSIFIED, "name": "Unclassified", "idle_timeout_min": 3, "media_counts_as_active": False},
)

SEED_PROCESSES: Final = {
    "robloxplayerbeta.exe": "games", "javaw.exe": "games",
    "fortniteclient-win64-shipping.exe": "games", "minecraft.exe": "games",
    "minecraftlauncher.exe": "games", "steam.exe": "games", "epicgameslauncher.exe": "games",
    "com.roblox.client": "games", "com.mojang.minecraftpe": "games",
    "com.supercell.brawlstars": "games", "com.supercell.clashofclans": "games",
    "com.epicgames.fortnite": "games", "com.innersloth.spacemafia": "games",
    "winword.exe": "schoolwork", "excel.exe": "schoolwork", "powerpnt.exe": "schoolwork",
    "onenote.exe": "schoolwork", "code.exe": "schoolwork", "devenv.exe": "schoolwork",
    "com.google.android.apps.docs.editors.docs": "schoolwork",
    "com.google.android.apps.classroom": "schoolwork",
    "com.google.android.youtube": "video", "com.netflix.mediaclient": "video",
    "com.disney.disneyplus": "video", "com.hulu.plus": "video",
    "com.zhiliaoapp.musically": "social", "com.discord": "social",
    "com.instagram.android": "social", "com.snapchat.android": "social",
}

SEED_DOMAINS: Final = {
    "youtube.com": "video", "youtu.be": "video", "netflix.com": "video",
    "hulu.com": "video", "disneyplus.com": "video", "twitch.tv": "video",
    "tiktok.com": "social", "discord.com": "social", "instagram.com": "social",
    "reddit.com": "social", "facebook.com": "social", "x.com": "social",
    "roblox.com": "games", "minecraft.net": "games", "steampowered.com": "games",
    "classroom.google.com": "schoolwork", "docs.google.com": "schoolwork",
    "khanacademy.org": "schoolwork", "quizlet.com": "schoolwork",
}


def usage_device_unique_id(kid: str, device: str, category: str) -> str:
    return f"gnomon_used_{kid}_{device}_{category}"


def usage_total_unique_id(kid: str, category: str) -> str:
    return f"gnomon_used_{kid}_{category}"


def limit_unique_id(kid: str, category: str) -> str:
    return f"gnomon_limit_{kid}_{category}"


def usage_overall_unique_id(kid: str) -> str:
    return f"gnomon_used_{kid}_total"


def usage_device_total_unique_id(kid: str, device: str) -> str:
    return f"gnomon_used_{kid}_{device}_total"


def limit_overall_unique_id(kid: str) -> str:
    return f"gnomon_limit_{kid}_total"


def limit_device_unique_id(kid: str, device: str) -> str:
    return f"gnomon_limit_{kid}_{device}_total"
