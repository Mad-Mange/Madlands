"""
Characters from Tripo (ADR 0014): a textured FBX standing in a T-pose or an A-pose becomes a character like the scripted
races. It gets the humanoid skeleton, so it plays the shared humanoid clips and can hold weapons. An unrigged model gets
joints read by hand and automatic weights; a model rigged by Tripo ("rigged": True) brings its own Mixamo skeleton.

    MODELS row -> three levels of detail, skin weights, Art/Characters/<Name>.fbx and Art/Textures/<Name>_*.png

The originals (about 18 MB each with their 4K textures) stay outside the repository, in MADLANDS_TRIPO_DIR. Only
what the game uses is committed. Without the original, a build skips the model and keeps the committed files.
"""

import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

import lowpoly as lp

SOURCE_DIR = os.environ.get("MADLANDS_TRIPO_DIR", r"D:\3D_Stuff\Characters\MadLands")
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEXTURE_DIR = os.path.join(ROOT, "client", "Assets", "Madlands", "Art", "Textures")
TEXTURE_SIZE = 1024
LOD_TRIANGLES = (8000, 3000, 1000)
MAX_INFLUENCES = 4
# Where a rigged model's weapon sits: this far from the wrist to the fingers, and this far under the hand's bone (both in
# parts of the hand's bone), so its closed fingers go round it.
GRIP_ALONG = 0.8
GRIP_DROP = 0.3
# How far below the shoulder joint (the source file's meters) cloth starts to leave the shoulder bone, and has left it.
SHOULDER_HANGS = (0.08, 0.16)
# How far to each side of an elbow or wrist the bones' weights blend (_limit_arms), in the source file's meters.
JOINT_BLEND = 0.025
# An edge longer than this many times its rest length in a test pose counts as torn (see _report).
TORN = 2.5

# "source": the FBX under SOURCE_DIR. "height": meters from the feet to the top in the game. "rigged": it has Tripo's rig.
# "joints": where the joints are in the source file (its meters, read off front and side renders; left is +X, front -Y).
# A model in a T-pose gives its arm as one line (arm_y, arm_z and an x per joint), one in an A-pose as points ("arm": the
# left arm, mirrored for the right): its arms are raised to a T-pose in the build (_raise_arms), which the clips expect.
# "hue_shift" (optional): (start, full, degrees) turns the base color's hues from `start` up by up to `degrees`, in full
# from `full`, and leaves lower hues alone: it recolors one material of a painted model, like skin apart from leather.
# "arm_reach" (optional): how far (source meters) the upper arm, forearm and hand reach from their bones, sleeves and
# armor included, and "trunk_x" how far the trunk reaches to each side, which is never the arm's: _limit_arms. "skirt" (optional): cloth hanging over the legs, from "top" to "hem", apart from what lies
# within "legs" of the leg bones: _soften_skirt. Heights and distances in the source file's meters.
MODELS = {
    "MireCroaker": {
        "source": "FrogMadLands/FrogRiggedMixamo/tripo_convert_eaff7566-a1dd-4ad0-9915-10038c94ab53.fbx",
        "rigged": True,
        "height": 1.55,
        "hue_shift": (30, 38, 40),  # skin (hues 34-45) to moss green, the leather (27-32) stays brown
    },
    # The classes' looks: each class shows its own model, whatever its race (content/classes.json).
    "Warrior": {
        "source": "Warrior/WarriorRiggedMixamo/tripo_convert_5c44d1a4-334e-4561-829a-b4c7213cb5c1.fbx",
        "rigged": True,
        "height": 1.88,
        "arm_reach": (0.085, 0.075, 0.07),
        "trunk_x": 0.125,
        "skirt": {"top": 0.47, "hem": 0.22, "legs": 0.06},
    },
    "Shardmage": {
        "source": "Mage/MageRiggedMixamo/tripo_convert_5ec5fe62-8d42-494f-af30-dfbfb99fe395.fbx",
        "rigged": True,
        "height": 1.78,
    },
    "KorrinWarden": {
        "source": "KorrinWarden/KorrinWardenRiggedMixamo/tripo_convert_b8fefe12-8c8c-48a9-a183-3baf811f2db9.fbx",
        "rigged": True,
        "height": 1.55,
        "arm_reach": (0.10, 0.072, 0.075),
        "trunk_x": 0.165,
        "skirt": {"top": 0.40, "hem": 0.20, "legs": 0.07},
    },
}


