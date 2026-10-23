#!/usr/bin/env python3
"""Generates the game's own sound effects (assets/sounds/*.wav) by synthesis. Needs numpy.

Every sound is built from a few simple pieces: pitch-dropping thumps, filtered noise, the
decaying "modes" of a struck object, and grains (tiny clicks). Each one comes in several
seeded variants, so it rebuilds identically and repeats never sound the same; the game also
shifts pitch and volume a little on every play (scripts/Core/SoundBank.cs).

The fight sounds are one family: the attacker's hit, the critical hit and getting hurt share
the same crack, punch and room, so played together they read as one blow.

Every file is mastered to the same loudness (TARGET_LUFS, the loudest 100 ms, measured like
EBU R128 loudness meters do) with its peaks at most CEILING_DB, and starts at its first
sample, so it lands on the frame it's played. The game sets the mix (SoundBank.cs).

    python3 tools/gen_sounds.py            # rebuild every sound, then re-import in Godot
    python3 tools/gen_sounds.py --report   # peak, loudness, length and tone of each file
    python3 tools/gen_sounds.py --analyze FILE.wav [...]
        # the same for any recording, e.g. one made with the game's --record-audio option,
        # plus how loud it gets second by second and whether anything clipped
"""
import sys
import wave
from functools import cache
from pathlib import Path

import numpy as np

RATE = 44100
OUT = Path(__file__).resolve().parent.parent / "assets" / "sounds"
TARGET_LUFS = -14.0
CEILING_DB = -1.0


# ---------------------------------------------------------------- building blocks

def seconds(duration):
    return int(round(duration * RATE))


def timeline(duration):
    return np.arange(seconds(duration)) / RATE


def env(t, attack, decay, hold=0.0):
    """Rises over `attack` seconds (a smooth S-curve), holds, then decays exponentially."""
    rise = np.where(t < attack, 0.5 - 0.5 * np.cos(np.pi * np.minimum(t / attack, 1.0)), 1.0)
    fall = np.where(t < attack + hold, 1.0, np.exp(-(t - attack - hold) / decay))
    return rise * fall


def delayed(x, delay):
    """`x` starting `delay` seconds later (same length)."""
    d = seconds(delay)
    return np.concatenate([np.zeros(d), x[: len(x) - d]]) if d > 0 else x


def svf(x, cutoff, q=0.707, kind="low"):
    """A state-variable filter (Zavalishin's), so the cutoff can sweep sample by sample."""
    n = len(x)
    cutoff = np.broadcast_to(np.minimum(cutoff, 0.45 * RATE), (n,)).astype(float)
    q = np.broadcast_to(q, (n,)).astype(float)
    g = np.tan(np.pi * cutoff / RATE)
    k = 1.0 / q
    a1 = 1.0 / (1.0 + g * (g + k))
    a2 = g * a1
    a3 = g * a2
    out = [0.0] * n
    ic1 = ic2 = 0.0
    for i, (xi, b1, b2, b3, ki) in enumerate(zip(x.tolist(), a1.tolist(), a2.tolist(), a3.tolist(), k.tolist())):
        v3 = xi - ic2
        v1 = b1 * ic1 + b2 * v3
        v2 = ic2 + b2 * ic1 + b3 * v3
        ic1 = 2 * v1 - ic1
        ic2 = 2 * v2 - ic2
        out[i] = v2 if kind == "low" else v1 if kind == "band" else xi - ki * v1 - v2
    return np.array(out)


def thump(t, f_start, f_end, drop, decay, attack=0.0012):
    """A sine whose pitch falls fast from f_start to f_end: the body of an impact."""
    f = f_end + (f_start - f_end) * np.exp(-t / drop)
    return np.sin(2 * np.pi * np.cumsum(f) / RATE) * env(t, attack, decay)


