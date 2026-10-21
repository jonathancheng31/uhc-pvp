#!/usr/bin/env python3
"""Generates our placeholder textures: the block atlas (assets/textures/blocks.png) and the
HUD icons (assets/textures/hud/*.png).

Every pixel is made here from seeded noise, so the textures are our own and
rebuild identically. Tile order must match the TILE_* constants in
scripts/world/blocks.gd. Requires Pillow: pip install pillow
"""
import random
from pathlib import Path

from PIL import Image

TILE = 16
COLUMNS = 4
OUT = Path(__file__).resolve().parent.parent / "assets" / "textures" / "blocks.png"


def clamp(v):
    return max(0, min(255, int(v)))


def noisy(rng, base, spread):
    shift = rng.randint(-spread, spread)
    return tuple(clamp(c + shift) for c in base)


def tile_noise(rng, base, spread, speckle=None, speckle_chance=0.0):
    img = Image.new("RGBA", (TILE, TILE))
    for y in range(TILE):
        for x in range(TILE):
            color = noisy(rng, base, spread)
            if speckle and rng.random() < speckle_chance:
                color = noisy(rng, speckle, spread // 2)
            img.putpixel((x, y), color + (255,))
    return img


def grass_top(rng):
    return tile_noise(rng, (92, 156, 54), 14, speckle=(70, 128, 40), speckle_chance=0.18)


def dirt(rng):
    return tile_noise(rng, (128, 92, 64), 12, speckle=(98, 70, 48), speckle_chance=0.2)


def grass_side(rng):
    img = dirt(rng)
    for x in range(TILE):
        depth = 3 + rng.randint(0, 2) - (1 if x % 5 == 0 else 0)
        for y in range(depth):
            img.putpixel((x, y), noisy(rng, (92, 156, 54), 12) + (255,))
    return img


def stone(rng):
    return tile_noise(rng, (122, 122, 124), 10, speckle=(98, 98, 102), speckle_chance=0.22)


def wall(rng):
    return tile_noise(rng, (48, 50, 58), 10, speckle=(84, 88, 100), speckle_chance=0.15)


def planks(rng):
    img = Image.new("RGBA", (TILE, TILE))
    for y in range(TILE):
        board = y // 4
        for x in range(TILE):
            base = (164, 128, 78) if board % 2 == 0 else (152, 118, 70)
            color = noisy(rng, base, 8)
            seam = (x + board * 7) % TILE == 0
            if y % 4 == 3 or seam:
                color = noisy(rng, (112, 84, 50), 6)
            img.putpixel((x, y), color + (255,))
    return img


TILES = [grass_top, grass_side, dirt, stone, wall, planks]

HUD_DIR = OUT.parent / "hud"

# 9x9 icons drawn from character maps: . transparent, o outline, and palette letters.
HEART = [
    ".oo...oo.",
    "orrooorro",
    "orwrrrrro",
    "orrrrrrro",
    "orrrrrrdo",
    ".orrrrdo.",
    "..orrdo..",
    "...odo...",
    "....o....",
]
ARMOR = [
    ".oo...oo.",
    "oggoooggo",
    "ogwgggggo",
    ".ogggggo.",
    ".ogggggo.",
    ".ogggggo.",
    ".ogggsso.",
    ".oooooo..",
    ".........",
]
SWORD = [
    "..............oo",
    ".............olo",
    "............olco",
    "...........olco.",
    "..........olco..",
    ".........olco...",
    "........olco....",
    "...oo..olco.....",
    "...ogoolco......",
    "....oglco.......",
    "....oggo........",
    "...ohoogo.......",
    "..ohho.oo.......",
    "oohho...........",
    "ohho............",
    "ooo.............",
]
PALETTE_FULL = {"o": (32, 16, 16, 255), "r": (214, 38, 46, 255), "w": (255, 200, 200, 255), "d": (150, 20, 28, 255),
                "g": (210, 214, 222, 255), "s": (150, 154, 162, 255), "l": (230, 252, 255, 255), "c": (110, 210, 225, 255), "h": (120, 76, 44, 255)}


def icon(rows, palette):
    img = Image.new("RGBA", (len(rows[0]), len(rows)), (0, 0, 0, 0))
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch != ".":
                img.putpixel((x, y), palette.get(ch, (255, 0, 255, 255)))
    return img


def empty_version(rows):
    """Same outline, dark translucent inside: an empty heart or armor slot."""
    palette = {"o": (32, 16, 16, 255)}
    for k in "rwdgsl":
        palette[k] = (40, 40, 44, 200)
    return icon(rows, palette)


def half_version(full, empty):
    img = empty.copy()
    w, h = full.size
    for y in range(h):
        for x in range(w // 2 + 1):
            img.putpixel((x, y), full.getpixel((x, y)))
    return img


def slot(size, border, fill):
    img = Image.new("RGBA", (size, size), fill)
    for i in range(size):
        for t in range(2):
            img.putpixel((i, t), border)
            img.putpixel((i, size - 1 - t), border)
            img.putpixel((t, i), border)
            img.putpixel((size - 1 - t, i), border)
    return img


def rod_icon():
    """Our fishing rod: a pole leaning like the sword, with the line hanging to a bobber."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    outline, grip, pole, reel = (32, 16, 16, 255), (90, 58, 34, 255), (176, 124, 72, 255), (150, 154, 162, 255)
    line, red, white = (50, 50, 54, 255), (214, 38, 46, 255), (240, 240, 236, 255)
    shaft = [(x, 15 - x) for x in range(1, 13)]
    for x, y in shaft:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            p = (x + dx, y + dy)
            if p not in shaft and 0 <= p[0] < 16 and 0 <= p[1] < 16:
                img.putpixel(p, outline)
    for i, p in enumerate(shaft):
        img.putpixel(p, grip if i < 4 else pole)
    for p in ((5, 12), (6, 12), (6, 13)):
        img.putpixel(p, reel)
    for y in range(3, 10):
        img.putpixel((13, y), line)
    for x in range(12, 15):
        img.putpixel((x, 9), outline if x != 13 else line)
        img.putpixel((x, 10), red)
        img.putpixel((x, 11), white)
        img.putpixel((x, 12), outline)
    for y in (10, 11):
        img.putpixel((11, y), outline)
        img.putpixel((15, y), outline)
    return img


def make_hud():
    HUD_DIR.mkdir(parents=True, exist_ok=True)
    for name, rows in (("heart", HEART), ("armor", ARMOR)):
        full = icon(rows, PALETTE_FULL)
        empty = empty_version(rows)
        full.save(HUD_DIR / f"{name}_full.png")
        half_version(full, empty).save(HUD_DIR / f"{name}_half.png")
        empty.save(HUD_DIR / f"{name}_empty.png")
    icon(SWORD, PALETTE_FULL).save(HUD_DIR / "sword.png")
    rod_icon().save(HUD_DIR / "rod.png")
    slot(22, (60, 60, 66, 255), (0, 0, 0, 110)).save(HUD_DIR / "slot.png")
    slot(24, (240, 240, 240, 255), (0, 0, 0, 0)).save(HUD_DIR / "slot_selected.png")
    print(f"wrote HUD icons to {HUD_DIR}")


def main():
    rng = random.Random(1889)  # fixed seed: same textures every run
    rows = (len(TILES) + COLUMNS - 1) // COLUMNS
    atlas = Image.new("RGBA", (TILE * COLUMNS, TILE * COLUMNS), (0, 0, 0, 0))
    assert rows <= COLUMNS
    for i, make in enumerate(TILES):
        atlas.paste(make(rng), ((i % COLUMNS) * TILE, (i // COLUMNS) * TILE))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(OUT)
    print(f"wrote {OUT}")
    make_hud()


if __name__ == "__main__":
    main()
