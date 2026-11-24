"""
Imports the music made with Suno (ADR 0016) into client/Assets/Madlands/Audio/Music. Runs inside Blender for its
audio encoder, like build.py:

    blender -b --factory-startup -P art/audio/suno.py -- [clip ...]

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
    "music_greywood": dict(
        title="The Greywood",
        style="fantasy RPG forest theme, instrumental, slow 70 bpm, E dorian, deep old forest, mysterious and quiet, alto "
              "flute and clarinet melody, plucked harp, soft pizzicato strings, gentle hand drum, mossy, dappled light, "
              "watchful but calm, seamless loopable background music",
        negative="vocals, lyrics, choir, electronic, synth, pop, rock, heavy drums, epic trailer"),
    "music_reedfen": dict(
        title="Reedfen",
        style="fantasy RPG marshland theme, instrumental, slow 62 bpm, A dorian, misty wetlands, breathy reed flute "
              "melody, hammered dulcimer, low string drone, soft wooden percussion, sparse plucked kalimba, humid and "
              "murky but gentle, a little strange, seamless loopable background music",
        negative="vocals, lyrics, choir, electronic, synth bass, pop, rock, heavy drums, epic trailer"),
    # The login and character screens.
    "music_title": dict(
        title="Madlands",
        style="fantasy MMORPG main menu theme, instrumental, 72 bpm, D major, warm orchestral, noble french horn melody "
              "answered by strings, flowing harp, soft flute, hopeful and adventurous, nostalgic longing, builds gently "
              "to a swell then settles, soft timpani rolls, seamless loopable background music",
        negative="vocals, lyrics, choir, electronic, synth, pop, rock, heavy drums, epic trailer"),
    # While Ruukhar or Gloomjaw fights nearby (GameSounds).
    "music_boss": dict(
        title="The Slagborn Wakes",
        style="fantasy RPG boss battle music, instrumental, 138 bpm, D minor, driving taiko and orchestral percussion, "
              "low brass ostinato, aggressive staccato strings, menacing horn calls, tense and relentless but heroic, "
              "steady intensity, orchestral game soundtrack",
        negative="vocals, lyrics, choir, electronic, dubstep, rock guitar, metal, quiet intro"),
}

# Clip name -> Suno id of the song. Takes with wordless humming were left out (title bc4f8ac5, Greywood 95786597).
TRACKS = {
    "music_hollowmere_1": "7b9a53a4-90e3-4fde-8364-363a67a89b2b",
    "music_hollowmere_2": "5b69c491-d7af-45b7-9d6e-b5ed122a9737",
    "music_mirewatch_1": "7fa5d0e1-49b7-4e19-ab23-b13b8d6c95bc",
    "music_mirewatch_2": "c5b0f836-671e-4132-8719-d5d1a70a2a68",
    "music_drowned_1": "8192e059-8b9c-4187-a1d8-c639155f708d",
    "music_drowned_2": "5e3451ea-f28a-43ca-b18c-7fe0c9db3d75",
    "music_greywood_1": "835d3f09-db68-467b-97e3-f82e31cbf0c6",
    "music_reedfen_1": "b97ada32-6cee-4483-b4bb-a32e8121cf73",
    "music_reedfen_2": "2bea0e76-2eb4-4eab-a0d4-f71990de351f",
    "music_title_1": "15ed8e3f-6f41-47e4-b61b-6a7f34b0276d",
    "music_boss_1": "e31bb5d9-af92-49b4-9148-e2a0a2827dc4",
    "music_boss_2": "736d19f7-e97b-409c-a4b2-3d9197434209",
}

SILENCE_DB = -60.0
FADE_IN = 0.05
FADE_OUT = 4.0
# The boss music ends on a hit and goes straight on to its next take: only a short fade.
FADE_OUT_OF = {"music_boss": 0.5}
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


def prepare(data, rate, fade_seconds):
    level = np.abs(data).max(axis=1)
    loud = np.nonzero(level > 10 ** (SILENCE_DB / 20))[0]
    if len(loud) == 0:
        raise ValueError("silent")
    data = data[loud[0]:loud[-1] + 1].copy()
    fade_in = int(FADE_IN * rate)
    data[:fade_in] *= np.linspace(0.0, 1.0, fade_in, dtype=np.float32)[:, None]
    fade_out = min(int(fade_seconds * rate), len(data) // 4)
    data[-fade_out:] *= (0.5 + 0.5 * np.cos(np.linspace(0.0, np.pi, fade_out, dtype=np.float32)))[:, None]
    rms = float(np.sqrt(np.mean(np.square(data))))
    gain = min(TARGET_RMS / rms, PEAK / float(np.abs(data).max()))
    return data * gain


def write(path, data, rate):
    channels = aud.CHANNELS_STEREO if data.shape[1] == 2 else aud.CHANNELS_MONO
    sound = aud.Sound.buffer(np.ascontiguousarray(data, dtype=np.float32), rate)
    sound.write(path, rate, channels, aud.FORMAT_FLOAT32, aud.CONTAINER_OGG, aud.CODEC_VORBIS, BITRATE)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or list(TRACKS)
    unknown = [n for n in names if n not in TRACKS]
    if unknown:
        print("Unknown clips: " + ", ".join(unknown))
        sys.exit(1)
    missing = [TRACKS[n] for n in names if not os.path.isfile(os.path.join(SOURCE_DIR, TRACKS[n] + ".wav"))]
    if missing:
        print("Missing in %s: %s (download the songs as WAV, named by id)" % (SOURCE_DIR, ", ".join(missing)))
        sys.exit(1)
    os.makedirs(OUT, exist_ok=True)
    for name in names:
        song = TRACKS[name]
        data, rate = load(os.path.join(SOURCE_DIR, song + ".wav"))
        data = prepare(data, rate, FADE_OUT_OF.get(name.rsplit("_", 1)[0], FADE_OUT))
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
