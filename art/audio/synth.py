"""
A small numpy synthesizer for the Madlands sounds and music: oscillators, envelopes, noise shaped
in the frequency domain, convolution reverb, and instruments (pluck, pad, bell, flute) built from
them. Everything is deterministic: a seeded random generator per sound.
"""

import numpy as np

RATE = 44100


def seconds(n):
    return np.arange(int(n * RATE)) / RATE


def rng(seed):
    return np.random.default_rng(seed)


def note_hz(name):
    """'A4', 'C#3', 'Bb2' -> frequency."""
    names = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}
    semis = names[name[0]]
    rest = name[1:]
    if rest.startswith("#"):
        semis += 1
        rest = rest[1:]
    elif rest.startswith("b"):
        semis -= 1
        rest = rest[1:]
    octave = int(rest)
    return 440.0 * 2 ** ((semis + 12 * (octave + 1) - 69) / 12)


def midi_hz(m):
    return 440.0 * 2 ** ((m - 69) / 12)


# ---------------------------------------------------------------- envelopes and helpers

def adsr(n, attack, decay, sustain, release, hold=None):
    """An ADSR envelope over n samples; `hold` (seconds) is when the release starts."""
    t = np.arange(n) / RATE
    hold = (n / RATE - release) if hold is None else hold
    env = np.where(t < attack, t / max(attack, 1e-6), 1.0)
    d = (t - attack) / max(decay, 1e-6)
    env = np.where((t >= attack) & (t < attack + decay), 1.0 - (1.0 - sustain) * d, env)
    env = np.where(t >= attack + decay, sustain, env)
    rel = np.clip((t - hold) / max(release, 1e-6), 0.0, 1.0)
    return env * (1.0 - rel)


def fade(x, fade_in=0.005, fade_out=0.02):
    n = len(x)
    a = min(int(fade_in * RATE), n)
    b = min(int(fade_out * RATE), n)
    y = x.copy()
    if a:
        y[:a] *= np.linspace(0, 1, a)
    if b:
        y[-b:] *= np.linspace(1, 0, b)
    return y


def normalize(x, peak=0.9):
    m = np.max(np.abs(x))
    return x * (peak / m) if m > 0 else x


def mix_into(buffer, sound, start, gain=1.0):
    """Adds `sound` into `buffer` at sample `start` (clipped to the buffer)."""
    start = int(start)
    if start >= len(buffer[0] if buffer.ndim > 1 else buffer):
        return
    if buffer.ndim == 1:
        end = min(len(buffer), start + len(sound))
        buffer[start:end] += sound[:end - start] * gain
    else:
        end = min(buffer.shape[1], start + sound.shape[-1])
        buffer[:, start:end] += sound[..., :end - start] * gain


# ---------------------------------------------------------------- noise and filtering

def spectral_filter(x, shape):
    """Filters x with a static magnitude response: shape(freqs) -> gain."""
    spec = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(len(x), 1 / RATE)
    return np.fft.irfft(spec * shape(freqs), len(x))


def lowpass(x, cutoff, order=2):
    return spectral_filter(x, lambda f: 1.0 / np.sqrt(1.0 + (f / cutoff) ** (2 * order)))


def highpass(x, cutoff, order=2):
    return spectral_filter(x, lambda f: 1.0 / np.sqrt(1.0 + (np.maximum(cutoff, 1e-3) / np.maximum(f, 1e-3)) ** (2 * order)))


def bandpass(x, center, width):
    return spectral_filter(x, lambda f: np.exp(-0.5 * ((f - center) / width) ** 2))


def swept_noise(duration, center_fn, width_fn, seed, frame=1024):
    """Noise whose band moves over time: center_fn(t) / width_fn(t) in Hz, t in 0..1 (an STFT shaper)."""
    g = rng(seed)
    n = int(duration * RATE)
    hop = frame // 4
    window = np.hanning(frame)
    out = np.zeros(n + frame)
    norm = np.zeros(n + frame)
    freqs = np.fft.rfftfreq(frame, 1 / RATE)
    for start in range(0, n, hop):
        t = start / max(n, 1)
        noise = g.standard_normal(frame)
        spec = np.fft.rfft(noise * window)
        spec *= np.exp(-0.5 * ((freqs - center_fn(t)) / max(width_fn(t), 1.0)) ** 2)
        out[start:start + frame] += np.fft.irfft(spec, frame) * window
        norm[start:start + frame] += window ** 2
    return out[:n] / np.maximum(norm[:n], 1e-3)


def pink(n, seed):
    """Pink-ish noise (-3 dB per octave)."""
    white = rng(seed).standard_normal(n)
    return normalize(spectral_filter(white, lambda f: 1.0 / np.sqrt(np.maximum(f, 20.0))), 0.5)


# ---------------------------------------------------------------- reverb

def reverb_ir(length, seed, brightness=4000.0, stereo=False):
    """A decaying noise tail: longer for low frequencies, like a room."""
    n = int(length * RATE)
    t = np.arange(n) / RATE
    g = rng(seed)
    channels = []
    for c in range(2 if stereo else 1):
        noise = g.standard_normal(n)
        tail = noise * np.exp(-6.9 * t / length)
        tail = lowpass(tail, brightness, 1)
        tail[0] = 0
        channels.append(tail / np.sqrt(np.sum(tail ** 2)))
    return np.array(channels) if stereo else channels[0]


