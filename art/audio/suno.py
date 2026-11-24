"""
Imports the music made with Suno (ADR 0016) into client/Assets/Madlands/Audio/Music. Runs inside Blender for its
audio encoder, like build.py:

    blender -b --factory-startup -P art/audio/suno.py

The originals are the songs' WAV downloads, named by their Suno id, in MADLANDS_SUNO_DIR (by default
D:\\3D_Stuff\\Music\\MadLands); they stay out of the repository, like the Tripo models. Each song is trimmed of silence,
given a short fade-in and a slow fade-out, brought to one loudness and written as OGG Vorbis under its clip name.
Takes of one area are variants (music_hollowmere_1, _2, ...): the client plays them one after another.
"""

import os
import sys

import aud
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "client", "Assets", "Madlands", "Audio", "Music")
SOURCE_DIR = os.environ.get("MADLANDS_SUNO_DIR", r"D:\3D_Stuff\Music\MadLands")

# The prompts the songs were made from (model v6, instrumental, 180 s), to make more takes in the same vein.
PROMPTS = {
    "music_hollowmere": dict(
        title="Hollowmere Valley",
        style="fantasy RPG overworld soundtrack, instrumental, slow 66 bpm, D dorian, warm solo cello melody, gentle harp "
              "arpeggios, soft wooden flute, low string pads, wide open valley, pastoral, calm and a little lonely, no "
              "percussion, seamless loopable background music",
        negative="vocals, choir lyrics, drums, electronic, synth bass, pop, rock, epic trailer"),
    "music_mirewatch": dict(
        title="Mirewatch",
        style="fantasy RPG village theme, instrumental, 84 bpm, G major, medieval folk, plucked lute and harp, fiddle "
              "melody, tin whistle, light frame drum, cozy, bright and unhurried, warm hearth, seamless loopable "
              "background music",
        negative="vocals, lyrics, electronic, synth, pop, rock, heavy drums, epic trailer"),
    "music_drowned": dict(
        title="The Drowned Ruins",
        style="dark fantasy RPG dungeon ambience, instrumental, very slow 56 bpm, A phrygian, low bowed strings drone, "
              "distant muffled bells under water, deep soft timpani swells, sparse eerie celesta, mysterious and "
              "ancient, tense but not horror, seamless loopable background music",
        negative="vocals, lyrics, electronic beats, pop, rock, jump scares, fast tempo"),
}

# Clip name -> Suno id of the song.
TRACKS = {
    "music_hollowmere_1": "7b9a53a4-90e3-4fde-8364-363a67a89b2b",
    "music_hollowmere_2": "5b69c491-d7af-45b7-9d6e-b5ed122a9737",
    "music_mirewatch_1": "7fa5d0e1-49b7-4e19-ab23-b13b8d6c95bc",
    "music_mirewatch_2": "c5b0f836-671e-4132-8719-d5d1a70a2a68",
    "music_drowned_1": "8192e059-8b9c-4187-a1d8-c639155f708d",
    "music_drowned_2": "5e3451ea-f28a-43ca-b18c-7fe0c9db3d75",
}

SILENCE_DB = -60.0
FADE_IN = 0.05
FADE_OUT = 4.0
TARGET_RMS = 0.12  # About -18.4 dBFS, where the synthesized music was.
PEAK = 0.89
BITRATE = 128000


def load(path):
    sound = aud.Sound(path)
    rate = int(sound.specs[0])
    data = np.asarray(sound.data(), dtype=np.float32)  # frames x channels
    if data.ndim == 1:
        data = data[:, None]
    return data, rate


def prepare(data, rate):
    level = np.abs(data).max(axis=1)
    loud = np.nonzero(level > 10 ** (SILENCE_DB / 20))[0]
    if len(loud) == 0:
        raise ValueError("silent")
    data = data[loud[0]:loud[-1] + 1].copy()
    fade_in = int(FADE_IN * rate)
    data[:fade_in] *= np.linspace(0.0, 1.0, fade_in, dtype=np.float32)[:, None]
    fade_out = min(int(FADE_OUT * rate), len(data) // 4)
    data[-fade_out:] *= (0.5 + 0.5 * np.cos(np.linspace(0.0, np.pi, fade_out, dtype=np.float32)))[:, None]
    rms = float(np.sqrt(np.mean(np.square(data))))
    gain = min(TARGET_RMS / rms, PEAK / float(np.abs(data).max()))
    return data * gain


def write(path, data, rate):
    channels = aud.CHANNELS_STEREO if data.shape[1] == 2 else aud.CHANNELS_MONO
    sound = aud.Sound.buffer(np.ascontiguousarray(data, dtype=np.float32), rate)
    sound.write(path, rate, channels, aud.FORMAT_FLOAT32, aud.CONTAINER_OGG, aud.CODEC_VORBIS, BITRATE)


def main():
    missing = [i for i in TRACKS.values() if not os.path.isfile(os.path.join(SOURCE_DIR, i + ".wav"))]
    if missing:
        print("Missing in %s: %s (download the songs as WAV, named by id)" % (SOURCE_DIR, ", ".join(missing)))
        sys.exit(1)
    os.makedirs(OUT, exist_ok=True)
    for name, song in TRACKS.items():
        data, rate = load(os.path.join(SOURCE_DIR, song + ".wav"))
        data = prepare(data, rate)
        write(os.path.join(OUT, name + ".ogg"), data, rate)
        rms = float(np.sqrt(np.mean(np.square(data))))
        print("Imported %-20s %6.1f s  rms %.3f  peak %.2f" % (name, len(data) / rate, rms, float(np.abs(data).max())))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(1)
