"""
The music: three seamless loops written as data (key, tempo, chords, parts) and played by the
instruments in synth.py. Melodies are generated from the chords with a seeded random walk, so a
track only changes when its definition does.
"""

import numpy as np

import synth as s
from synth import RATE

CHORDS = {
    # Triads voiced around the middle of the keyboard (MIDI note numbers).
    "G": [55, 59, 62], "Em": [52, 55, 59], "C": [48, 52, 55], "D": [50, 54, 57], "Am": [57, 60, 64],
    "Dm": [50, 53, 57], "F": [53, 57, 60], "Bb": [46, 50, 53], "Gm": [55, 58, 62], "E": [52, 56, 59],
    "Cm": [48, 51, 55], "Fm": [53, 56, 60],
}

SCALES = {
    "G major": [55, 57, 59, 60, 62, 64, 66],
    "D dorian": [50, 52, 53, 55, 57, 59, 60],
    "A phrygian": [57, 58, 60, 62, 64, 65, 67],
}

TRACKS = {
    # A village theme: bright and unhurried.
    "music_mirewatch": dict(
        bpm=84, scale="G major", seed=1,
        sections=[("A", "G Em C D G Em C D"), ("B", "C G Am D C G Am D"), ("A", "G Em C D G Em C D"), ("B", "C G Am D C G Am G")],
        melody_in=("A", "B"), arp="eighths", arp_octave=12, pad=0.5, bass=True, drone=None, bells=False, harp_brightness=1.2,
    ),
    # Out in the valley: wide and a little lonely.
    "music_hollowmere": dict(
        bpm=66, scale="D dorian", seed=2,
        sections=[("A", "Dm G Dm C Dm G Am Dm"), ("B", "F C G Dm F C Am Dm"), ("A", "Dm G Dm C Dm G Am Dm"), ("B", "F C G Dm Bb F C Dm")],
        melody_in=("B",), arp="quarters", arp_octave=12, pad=0.7, bass=False, drone=38, bells=False, harp_brightness=0.9,
    ),
    # The drowned town: dark, slow, bells under water.
    "music_drowned": dict(
        bpm=56, scale="A phrygian", seed=3,
        sections=[("A", "Am Bb Am Gm Am F Bb Am"), ("B", "Dm Bb Am E Dm Bb E Am"), ("A", "Am Bb Am Gm Am F Bb Am")],
        melody_in=(), arp="halves", arp_octave=0, pad=0.8, bass=False, drone=33, bells=True, harp_brightness=0.6,
    ),
}

RHYTHMS = [[2, 1, 1], [1, 1, 2], [3, 1], [4], [1, 1, 1, 1], [2, 2], [1.5, 0.5, 2]]


def melody_for_bar(chord, scale, rng, last):
    """A bar of melody: (beat, length, midi) notes, walking through the scale from `last`."""
    rhythm = RHYTHMS[rng.integers(len(RHYTHMS))]
    pool = sorted({n + 12 * o for n in scale for o in (0, 1)})
    chord_tones = {n % 12 for n in chord}
    notes = []
    beat = 0.0
    for i, length in enumerate(rhythm):
        if last is None:
            choice = [p for p in pool if p % 12 in chord_tones and 64 <= p <= 76]
            pitch = int(rng.choice(choice))
        else:
            idx = int(np.argmin([abs(p - last) for p in pool]))
            idx = int(np.clip(idx + rng.choice([-2, -1, -1, 1, 1, 2, 0]), 0, len(pool) - 1))
            pitch = pool[idx]
            # Strong beats land on the chord.
            if beat in (0.0, 2.0) and pitch % 12 not in chord_tones:
                near = [p for p in pool if p % 12 in chord_tones]
                pitch = min(near, key=lambda p: abs(p - pitch))
        pitch = int(np.clip(pitch, 62, 79))
        notes.append((beat, length, pitch))
        last = pitch
        beat += length
    return notes, last


def render(name):
    spec = TRACKS[name]
    rng = s.rng(spec["seed"])
    beat = 60.0 / spec["bpm"]
    bar = beat * 4
    bars = [(section, chord) for section, line in spec["sections"] for chord in line.split()]
    length = len(bars) * bar
    tail = 4.0
    total = int((length + tail) * RATE)
    left = np.zeros(total)
    right = np.zeros(total)
    scale = SCALES[spec["scale"]]

    def add(sound, at, gain, pan):
        s.mix_into(left, sound * gain * (1 - pan) ** 0.5, at * RATE)
        s.mix_into(right, sound * gain * pan ** 0.5, at * RATE)

    last = None
    for i, (section, chord_name) in enumerate(bars):
        t0 = i * bar
        chord = CHORDS[chord_name]
        # Pad: the chord held through the bar.
        for k, n in enumerate(chord):
            add(s.pad(s.midi_hz(n), bar + 1.0, attack=bar * 0.3, release=1.0, seed=i * 7 + k, cutoff=5), t0, spec["pad"] * 0.55, 0.3 + 0.2 * k)
        # Harp arpeggio over the chord.
        tones = [n + spec["arp_octave"] for n in chord] + [chord[0] + spec["arp_octave"] + 12]
        step = {"eighths": 0.5, "quarters": 1.0, "halves": 2.0}[spec["arp"]]
        pattern = [0, 1, 2, 3, 2, 1, 2, 1] if step == 0.5 else [0, 1, 2, 3] if step == 1.0 else [0, 2]
        for j, idx in enumerate(pattern):
            add(s.pluck(s.midi_hz(tones[idx]), 2.0, seed=i * 31 + j, brightness=spec["harp_brightness"]), t0 + j * step * beat, 0.45, 0.65)
        if spec["bass"]:
            for b in (0, 2):
                add(s.pluck(s.midi_hz(chord[0] - 12), 1.6, seed=i * 13 + b, brightness=0.5), t0 + b * beat, 0.6, 0.5)
        if spec["drone"] is not None and i % 2 == 0:
            add(s.drone(s.midi_hz(spec["drone"]), bar * 2 + 1.0, seed=i), t0, 0.5, 0.5)
        if spec["bells"] and rng.random() < 0.45:
            n = int(rng.choice(chord)) + 12
            add(s.bell(s.midi_hz(n), 3.0, seed=i, decay=1.4), t0 + rng.integers(0, 4) * beat, 0.35, rng.random())
        # The melody on a flute, in the sections that have one (and not in the last bar of a phrase).
        if section in spec["melody_in"] and i % 4 != 3:
            notes, last = melody_for_bar(chord, scale, rng, last)
            for b, l, n in notes:
                add(s.flute(s.midi_hz(n), l * beat * 0.95, seed=i * 17 + int(b * 4)), t0 + b * beat, 0.55, 0.45)
        elif i % 4 == 3:
            last = None

    mix = np.array([left, right])
    # Room, then wrap the tail around so the loop has no seam.
    wet = np.array([s.reverb(mix[0], 3.0, 0.32, spec["seed"] * 10)[:total], s.reverb(mix[1], 3.0, 0.32, spec["seed"] * 10 + 1)[:total]])
    loop_n = int(length * RATE)
    out = wet[:, :loop_n].copy()
    out[:, :total - loop_n] += wet[:, loop_n:]
    return s.normalize(out, 0.7)


MUSIC = {name: ("Music", lambda name=name: render(name)) for name in TRACKS}
