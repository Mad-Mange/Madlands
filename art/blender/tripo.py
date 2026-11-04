"""
Characters from Tripo (ADR 0014): an unrigged, textured FBX standing in a T-pose becomes a character like the
scripted races. It gets the humanoid skeleton, so it plays the shared humanoid clips and can hold weapons.

    MODELS row -> three levels of detail, skin weights, Art/Characters/<Name>.fbx and Art/Textures/<Name>_*.png

The originals (about 18 MB each with their 4K textures) stay outside the repository, in MADLANDS_TRIPO_DIR. Only
what the game uses is committed. Without the original, a build skips the model and keeps the committed files.
"""

import math
import os

import bpy
import numpy as np
from mathutils import Matrix, Vector

import lowpoly as lp

SOURCE_DIR = os.environ.get("MADLANDS_TRIPO_DIR", r"D:\3D_Stuff\Characters")
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEXTURE_DIR = os.path.join(ROOT, "client", "Assets", "Madlands", "Art", "Textures")
TEXTURE_SIZE = 1024
LOD_TRIANGLES = (8000, 3000, 1000)
MAX_INFLUENCES = 4
# An edge longer than this many times its rest length in a test pose counts as torn (see _report).
TORN = 2.5

# "source": the FBX under SOURCE_DIR. "height": meters from the feet to the top in the game.
# "joints": where the joints are in the source file (its meters, read off front and side renders; left is +X, front -Y).
# "hue_shift" (optional): (start, full, degrees) turns the base color's hues from `start` up by up to `degrees`, in full
# from `full`, and leaves lower hues alone: it recolors one material of a painted model, like skin apart from leather.
MODELS = {
    "MireCroaker": {
        "source": "FrogMadLands/FrogOwn.fbx",
        "height": 1.55,
        "hue_shift": (30, 38, 40),  # skin (hues 34-45) to moss green, the leather (27-32) stays brown
        "joints": {
            "hips": (0.01, 0.50), "spine": 0.56, "chest": 0.64,
            "neck": (-0.01, 0.75), "head": (-0.02, 0.80), "top": (-0.04, 0.94),
            "arm_y": -0.02, "arm_z": 0.73,
            "clavicle_x": 0.04, "shoulder_x": 0.13, "elbow_x": 0.26, "wrist_x": 0.35, "fingertip_x": 0.49,
            "hip_x": 0.085, "knee": (0.09, 0.0, 0.24), "ankle": (0.10, 0.03, 0.07),
            "ball": (0.10, -0.04, 0.02), "toe": (0.10, -0.12, 0.02),
        },
    },
}


class MissingSource(Exception):
    """The original of a model is not on this machine."""


def builder(name):
    """The build.py builder of a model: returns its armature."""
    return lambda: _build(name, MODELS[name])


def preview_dress(name):
    """For build.py --preview: shows the textures and one level of detail (the FBX itself has plain materials)."""

    def dress(arm):
        material = bpy.data.materials[name]
        nodes = material.node_tree.nodes
        texture = nodes.new("ShaderNodeTexImage")
        texture.image = bpy.data.images.load(_texture_path(name, "basecolor"))
        material.node_tree.links.new(texture.outputs["Color"], nodes["Principled BSDF"].inputs["Base Color"])
        nodes.active = texture
        bpy.context.scene.display.shading.color_type = "TEXTURE"
        for child in arm.children:
            child.hide_render = not child.name.endswith("_LOD0")

    return dress