# A model rigged by Tripo has a Mixamo skeleton: its bones under the names of ours. Mixamo's three spine bones become our
# Spine and Chest, and the bones it ends its chains with carry no weights. Fingers keep their own bones (LeftHandIndex1 ...).
MIXAMO_BONES = {
    "Hips": "Hips", "Spine": "Spine", "Spine1": "Chest", "Spine2": "Chest", "Neck": "Neck", "Head": "Head",
    "LeftShoulder": "LeftShoulder", "LeftArm": "LeftUpperArm", "LeftForeArm": "LeftLowerArm", "LeftHand": "LeftHand",
    "RightShoulder": "RightShoulder", "RightArm": "RightUpperArm", "RightForeArm": "RightLowerArm", "RightHand": "RightHand",
    "LeftUpLeg": "LeftUpperLeg", "LeftLeg": "LeftLowerLeg", "LeftFoot": "LeftFoot", "LeftToeBase": "LeftToes",
    "RightUpLeg": "RightUpperLeg", "RightLeg": "RightLowerLeg", "RightFoot": "RightFoot", "RightToeBase": "RightToes",
}
MIXAMO_DROPPED = ("HeadTop_End", "LeftToe_End", "RightToe_End")


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
    if model.get("rigged"):
        return _build_rigged(name, model, source)

    scale, floor = _normalize(source, model["height"])
    joints = model["joints"]
    arm = _rig(joints, scale, floor)

    material = lp.material(name, "#ffffff")  # ArtImporter finds the textures by this name
    levels = [_decimated(source, "%sBody_LOD%d" % (name, i), count, material) for i, count in enumerate(LOD_TRIANGLES)]
    bpy.data.objects.remove(source)

    _skin_automatically(levels[0], arm)
    _refine_weights(levels[0], model, scale, floor)
    for level in levels[1:]:
        _copy_weights(levels[0], level)
        lp.bind(level, arm)
    for level in levels:
        _tidy_weights(level)
    if "arm" in joints:
        if "arm_reach" in model:
            _free_arms(name, levels, joints, scale, floor)
        arm = _raise_arms(arm, levels, joints, scale, floor)
    _report(name, levels[0], arm)
    _reset_pose(arm)
    return arm


def _build_rigged(name, model, source):
    """A model rigged by Tripo: its own joints and weights, brought onto our bone names, in a T-pose, in levels of detail."""
    rig = source.parent
    if rig is None or rig.type != "ARMATURE":
        raise ValueError("%s: a rigged model needs an armature" % name)
    world = rig.matrix_world.copy()
    joints = {bone.name.replace("mixamorig:", ""): (world @ bone.head_local, world @ bone.tail_local) for bone in rig.data.bones}
    parents = {bone.name.replace("mixamorig:", ""): bone.parent.name.replace("mixamorig:", "") if bone.parent else None for bone in rig.data.bones}
    for group in list(source.vertex_groups):
        group.name = group.name.replace("mixamorig:", "")
    placed = source.matrix_world.copy()
    source.parent = None
    source.matrix_world = placed
    source.modifiers.clear()
    bpy.data.objects.remove(rig)

    scale, floor = _normalize(source, model["height"])

    def at(point):
        return (point.x * scale, point.y * scale, (point.z - floor) * scale)

    arm = lp.armature("Armature", _rigged_skeleton(joints, parents, at))
    arm.data.bones["MainHandGrip"].use_deform = False
    arm.data.bones["OffHandGrip"].use_deform = False
    _merge_groups(source)
    rig_joints = _joints_of_rig(joints)
    _refine_weights(source, dict(model, joints=rig_joints), scale, floor)

    material = lp.material(name, "#ffffff")  # ArtImporter finds the textures by this name
    levels = [_decimated(source, "%sBody_LOD%d" % (name, i), count, material) for i, count in enumerate(LOD_TRIANGLES)]
    bpy.data.objects.remove(source)
    for level in levels:
        _tidy_weights(level)
        lp.bind(level, arm)
    if "arm_reach" in model:
        _free_arms(name, levels, rig_joints, scale, floor)
    arm = _straighten_arms(arm, levels)
    _report(name, levels[0], arm)
    _reset_pose(arm)
    return arm