def modes(t, freqs, decays, amps, attack=0.0004):
    """A struck object: decaying sines at its resonant frequencies."""
    out = np.zeros_like(t)
    for f, d, a in zip(freqs, decays, amps):
        if f < 0.45 * RATE:
            out += a * np.sin(2 * np.pi * f * t) * np.exp(-t / d)
    return out * env(t, attack, 1e9)


def crack(rng, t, highpass, decay):
    """The sharp front edge of a blow: a very short burst of bright noise."""
    burst = rng.standard_normal(len(t)) * env(t, 0.00015, decay)
    return svf(svf(burst, highpass, 0.6, "high"), 11000, 0.6, "low")


def grains(rng, t, times, lengths, amps):
    """Tiny noise clicks at the given times (crunching grass, crackling sparks)."""
    out = np.zeros_like(t)
    for start, length, amp in zip(times, lengths, amps):
        i, n = seconds(start), max(seconds(length), 4)
        if i + n >= len(t):
            continue
        out[i:i + n] += amp * rng.standard_normal(n) * np.hanning(n)
    return out


def blade_ring(rng, t, base, decay, amp):
    """The short, bright ring of the sword's edge on a hit: inharmonic partials, two of them
    close enough to shimmer against each other."""
    ratios = np.array([1.0, 1.006, 1.507, 2.08, 2.71, 3.38])
    gains = np.array([1.0, 0.55, 0.7, 0.45, 0.3, 0.18])
    decays = decay * np.array([1.0, 1.0, 0.75, 0.55, 0.42, 0.33])
    freqs = base * ratios * rng.uniform(0.985, 1.015, len(ratios))
    return amp * modes(t, freqs, decays, gains) / gains.sum()


def string_twang(rng, t, f0, decay, start):
    """A taut line plucked: harmonics of a slightly stiff string, sliding up to pitch as it
    snaps tight, the high ones dying first."""
    out = np.zeros_like(t)
    glide = 0.82 + 0.18 * (1 - np.exp(-t / 0.012))
    for n in range(1, 11):
        f = f0 * n * np.sqrt(1 + 0.0004 * n * n) * glide
        phase = 2 * np.pi * np.cumsum(f) / RATE
        out += np.sin(phase) * np.exp(-t / (decay / (1 + 0.45 * (n - 1)))) / n ** 1.1
    return delayed(out * env(t, 0.0015, 1e9), start)


def soft_clip(x, drive):
    """Gentle saturation: rounds the loudest moments and thickens the sound."""
    return np.tanh(drive * x) / np.tanh(drive)


@cache
def room(seed=7):
    """The same small outdoor space for every fight sound: a ground reflection and a short,
    darkening tail, so the family sounds like it happens in one place."""
    rng = np.random.default_rng(seed)
    t = timeline(0.16)
    tail = rng.standard_normal(len(t)) * np.exp(-t / 0.035)
    tail = svf(tail, 4200, 0.6, "low")
    tail[: seconds(0.006)] = 0
    ir = tail / np.sqrt(np.sum(tail ** 2))
    for delay, gain in ((0.0043, 0.5), (0.0091, 0.28), (0.0137, 0.18)):
        ir[seconds(delay)] += gain
    return ir