def _build(name, model):
    path = os.path.join(SOURCE_DIR, *model["source"].split("/"))
    if not os.path.exists(path):
        raise MissingSource(path + " not found (set MADLANDS_TRIPO_DIR)")
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if len(meshes) != 1:
        raise ValueError("%s: expected one mesh, found %d" % (path, len(meshes)))
    source = meshes[0]
    _write_textures(name, _source_maps(path), model.get("hue_shift"))

    scale, floor = _normalize(source, model["height"])
    arm = lp.armature("Armature", _skeleton(model["joints"], scale, floor))
    for grip in ("MainHandGrip", "OffHandGrip"):
        arm.data.bones[grip].use_deform = False  # no skin weights flow to them

    material = lp.material(name, "#ffffff")  # ArtImporter finds the textures by this name
    levels = [_decimated(source, "%sBody_LOD%d" % (name, i), count, material) for i, count in enumerate(LOD_TRIANGLES)]
    bpy.data.objects.remove(source)

    _skin_automatically(levels[0], arm)
    for level in levels[1:]:
        _copy_weights(levels[0], level)
        lp.bind(level, arm)
    for level in levels:
        _tidy_weights(level)
    _report(name, levels[0], arm)
    _reset_pose(arm)
    return arm


def _source_maps(path):
    """The source's texture maps by kind, from the images its FBX refers to."""
    maps = {}
    for image in bpy.data.images:
        stem = image.name.lower().rsplit(".", 1)[0]
        for kind in ("basecolor", "normal", "roughness", "metallic"):
            if stem.endswith("_" + kind):
                maps[kind] = image
    missing = {"basecolor", "normal", "roughness", "metallic"} - set(maps)
    if missing:
        raise ValueError("%s: no %s texture found next to it" % (path, ", ".join(sorted(missing))))
    return maps


def _normalize(source, height):
    """Scales the mesh to its height in the game, feet at the ground. Returns the scale and the source's floor."""
    bpy.ops.object.select_all(action="DESELECT")
    source.select_set(True)
    bpy.context.view_layer.objects.active = source
    if source.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    source.data.transform(source.matrix_world)
    source.matrix_world = Matrix.Identity(4)
    heights = [v.co.z for v in source.data.vertices]
    floor = min(heights)
    scale = height / (max(heights) - floor)
    source.data.transform(Matrix.Scale(scale, 4) @ Matrix.Translation((0, 0, -floor)))
    source.data.shade_smooth()
    return scale, floor


def _skeleton(j, scale, floor):
    """Bone list for lp.armature: the humanoid bones of humanoid.skeleton, at this model's joints."""

    def at(x, y, z):
        return (x * scale, y * scale, (z - floor) * scale)

    def spine_point(y, z):
        return at(0, y, z)

    hips_y, hips_z = j["hips"]
    bones = [
        ("Hips", spine_point(hips_y, hips_z), spine_point(hips_y, j["spine"]), None),
        ("Spine", spine_point(hips_y, j["spine"]), spine_point(hips_y, j["chest"]), "Hips"),
        ("Chest", spine_point(hips_y, j["chest"]), spine_point(*j["neck"]), "Spine"),
        ("Neck", spine_point(*j["neck"]), spine_point(*j["head"]), "Chest"),
        ("Head", spine_point(*j["head"]), spine_point(*j["top"]), "Neck"),
    ]
    arm_y, arm_z = j["arm_y"], j["arm_z"]
    for side, s in (("Left", 1), ("Right", -1)):

        def arm_point(x):
            return at(s * x, arm_y, arm_z)

        def leg_point(point):
            return at(s * point[0], point[1], point[2])

        hip = (j["hip_x"], hips_y, hips_z)
        bones += [
            (side + "Shoulder", arm_point(j["clavicle_x"]), arm_point(j["shoulder_x"]), "Chest"),
            (side + "UpperArm", arm_point(j["shoulder_x"]), arm_point(j["elbow_x"]), side + "Shoulder"),
            (side + "LowerArm", arm_point(j["elbow_x"]), arm_point(j["wrist_x"]), side + "UpperArm"),
            (side + "Hand", arm_point(j["wrist_x"]), arm_point(j["fingertip_x"]), side + "LowerArm"),
            (side + "UpperLeg", leg_point(hip), leg_point(j["knee"]), "Hips"),
            (side + "LowerLeg", leg_point(j["knee"]), leg_point(j["ankle"]), side + "UpperLeg"),
            (side + "Foot", leg_point(j["ankle"]), leg_point(j["ball"]), side + "LowerLeg"),
            (side + "Toes", leg_point(j["ball"]), leg_point(j["toe"]), side + "Foot"),
        ]
    # Weapon anchors in the middle of each hand, pointing forward (humanoid.skeleton).
    grip_x = (j["wrist_x"] + j["fingertip_x"]) / 2
    forward = 0.1 / scale
    bones += [
        ("MainHandGrip", at(-grip_x, arm_y, arm_z), at(-grip_x, arm_y - forward, arm_z), "RightHand"),
        ("OffHandGrip", at(grip_x, arm_y, arm_z), at(grip_x, arm_y - forward, arm_z), "LeftHand"),
    ]
    return bones