def _joints_of_rig(rig):
    """The source's joints in the form of a MODELS row, for the weight corrections."""

    def head(bone):
        return tuple(rig[bone][0])

    return {
        "hips": (rig["Hips"][0].y, rig["Hips"][0].z), "spine": rig["Spine"][0].z, "chest": rig["Spine1"][0].z,
        "neck": (rig["Neck"][0].y, rig["Neck"][0].z),
        "arm": {
            "clavicle": head("LeftShoulder"), "shoulder": head("LeftArm"), "elbow": head("LeftForeArm"),
            "wrist": head("LeftHand"), "fingertip": tuple(rig["LeftHandMiddle4"][1]),
        },
        "hip_x": rig["LeftUpLeg"][0].x, "knee": head("LeftLeg"), "ankle": head("LeftFoot"),
    }


def _our_name(bone):
    return MIXAMO_BONES.get(bone, bone)


def _rigged_skeleton(joints, parents, at):
    """Bone list for lp.armature from the source's joints: our names, the weapon grips and no bones that end a chain."""
    bones = []
    for bone, (head, tail) in joints.items():
        if bone in MIXAMO_DROPPED or bone.endswith("4") or bone == "Spine2":
            continue
        if bone == "Spine1":
            tail = joints["Spine2"][1]  # our Chest is both of Mixamo's upper spine bones
        parent = parents[bone]
        bones.append((_our_name(bone), at(head), at(tail), _our_name(parent) if parent else None))
    for grip, hand in (("MainHandGrip", "RightHand"), ("OffHandGrip", "LeftHand")):
        head, tail = joints[hand]
        middle = at((head + tail) / 2)
        bones.append((grip, middle, (middle[0], middle[1] - 0.1, middle[2]), hand))
    return bones


def _merge_groups(mesh):
    """Renames the source's vertex groups to our bones, adding up those that become one bone."""
    names = {group.index: group.name for group in mesh.vertex_groups}
    weights = []
    for vertex in mesh.data.vertices:
        w = {}
        for g in vertex.groups:
            bone = _our_name(names[g.group])
            w[bone] = w.get(bone, 0.0) + g.weight
        # The odd stray vertex without weights: the hips will do.
        weights.append(w if w else {"Hips": 1.0})
    mesh.vertex_groups.clear()
    _store_weights(mesh, weights)


def _straighten_arms(arm, levels):
    """
    Bakes the arms straight out along X, the T-pose the clips and the weapon grips expect, whatever pose the model was
    rigged in, and rebuilds the armature in that pose, so a new rest pose has the rolls of any other model.
    """
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    for side, s in (("Left", 1), ("Right", -1)):
        for bone in ("UpperArm", "LowerArm", "Hand"):
            _aim_along_x(arm, side + bone, s)
    bones = [(b.name, tuple(arm.pose.bones[b.name].head), tuple(arm.pose.bones[b.name].tail), b.parent.name if b.parent else None)
             for b in arm.data.bones]
    for i, (bone, _, _, parent) in enumerate(bones):
        if bone in ("MainHandGrip", "OffHandGrip"):
            wrist, knuckles = arm.pose.bones[parent].head, arm.pose.bones[parent].tail
            point = wrist.lerp(knuckles, GRIP_ALONG) - Vector((0, 0, GRIP_DROP * (knuckles - wrist).length))
            bones[i] = (bone, tuple(point), tuple(point - Vector((0, 0.1, 0))), parent)
    bpy.ops.object.mode_set(mode="OBJECT")
    for level in levels:
        bpy.ops.object.select_all(action="DESELECT")
        level.select_set(True)
        bpy.context.view_layer.objects.active = level
        bpy.ops.object.modifier_apply(modifier="Armature")
        level.parent = None
    bpy.data.objects.remove(arm)
    straight = lp.armature("Armature", bones)
    for grip in ("MainHandGrip", "OffHandGrip"):
        straight.data.bones[grip].use_deform = False
    for level in levels:
        lp.bind(level, straight)
    return straight


