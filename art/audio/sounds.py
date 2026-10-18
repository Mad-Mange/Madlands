"""
Sound effects, jingles and ambience loops. Each function returns a float array: mono (n,) for
effects and jingles, stereo (2, n) for ambience. Names in SOUNDS are the clip names the client uses.
"""

import math

import numpy as np

import synth as s
from synth import RATE


# ---------------------------------------------------------------- combat

def swing(seed, heavy=False):
    dur = 0.36 if heavy else 0.24
    lo, hi = (250, 1200) if heavy else (500, 2600)
    x = s.swept_noise(dur, lambda t: lo + (hi - lo) * math.sin(math.pi * min(t * 1.2, 1.0)), lambda t: 300 + 500 * t, seed)
    env = np.sin(np.pi * np.clip(np.arange(len(x)) / len(x), 0, 1)) ** 1.5
    return s.normalize(s.fade(x * env), 0.7 if heavy else 0.55)


def splash(seed):
    """A footstep in shallow water."""
    g = s.rng(seed)
    n = int(0.35 * RATE)
    t = np.arange(n) / RATE
    body = s.lowpass(g.standard_normal(n), 900) * np.exp(-t * 14)
    spray = s.highpass(g.standard_normal(n), 2500) * np.exp(-t * 30) * 0.4
    plop = np.sin(2 * np.pi * (380 - 500 * t) * t) * np.exp(-t * 25) * 0.6
    return s.normalize(s.fade(body + spray + plop), 0.55)


# ---------------------------------------------------------------- spells by school

SCHOOL_NOTES = {"arcane": "E5", "fire": "A3", "frost": "B5", "holy": "G4", "nature": "D4", "shadow": "C3", "physical": "E3"}


def cast(school, seed):
    """The start of a cast: a short rising swell in the school's color."""
    f = s.note_hz(SCHOOL_NOTES[school])
    dur = 0.7
    t = s.seconds(dur)
    swell = np.clip(t / dur, 0, 1) ** 1.5
    if school == "fire":
        x = s.swept_noise(dur, lambda u: 300 + 1500 * u, lambda u: 500, seed) * swell
        # Crackle: short bursts of bright noise.
        g = s.rng(seed + 1)
        for _ in range(14):
            n = int(0.02 * RATE)
            burst = s.highpass(g.standard_normal(n), 2000) * np.exp(-np.arange(n) / RATE * 200) * 0.4
            s.mix_into(x, burst, int(g.random() * (len(t) - n)))
    elif school == "frost":
        x = sum(s.bell(f * r, dur, seed + i, decay=0.6) for i, r in enumerate((1.0, 1.5, 2.0))) * swell
    elif school == "holy":
        x = sum(s.pad(f * r, dur, attack=0.5, release=0.2, seed=seed + i, cutoff=4) for i, r in enumerate((1.0, 1.25, 1.5))) * 2
    elif school == "shadow":
        x = s.pad(f, dur, attack=0.6, release=0.1, seed=seed, cutoff=8) * 2 + s.swept_noise(dur, lambda u: 200 + 200 * u, lambda u: 150, seed) * swell * 0.5
    elif school == "nature":
        x = s.swept_noise(dur, lambda u: 2000 + 2000 * u, lambda u: 1500, seed) * swell * 0.5 + s.pad(f, dur, 0.5, 0.1, seed, cutoff=3) * 1.5
    else:  # arcane
        x = np.zeros(len(t))
        g = s.rng(seed)
        for i in range(10):
            start = int(g.random() * len(t) * 0.8)
            ping = s.bell(f * (1 + g.integers(0, 4) * 0.25), 0.3, seed + i, decay=0.3)
            s.mix_into(x, ping * (0.3 + start / len(t)), start)
    return s.normalize(s.fade(s.reverb(x, 0.8, 0.25, seed), 0.01, 0.1), 0.5)


