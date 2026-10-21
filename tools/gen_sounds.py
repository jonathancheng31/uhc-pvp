#!/usr/bin/env python3
"""Generates our own sound effects (assets/sounds/*.wav) from simple synthesis.

hurt.wav: a short, punchy hit: a falling low thump plus a filtered noise crack.
Seeded, so it rebuilds identically. Uses only the Python standard library.
"""
import math
import random
import struct
import wave
from pathlib import Path

RATE = 44100
OUT = Path(__file__).resolve().parent.parent / "assets" / "sounds"


def write(name, samples):
    OUT.mkdir(parents=True, exist_ok=True)
    peak = max(abs(s) for s in samples) or 1.0
    with wave.open(str(OUT / name), "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(RATE)
        f.writeframes(b"".join(struct.pack("<h", int(s / peak * 0.8 * 32767)) for s in samples))
    print(f"wrote {OUT / name}")


def hurt():
    rng = random.Random(1889)
    n = int(RATE * 0.22)
    out = []
    phase = 0.0
    low = 0.0
    for i in range(n):
        t = i / RATE
        freq = 70 + 150 * math.exp(-t / 0.03)  # pitch drops fast: a thump
        phase += 2 * math.pi * freq / RATE
        thump = math.sin(phase) * math.exp(-t / 0.06)
        noise = rng.uniform(-1, 1)
        low += (noise - low) * 0.25  # one-pole low-pass: softer crack
        crack = low * math.exp(-t / 0.018) * 1.4
        attack = min(1.0, t / 0.002)  # avoid a click at the start
        out.append((thump + crack) * attack)
    return out


if __name__ == "__main__":
    write("hurt.wav", hurt())