def _aim_along_x(arm, bone, s):
    """Turns a posed bone about its joint to point along +X (s = 1) or -X (s = -1)."""
    pose_bone = arm.pose.bones[bone]
    head = pose_bone.head.copy()
    direction = (pose_bone.tail - head).normalized()
    turn = direction.rotation_difference(Vector((s, 0, 0))).to_matrix().to_4x4()
    pose_bone.matrix = Matrix.Translation(head) @ turn @ Matrix.Translation(-head) @ pose_bone.matrix
    bpy.context.view_layer.update()


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


def _rig(joints, scale, floor):
    arm = lp.armature("Armature", _skeleton(joints, scale, floor))
    for grip in ("MainHandGrip", "OffHandGrip"):
        arm.data.bones[grip].use_deform = False  # no skin weights flow to them
    return arm


def _arm_points(j, s):
    """An arm's joints in the source file, by name, on the side `s` (1 left, -1 right)."""
    if "arm" in j:
        return {joint: (s * x, y, z) for joint, (x, y, z) in j["arm"].items()}
    return {joint: (s * j[joint + "_x"], j["arm_y"], j["arm_z"]) for joint in ("clavicle", "shoulder", "elbow", "wrist", "fingertip")}


def _free_arms(name, levels, joints, scale, floor):
    """
    In an A-pose a sleeve or a hand can have grown into the clothes beside it. Weights cannot part them: the faces between
    what follows an arm and what does not would stretch as soon as the arm moves. So those faces go, below the armpit:
    each side keeps its own surface, and the small gap lies where the two touched. Making a level of detail can mix an
    arm's weights with a leg's where the two lie close: the larger side keeps the vertex.
    """
    arm = joints["arm"]
    below = ((arm["shoulder"][2] + arm["elbow"][2]) / 2 - floor) * scale
    for level in levels:
        groups = {group.index: group.name for group in level.vertex_groups}
        for v in level.data.vertices:
            limbs = {g.group: g.weight for g in v.groups if _is_arm(groups[g.group]) or _is_leg(groups[g.group])}
            on_arm = sum(weight for group, weight in limbs.items() if _is_arm(groups[group]))
            on_leg = sum(limbs.values()) - on_arm
            if on_arm > 0.0 and on_leg > 0.0:
                for group in limbs:
                    if _is_arm(groups[group]) != (on_arm >= on_leg):
                        level.vertex_groups[group].remove([v.index])
        _tidy_weights(level)
        share = [sum(g.weight for g in v.groups if _is_arm(groups[g.group])) for v in level.data.vertices]
        mesh = bmesh.new()
        mesh.from_mesh(level.data)
        mesh.verts.index_update()
        bridges = [f for f in mesh.faces if f.calc_center_median().z < below
                   and max(share[v.index] for v in f.verts) > 0.7 and min(share[v.index] for v in f.verts) < 0.3]
        bmesh.ops.delete(mesh, geom=bridges, context="FACES")
        mesh.to_mesh(level.data)
        mesh.free()
        print("%s: %s: cut %d faces between the arms and the clothes" % (name, level.name, len(bridges)))


def _raise_arms(arm, levels, joints, scale, floor):
    """
    Bakes an A-pose into a T-pose: Unity's humanoid clips are measured from a T-pose, and the weapon grips assume one.
    The weights come from the A-pose (arms clear of the body weigh better); the same weights bring the arms back down
    in the game. Returns a new armature, built like one for a model made in a T-pose.
    """
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    for side, s in (("Left", 1), ("Right", -1)):
        for bone in ("UpperArm", "LowerArm", "Hand"):
            _aim_along_x(arm, side + bone, s)
    bpy.ops.object.mode_set(mode="OBJECT")
    for level in levels:
        bpy.ops.object.select_all(action="DESELECT")
        level.select_set(True)
        bpy.context.view_layer.objects.active = level
        bpy.ops.object.modifier_apply(modifier="Armature")
        level.parent = None
    bpy.data.objects.remove(arm)
    raised = _rig(_t_pose(joints), scale, floor)
    for level in levels:
        lp.bind(level, raised)
    return raised


