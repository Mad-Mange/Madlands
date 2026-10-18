"""Renders quick pictures of built assets (Workbench engine) to check them without Unity."""

import math
import os

import bpy
import numpy as np
from mathutils import Vector

CELL = 220


def _setup(scene):
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x = CELL
    scene.render.resolution_y = CELL
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    world = bpy.data.worlds.new("Preview") if scene.world is None else scene.world
    scene.world = world


def _bounds(objects):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        if obj.type != "MESH":
            continue
        for corner in obj.bound_box:
            p = obj.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
    return lo, hi


def _camera(scene, lo, hi, yaw_deg, pitch_deg=15.0):
    center = (lo + hi) / 2
    size = max((hi - lo).length, 0.5)
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = size * 0.95
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    yaw, pitch = math.radians(yaw_deg), math.radians(pitch_deg)
    # yaw 0 = looking at the model's front (the model faces -Y).
    direction = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    cam.location = center + direction * size * 3
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    return cam


def _render(scene, path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    pixels = np.array(img.pixels[:], dtype=np.float32).reshape(CELL, CELL, 4)
    bpy.data.images.remove(img)
    os.remove(path)
    return pixels


def _save_sheet(rows, path):
    height = len(rows) * CELL
    width = max(len(r) for r in rows) * CELL
    sheet = np.ones((height, width, 4), dtype=np.float32)
    for ri, row in enumerate(rows):
        for ci, cell in enumerate(row):
            # Blender images start at the bottom row: the first row of cells goes on top.
            y0 = height - (ri + 1) * CELL
            sheet[y0:y0 + CELL, ci * CELL:(ci + 1) * CELL] = cell
    img = bpy.data.images.new("Sheet", width, height, alpha=True)
    img.pixels = sheet.ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()


def render_asset(name, root, animated, out_dir, dress=None):
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    _setup(scene)
    if dress is not None:
        dress(root)
    objects = [root] + list(root.children_recursive)
    tmp = os.path.join(out_dir, "_cell.png")

    if root.animation_data is not None:
        root.animation_data.action = None
    if root.type == "ARMATURE":
        for pb in root.pose.bones:
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.location = (0, 0, 0)
    bpy.context.view_layer.update()
    lo, hi = _bounds(objects)

    views = []
    for yaw in (0, 45, 90, 180):
        cam = _camera(scene, lo, hi, yaw)
        views.append(_render(scene, tmp))
        bpy.data.objects.remove(cam)
    rows = [views]

    if animated:
        # Rows: one per action, five moments each, seen from the front-left.
        pad = Vector((0.15, 0.15, 0.0))
        cam = _camera(scene, lo - pad - Vector((0, 0, 0.1)), hi + pad, 35)
        for action in sorted(bpy.data.actions, key=lambda a: a.name):
            root.animation_data.action = action
            if action.slots:
                root.animation_data.action_slot = action.slots[0]
            start, end = action.frame_range
            row = []
            for i in range(5):
                scene.frame_set(int(round(start + (end - start) * i / 4)))
                row.append(_render(scene, tmp))
            rows.append(row)
            print("  preview " + action.name)
        bpy.data.objects.remove(cam)
    _save_sheet(rows[:1], os.path.join(out_dir, name + ".png"))
    # Animation sheets with four actions each, so they stay readable.
    for i in range(1, len(rows), 4):
        _save_sheet(rows[i:i + 4], os.path.join(out_dir, "%s_anim%d.png" % (name, (i - 1) // 4 + 1)))
