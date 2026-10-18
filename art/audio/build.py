"""
Builds the synthesized sounds and music as OGG Vorbis into the Unity project. Runs inside
Blender for its numpy and its audio encoder (the same tool as the models, art/README.md):

    blender -b --factory-startup -P art/audio/build.py -- [name ...]

Without names everything is built. The CC0 sound effects from Kenney are imported separately
(art/audio/kenney.py).
"""

import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import aud  # noqa: E402
import numpy as np  # noqa: E402

import music  # noqa: E402
import sounds  # noqa: E402
from synth import RATE  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
UNITY_AUDIO = os.path.join(ROOT, "client", "Assets", "Madlands", "Audio")

ALL = dict(sounds.SOUNDS)
ALL.update(music.MUSIC)
BITRATES = {"Sfx": 96000, "Ambience": 112000, "Music": 128000}


def write(path, data, bitrate):
    data = np.asarray(data, dtype=np.float32)
    if not np.all(np.isfinite(data)):
        raise ValueError(path + ": not finite")
    stereo = data.ndim == 2
    frames = data.T.copy() if stereo else data.reshape(-1, 1)
    sound = aud.Sound.buffer(frames, RATE)
    channels = aud.CHANNELS_STEREO if stereo else aud.CHANNELS_MONO
    sound.write(path, RATE, channels, aud.FORMAT_FLOAT32, aud.CONTAINER_OGG, aud.CODEC_VORBIS, bitrate)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or list(ALL)
    unknown = [n for n in names if n not in ALL]
    if unknown:
        print("Unknown sounds: " + ", ".join(unknown))
        sys.exit(1)
    for name in names:
        folder, builder = ALL[name]
        started = time.time()
        data = builder()
        out_dir = os.path.join(UNITY_AUDIO, folder)
        os.makedirs(out_dir, exist_ok=True)
        write(os.path.join(out_dir, name + ".ogg"), data, BITRATES[folder])
        samples = data.shape[-1]
        rms = float(np.sqrt(np.mean(np.square(data))))
        print("Built %-24s %6.2f s  peak %.2f  rms %.3f  (%.1f s)" % (name, samples / RATE, float(np.max(np.abs(data))), rms, time.time() - started))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(1)