def convolve(x, ir):
    n = len(x) + len(ir) - 1
    size = 1 << (n - 1).bit_length()
    return np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size)[:n]


def reverb(x, length=1.6, mix=0.25, seed=1, brightness=4000.0):
    """Mono in, mono out, with the tail appended."""
    wet = convolve(x, reverb_ir(length, seed, brightness))
    dry = np.concatenate([x, np.zeros(len(wet) - len(x))])
    return dry * (1 - mix) + wet * mix


def stereo_reverb(x, length=2.5, mix=0.3, seed=1, brightness=3500.0):
    """Mono in, stereo out (two different tails make it wide)."""
    ir = reverb_ir(length, seed, brightness, stereo=True)
    left = convolve(x, ir[0])
    right = convolve(x, ir[1])
    dry = np.concatenate([x, np.zeros(len(left) - len(x))])
    return np.array([dry * (1 - mix) + left * mix, dry * (1 - mix) + right * mix])


# ---------------------------------------------------------------- instruments

def pluck(freq, duration, seed=0, brightness=1.0, harmonics=14):
    """A plucked string (harp, lute): harmonics that fade faster the higher they are."""
    t = seconds(duration)
    g = rng(seed)
    x = np.zeros(len(t))
    for k in range(1, harmonics + 1):
        f = freq * k * (1 + 0.0004 * k * k)  # a little inharmonicity
        if f > RATE / 2 - 1000:
            break
        amp = (1.0 / k ** (1.4 - 0.4 * brightness)) * (0.6 + 0.4 * g.random())
        decay = 1.6 + 0.9 * k ** 1.1 / brightness
        x += amp * np.exp(-decay * t) * np.sin(2 * np.pi * f * t + g.random() * 6.28)
    attack = int(0.004 * RATE)
    x[:attack] *= np.linspace(0, 1, attack)
    return x * 0.5


def pad(freq, duration, attack=0.8, release=1.2, seed=0, cutoff=6, voices=3, detune=0.006):
    """A soft string pad: detuned saws with their highs rolled off (`cutoff` harmonics)."""
    t = seconds(duration)
    g = rng(seed)
    x = np.zeros(len(t))
    for v in range(voices):
        f0 = freq * (1 + detune * (v - (voices - 1) / 2))
        vib = 1 + 0.002 * np.sin(2 * np.pi * (4.5 + g.random()) * t + g.random() * 6.28)
        phase = 2 * np.pi * np.cumsum(f0 * vib) / RATE
        for k in range(1, 3 * cutoff):
            if f0 * k > RATE / 2 - 1000:
                break
            x += np.sin(k * phase + g.random() * 6.28) / k * np.exp(-((k - 1) / cutoff) ** 2)
    return x * adsr(len(t), attack, 0.3, 0.85, release) / voices * 0.35


def bell(freq, duration, seed=0, decay=1.0):
    """A bell: inharmonic partials with their own decays."""
    t = seconds(duration)
    g = rng(seed)
    partials = [(0.5, 1.0, 0.7), (1.0, 1.0, 1.0), (2.0, 0.6, 1.6), (2.76, 0.45, 2.2), (5.4, 0.25, 3.5), (8.93, 0.12, 5.0)]
    x = np.zeros(len(t))
    for ratio, amp, d in partials:
        x += amp * np.exp(-d * t / decay) * np.sin(2 * np.pi * freq * ratio * t + g.random() * 6.28)
    attack = int(0.002 * RATE)
    x[:attack] *= np.linspace(0, 1, attack)
    return x * 0.3


def flute(freq, duration, seed=0, vibrato=0.006):
    """A breathy wooden flute."""
    t = seconds(duration)
    g = rng(seed)
    vib = 1 + vibrato * np.sin(2 * np.pi * 5.2 * t) * np.clip(t / 0.35, 0, 1)
    phase = 2 * np.pi * np.cumsum(freq * vib) / RATE
    x = np.sin(phase) + 0.18 * np.sin(2 * phase) + 0.06 * np.sin(3 * phase)
    breath = bandpass(g.standard_normal(len(t)), freq * 2, freq * 1.5) * 0.12
    env = adsr(len(t), 0.06, 0.1, 0.8, min(0.25, duration * 0.4))
    return (x * 0.3 + breath) * env


def drone(freq, duration, seed=0):
    """A low, slowly breathing drone."""
    t = seconds(duration)
    g = rng(seed)
    x = np.zeros(len(t))
    for k, amp in ((1, 1.0), (2, 0.5), (3, 0.25), (4, 0.12)):
        lfo = 0.7 + 0.3 * np.sin(2 * np.pi * (0.05 + 0.03 * g.random()) * t + g.random() * 6.28)
        x += amp * lfo * np.sin(2 * np.pi * freq * k * t + g.random() * 6.28)
    return x * 0.2


# ---------------------------------------------------------------- loops

def make_loop(stereo, overlap):
    """Crossfades the end over the start so the sound repeats without a seam."""
    n = int(overlap * RATE)
    body = stereo[..., :-n].copy()
    tail = stereo[..., -n:]
    ramp = np.linspace(0, 1, n)
    body[..., :n] = body[..., :n] * ramp + tail * (1 - ramp)
    return body