def release(school, seed):
    """A spell leaving the hands."""
    f = s.note_hz(SCHOOL_NOTES[school])
    dur = 0.45
    t = s.seconds(dur)
    whoosh = s.swept_noise(dur, lambda u: 2400 - 1800 * u, lambda u: 700, seed) * np.exp(-t * 6)
    if school == "fire":
        x = s.swept_noise(dur, lambda u: 1200 - 900 * u, lambda u: 900, seed) * np.exp(-t * 5) * 1.4
    elif school == "frost":
        x = whoosh * 0.6 + s.bell(f, dur, seed, 0.4) * 1.2
    elif school == "holy":
        x = s.bell(f, dur * 2, seed, 0.8)[:len(t)] + whoosh * 0.3
    elif school == "shadow":
        x = whoosh * 0.6 + np.sin(2 * np.pi * (f * 2 - 200 * t) * t) * np.exp(-t * 6) * 0.6
    elif school == "nature":
        x = whoosh * 0.5 + s.pluck(f, dur, seed) * 1.2
    else:  # arcane
        x = whoosh * 0.6 + np.sin(2 * np.pi * (f * (1.6 - 0.8 * t / dur)) * t) * np.exp(-t * 7) * 0.7
    return s.normalize(s.fade(s.reverb(x, 0.7, 0.2, seed)), 0.6)


def impact(school, seed):
    """A spell landing."""
    f = s.note_hz(SCHOOL_NOTES[school])
    dur = 0.6
    t = s.seconds(dur)
    g = s.rng(seed)
    thump = s.lowpass(g.standard_normal(len(t)), 400) * np.exp(-t * 18)
    if school == "fire":
        x = thump + s.swept_noise(dur, lambda u: 900 - 500 * u, lambda u: 800, seed) * np.exp(-t * 6)
    elif school == "frost":
        x = thump * 0.6 + sum(s.bell(f * r, dur, seed + i, 0.35) for i, r in enumerate((1.0, 1.33, 2.0)))
    elif school == "holy":
        x = thump * 0.4 + s.bell(f * 2, dur, seed, 0.7) * 1.4
    elif school == "shadow":
        x = thump * 1.2 + np.sin(2 * np.pi * (f - 30 * t) * t) * np.exp(-t * 5)
    elif school == "nature":
        x = thump * 0.6 + s.pluck(f * 2, dur, seed, 0.6)
    else:  # arcane
        x = thump * 0.7 + np.sin(2 * np.pi * f * 1.5 * t) * np.exp(-t * 9) + s.highpass(g.standard_normal(len(t)), 4000) * np.exp(-t * 20) * 0.3
    return s.normalize(s.fade(s.reverb(x, 0.9, 0.25, seed)), 0.65)


def heal(seed):
    t = s.seconds(1.0)
    x = sum(s.bell(s.note_hz(n), 1.0, seed + i, 0.6) * (0.9 - 0.15 * i) for i, n in enumerate(("G5", "B5", "D6")))
    x *= np.clip(t / 0.05, 0, 1)
    return s.normalize(s.fade(s.reverb(x, 1.2, 0.3, seed)), 0.45)


# ---------------------------------------------------------------- jingles

def arpeggio(notes, step, ring, instrument, seed, chord_at=None):
    total = step * len(notes) + ring
    x = np.zeros(int(total * RATE))
    for i, n in enumerate(notes):
        s.mix_into(x, instrument(s.note_hz(n), ring, seed + i), i * step * RATE, 0.8)
    if chord_at is not None:
        for i, n in enumerate(chord_at[1]):
            s.mix_into(x, s.pad(s.note_hz(n), ring, 0.05, 0.8, seed + 20 + i, cutoff=5), chord_at[0] * RATE, 0.9)
    return x


def level_up():
    x = arpeggio(["C4", "E4", "G4", "C5", "E5", "G5"], 0.09, 1.6, lambda f, d, sd: s.pluck(f, d, sd, 1.3), 1,
                 chord_at=(0.5, ["C4", "G4", "E5"]))
    shimmer = sum(s.bell(s.note_hz(n), 1.6, 9 + i, 1.2) for i, n in enumerate(("C6", "G6")))
    s.mix_into(x, shimmer * 0.5, int(0.5 * RATE))
    return s.normalize(s.fade(s.reverb(x, 1.8, 0.3, 2)), 0.8)


def quest_complete():
    x = arpeggio(["G3", "B3", "D4", "G4"], 0.11, 1.4, lambda f, d, sd: s.pluck(f, d, sd, 1.1), 3,
                 chord_at=(0.44, ["G3", "D4", "B4"]))
    s.mix_into(x, s.bell(s.note_hz("G5"), 1.2, 4, 1.0) * 0.6, int(0.44 * RATE))
    return s.normalize(s.fade(s.reverb(x, 1.6, 0.3, 5)), 0.75)