def _decimated(source, name, triangles, material):
    """A copy of the source with about this many triangles."""
    obj = source.copy()
    obj.data = source.data.copy()
    obj.name = name
    obj.data.name = name
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.clear()
    obj.data.materials.append(material)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    obj.data.calc_loop_triangles()
    modifier = obj.modifiers.new("Decimate", "DECIMATE")
    modifier.ratio = min(1.0, triangles / len(obj.data.loop_triangles))
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def _skin_automatically(mesh, arm):
    """Blender's automatic weights: heat flowing out of the deforming bones through the surface."""
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")


def _copy_weights(source, target):
    """Gives a coarser level the weights of the finest one, whose surface it follows."""
    for group in source.vertex_groups:
        target.vertex_groups.new(name=group.name)
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    modifier = target.modifiers.new("Weights", "DATA_TRANSFER")
    modifier.object = source
    modifier.use_vert_data = True
    modifier.data_types_verts = {"VGROUP_WEIGHTS"}
    modifier.vert_mapping = "POLYINTERP_NEAREST"
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def _tidy_weights(mesh):
    """At most four bones per vertex, adding up to 1 (what Unity skins with)."""
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.vertex_group_clean(group_select_mode="ALL", limit=0.01)
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=MAX_INFLUENCES)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)


def _write_textures(name, maps, hue_shift):
    """The game's textures at TEXTURE_SIZE: base color, normal map, metallic (R) with smoothness (A) for URP Lit."""
    os.makedirs(TEXTURE_DIR, exist_ok=True)
    size = TEXTURE_SIZE
    base_color = _resized(maps["basecolor"], size)
    if hue_shift is not None:
        base_color = _shift_hues(base_color, *hue_shift)
    _write_png(_texture_path(name, "basecolor"), base_color, size)

    normal = _resized(maps["normal"], size)
    xyz = normal[..., :3] * 2.0 - 1.0
    xyz /= np.maximum(np.linalg.norm(xyz, axis=-1, keepdims=True), 1e-5)
    normal[..., :3] = xyz * 0.5 + 0.5
    normal[..., 3] = 1.0
    _write_png(_texture_path(name, "normal"), normal, size)

    packed = np.zeros((size, size, 4), dtype=np.float32)
    packed[..., 0] = _resized(maps["metallic"], size)[..., 0]
    packed[..., 3] = 1.0 - _resized(maps["roughness"], size)[..., 0]
    _write_png(_texture_path(name, "ms"), packed, size)


def _shift_hues(pixels, start, full, degrees):
    """Turns hues from `start` up by up to `degrees` (see MODELS), keeping each pixel's brightness and saturation."""
    r, g, b = pixels[..., 0], pixels[..., 1], pixels[..., 2]
    top = np.max(pixels[..., :3], axis=-1)
    chroma = top - np.min(pixels[..., :3], axis=-1)
    safe = np.maximum(chroma, 1e-6)
    sector = np.where(top == r, ((g - b) / safe) % 6, np.where(top == g, (b - r) / safe + 2, (r - g) / safe + 4))
    amount = np.clip((sector * 60 - start) / (full - start), 0, 1)
    sector = (sector + amount * amount * (3 - 2 * amount) * degrees / 60) % 6
    shifted = pixels.copy()
    for channel, offset in enumerate((5, 3, 1)):
        k = (offset + sector) % 6
        shifted[..., channel] = top - chroma * np.clip(np.minimum(k, 4 - k), 0, 1)
    return shifted


