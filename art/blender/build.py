"""
Builds the Madlands art: every asset is made by a Python function, saved as a .blend (for looking at
and editing in Blender) and exported as FBX into the Unity project.

    blender -b --factory-startup -P art/blender/build.py -- [asset ...] [--preview DIR]

Without asset names everything is built. --preview also renders a picture of each asset (and of
the animations) into DIR, for checking the art without Unity.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

import beasts  # noqa: E402
import brute  # noqa: E402
import buildings  # noqa: E402
import creatures  # noqa: E402
import effects  # noqa: E402
import humanoid  # noqa: E402
import items  # noqa: E402
import lowpoly as lp  # noqa: E402
import preview  # noqa: E402
import props  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
UNITY_ART = os.path.join(ROOT, "client", "Assets", "Madlands", "Art")
BLEND_DIR = os.path.join(ROOT, "art", "blend")

# name -> (Unity folder, builder returning the root object, has animations)
ASSETS = {
    "BrightvaleHuman": ("Characters", lambda: humanoid.build_race("BrightvaleHuman"), False),
    "Korrin": ("Characters", lambda: humanoid.build_race("Korrin"), False),
    "Mireling": ("Characters", lambda: humanoid.build_race("Mireling"), False),
    "HumanoidAnimations": ("Characters", humanoid.build_animations, True),
    "Wolf": ("Creatures", creatures.build_wolf, True),
    "Frog": ("Creatures", creatures.build_frog, True),
    "TrainingDummy": ("Creatures", creatures.build_training_dummy, True),
    "Boar": ("Creatures", lambda: beasts.build_quad("Boar"), True),
    "Bear": ("Creatures", lambda: beasts.build_quad("Bear"), True),
    "Crocodile": ("Creatures", lambda: beasts.build_quad("Crocodile"), True),
    "Gloomjaw": ("Creatures", lambda: beasts.build_quad("Gloomjaw"), True),
    "Spider": ("Creatures", beasts.build_spider, True),
    "Shardling": ("Creatures", beasts.build_shardling, True),
    "Slagborn": ("Creatures", brute.build_slagborn, True),
}
ASSETS.update({name: ("Items", builder, False) for name, builder in items.WEAPONS.items()})
ASSETS.update({name: ("Props", builder, False) for name, builder in props.PROPS.items()})
ASSETS.update({name: ("Props", builder, False) for name, builder in buildings.BUILDINGS.items()})
ASSETS.update({name: ("Effects", builder, False) for name, builder in effects.EFFECTS.items()})

# Previews of mesh-less animation files put a body on the skeleton (after the export).
PREVIEW_DRESS = {
    "HumanoidAnimations": lambda arm: lp.bind(humanoid.body("PreviewBody", humanoid.RACES["BrightvaleHuman"]), arm),
}


def build(name, preview_dir):
    folder, builder, animated = ASSETS[name]
    lp.reset_scene()
    root = builder()
    os.makedirs(BLEND_DIR, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BLEND_DIR, name + ".blend"), compress=True)
    out_dir = os.path.join(UNITY_ART, folder)
    os.makedirs(out_dir, exist_ok=True)
    lp.export_fbx(os.path.join(out_dir, name + ".fbx"), [root], animated)
    print("Built " + name)
    if preview_dir:
        preview.render_asset(name, root, animated, preview_dir, PREVIEW_DRESS.get(name))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    preview_dir = None
    if "--preview" in argv:
        i = argv.index("--preview")
        preview_dir = os.path.abspath(argv[i + 1])
        del argv[i:i + 2]
    names = argv or list(ASSETS)
    unknown = [n for n in names if n not in ASSETS]
    if unknown:
        print("Unknown assets: " + ", ".join(unknown) + ". Known: " + ", ".join(ASSETS))
        sys.exit(1)
    for name in names:
        build(name, preview_dir)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(1)