def fade_out(x, duration=0.02):
    """Fades the last stretch to silence, so a sound cut at its buffer's end never clicks."""
    n = min(seconds(duration), len(x) // 4)
    x = x.copy()
    x[-n:] *= np.cos(np.linspace(0, np.pi / 2, n)) ** 2
    return x


def pinkish(rng, n):
    """Noise with its top end turned down (from about 1 kHz up, ending some 15 dB under the
    lows): softer than white noise."""
    white = rng.standard_normal(n)
    return 0.35 * white + svf(white, 1000, 0.5, "low") * 1.6


def in_room(x, wet_db):
    """`x` in the shared room, its reflections `wet_db` under the sound itself."""
    x = fade_out(x)
    wet = np.convolve(x, room())
    dry = np.concatenate([x, np.zeros(len(room()) - 1)])
    return dry + 10 ** (wet_db / 20) * wet


# ---------------------------------------------------------------- the sounds

def swing(rng):
    """Sword swing. Shaped on the swing animation: the blade is fastest at the start of the
    chop and stops a quarter of the way through (75 ms), so the whoosh swells and rises in
    pitch with the blade's speed, falls as it slows, and is gone before the arm comes back.
    Soft noise through a fairly narrow, sweeping filter: a "vwsh", not a hiss."""
    t = timeline(0.15)
    peak = rng.uniform(0.009, 0.014)
    stop = rng.uniform(0.065, 0.08)
    rise = np.sin(0.5 * np.pi * np.minimum(t / peak, 1.0)) ** 0.8
    fall = np.where(t < peak, 1.0, np.exp(-(np.maximum(t - peak, 0) / (stop - peak) * 2.0) ** 1.6))
    speed = rise * fall
    lo, hi = rng.uniform(480, 650), rng.uniform(1600, 2300)
    center = lo + (hi - lo) * speed
    body = svf(pinkish(rng, len(t)), center, rng.uniform(2.0, 2.8), "band") * speed ** 1.2
    edge = svf(pinkish(rng, len(t)), center * rng.uniform(2.0, 2.3), 2.0, "band") * speed ** 1.6
    air = svf(rng.standard_normal(len(t)), rng.uniform(260, 340), 0.7, "low") * delayed(speed, 0.005) ** 1.1
    x = body + rng.uniform(0.25, 0.4) * edge + 0.55 * air
    return fade_out(svf(svf(svf(x, 110, 0.7, "high"), 6000, 0.7, "low"), 6000, 0.7, "low"))


def hit(rng):
    """Hit landed, heard by the attacker: a crisp crack, a short hard knock you feel, a smack in
    the middle and the brief bright ring of the blade. It plays with the victim's hurt sound
    (which brings the deep thump), and together they're the whole blow."""
    t = timeline(0.24)
    snap = svf(rng.standard_normal(len(t)), rng.uniform(1500, 2500), rng.uniform(1.0, 1.5), "band")
    snap *= env(t, 0.0004, rng.uniform(0.01, 0.016))
    smack = svf(rng.standard_normal(len(t)), rng.uniform(650, 1050), 1.1, "band") * env(t, 0.0006, rng.uniform(0.014, 0.022))
    knock = thump(t, rng.uniform(200, 250), rng.uniform(82, 105), 0.008, rng.uniform(0.022, 0.032), attack=0.0006)
    ring = blade_ring(rng, t, rng.uniform(1800, 2350), rng.uniform(0.06, 0.08), rng.uniform(0.4, 0.6))
    x = 1.2 * crack(rng, t, rng.uniform(1600, 2200), 0.0025) + 0.85 * snap + 0.55 * smack + 0.75 * knock + ring
    return in_room(soft_clip(x, 1.8), -22)


def hurt(rng):
    """Getting hurt, heard by everyone at the victim: a heavy body thump, a muffled smack and
    the hard knock of the armor. Duller and lower than the attacker's hit, with no ring, but
    with enough middle to come through small speakers. Variants differ in how deep and long the
    thump is, which part of the armor knocks, and whether the smack lands a hair after it."""
    t = timeline(0.26)
    body = thump(t, rng.uniform(140, 185), rng.uniform(54, 68), rng.uniform(0.02, 0.03), rng.uniform(0.045, 0.068))
    smack = svf(svf(rng.standard_normal(len(t)), rng.uniform(650, 1100), rng.uniform(0.8, 1.3), "band"), 3000, 0.7, "low")
    smack = delayed(smack * env(t, 0.0008, rng.uniform(0.018, 0.032)), rng.uniform(0.0, 0.004))
    flesh = svf(rng.standard_normal(len(t)), rng.uniform(320, 480), 0.8, "low") * env(t, 0.002, rng.uniform(0.028, 0.042))
    base = rng.uniform(340, 500)
    knock = modes(t, base * np.array([1, 2.32, 4.25]) * rng.uniform(0.96, 1.04, 3), [0.05, 0.026, 0.012],
                  np.array([0.45, 0.25, 0.1]) * rng.uniform(0.7, 1.2, 3))
    front = svf(rng.standard_normal(len(t)), 2000, 0.7, "low") * env(t, 0.0003, 0.005)
    x = 0.85 * body + 1.25 * smack + 0.6 * flesh + knock + 0.8 * front
    return in_room(soft_clip(x, 2.0), -22)


def crit(rng):
    """Critical hit, heard by everyone at the victim along with the sparks: a sharper crack, a
    quick rising zing and a crackle of sparks dying away. It plays on top of the hit and hurt,
    so it brings only the top end."""
    t = timeline(0.36)
    f0, f1 = rng.uniform(1200, 1450), rng.uniform(2900, 3400)
    sweep = f0 * (f1 / f0) ** np.minimum(t / 0.05, 1.0)
    phase = 2 * np.pi * np.cumsum(sweep) / RATE
    zing = (np.sin(phase) + 0.3 * np.sin(2 * phase)) * env(t, 0.001, 0.04)
    ring = blade_ring(rng, t, rng.uniform(2700, 3100), 0.07, 0.4)
    count = int(rng.integers(30, 42))
    times = np.minimum(rng.exponential(0.07, count) + 0.004, 0.33)
    sparks = grains(rng, t, times, rng.uniform(0.0006, 0.0025, count), rng.uniform(0.3, 1.0, count) * np.exp(-times / 0.11))
    sparks = svf(sparks, rng.uniform(4800, 6000), 0.9, "band")
    x = 0.6 * crack(rng, t, 3000, 0.003) + 0.38 * zing + ring + 1.4 * sparks
    return in_room(soft_clip(svf(x, 1300, 0.7, "high"), 1.4), -20)


def rod_cast(rng):
    """Throwing the rod: the whip of the thin rod flicking forward, then a short burst of the
    reel's ratchet as the line pays out, spinning down quickly, with a faint hiss of line."""
    t = timeline(0.36)
    peak, stop = rng.uniform(0.010, 0.014), rng.uniform(0.045, 0.058)
    rise = 0.5 - 0.5 * np.cos(np.pi * np.minimum(t / peak, 1.0))
    fall = np.where(t < peak, 1.0, np.exp(-(np.maximum(t - peak, 0) / (stop - peak) * 2.0) ** 1.6))
    speed = rise * fall
    center = 850 + (rng.uniform(3000, 3700) - 850) * speed
    whip = svf(pinkish(rng, len(t)), center, 1.8, "band") * speed ** 1.2
    whip += 0.3 * svf(pinkish(rng, len(t)), center * 2.1, 2.0, "band") * speed ** 1.5
    spin = np.exp(-t / rng.uniform(0.09, 0.12))
    hiss = svf(rng.standard_normal(len(t)), 4500, 0.8, "band") * env(t, 0.015, 1e9) * spin ** 1.5
    rate = rng.uniform(85, 105) * np.sqrt(spin)
    ratchet = np.zeros_like(t)
    tick_t = timeline(0.012)
    pitch = rng.uniform(0.95, 1.05)
    phase = 0.0
    for i in range(seconds(0.012), len(t)):
        phase += rate[i] / RATE
        if phase < 1.0:
            continue
        phase -= 1.0
        tick = modes(tick_t, np.array([2000, 3300, 5100, 900]) * pitch * rng.uniform(0.98, 1.02),
                     [0.003, 0.002, 0.0012, 0.002], [0.5, 0.3, 0.12, 0.2])
        n = min(len(tick), len(t) - i)
        ratchet[i:i + n] += tick[:n] * spin[i] * rng.uniform(0.75, 1.0)
    x = whip + 0.35 * crack(rng, t, 2500, 0.0015) + 0.05 * hiss + 0.3 * ratchet
    return fade_out(svf(svf(x, 250, 0.7, "high"), 8000, 0.7, "low"), 0.04)


def bobber_hit(rng):
    """The bobber catching a player: a hard little knock as the float strikes, then the line
    twangs taut. Played with the victim's hurt sound, which brings the body."""
    t = timeline(0.26)
    base = rng.uniform(900, 1100)
    knock = modes(t, base * np.array([1, 2.41, 4.1]), [0.03, 0.014, 0.007], [0.6, 0.35, 0.15])
    thud = svf(rng.standard_normal(len(t)), 500, 0.7, "low") * env(t, 0.0005, 0.018)
    twang = string_twang(rng, t, rng.uniform(190, 235), 0.11, 0.004)
    zip_sweep = 2000 * (5500 / 2000) ** np.minimum(t / 0.035, 1.0)
    zipper = svf(rng.standard_normal(len(t)), zip_sweep, 3.0, "band") * env(t, 0.002, 0.02)
    x = knock + 0.7 * crack(rng, t, 2500, 0.002) + 0.5 * thud + 0.32 * twang + 0.15 * zipper
    return in_room(soft_clip(svf(x, 90, 0.7, "high"), 1.4), -21)


def bobber_land(rng):
    """The bobber landing on the ground: a small, soft plop in the grass."""
    t = timeline(0.11)
    thud = svf(rng.standard_normal(len(t)), rng.uniform(700, 950), 0.8, "low") * env(t, 0.0006, 0.018)
    tok = modes(t, np.array([1, 2.3]) * rng.uniform(600, 760), [0.016, 0.007], [0.35, 0.12])
    count = int(rng.integers(6, 11))
    times = rng.uniform(0.0, 0.025, count)
    blades = svf(grains(rng, t, times, rng.uniform(0.0004, 0.0012, count), rng.uniform(0.3, 1.0, count)), 3200, 0.8, "band")
    x = 0.8 * thud + tok + 0.5 * blades
    return svf(x, 80, 0.7, "high")


def _crunch(rng, t, count, start, spread, amp):
    """Grass blades crushing under a foot: a cluster of grains, denser at first."""
    times = start + rng.gamma(1.6, spread / 1.6, count)
    lengths = rng.uniform(0.0004, 0.0016, count)
    amps = rng.uniform(0.25, 1.0, count) * amp
    bright = svf(grains(rng, t, times, lengths, amps), rng.uniform(2400, 3200), 0.7, "band")
    dark = svf(grains(rng, t, times + 0.001, lengths, amps * 0.7), rng.uniform(1100, 1500), 0.7, "band")
    return bright + dark


def step(rng):
    """A footstep on grass: a soft heel thud and the crunch of blades rolling to the toe. Soft
    edged and short, so a stream of them never ticks."""
    t = timeline(0.16)
    heel = thump(t, rng.uniform(110, 130), rng.uniform(58, 66), 0.012, 0.022, attack=0.002)
    toe_at = rng.uniform(0.035, 0.05)
    crunch = _crunch(rng, t, int(rng.integers(28, 45)), 0.001, 0.014, 1.0)
    crunch += _crunch(rng, t, int(rng.integers(10, 20)), toe_at, 0.01, 0.55)
    brush = svf(rng.standard_normal(len(t)), rng.uniform(1300, 1900), 0.6, "band") * env(t, 0.004, 0.028)
    x = 0.55 * heel + 0.9 * crunch + 0.3 * brush
    return svf(svf(x, 70, 0.7, "high"), 7000, 0.7, "low")


def land(rng):
    """Landing from a jump or a knock: both feet come down, a deeper thud with more crunch."""
    t = timeline(0.22)
    second = rng.uniform(0.012, 0.022)
    heel = thump(t, rng.uniform(95, 110), rng.uniform(48, 54), 0.015, 0.05, attack=0.0015)
    other = delayed(thump(t, rng.uniform(105, 120), rng.uniform(52, 58), 0.012, 0.035, attack=0.0015), second)
    crunch = _crunch(rng, t, int(rng.integers(45, 65)), 0.001, 0.02, 1.0)
    crunch += _crunch(rng, t, int(rng.integers(25, 40)), second, 0.02, 0.8)
    brush = svf(rng.standard_normal(len(t)), rng.uniform(1100, 1500), 0.6, "band") * env(t, 0.004, 0.045)
    x = heel + 0.6 * other + 0.9 * crunch + 0.35 * brush
    return svf(svf(x, 45, 0.7, "high"), 7000, 0.7, "low")


# name: (recipe, variants, seed). Seeds are fixed so the files rebuild identically.
SOUNDS = {
    "swing": (swing, 8, 1101),
    "hit": (hit, 8, 1202),
    "hurt": (hurt, 8, 1303),
    "crit": (crit, 4, 1404),
    "rod_cast": (rod_cast, 4, 1505),
    "bobber_hit": (bobber_hit, 4, 1606),
    "bobber_land": (bobber_land, 4, 1707),
    "step": (step, 8, 1808),
    "land": (land, 4, 1909),
}


# ---------------------------------------------------------------- mastering and measuring

def biquad(x, b, a):
    b0, b1, b2 = (v / a[0] for v in b)
    a1, a2 = a[1] / a[0], a[2] / a[0]
    out = [0.0] * len(x)
    x1 = x2 = y1 = y2 = 0.0
    for i, xi in enumerate(x.tolist()):
        yi = b0 * xi + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, xi, y1, yi
        out[i] = yi
    return np.array(out)


def k_weighted(x):
    """ITU-R BS.1770's K-weighting (a high shelf, then a high-pass), as loudness meters use."""
    gain, q, fc = 3.99984385397, 0.7071752369554193, 1681.9744509555319
    a = 10 ** (gain / 40)
    w0 = 2 * np.pi * fc / RATE
    alpha, c = np.sin(w0) / (2 * q), np.cos(w0)
    x = biquad(x,
               [a * ((a + 1) + (a - 1) * c + 2 * np.sqrt(a) * alpha), -2 * a * ((a - 1) + (a + 1) * c),
                a * ((a + 1) + (a - 1) * c - 2 * np.sqrt(a) * alpha)],
               [(a + 1) - (a - 1) * c + 2 * np.sqrt(a) * alpha, 2 * ((a - 1) - (a + 1) * c),
                (a + 1) - (a - 1) * c - 2 * np.sqrt(a) * alpha])
    q, fc = 0.5003270373253953, 38.13547087613982
    w0 = 2 * np.pi * fc / RATE
    alpha, c = np.sin(w0) / (2 * q), np.cos(w0)
    return biquad(x, [(1 + c) / 2, -(1 + c), (1 + c) / 2], [1 + alpha, -2 * c, 1 - alpha])


def loudness_curve(x, window=0.1, hop=0.01):
    """Loudness (LUFS) of each `window`-long stretch, every `hop` seconds."""
    y = k_weighted(np.asarray(x, dtype=float))
    w, h = seconds(window), seconds(hop)
    y = np.concatenate([y, np.zeros(w)])
    sums = np.concatenate([[0.0], np.cumsum(y * y)])
    starts = np.arange(0, len(y) - w + 1, h)
    return -0.691 + 10 * np.log10((sums[starts + w] - sums[starts]) / w + 1e-20)


def loudness(x):
    """How loud a short sound is: its loudest 100 ms."""
    return float(loudness_curve(x, hop=0.002).max())


def limit(x, ceiling_db, release=0.03):
    """A look-ahead peak limiter: turns down only the moments that would pass the ceiling."""
    ceiling = 10 ** (ceiling_db / 20)
    need = np.minimum(1.0, ceiling / np.maximum(np.abs(x), 1e-12))
    ahead = seconds(0.0015)
    padded = np.concatenate([need, np.ones(ahead)])
    gain = np.lib.stride_tricks.sliding_window_view(padded, ahead + 1).min(axis=1)[: len(x)]
    keep = np.exp(-1.0 / (release * RATE))
    smooth = [1.0] * len(gain)
    g = 1.0
    for i, target in enumerate(gain.tolist()):
        g = target if target < g else target + (g - target) * keep
        smooth[i] = g
    gain = np.convolve(np.array(smooth), np.ones(ahead + 1) / (ahead + 1))[: len(x)]
    return x * np.minimum(gain, need)


def master(x):
    """Same loudness for every file, peaks under the ceiling, a clean start and end."""
    x = svf(np.asarray(x, dtype=float), 25, 0.7, "high")
    x[: seconds(0.0003)] *= np.linspace(0, 1, seconds(0.0003))
    peak = np.abs(x).max()
    above = np.nonzero(np.abs(x) > peak * 10 ** (-60 / 20))[0]
    x = x[: above[-1] + 1]
    fade = min(seconds(0.012), len(x) // 4)
    x[-fade:] *= np.cos(np.linspace(0, np.pi / 2, fade)) ** 2
    for _ in range(40):
        x *= 10 ** ((TARGET_LUFS - loudness(x)) / 20)
        if 20 * np.log10(np.abs(x).max()) <= CEILING_DB:
            break
        x = limit(x, CEILING_DB - 0.05)
        if abs(loudness(x) - TARGET_LUFS) < 0.1:
            break
    return x


def write(path, samples):
    pcm = np.clip(np.round(samples * 32767), -32768, 32767).astype("<i2")
    with wave.open(str(path), "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(RATE)
        f.writeframes(pcm.tobytes())


def read(path):
    with wave.open(str(path), "rb") as f:
        channels, rate = f.getnchannels(), f.getframerate()
        data = np.frombuffer(f.readframes(f.getnframes()), dtype="<i2").astype(float) / 32768
    if channels > 1:
        data = data.reshape(-1, channels).mean(axis=1)
    if rate != RATE:
        sys.exit(f"{path}: expected {RATE} Hz, got {rate}")
    return data


def describe(x):
    """Peak, loudness, length, when it's loudest, and its tone (where its energy sits)."""
    spectrum = np.abs(np.fft.rfft(x, 1 << 16)) ** 2
    freqs = np.fft.rfftfreq(1 << 16, 1 / RATE)
    total = spectrum.sum() + 1e-20
    band = lambda lo, hi: 100 * spectrum[(freqs >= lo) & (freqs < hi)].sum() / total  # noqa: E731
    return (f"{len(x) / RATE * 1000:4.0f} ms  peak {20 * np.log10(np.abs(x).max()):5.1f} dBFS  "
            f"loud {loudness(x):5.1f} LUFS  loudest at {np.argmax(np.abs(x)) / RATE * 1000:4.1f} ms  "
            f"centre {(freqs * spectrum).sum() / total:5.0f} Hz  "
            f"low/mid/high {band(0, 250):3.0f}/{band(250, 2000):3.0f}/{band(2000, RATE):3.0f} %")


def build():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, (recipe, variants, seed) in SOUNDS.items():
        for i in range(variants):
            x = master(recipe(np.random.default_rng(seed * 100 + i)))
            path = OUT / f"{name}_{i + 1}.wav"
            write(path, x)
            print(f"{path.name:18} {describe(x)}")


def report():
    for name, (_, variants, _) in SOUNDS.items():
        for i in range(variants):
            path = OUT / f"{name}_{i + 1}.wav"
            print(f"{path.name:18} {describe(read(path))}")


def analyze(paths):
    for path in paths:
        x = read(path)
        print(f"{path}: {describe(x)}")
        clipped = int(np.sum(np.abs(x) >= 0.999))
        print(f"  samples at full scale (clipping): {clipped}")
        curve = loudness_curve(x, window=1.0, hop=1.0)
        print("  loudness each second (LUFS): " + " ".join(f"{v:.0f}" for v in curve[: len(x) // RATE]))


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--report":
        report()
    elif len(sys.argv) > 2 and sys.argv[1] == "--analyze":
        analyze(sys.argv[2:])
    else:
        build()