def _texture_path(name, kind):
    return os.path.join(TEXTURE_DIR, "%s_%s.png" % (name, kind))


def _resized(image, size):
    copy = image.copy()
    copy.scale(size, size)
    pixels = np.empty(size * size * 4, dtype=np.float32)
    copy.pixels.foreach_get(pixels)
    bpy.data.images.remove(copy)
    return pixels.reshape(size, size, 4)


def _write_png(path, pixels, size):
    image = bpy.data.images.new(os.path.basename(path), size, size, alpha=True)
    image.colorspace_settings.name = "Non-Color"  # the values go to the file as they are
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


# ---------------------------------------------------------------- checks


def _reset_pose(arm):
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def _turn(arm, bone, axis, degrees):
    """Turns a bone about a world axis through its joint."""
    pose_bone = arm.pose.bones[bone]
    head = pose_bone.matrix.to_translation()
    pose_bone.matrix = (Matrix.Translation(head) @ Matrix.Rotation(math.radians(degrees), 4, axis)
                        @ Matrix.Translation(-head) @ pose_bone.matrix)
    bpy.context.view_layer.update()


# Arms down and a long stride: what the shared clips ask of a model standing in a T-pose.
TEST_POSES = {
    "arms down": [("LeftUpperArm", "Y", 72), ("RightUpperArm", "Y", -72), ("LeftLowerArm", "X", -18), ("RightLowerArm", "X", -18)],
    "stride": [
        ("LeftUpperArm", "Y", 72), ("RightUpperArm", "Y", -72),
        ("LeftUpperLeg", "X", -32), ("LeftLowerLeg", "X", 12), ("RightUpperLeg", "X", 24), ("RightLowerLeg", "X", 48),
    ],
}


def _report(name, mesh, arm):
    """Logs the triangle counts and the skin's torn edges (bad weights), to check a new model from the build's output."""
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    for child in arm.children:
        child.data.calc_loop_triangles()
    print("%s: triangles %s" % (name, [len(c.data.loop_triangles) for c in sorted(arm.children, key=lambda c: c.name)]))
    unweighted = sum(1 for v in mesh.data.vertices if not v.groups)
    rest = [v.co.copy() for v in mesh.data.vertices]
    edges = [tuple(e.vertices) for e in mesh.data.edges]
    groups = {g.index: g.name for g in mesh.vertex_groups}
    print("%s: %d of %d vertices have no weights" % (name, unweighted, len(rest)))
    for pose, moves in TEST_POSES.items():
        _reset_pose(arm)
        for bone, axis, degrees in moves:
            _turn(arm, bone, axis, degrees)
        evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
        posed = evaluated.to_mesh()
        stretch = []
        for a, b in edges:
            before = (rest[a] - rest[b]).length
            after = (posed.vertices[a].co - posed.vertices[b].co).length
            if before > 1e-5:
                stretch.append((after / before, a, b))
        evaluated.to_mesh_clear()
        stretch.sort(reverse=True)
        torn = sum(1 for s in stretch if s[0] > TORN)
        print("%s: %s: %d of %d edges torn, worst %.1fx" % (name, pose, torn, len(edges), stretch[0][0]))
        for ratio, a, b in stretch[:8 if torn else 3]:
            middle = (rest[a] + rest[b]) / 2
            bones = sorted({groups[g.group] for v in (a, b) for g in mesh.data.vertices[v].groups if g.weight > 0.2})
            print("  %.1fx at (%.2f, %.2f, %.2f) %s" % (ratio, middle.x, middle.y, middle.z, ",".join(bones)))
    bpy.ops.object.mode_set(mode="OBJECT")