def quest_accept():
    x = arpeggio(["D4", "A4"], 0.12, 0.8, lambda f, d, sd: s.pluck(f, d, sd, 1.0), 6)
    return s.normalize(s.fade(s.reverb(x, 1.0, 0.25, 7)), 0.6)


def objective_done():
    x = arpeggio(["A4", "E5"], 0.08, 0.7, lambda f, d, sd: s.bell(f, d, sd, 0.6), 8)
    return s.normalize(s.fade(s.reverb(x, 0.9, 0.25, 9)), 0.45)


def death():
    x = arpeggio(["A3", "F3", "D3", "A2"], 0.32, 2.4, lambda f, d, sd: s.pluck(f, d, sd, 0.7), 10,
                 chord_at=(0.0, ["D3", "F3", "A3"]))
    return s.normalize(s.fade(s.reverb(x, 2.4, 0.35, 11)), 0.7)


# ---------------------------------------------------------------- ambience (stereo loops)

LOOP = 26.0


def wind(dur, seed, low=250, high=800, gain=0.5):
    g = s.rng(seed)
    phase = g.random() * 6.28
    x = s.swept_noise(dur, lambda u: low + (high - low) * (0.5 + 0.5 * math.sin(2 * math.pi * u * 3 + phase)),
                      lambda u: 250 + 150 * math.sin(2 * math.pi * u * 2), seed, frame=2048)
    t = s.seconds(dur)
    amp = 0.6 + 0.4 * np.sin(2 * np.pi * t / dur * 4 + phase) * np.sin(2 * np.pi * t / dur * 1.0 + 1)
    return s.normalize(x * amp, gain)


def stereo(left, right):
    n = min(len(left), len(right))
    return np.array([left[:n], right[:n]])


def birdsong(dur, seed, density=0.5, pitch=1.0):
    g = s.rng(seed)
    n = int(dur * RATE)
    out = np.zeros((2, n))
    calls = int(dur * density)
    for c in range(calls):
        start = g.random() * (dur - 1.5)
        base = (2200 + g.random() * 2500) * pitch
        pan = g.random()
        for k in range(g.integers(2, 6)):
            length = 0.06 + g.random() * 0.12
            t = s.seconds(length)
            sweep = base * (1 + (g.random() - 0.5) * 0.6 * t / length)
            chirp = np.sin(2 * np.pi * np.cumsum(sweep) / RATE) * np.sin(np.pi * t / length) ** 2
            chirp += 0.2 * np.sin(2 * 2 * np.pi * np.cumsum(sweep) / RATE) * np.sin(np.pi * t / length) ** 2
            at = (start + k * (length + 0.04 + g.random() * 0.06)) * RATE
            s.mix_into(out[0], chirp * (1 - pan) * 0.25, at)
            s.mix_into(out[1], chirp * pan * 0.25, at)
    return out


def water(dur, seed):
    g = s.rng(seed)
    n = int(dur * RATE)
    base = s.lowpass(g.standard_normal(n), 500) * 0.3
    laps = np.zeros(n)
    for _ in range(int(dur * 1.5)):
        at = int(g.random() * (n - RATE))
        length = int((0.3 + g.random() * 0.5) * RATE)
        t = np.arange(length) / RATE
        burst = s.bandpass(g.standard_normal(length), 600 + g.random() * 600, 400) * np.sin(np.pi * t / t[-1]) ** 2
        laps[at:at + length] += burst * (0.5 + g.random())
    return base + laps


def frogs(dur, seed):
    g = s.rng(seed)
    n = int(dur * RATE)
    out = np.zeros((2, n))
    for _ in range(int(dur * 0.6)):
        at = g.random() * (dur - 1.0) * RATE
        f = 140 + g.random() * 120
        length = 0.25 + g.random() * 0.25
        t = s.seconds(length)
        pulses = (np.sin(2 * np.pi * (18 + g.random() * 10) * t) > 0.2).astype(float)
        croak = np.sin(2 * np.pi * f * t) * pulses * np.sin(np.pi * t / length)
        croak = s.bandpass(croak + 0.3 * np.sin(4 * np.pi * f * t) * pulses, f * 2, f * 2)
        pan = g.random()
        s.mix_into(out[0], croak * (1 - pan) * 0.5, at)
        s.mix_into(out[1], croak * pan * 0.5, at)
    return out