def _t_pose(j):
    """The joints with the arm straight out from the shoulder, along +X, its bones as long as before."""
    arm = dict(j["arm"])
    x, y, z = arm["shoulder"]
    for joint, previous in (("elbow", "shoulder"), ("wrist", "elbow"), ("fingertip", "wrist")):
        x += (Vector(j["arm"][joint]) - Vector(j["arm"][previous])).length
        arm[joint] = (x, y, z)
    return dict(j, arm=arm)


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
    grips = {}
    for side, s in (("Left", 1), ("Right", -1)):
        arm = _arm_points(j, s)

        def arm_point(joint):
            return at(*arm[joint])

        def leg_point(point):
            return at(s * point[0], point[1], point[2])

        hip = (j["hip_x"], hips_y, hips_z)
        bones += [
            (side + "Shoulder", arm_point("clavicle"), arm_point("shoulder"), "Chest"),
            (side + "UpperArm", arm_point("shoulder"), arm_point("elbow"), side + "Shoulder"),
            (side + "LowerArm", arm_point("elbow"), arm_point("wrist"), side + "UpperArm"),
            (side + "Hand", arm_point("wrist"), arm_point("fingertip"), side + "LowerArm"),
            (side + "UpperLeg", leg_point(hip), leg_point(j["knee"]), "Hips"),
            (side + "LowerLeg", leg_point(j["knee"]), leg_point(j["ankle"]), side + "UpperLeg"),
            (side + "Foot", leg_point(j["ankle"]), leg_point(j["ball"]), side + "LowerLeg"),
            (side + "Toes", leg_point(j["ball"]), leg_point(j["toe"]), side + "Foot"),
        ]
        grips[side] = (Vector(arm["wrist"]) + Vector(arm["fingertip"])) / 2
    # Weapon anchors in the middle of each hand, pointing forward (humanoid.skeleton).
    forward = Vector((0, 0.1 / scale, 0))
    for grip, side in (("MainHandGrip", "Right"), ("OffHandGrip", "Left")):
        point = grips[side]
        bones.append((grip, at(*point), at(*(point - forward)), side + "Hand"))
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


def _refine_weights(mesh, model, scale, floor):
    """Corrects the automatic weights where a model asks for it (see MODELS)."""
    if "arm_reach" not in model and "skirt" not in model:
        return
    names = {group.index: group.name for group in mesh.vertex_groups}
    weights = [{names[g.group]: g.weight for g in v.groups} for v in mesh.data.vertices]
    points = np.array([v.co for v in mesh.data.vertices])
    joints = model["joints"]

    def at(point):
        return np.array((point[0] * scale, point[1] * scale, (point[2] - floor) * scale))

    if "arm_reach" in model:
        _limit_arms(points, weights, joints, model["arm_reach"], model["trunk_x"] * scale, scale, at, model.get("rigged", False))
    if "skirt" in model:
        _soften_skirt(points, weights, joints, model["skirt"], scale, floor, at)
    _store_weights(mesh, weights)


def _distance_to_segment(points, a, b):
    along = np.clip((points - a) @ (b - a) / ((b - a) @ (b - a)), 0.0, 1.0)
    return np.linalg.norm(points - (a + along[:, None] * (b - a)), axis=1)


