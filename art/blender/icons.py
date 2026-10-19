"""
Small symbols for the interface (what an NPC offers, shown over its head and on the maps), modelled
low-poly like everything else and rendered from the front to transparent PNGs.

    blender -b --factory-startup -P art/blender/icons.py -- [icon ...]

Writes client/Assets/Madlands/Art/Icons/<Name>.png (ArtImporter sets them up as interface textures).
"""

import math
import os
import random
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import items  # noqa: E402
import lowpoly as lp  # noqa: E402

SIZE = 128
# A dark rim around every icon, so it reads over bright grass and sky when drawn small.
OUTLINE = (0.07, 0.05, 0.03)
OUTLINE_PX = 4
OUT_DIR = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                                        "client", "Assets", "Madlands", "Art", "Icons"))


def goods():
    """General goods: a full sack tied at the neck."""
    mb = lp.MeshBuilder("IconGoods")
    canvas = lp.material("Canvas", "#d8c08a")
    rope = lp.material("Rope", "#7a5230")
    rng = random.Random(7)
    pts = []
    for _ in range(40):
        theta = rng.uniform(0, 2 * math.pi)
        z = rng.uniform(-1, 1)
        r = math.sqrt(1 - z * z)
        pts.append((0.42 * r * math.cos(theta), 0.42 * r * math.sin(theta), 0.4 + 0.36 * z))
    mb.hull(pts, canvas)
    mb.frustum((0, 0, 0.72), (0, 0, 0.84), 0.14, 0.1, canvas, sides=7)
    mb.frustum((0, 0, 0.8), (0, 0, 0.86), 0.12, 0.12, rope, sides=7)
    # The open top flares out.
    mb.frustum((0, 0, 0.86), (0, 0, 1.0), 0.1, 0.2, canvas, sides=7, cap_a=False)
    # A few coins in front: this is where you buy and sell.
    gold = lp.material("Gold", "#f0c040")
    for x, z in ((-0.3, 0.06), (-0.12, 0.05), (-0.22, 0.15)):
        mb.frustum((x, -0.4, z), (x, -0.43, z), 0.1, 0.1, gold, sides=10)
    return [mb.finish()]


def inn():
    """The inn: a wooden tankard, banded with iron, foaming over."""
    mb = lp.MeshBuilder("IconInn")
    wood = lp.material("Wood", "#9a6a3a")
    band = lp.material("Metal", "#6e737b")
    foam = lp.material("Foam", "#f6f0dc")
    mb.frustum((0, 0, 0), (0, 0, 0.8), 0.32, 0.3, wood, sides=10)
    for z in (0.12, 0.66):
        mb.frustum((0, 0, z), (0, 0, z + 0.07), 0.335, 0.33, band, sides=10)
    # Handle on the right: three bars bent around.
    mb.box((0.38, 0, 0.62), (0.16, 0.08, 0.08), wood)
    mb.box((0.46, 0, 0.4), (0.08, 0.08, 0.5), wood)
    mb.box((0.38, 0, 0.18), (0.16, 0.08, 0.08), wood)
    rng = random.Random(3)
    for x, z, r in ((-0.14, 0.86, 0.17), (0.12, 0.88, 0.16), (0.0, 0.94, 0.18), (0.22, 0.8, 0.12), (-0.26, 0.8, 0.11)):
        mb.sphere((x, rng.uniform(-0.05, 0.05), z), r, foam, segments=7, rings=5)
    return [mb.finish()]


def weapons():
    """The weaponsmith: two broad swords crossed over a round shield."""
    shield = items.buckler()
    shield.rotation_euler = (0, 0, math.radians(-90))  # its face (+X) toward the camera (-Y)
    shield.scale = (1.5, 1.5, 1.5)
    shield.location = (0, 0.25, -0.15)
    objects = [shield]
    for angle in (-42, 42):
        sword = items.shortsword()
        sword.scale = (2.0, 2.0, 1.0)
        rotation = Matrix.Rotation(math.radians(angle), 4, "Y")
        sword.rotation_euler = rotation.to_euler()
        # Cross the blades at their middle, not at the grips.
        sword.location = -(rotation @ Vector((0, 0, 0.36)))
        objects.append(sword)
    return objects


# (builder, camera yaw, camera pitch): yaw 0 looks at the front (-Y side).
ICONS = {
    "IconGoods": (goods, 20.0, 12.0),
    "IconInn": (inn, 18.0, 14.0),
    "IconWeapons": (weapons, 0.0, 0.0),
}


def render(name):
    builder, yaw, pitch = ICONS[name]
    lp.reset_scene()
    scene = bpy.context.scene
    objects = builder()
    bpy.context.view_layer.update()

    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_object_outline = True
    scene.display.shading.object_outline_color = (0.05, 0.035, 0.02)
    scene.display.render_aa = "16"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.exposure = 0.45
    scene.render.film_transparent = True
    scene.render.resolution_x = SIZE
    scene.render.resolution_y = SIZE
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"

    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        for corner in obj.bound_box:
            p = obj.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
    center = (lo + hi) / 2
    size = max(hi.x - lo.x, hi.z - lo.z, 0.1)
    cam_data = bpy.data.cameras.new("IconCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = size * 1.3  # room for the outline
    cam = bpy.data.objects.new("IconCam", cam_data)
    scene.collection.objects.link(cam)
    y, p = math.radians(yaw), math.radians(pitch)
    direction = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
    cam.location = center + direction * 10
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam

    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name + ".png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    add_outline(path)
    print("[icons] " + path)


def add_outline(path):
    """Puts a dark rim of OUTLINE_PX pixels around the icon's shape."""
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    alpha = px[:, :, 3]
    grown = alpha.copy()
    for dy in range(-OUTLINE_PX, OUTLINE_PX + 1):
        for dx in range(-OUTLINE_PX, OUTLINE_PX + 1):
            if dx * dx + dy * dy <= OUTLINE_PX * OUTLINE_PX:
                grown = np.maximum(grown, np.roll(np.roll(alpha, dy, 0), dx, 1))
    a = alpha[:, :, None]
    out_a = a + grown[:, :, None] * (1 - a)
    rgb = (px[:, :, :3] * a + np.array(OUTLINE, dtype=np.float32) * grown[:, :, None] * (1 - a)) / np.maximum(out_a, 1e-4)
    img.pixels = np.concatenate([rgb, out_a], axis=2).ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or list(ICONS)
    unknown = [n for n in names if n not in ICONS]
    if unknown:
        print("Unknown icons: " + ", ".join(unknown) + ". Known: " + ", ".join(ICONS))
        sys.exit(1)
    for name in names:
        render(name)


main()
