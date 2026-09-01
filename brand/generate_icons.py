"""Generate committed PNG/ICO variants from the two-shape Gnomon mark."""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
TERRACOTTA = "#c46b44"
NAVY = "#151f2e"


def cubic(p0, p1, p2, p3, steps=24):
    for index in range(1, steps + 1):
        t = index / steps
        u = 1 - t
        yield (
            u**3 * p0[0] + 3 * u**2 * t * p1[0] + 3 * u * t**2 * p2[0] + t**3 * p3[0],
            u**3 * p0[1] + 3 * u**2 * t * p1[1] + 3 * u * t**2 * p2[1] + t**3 * p3[1],
        )


def master(size=1024):
    scale = size / 64
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.ellipse(tuple(value * scale for value in (5, 5, 59, 59)), fill=TERRACOTTA)

    points = [(27.5, 48)]
    points += list(cubic((27.5, 48), (29.7, 34.2), (33.6, 22.2), (39.7, 11.9)))
    points += list(cubic((39.7, 11.9), (41, 9.7), (44.4, 10.6), (44.4, 13.2)))
    points.append((44.4, 38.3))
    points += list(cubic((44.4, 38.3), (44.4, 43.8), (39.9, 48.2), (34.5, 48.2)))
    points.append((27.5, 48))
    draw.polygon([(round(x * scale), round(y * scale)) for x, y in points], fill=NAVY)
    return image


def save_png(source, path, size):
    path.parent.mkdir(parents=True, exist_ok=True)
    source.resize((size, size), Image.Resampling.LANCZOS).save(path, optimize=True)


def main():
    source = master()
    save_png(source, ROOT / "brand" / "icon.png", 256)

    ico_path = ROOT / "windows" / "src" / "Gnomon.Agent" / "Assets" / "gnomon.ico"
    ico_path.parent.mkdir(parents=True, exist_ok=True)
    source.save(ico_path, format="ICO", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])

    android_res = ROOT / "android" / "app" / "src" / "main" / "res"
    for density, size in {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}.items():
        save_png(source, android_res / f"mipmap-{density}" / "ic_launcher.png", size)


if __name__ == "__main__":
    main()