def drips(dur, seed):
    g = s.rng(seed)
    n = int(dur * RATE)
    out = np.zeros((2, n))
    for _ in range(int(dur * 0.5)):
        at = g.random() * (dur - 1.0) * RATE
        f = 1400 + g.random() * 1600
        t = s.seconds(0.15)
        drop = np.sin(2 * np.pi * f * (1 + 0.5 * t) * t) * np.exp(-t * 40)
        pan = g.random()
        s.mix_into(out[0], drop * (1 - pan) * 0.2, at)
        s.mix_into(out[1], drop * pan * 0.2, at)
    return out


def leaves(dur, seed):
    g = s.rng(seed)
    n = int(dur * RATE)
    noise = s.highpass(g.standard_normal(n), 3000)
    t = np.arange(n) / RATE
    gusts = np.clip(np.sin(2 * np.pi * t / dur * 5 + 1.3) * np.sin(2 * np.pi * t / dur * 2), 0, 1) ** 2
    return noise * gusts * 0.08


def ambience(name):
    d = LOOP + 3.0
    if name == "amb_village":
        x = stereo(wind(d, 1, 300, 700, 0.12), wind(d, 2, 300, 700, 0.12)) + birdsong(d, 3, 0.35)
    elif name == "amb_wilds":
        x = stereo(wind(d, 4, 250, 900, 0.22), wind(d, 5, 250, 900, 0.22)) + birdsong(d, 6, 0.5)
    elif name == "amb_marsh":
        w = water(d, 7)
        x = stereo(w, np.roll(w, RATE // 3)) * 0.5 + frogs(d, 8) + stereo(wind(d, 9, 200, 500, 0.12), wind(d, 10, 200, 500, 0.12))
    elif name == "amb_forest":
        lv = leaves(d, 11)
        x = stereo(wind(d, 12, 200, 600, 0.16), wind(d, 13, 200, 600, 0.16)) + stereo(lv, np.roll(lv, RATE // 2)) + birdsong(d, 14, 0.8, 1.1)
    else:  # amb_ruins
        dr = s.drone(s.note_hz("A1"), d, 15)
        x = stereo(wind(d, 16, 150, 450, 0.28), wind(d, 17, 150, 450, 0.28)) + stereo(dr, dr) * 0.25 + drips(d, 18)
        x = np.array([s.reverb(x[0], 2.5, 0.3, 19)[:x.shape[1]], s.reverb(x[1], 2.5, 0.3, 20)[:x.shape[1]]])
    return s.normalize(s.make_loop(x, 3.0), 0.5)


SCHOOLS = ("arcane", "fire", "frost", "holy", "nature", "shadow")

# clip name -> (folder, builder)
SOUNDS = {}
for i in range(3):
    SOUNDS["swing_%d" % (i + 1)] = ("Sfx", lambda i=i: swing(100 + i))
for i in range(2):
    SOUNDS["swing_heavy_%d" % (i + 1)] = ("Sfx", lambda i=i: swing(110 + i, heavy=True))
for i in range(4):
    SOUNDS["splash_%d" % (i + 1)] = ("Sfx", lambda i=i: splash(120 + i))
for school in SCHOOLS:
    SOUNDS["cast_" + school] = ("Sfx", lambda school=school: cast(school, 200 + SCHOOLS.index(school)))
    SOUNDS["release_" + school] = ("Sfx", lambda school=school: release(school, 300 + SCHOOLS.index(school)))
    SOUNDS["impact_" + school] = ("Sfx", lambda school=school: impact(school, 400 + SCHOOLS.index(school)))
SOUNDS["heal"] = ("Sfx", lambda: heal(500))
SOUNDS["jingle_level_up"] = ("Sfx", level_up)
SOUNDS["jingle_quest_complete"] = ("Sfx", quest_complete)
SOUNDS["jingle_quest_accept"] = ("Sfx", quest_accept)
SOUNDS["jingle_objective"] = ("Sfx", objective_done)
SOUNDS["jingle_death"] = ("Sfx", death)
for amb in ("amb_village", "amb_wilds", "amb_marsh", "amb_forest", "amb_ruins"):
    SOUNDS[amb] = ("Ambience", lambda amb=amb: ambience(amb))