def _limit_arms(points, weights, joints, reach, trunk_x, scale, at, keep_arm_weights):
    """
    Heat flows from a hand hanging at the hip into the belt and cloth beside it, which then swing with the arm: nothing
    beyond an arm's reach follows it. Heat also reaches the shell of a forearm and curled fingers from further up the arm,
    so bending the elbow would pull them out into long spikes: a hand and a forearm are weighted by where they lie along
    the arm. A rig's own weights for the arm's bones and fingers stay (`keep_arm_weights`), without anything else mixed in.
    A cloak hanging from a shoulder bone swings out on that long lever when the bone turns: further down it hangs from the
    chest. What is left without weights goes to the nearest bone.
    """
    bones = _body_segments(joints, at)
    for side, s in (("Left", 1), ("Right", -1)):
        arm = {joint: at(point) for joint, point in _arm_points(joints, s).items()}
        segments = {"UpperArm": ("shoulder", "elbow", reach[0]), "LowerArm": ("elbow", "wrist", reach[1]), "Hand": ("wrist", "fingertip", reach[2])}
        distance = {bone: _distance_to_segment(points, arm[a], arm[b]) / (r * scale) for bone, (a, b, r) in segments.items()}
        bones.update({side + bone: (arm[a], arm[b]) for bone, (a, b, _) in segments.items()})
        within = np.minimum(np.minimum(distance["UpperArm"], distance["LowerArm"]), distance["Hand"]) < 1.0
        own = (np.minimum(distance["LowerArm"], distance["Hand"]) < 1.0) & (np.abs(points[:, 0]) > trunk_x)
        along = (arm["wrist"] - arm["elbow"]) / np.linalg.norm(arm["wrist"] - arm["elbow"])
        blend = JOINT_BLEND * scale
        hand = _smooth((points - arm["wrist"]) @ along / blend)
        upper = 1.0 - _smooth((points - arm["elbow"]) @ along / blend)
        on_shoulder = np.clip((points[:, 2] - (arm["shoulder"][2] - SHOULDER_HANGS[1] * scale))
                              / ((SHOULDER_HANGS[1] - SHOULDER_HANGS[0]) * scale), 0.0, 1.0)
        for i, w in enumerate(weights):
            if own[i]:
                ours = {bone: weight for bone, weight in w.items() if bone.startswith(side) and _is_arm(bone)} if keep_arm_weights else None
                w.clear()
                if ours:
                    w.update(ours)
                    continue
                w.update({side + "UpperArm": upper[i], side + "LowerArm": 1.0 - hand[i] - upper[i], side + "Hand": hand[i]})
                for bone in [bone for bone, weight in w.items() if weight <= 0.0]:
                    del w[bone]
            elif not within[i]:
                for bone in [bone for bone in w if bone.startswith(side) and _is_arm(bone)]:
                    del w[bone]
                shoulder = w.get(side + "Shoulder", 0.0)
                if shoulder > 0.0 and on_shoulder[i] < 1.0:
                    w["Chest"] = w.get("Chest", 0.0) + shoulder * (1.0 - on_shoulder[i])
                    w[side + "Shoulder"] = shoulder * on_shoulder[i]
                    if on_shoulder[i] == 0.0:
                        del w[side + "Shoulder"]
    near = {bone: _distance_to_segment(points, a, b) for bone, (a, b) in bones.items()}
    for i, w in enumerate(weights):
        if any(_is_leg(bone) for bone in w):  # a belt pouch under a big shoulder: it follows the leg
            for bone in [bone for bone in w if _is_arm(bone)]:
                del w[bone]
        if not w:
            w[min(near, key=lambda bone: near[bone][i])] = 1.0


