"""
Humanoid clips from Quaternius's Universal Animation Library (CC0, CREDITS.md): motion-captured idling, running,
jumping, sword and fist attacks, spells, a hit and a death. The library's skeleton gets our bone names
(humanoid.py), so ArtImporter maps it like our own and Unity plays its clips on every humanoid, Tripo models
included. What the free library lacks or does not suit (walking at our 2.5 m/s, running backwards, strafing,
sitting on the ground) stays the scripted clips of HumanoidAnimations: EntityVisuals takes a clip from here when
there is one.

    blender -b --factory-startup -P art/blender/build.py -- HumanoidMotions

The library's FBX files (about 25 MB each) stay outside the repository, in MADLANDS_ANIMATION_DIR (default
D:\\3D_Stuff\\Animations\\Quaternius, unpacked from the Standard zips of quaternius.itch.io): only the chosen clips are
committed, in Art/Characters/HumanoidMotions.fbx. Without them a build skips the file and keeps the committed one.
"""

import os

import bpy
from mathutils import Matrix

import tripo

SOURCE_DIR = os.environ.get("MADLANDS_ANIMATION_DIR", r"D:\3D_Stuff\Animations\Quaternius")
SOURCES = {
    "UAL1": "UAL1/Universal Animation Library[Standard]/Unity/UAL1_Standard.fbx",
    "UAL2": "UAL2/Universal Animation Library 2[Standard]/Unity/UAL2_Standard.fbx",
}

# The library's bones under our names (humanoid.py, ArtImporter.HumanBones). Its middle spine bone and the fingers keep
# theirs: Unity folds a bone between two mapped ones into them, and our models' fingers are not animated.
BONES = {
    "pelvis": "Hips", "spine_01": "Spine", "spine_03": "Chest", "neck_01": "Neck", "Head": "Head",
    "clavicle_l": "LeftShoulder", "upperarm_l": "LeftUpperArm", "lowerarm_l": "LeftLowerArm", "hand_l": "LeftHand",
    "clavicle_r": "RightShoulder", "upperarm_r": "RightUpperArm", "lowerarm_r": "RightLowerArm", "hand_r": "RightHand",
    "thigh_l": "LeftUpperLeg", "calf_l": "LeftLowerLeg", "foot_l": "LeftFoot", "ball_l": "LeftToes",
    "thigh_r": "RightUpperLeg", "calf_r": "RightLowerLeg", "foot_r": "RightFoot", "ball_r": "RightToes",
}

# Our clip names (UnitAnimator) from the library's, played one after the other when there are several.
CLIPS = {
    "Idle": [("UAL1", "Idle_Loop")],
    "Run": [("UAL1", "Jog_Fwd_Loop")],
    "Jump": [("UAL1", "Jump_Start")],
    "Fall": [("UAL1", "Jump_Loop")],
    "Attack1H": [("UAL1", "Sword_Attack")],
    "Attack2H": [("UAL2", "Sword_Regular_A"), ("UAL2", "Sword_Regular_A_Rec")],
    "AttackUnarmed": [("UAL1", "Punch_Cross")],
    "CastLoop": [("UAL1", "Spell_Simple_Idle_Loop")],
    "CastRelease": [("UAL1", "Spell_Simple_Shoot")],
    "Hit": [("UAL1", "Hit_Chest")],
    "Death": [("UAL1", "Death01")],
}

# The library's Jog_Fwd_Loop moves its root 5.0 m in 28 frames at 30 fps: the speed at which our Run looks right
# (EntityVisuals.MotionRunSpeed). Its hips stand as high as Brightvale's (0.92 m), so the stride fits our humans.
RUN_SPEED = 5.36


def build():
    """The library's armature under our bone names with the chosen clips; returns the armature."""
    paths = {name: os.path.join(SOURCE_DIR, *path.split("/")) for name, path in SOURCES.items()}
    for path in paths.values():
        if not os.path.exists(path):
            raise tripo.MissingSource(path + " not found (set MADLANDS_ANIMATION_DIR)")

    samples = {}
    arm = None
    for source, path in paths.items():
        _clear()
        bpy.ops.import_scene.fbx(filepath=path)
        arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
        for clip, parts in CLIPS.items():
            for i, (part_source, action) in enumerate(parts):
                if part_source == source:
                    samples[(clip, i)] = _sample(arm, action)

    for obj in [o for o in bpy.data.objects if o is not arm]:
        bpy.data.objects.remove(obj)
    arm.animation_data.action = None
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    # The library's armature is turned to face +Y: unturned, it faces -Y like ours.
    arm.matrix_world = Matrix.Identity(4)
    arm.name = arm.data.name = "Armature"
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for old, new in BONES.items():
        arm.data.edit_bones[old].name = new
    # The library's root bone lies flat on the ground under the hips and never moves in clips without root motion. Unity
    # would take its turn for the avatar's: the hips are the top bone, as in our skeleton.
    bones = arm.data.edit_bones
    bones["Hips"].parent = None
    bones.remove(bones["root"])
    bpy.ops.object.mode_set(mode="OBJECT")
    ours = {new: old for old, new in BONES.items()}

    # Unity takes the first frame of the file's first clip as the pose its avatar is measured in: the rest pose (a
    # T-pose), as in the library's own files. Clips are written in order of their names, so this one comes first.
    _write(arm, "A_TPose", [{pb.name: Matrix.Identity(4) for pb in arm.pose.bones}] * 2, {})
    for clip, parts in CLIPS.items():
        frames = []
        for i in range(len(parts)):
            part = samples[(clip, i)]
            # The next part starts where the last one ended.
            frames += part[1:] if frames else part
        _write(arm, clip, frames, ours)
        print("%s: %s from %s, %d frames" % ("HumanoidMotions", clip, " + ".join(a for _, a in parts), len(frames)))
    return arm


def _clear():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)


def _sample(arm, name):
    """Every frame of one of the library's clips: each bone's pose relative to its rest, by the library's bone names."""
    action = next(a for a in bpy.data.actions if a.name.split("|")[-1] == name)
    arm.animation_data.action = action
    scene = bpy.context.scene
    start, end = (int(round(f)) for f in action.frame_range)
    frames = []
    for frame in range(start, end + 1):
        scene.frame_set(frame)
        frames.append({pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones})
    return frames


def _write(arm, name, frames, ours):
    """One action from sampled poses, keyed on every frame from 0 (quaternions kept on one side, so keys blend)."""
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    arm.animation_data.action = action
    previous = {}
    for frame, pose in enumerate(frames):
        for pb in arm.pose.bones:
            pb.rotation_mode = "QUATERNION"
            pb.matrix_basis = pose[ours.get(pb.name, pb.name)]
            q = pb.rotation_quaternion.copy()
            if pb.name in previous and previous[pb.name].dot(q) < 0:
                q.negate()
                pb.rotation_quaternion = q
            previous[pb.name] = q
            pb.keyframe_insert("location", frame=frame, group=pb.name)
            pb.keyframe_insert("rotation_quaternion", frame=frame, group=pb.name)
            pb.keyframe_insert("scale", frame=frame, group=pb.name)
    arm.animation_data.action = None
