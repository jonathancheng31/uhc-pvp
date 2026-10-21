#!/usr/bin/env python3
"""Generates the placeholder block texture atlas (assets/textures/blocks.png).

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


if __name__ == "__main__":
    main()