def _smooth(x):
    """0 below -1, 1 above 1, a smooth step between."""
    t = np.clip((x + 1.0) / 2.0, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def _body_segments(joints, at):
    """The bones of the trunk and the legs, as (head, tail) in the mesh's space."""
    hips_y, hips_z = joints["hips"]
    bones = {
        "Hips": (at((0, hips_y, hips_z)), at((0, hips_y, joints["spine"]))),
        "Spine": (at((0, hips_y, joints["spine"])), at((0, hips_y, joints["chest"]))),
        "Chest": (at((0, hips_y, joints["chest"])), at((0, joints["neck"][0], joints["neck"][1]))),
    }
    for side, s in (("Left", 1), ("Right", -1)):
        hip = at((s * joints["hip_x"], hips_y, hips_z))
        knee = at((s * joints["knee"][0], joints["knee"][1], joints["knee"][2]))
        ankle = at((s * joints["ankle"][0], joints["ankle"][1], joints["ankle"][2]))
        bones[side + "UpperLeg"] = (hip, knee)
        bones[side + "LowerLeg"] = (knee, ankle)
    return bones


def _soften_skirt(points, weights, joints, skirt, scale, floor, at):
    """
    Automatic weights tie cloth to the nearer leg, so a long stride tears it apart between the legs. A skirt hangs from
    the hips instead, and towards the hem follows the thighs by how far to the side it hangs: in the middle it stays
    between them. Hands hanging at the hip in an A-pose are not cloth: what follows an arm stays with it.
    """
    top = (skirt["top"] - floor) * scale
    hem = (skirt["hem"] - floor) * scale
    hips_y, hips_z = joints["hips"]
    width = joints["hip_x"] * scale * 1.5
    leg_distance = np.full(len(points), np.inf)
    for s in (1, -1):
        hip = at((s * joints["hip_x"], hips_y, hips_z))
        knee = at((s * joints["knee"][0], joints["knee"][1], joints["knee"][2]))
        ankle = at((s * joints["ankle"][0], joints["ankle"][1], joints["ankle"][2]))
        leg_distance = np.minimum(leg_distance, _distance_to_segment(points, knee, ankle))
        if not skirt.get("below_knee"):
            leg_distance = np.minimum(leg_distance, _distance_to_segment(points, hip, knee))
    # What lies on a leg stays on it; the cloth starts a little further out, without a seam that could tear.
    reach = skirt["legs"] * scale
    off_leg = np.clip((leg_distance - reach) / (0.5 * reach), 0.0, 1.0)
    for i, w in enumerate(weights):
        z = points[i][2]
        if off_leg[i] == 0.0 or not hem < z < top or any(_is_arm(bone) for bone in w):
            continue
        down = (top - z) / (top - hem)
        share = 0.7 * down
        left = np.clip((points[i][0] / width + 1) / 2, 0.0, 1.0)
        left = left * left * (3 - 2 * left)
        wanted = {"Hips": 1 - share, "LeftUpperLeg": share * left, "RightUpperLeg": share * (1 - left)}
        blend = min(1.0, down / 0.15) * off_leg[i]  # into the waist's own weights at the top
        total = sum(w.values()) or 1.0
        mixed = {bone: weight / total * (1 - blend) for bone, weight in w.items()}
        for bone, weight in wanted.items():
            mixed[bone] = mixed.get(bone, 0.0) + weight * blend
        w.clear()
        w.update(mixed)


def _is_arm(bone):
    """An arm's bones and the fingers of a rigged model."""
    return "Arm" in bone or "Hand" in bone


def _is_leg(bone):
    return bone.endswith(("UpperLeg", "LowerLeg", "Foot", "Toes"))


def _store_weights(mesh, weights):
    for name in {bone for w in weights for bone in w}:
        if name not in mesh.vertex_groups:
            mesh.vertex_groups.new(name=name)
    everyone = list(range(len(weights)))
    for group in mesh.vertex_groups:
        group.remove(everyone)
    for i, w in enumerate(weights):
        # Heat can leave a vertex with tiny weights, which _tidy_weights would clean away: they count in proportion.
        total = sum(w.values())
        for bone, weight in w.items():
            mesh.vertex_groups[bone].add([i], weight / total, "REPLACE")


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


# Arms down, a long stride, a swing forward, an arm overhead and bent elbows and wrists: what the shared clips ask of a
# model in a T-pose.
TEST_POSES = {
    "bent": [("RightLowerArm", "Z", 90), ("LeftLowerArm", "Z", -90), ("RightHand", "X", 50), ("LeftHand", "X", 50)],
    "reach": [("RightUpperArm", "Z", 90), ("LeftUpperArm", "Z", -90)],
    "overhead": [("RightUpperArm", "Y", 75), ("LeftUpperArm", "Y", -75)],
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
    tied = sum(1 for v in mesh.data.vertices if any(_is_arm(groups[g.group]) and g.weight > 0.02 for g in v.groups)
               and any(_is_leg(groups[g.group]) and g.weight > 0.02 for g in v.groups))
    print("%s: %d vertices follow both an arm and a leg" % (name, tied))
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
            bones = sorted({groups[g.group] for v in (a, b) for g in mesh.data.vertices[v].groups if g.weight > 0.05})
            print("  %.1fx at (%.2f, %.2f, %.2f) %s" % (ratio, middle.x, middle.y, middle.z, ",".join(bones)))
    bpy.ops.object.mode_set(mode="OBJECT")
