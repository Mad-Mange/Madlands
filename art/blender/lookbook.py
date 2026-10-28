"""
Renders the humanoid races dressed in outfits, to compare the character art with its reference
sheets without Unity: front, three-quarter, side and back, a few animation poses and the head close up.

    blender -b --factory-startup -P art/blender/lookbook.py -- [look ...] [--out DIR] [--lod N]

Without look names every look is rendered. Pictures go to DIR (default builds/lookbook) as
<look>.png (with --lod N: <look>_lod<N>.png). The colors and pieces follow the client's rules
(GearVisuals.cs): body regions take the item colors, a region's Trim material a darker tone; gear
pieces are tinted with their item's color (GearTrim darker, GearDark much darker); everything else
keeps its own color.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

import humanoid  # noqa: E402
import lowpoly as lp  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TRIM_SHADE, DARK_SHADE = 0.62, 0.38

# look -> (race, body region colors, [(piece, color), ...])
LOOKS = {
    "base_human": ("BrightvaleHuman", {}, []),
    "base_korrin": ("Korrin", {}, []),
    "warrior": ("BrightvaleHuman",
                dict(Chest="#3b3c44", Arms="#3b3c44", Legs="#34343a", Feet="#4a3222", Hands="#5a3a26", Waist="#5a3a22"),
                [("Breastplate", "#a9aeb6"), ("StarEmblem", "#a9aeb6"), ("Pauldrons", "#a9aeb6"), ("Gauntlets", "#a9aeb6"),
                 ("Greaves", "#a9aeb6"), ("Tabard", "#2c4a8c"), ("Cowl", "#2c4a8c")]),
    "shardmage": ("BrightvaleHuman",
                  dict(Chest="#4b3480", Arms="#4b3480", Legs="#2e2440", Feet="#3a2618", Hands="#4a3020", Waist="#5a3a22",
                       Hair="#d9d6de"),
                  [("Coat", "#4b3480"), ("Cowl", "#4b3480")]),
    "warden": ("Korrin",
               dict(Chest="#2f5a58", Arms="#5a3a26", Legs="#3c3a36", Feet="#4a3222", Hands="#5a3a26", Waist="#5a3a22"),
               [("FurMantle", "#c9b48e"), ("Harness", "#5a3a22"), ("Tabard", "#2f5a58"), ("WardenCloak", "#2f5a58")]),
    "captain": ("BrightvaleHuman",
                dict(Chest="#3a4a6e", Arms="#5d6676", Legs="#2f3d66", Hands="#6d7480", Feet="#4a4f58"),
                [("Helm", "#9aa3b2"), ("Pauldrons", "#9aa3b2"), ("Breastplate", "#9aa3b2"), ("Cloak", "#27428a")]),
    "korrin_robe": ("Korrin", dict(Chest="#5a4370", Arms="#5a4370", Legs="#4a3a5c"),
                    [("RobeSkirt", "#5a4370"), ("RobeCuffs", "#5a4370"), ("Hood", "#3a2f4a")]),
    "prospector": ("Korrin", dict(Chest="#5c616b", Arms="#5c616b", Legs="#4a3a2a", Hair="#3a2a1e"), [("MinerHelm", "#7d8490")]),
    "fisherman": ("Korrin", dict(Chest="#4a6a8a", Arms="#4a6a8a", Legs="#4a4438", Hair="#d0ccc4"), [("OilskinHat", "#c9a640")]),
    "farmer": ("BrightvaleHuman", dict(Chest="#6b7a48", Arms="#6b7a48", Legs="#5a4a35", Hair="#8a7d6c"), [("StrawHat", "#d8bf6a")]),
    "herbalist": ("BrightvaleHuman", dict(Chest="#5a4370", Arms="#5a4370", Legs="#4a3a5c", Hair="#c8c4bc"),
                  [("RobeSkirt", "#5a4370"), ("RobeCuffs", "#5a4370"), ("WizardHat", "#2c2433")]),
    "vorn": ("BrightvaleHuman", dict(Chest="#5a1a1a", Arms="#2a2a2c", Legs="#2a2a2c", Hair="#1a1a1a", Hands="#2a2a2c"),
             [("HornedHelm", "#3a3c42"), ("SpikedPauldrons", "#3a3c42"), ("Cloak", "#5a1a1a")]),
    "mireling": ("Mireling", {}, []),
    "oracle": ("Mireling", dict(Chest="#5a7a9a", Arms="#5a7a9a", Hair="#3a4a6a", Waist="#7a4a8a"),
               [("RobeSkirt", "#4a3a6a"), ("RobeCuffs", "#4a3a6a")]),
}

HIDES_HAIR = {"Hood", "BoneHood", "Helm", "HornedHelm", "MinerHelm", "OilskinHat"}
HIDES_LEGS = {"RobeSkirt", "Rags"}
POSES = [("Idle", humanoid.idle, 0.25), ("Run", humanoid.run, 0.15), ("Run", humanoid.run, 0.6),
         ("Attack1H", None, 11), ("CastLoop", humanoid.cast_loop, 0.3), ("Sit", humanoid.sit, 0.0)]


def _linear(color):
    return tuple(lp.srgb_to_linear(c) for c in color) + (1.0,)


def _tinted(source, srgb, shade):
    mat = source.copy()
    mat.name = source.name + "_look"
    mat.diffuse_color = _linear(tuple(c * shade for c in srgb))
    return mat


def dress(arm, race, regions, pieces, lod):
    """Shows the worn pieces (and the given LOD), tints body regions and pieces like the client does."""
    worn = {name for name, _ in pieces}
    suffix = "_LOD%d" % lod
    hidden_regions = set()
    for name in worn:
        if name in HIDES_HAIR:
            hidden_regions.add("Hair")
        if name in HIDES_LEGS:
            hidden_regions.add("Legs")
    for obj in arm.children:
        if obj.type != "MESH":
            continue
        base = obj.name[:-len(suffix)] if obj.name.endswith(suffix) else None
        if "_LOD" in obj.name and base is None:
            obj.hide_render = True
            continue
        base = base or obj.name
        if base.startswith("Gear"):
            piece = base[4:]
            obj.hide_render = piece not in worn
            if piece in worn:
                srgb = lp.hex_color(dict(pieces)[piece])
                for slot in obj.material_slots:
                    m = slot.material
                    if m is None or not m.name.startswith("Gear"):
                        continue
                    shade = TRIM_SHADE if m.name.endswith("Trim") else DARK_SHADE if m.name.endswith("Dark") else 1.0
                    slot.link = "OBJECT"
                    slot.material = _tinted(m, srgb, shade)
            continue
        obj.hide_render = False
        for slot in obj.material_slots:
            m = slot.material
            if m is None:
                continue
            region = m.name
            for name, color in regions.items():
                shade = None
                if region == name or region == name + "Lower" or (name == "Hair" and region == "Beard"):
                    shade = 1.0
                elif region == name + "Trim":
                    shade = TRIM_SHADE
                if shade is not None:
                    # Under a breastplate the shirt is a darker padding (GearVisuals).
                    if name in ("Chest", "Arms") and "Breastplate" in worn:
                        shade *= 0.6
                    slot.link = "OBJECT"
                    slot.material = _tinted(m, lp.hex_color(color), shade)
        _remove_faces(obj, hidden_regions)


def _remove_faces(obj, regions):
    """Deletes the faces of hidden regions (the client draws them with an invisible material)."""
    if not regions:
        return
    indices = {i for i, slot in enumerate(obj.data.materials) if slot is not None and slot.name in regions}
    if not indices:
        return
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index in indices], context="FACES")
    bm.to_mesh(obj.data)
    bm.free()


def _setup(scene, size):
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "MATERIAL"
    shading.show_object_outline = False
    shading.show_cavity = True
    shading.cavity_type = "WORLD"
    shading.show_specular_highlight = True
    shading.show_shadows = False
    # Unity draws only front faces: cloth is a surface with its lining facing the other way.
    shading.show_backface_culling = True
    scene.render.resolution_x = size[0]
    scene.render.resolution_y = size[1]
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    scene.display.render_aa = "8"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("Look")
    scene.world.color = (0.32, 0.32, 0.34)
    shading.background_type = "WORLD"


def _visible_bounds(arm):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in arm.children:
        if obj.type != "MESH" or obj.hide_render:
            continue
        ev = obj.evaluated_get(depsgraph)
        mesh = ev.to_mesh()
        for v in mesh.vertices:
            p = ev.matrix_world @ v.co
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
        ev.to_mesh_clear()
    return lo, hi


def _shot(scene, center, height, yaw, pitch, path, width_ratio=1.0):
    cam_data = bpy.data.cameras.new("LookCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = height * max(1.0, width_ratio)
    cam = bpy.data.objects.new("LookCam", cam_data)
    scene.collection.objects.link(cam)
    yaw, pitch = math.radians(yaw), math.radians(pitch)
    direction = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    cam.location = center + direction * 10
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)
    img = bpy.data.images.load(path)
    w, h = img.size
    pixels = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(img)
    os.remove(path)
    return pixels


def _sheet(rows, path):
    height = sum(max(c.shape[0] for c in row) for row in rows)
    width = max(sum(c.shape[1] for c in row) for row in rows)
    sheet = np.zeros((height, width, 4), dtype=np.float32)
    sheet[..., 3] = 1.0
    y = height
    for row in rows:
        rh = max(c.shape[0] for c in row)
        y -= rh
        x = 0
        for cell in row:
            sheet[y:y + cell.shape[0], x:x + cell.shape[1]] = cell
            x += cell.shape[1]
    img = bpy.data.images.new("Sheet", width, height, alpha=True)
    img.pixels = sheet.ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()


def _pose(arm, name, func, at):
    """Poses the armature: a sampled pose function at t, or a keyframed clip at a frame."""
    if arm.animation_data is None:
        arm.animation_data_create()
    action = bpy.data.actions.get("Look" + name)
    if action is None:
        if func is not None:
            action = lp.sample_action(arm, "Look" + name, 30, func, loop=True)
        else:
            action = lp.keyframe_action(arm, "Look" + name, humanoid.attack_one_hand())
    arm.animation_data.action = action
    if action.slots:
        arm.animation_data.action_slot = action.slots[0]
    start, end = action.frame_range
    frame = start + (end - start) * at if func is not None else at
    bpy.context.scene.frame_set(int(round(frame)))


def _rest(arm):
    if arm.animation_data is not None:
        arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
    bpy.context.view_layer.update()


def render_look(name, out_dir, lod):
    race, regions, pieces = LOOKS[name]
    lp.reset_scene()
    arm = humanoid.build_race(race)
    dress(arm, race, regions, pieces, lod)
    scene = bpy.context.scene
    tmp = os.path.join(out_dir, "_look.png")
    _rest(arm)
    lo, hi = _visible_bounds(arm)
    height = (hi.z - lo.z) * 1.08
    center = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, (lo.z + hi.z) / 2))
    r = humanoid.RACES[race]

    _setup(scene, (360, 600))
    body = [_shot(scene, center, height, yaw, 6, tmp, 0.6) for yaw in (0, 35, 90, 180)]
    # The head close up, front and three-quarter, then from behind.
    head = Vector((0, -0.02, r["head"] + 0.13))
    _setup(scene, (360, 360))
    faces = [_shot(scene, head, 0.5, yaw, 4, tmp) for yaw in (0, 35, 150)]
    _setup(scene, (300, 500))
    poses = []
    pad = height * 1.15
    for clip, func, at in POSES:
        _pose(arm, clip, func, at)
        poses.append(_shot(scene, center + Vector((0, 0, -0.05)), pad, 30, 8, tmp, 0.6))
    _rest(arm)
    suffix = "" if lod == 0 else "_lod%d" % lod
    path = os.path.join(out_dir, name + suffix + ".png")
    _sheet([body + faces[:1], poses, faces[1:]], path)
    print("Lookbook " + path)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out_dir = os.path.join(ROOT, "builds", "lookbook")
    lod = 0
    if "--out" in argv:
        i = argv.index("--out")
        out_dir = os.path.abspath(argv[i + 1])
        del argv[i:i + 2]
    if "--lod" in argv:
        i = argv.index("--lod")
        lod = int(argv[i + 1])
        del argv[i:i + 2]
    names = argv or list(LOOKS)
    unknown = [n for n in names if n not in LOOKS]
    if unknown:
        print("Unknown looks: " + ", ".join(unknown) + ". Known: " + ", ".join(LOOKS))
        sys.exit(1)
    os.makedirs(out_dir, exist_ok=True)
    for name in names:
        render_look(name, out_dir, lod)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(1)
