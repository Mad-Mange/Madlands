"""
Humanoid races and the shared humanoid animation set.

Every race uses the same bone names (Unity's humanoid names, so Unity maps the avatar by itself) in a
T-pose. Animations are made once on the Brightvale Human skeleton and exported on their own
(HumanoidAnimations.fbx); Unity retargets them to every race.

Material names are the gear regions the client tints: Skin, Hair, Eyes, Chest (torso and sleeves),
Arms (forearms), Hands, Waist, Legs and Feet.
"""

import math

import lowpoly as lp

# Joint heights and widths in meters. Z up, the model faces -Y, +X is its left.
RACES = {
    "BrightvaleHuman": dict(
        ankle=0.09, knee=0.5, hip=0.93, spine=1.06, chest=1.24, neck=1.47, head=1.57, top=1.84,
        leg_x=0.1, shoulder_z=1.43, clavicle_x=0.05, shoulder_x=0.19, elbow_x=0.46, wrist_x=0.7, hand=0.12,
        chest_w=0.4, chest_d=0.23, waist_w=0.32, hips_w=0.33, head_size=(0.22, 0.24, 0.28),
        upper_arm_r=0.06, forearm_r=0.05, thigh_r=0.085, shin_r=0.062, foot=(0.12, 0.25, 0.09),
        colors=dict(Skin="#e3b08a", Hair="#5b3920", Eyes="#1d1b22", Chest="#8c6e4b", Arms="#8c6e4b",
                    Hands="#e3b08a", Waist="#4b3424", Legs="#4e5868", Feet="#3e2c20"),
        beard=False,
    ),
    # Stocky mountain folk: short legs, broad shoulders, big hands and a beard.
    "Korrin": dict(
        ankle=0.08, knee=0.35, hip=0.66, spine=0.78, chest=0.95, neck=1.15, head=1.22, top=1.5,
        leg_x=0.13, shoulder_z=1.1, clavicle_x=0.06, shoulder_x=0.25, elbow_x=0.5, wrist_x=0.72, hand=0.14,
        chest_w=0.56, chest_d=0.32, waist_w=0.48, hips_w=0.44, head_size=(0.26, 0.27, 0.29),
        upper_arm_r=0.08, forearm_r=0.07, thigh_r=0.1, shin_r=0.08, foot=(0.15, 0.27, 0.1),
        colors=dict(Skin="#d49a78", Hair="#a4512a", Eyes="#1d1b22", Chest="#6f7a5a", Arms="#6f7a5a",
                    Hands="#d49a78", Waist="#4b3424", Legs="#5e4a3a", Feet="#3e2c20"),
        beard=True,
    ),
}

SIDES = (("Left", 1.0), ("Right", -1.0))


def skeleton(r):
    """Bone list for lp.armature: Unity humanoid names plus the two weapon grips."""
    bones = [
        ("Hips", (0, 0, r["hip"]), (0, 0, r["spine"]), None),
        ("Spine", (0, 0, r["spine"]), (0, 0, r["chest"]), "Hips"),
        ("Chest", (0, 0, r["chest"]), (0, 0, r["neck"]), "Spine"),
        ("Neck", (0, 0, r["neck"]), (0, 0, r["head"]), "Chest"),
        ("Head", (0, 0, r["head"]), (0, 0, r["top"]), "Neck"),
    ]
    for side, s in SIDES:
        z = r["shoulder_z"]
        bones += [
            (side + "Shoulder", (s * r["clavicle_x"], 0, z - 0.02), (s * r["shoulder_x"], 0, z), "Chest"),
            (side + "UpperArm", (s * r["shoulder_x"], 0, z), (s * r["elbow_x"], 0, z), side + "Shoulder"),
            (side + "LowerArm", (s * r["elbow_x"], 0, z), (s * r["wrist_x"], 0, z), side + "UpperArm"),
            (side + "Hand", (s * r["wrist_x"], 0, z), (s * (r["wrist_x"] + r["hand"]), 0, z), side + "LowerArm"),
            (side + "UpperLeg", (s * r["leg_x"], 0, r["hip"]), (s * r["leg_x"], 0, r["knee"]), "Hips"),
            (side + "LowerLeg", (s * r["leg_x"], 0, r["knee"]), (s * r["leg_x"], 0, r["ankle"]), side + "UpperLeg"),
            (side + "Foot", (s * r["leg_x"], 0, r["ankle"]), (s * r["leg_x"], -r["foot"][1] * 0.55, 0.02), side + "LowerLeg"),
            (side + "Toes", (s * r["leg_x"], -r["foot"][1] * 0.55, 0.02), (s * r["leg_x"], -r["foot"][1] * 0.8, 0.02), side + "Foot"),
        ]
    # Weapon anchors in the middle of each fist, pointing forward. Not part of the humanoid avatar.
    grip_x = r["wrist_x"] + r["hand"] * 0.5
    z = r["shoulder_z"]
    bones += [
        ("MainHandGrip", (-grip_x, -0.01, z), (-grip_x, -0.11, z), "RightHand"),
        ("OffHandGrip", (grip_x, -0.01, z), (grip_x, -0.11, z), "LeftHand"),
    ]
    return bones


def body(name, r):
    """The body mesh in T-pose, every part bound to one bone."""
    mats = {k: lp.material(k, v) for k, v in r["colors"].items()}
    mb = lp.MeshBuilder(name)

    # Pelvis and belt, belly, chest (wider at the shoulders).
    pelvis_h = r["spine"] - (r["hip"] - 0.1)
    mb.box((0, 0, r["hip"] - 0.1 + pelvis_h / 2), (r["hips_w"], r["chest_d"] * 0.9, pelvis_h), mats["Legs"], "Hips", top=(0.95, 1.0))
    mb.box((0, 0, r["spine"]), (r["waist_w"] + 0.03, r["chest_d"] * 0.95, 0.07), mats["Waist"], "Hips")
    belly_h = r["chest"] - r["spine"] + 0.04
    mb.box((0, 0, r["spine"] + belly_h / 2), (r["waist_w"], r["chest_d"] * 0.88, belly_h), mats["Chest"], "Spine", top=(1.08, 1.05))
    chest_h = r["neck"] - r["chest"]
    mb.box((0, -0.005, r["chest"] + chest_h / 2), (r["chest_w"] * 0.86, r["chest_d"], chest_h), mats["Chest"], "Chest", top=(1.16, 0.92))

    # Neck and head: a faceted block, nose, eyes and hair.
    mb.frustum((0, 0, r["neck"] - 0.03), (0, 0, r["head"] + 0.04), 0.06 * r["head_size"][0] / 0.22, 0.055 * r["head_size"][0] / 0.22, mats["Skin"], "Neck", sides=6)
    hx, hy, hz = r["head_size"]
    head_c = (0, -0.01, r["head"] + hz * 0.47)
    mb.sphere(head_c, (hx / 2, hy / 2, hz / 2), mats["Skin"], "Head", segments=8, rings=5)
    mb.box((0, head_c[1] - hy * 0.52, head_c[2] - hz * 0.04), (hx * 0.16, hy * 0.18, hz * 0.2), mats["Skin"], "Head", top=(0.6, 0.5))
    for s in (1, -1):
        mb.box((s * hx * 0.2, head_c[1] - hy * 0.47, head_c[2] + hz * 0.08), (hx * 0.14, 0.02, hz * 0.07), mats["Eyes"], "Head")
    # Hair: a cap over the top and the back of the head.
    mb.sphere((0, head_c[1] + hy * 0.07, head_c[2] + hz * 0.1), (hx * 0.55, hy * 0.53, hz * 0.47), mats["Hair"], "Head", segments=8, rings=5)
    if r["beard"]:
        mb.hull([(s * hx * 0.45, head_c[1] - hy * 0.3, head_c[2] - hz * 0.05) for s in (1, -1)]
                + [(s * hx * 0.35, head_c[1] - hy * 0.52, head_c[2] - hz * 0.25) for s in (1, -1)]
                + [(0, head_c[1] - hy * 0.55, head_c[2] - hz * 0.62), (0, head_c[1] - hy * 0.2, head_c[2] - hz * 0.5)],
                mats["Hair"], "Head")

    for side, s in SIDES:
        z = r["shoulder_z"]
        # Shoulder ball, sleeve (Chest), forearm (Arms), hand with a thumb.
        mb.sphere((s * r["shoulder_x"], 0, z - 0.01), r["upper_arm_r"] * 1.35, mats["Chest"], side + "UpperArm", segments=6, rings=4)
        mb.frustum((s * (r["shoulder_x"] - 0.02), 0, z), (s * (r["elbow_x"] + 0.02), 0, z), r["upper_arm_r"], r["upper_arm_r"] * 0.85, mats["Chest"], side + "UpperArm")
        mb.frustum((s * (r["elbow_x"] - 0.02), 0, z), (s * r["wrist_x"], 0, z), r["forearm_r"], r["forearm_r"] * 0.8, mats["Arms"], side + "LowerArm")
        hand_len = r["hand"]
        mb.box((s * (r["wrist_x"] + hand_len * 0.5), -0.005, z - 0.005), (hand_len, r["forearm_r"] * 1.7, r["forearm_r"] * 1.9), mats["Hands"], side + "Hand")
        mb.box((s * (r["wrist_x"] + hand_len * 0.3), -r["forearm_r"] * 1.0, z - 0.01), (hand_len * 0.45, r["forearm_r"] * 0.7, r["forearm_r"] * 0.8), mats["Hands"], side + "Hand")

        # Thigh and shin (Legs), boot shaft, foot and toe cap (Feet).
        x = s * r["leg_x"]
        mb.frustum((x, 0, r["hip"] + 0.03), (x, 0, r["knee"] - 0.02), r["thigh_r"], r["thigh_r"] * 0.78, mats["Legs"], side + "UpperLeg")
        mb.frustum((x, 0, r["knee"] + 0.02), (x, 0, r["ankle"] + 0.12), r["shin_r"], r["shin_r"] * 0.85, mats["Legs"], side + "LowerLeg")
        mb.frustum((x, 0, r["ankle"] + 0.16), (x, 0, r["ankle"] - 0.02), r["shin_r"] * 1.12, r["shin_r"] * 1.05, mats["Feet"], side + "LowerLeg")
        fw, fl, fh = r["foot"]
        mb.box((x, -fl * 0.22, fh * 0.5), (fw, fl * 0.62, fh), mats["Feet"], side + "Foot", top=(0.9, 0.8))
        mb.box((x, -fl * 0.66, fh * 0.32), (fw * 0.95, fl * 0.3, fh * 0.64), mats["Feet"], side + "Toes", top=(0.9, 0.7))
    return mb.finish()


def build_race(race):
    """Armature + body for one race (no animations)."""
    r = RACES[race]
    arm = lp.armature("Armature", skeleton(r))
    mesh = body(race + "Body", r)
    lp.bind(mesh, arm)
    return arm


# ---------------------------------------------------------------- animations

def side_pose(pose, bone, left_rotations):
    """Sets Left<bone> and the mirrored Right<bone>."""
    pose["Left" + bone] = list(left_rotations)
    pose["Right" + bone] = lp.mirror(left_rotations)


def stance(arm_down=74.0, elbow=14.0):
    """Relaxed standing: arms down, elbows a little bent, feet a little apart."""
    pose = {}
    side_pose(pose, "UpperArm", [("Y", arm_down)])
    side_pose(pose, "LowerArm", [("Z", -elbow)])
    side_pose(pose, "UpperLeg", [("Y", -3)])
    side_pose(pose, "Foot", [("Y", 3)])
    return pose


def arms(pose, left, right):
    """Arm rotations per side, both written as for the left arm ([(axis, deg)]); the right is mirrored."""
    pose["LeftUpperArm"] = left[0]
    pose["LeftLowerArm"] = left[1]
    pose["RightUpperArm"] = lp.mirror(right[0])
    pose["RightLowerArm"] = lp.mirror(right[1])


def legs(pose, side, thigh, knee, foot=0.0, spread=3.0):
    """Leg swing (negative = forward), knee bend (positive), foot pitch (negative = toes up)."""
    s = 1 if side == "Left" else -1
    pose[side + "UpperLeg"] = [("Y", -spread * s), ("X", thigh)]
    pose[side + "LowerLeg"] = [("X", knee)]
    pose[side + "Foot"] = [("X", foot)]


def cycle(t, phase=0.0):
    return math.sin(2 * math.pi * (t + phase))


def gait(t, swing, knee_swing, knee_stance, arm_swing, elbow, lean, bob, bounce_height):
    """A two-step cycle; at t = 0 the left foot lands in front."""
    pose = {}
    for side, phase in (("Left", 0.0), ("Right", 0.5)):
        c = math.cos(2 * math.pi * (t + phase))          # 1 = leg in front
        lift = max(0.0, math.sin(2 * math.pi * (t + phase - 0.5)))  # swing phase: leg moves forward
        thigh = -swing * c - knee_swing * 0.25 * lift
        knee = knee_stance + knee_swing * lift
        foot = -thigh * 0.35 - knee * 0.25 + (8 if c < -0.3 else 0)
        legs(pose, side, thigh, knee, foot)
        # The arm on this side swings against its leg.
        s = 1 if side == "Left" else -1
        pose[side + "UpperArm"] = [("Y", 70 * s), ("X", arm_swing * c)]
        pose[side + "LowerArm"] = [("Z", -elbow * s - (abs(c) * 6 * s))]
    pose["Spine"] = [("X", lean), ("Z", 4 * cycle(t))]
    pose["Chest"] = [("Z", -6 * cycle(t))]
    pose["Hips"] = [("Z", -5 * cycle(t))]
    pose["Neck"] = [("X", -lean * 0.6)]
    pose["@root"] = (0, 0, -bob + bounce_height * abs(math.sin(2 * math.pi * t)))
    return pose


def idle(t):
    b = cycle(t)
    pose = stance(74 - 1.5 * b, 14 + 2 * b)
    pose["Spine"] = [("X", 1.0 * b)]
    pose["Chest"] = [("X", 1.5 * b)]
    pose["Head"] = [("X", -1.2 * b), ("Z", 2 * math.sin(math.pi * t))]
    pose["@root"] = (0, 0, -0.006 * (1 + b))
    return pose


def walk(t):
    return gait(t, swing=26, knee_swing=40, knee_stance=6, arm_swing=22, elbow=14, lean=3, bob=0.04, bounce_height=0.03)


def run(t):
    return gait(t, swing=40, knee_swing=85, knee_stance=18, arm_swing=38, elbow=75, lean=11, bob=0.09, bounce_height=0.07)


def run_back(t):
    pose = gait(1.0 - t, swing=28, knee_swing=55, knee_stance=12, arm_swing=20, elbow=50, lean=-4, bob=0.06, bounce_height=0.04)
    return pose


def strafe(direction):
    """Runs sideways: the hips turn toward the movement, the chest keeps facing forward."""
    def pose_at(t):
        pose = gait(t, swing=30, knee_swing=70, knee_stance=14, arm_swing=18, elbow=60, lean=6, bob=0.07, bounce_height=0.05)
        turn = 55 * direction
        pose["Hips"] = [("Z", turn)]
        pose["Spine"] = [("X", 6), ("Z", -turn * 0.6)]
        pose["Chest"] = [("Z", -turn * 0.4)]
        return pose
    return pose_at


def fall(t):
    b = cycle(t)
    pose = {}
    arms(pose, ([("Y", 40 + 4 * b), ("X", -15)], [("Z", -30)]), ([("Y", 40 - 4 * b), ("X", -15)], [("Z", -30)]))
    legs(pose, "Left", -35, 60, -10)
    legs(pose, "Right", -10, 35, -5)
    pose["Spine"] = [("X", 4)]
    return pose


def jump_keys():
    crouch = stance(60, 30)
    legs(crouch, "Left", -30, 55, -12)
    legs(crouch, "Right", -30, 55, -12)
    crouch["Spine"] = [("X", 14)]
    crouch["@root"] = (0, 0, -0.16)
    push = stance(45, 20)
    legs(push, "Left", -5, 5, 20)
    legs(push, "Right", 0, 0, 20)
    push["@root"] = (0, 0, 0.02)
    return [(0, stance()), (3, crouch), (6, push), (10, fall(0))]


def with_stance(changes):
    pose = stance()
    pose.update(changes)
    return pose


def attack_one_hand():
    # Right arm: up and back over the shoulder, then a diagonal cut forward and down.
    windup = with_stance({
        "RightUpperArm": lp.mirror([("Y", 30), ("X", -150)]),
        "RightLowerArm": lp.mirror([("Z", -70)]),
        "RightHand": lp.mirror([("X", -30)]),
        "Chest": [("Z", -22), ("X", -4)],
        "Spine": [("Z", -8)],
    })
    strike = with_stance({
        "RightUpperArm": lp.mirror([("Y", 60), ("X", -55)]),
        "RightLowerArm": lp.mirror([("Z", -5)]),
        "RightHand": lp.mirror([("X", 35)]),
        "Chest": [("Z", 20), ("X", 10)],
        "Spine": [("Z", 8), ("X", 6)],
    })
    legs(strike, "Left", -18, 12, 5)
    return [(0, stance()), (6, windup), (8, windup), (11, strike), (14, strike), (22, stance())]


def attack_two_hand():
    # Both hands on the hilt: overhead, then straight down.
    windup = with_stance({
        "Chest": [("X", -12), ("Z", -10)],
        "Spine": [("X", -6)],
    })
    arms(windup, ([("Y", 40), ("X", -165)], [("Z", -60)]), ([("Y", 20), ("X", -160)], [("Z", -80)]))
    windup["RightHand"] = lp.mirror([("X", -40)])
    strike = with_stance({
        "Chest": [("X", 18), ("Z", 6)],
        "Spine": [("X", 10)],
    })
    arms(strike, ([("Y", 30), ("X", -60)], [("Z", -30)]), ([("Y", 15), ("X", -55)], [("Z", -20)]))
    strike["RightHand"] = lp.mirror([("X", 40)])
    legs(strike, "Left", -25, 20, 8)
    strike["@root"] = (0, 0, -0.05)
    return [(0, stance()), (8, windup), (10, windup), (14, strike), (18, strike), (27, stance())]


def attack_unarmed():
    guard = with_stance({"Chest": [("Z", -12)]})
    arms(guard, ([("Y", 55), ("X", -60)], [("Z", -100)]), ([("Y", 55), ("X", -55)], [("Z", -110)]))
    punch = with_stance({"Chest": [("Z", 18), ("X", 6)]})
    arms(punch, ([("Y", 55), ("X", -60)], [("Z", -100)]), ([("Y", 5), ("X", -85)], [("Z", -5)]))
    return [(0, stance()), (4, guard), (8, punch), (10, punch), (16, stance())]


def cast_loop(t):
    b = cycle(t)
    pose = with_stance({
        "Chest": [("X", -4 + b)],
        "Head": [("X", -4)],
    })
    arms(pose, ([("Y", 50 - 3 * b), ("X", -55)], [("Z", -55 - 4 * b)]), ([("Y", 50 + 3 * b), ("X", -55)], [("Z", -55 + 4 * b)]))
    pose["@root"] = (0, 0, -0.01 * (1 + b))
    return pose


def cast_release():
    thrust = with_stance({"Chest": [("X", 10)], "Spine": [("X", 4)]})
    arms(thrust, ([("Y", 25), ("X", -80)], [("Z", -5)]), ([("Y", 25), ("X", -80)], [("Z", -5)]))
    return [(0, cast_loop(0)), (4, thrust), (8, thrust), (16, stance())]


def hit():
    flinch = with_stance({"Chest": [("X", -12)], "Spine": [("X", -5)], "Head": [("X", -10)]})
    arms(flinch, ([("Y", 60), ("X", -15)], [("Z", -30)]), ([("Y", 60), ("X", -15)], [("Z", -30)]))
    return [(0, stance()), (3, flinch), (10, stance())]


def death():
    knees = with_stance({"Spine": [("X", -10)], "Head": [("X", -15)]})
    legs(knees, "Left", -20, 50, -5)
    legs(knees, "Right", -15, 45, -5)
    knees["@root"] = (0, 0, -0.25)
    down = {}
    down["Hips"] = [("X", -84)]
    # The legs turn with the hips: they end up lying along the ground in front.
    legs(down, "Left", 6, 10, 20, spread=8)
    legs(down, "Right", -2, 16, 15, spread=6)
    arms(down, ([("Y", 30), ("X", 20)], [("Z", -10)]), ([("Y", 55), ("X", 10)], [("Z", -20)]))
    down["Head"] = [("Z", 20), ("X", -5)]
    down["@root"] = (0, 0.12, -0.8)
    return [(0, stance()), (8, knees), (16, down), (18, down), (30, down)]


def sit(t):
    b = cycle(t)
    pose = {}
    legs(pose, "Left", -88, 92, 0)
    legs(pose, "Right", -86, 94, 0)
    pose["Spine"] = [("X", 4 + b)]
    arms(pose, ([("Y", 70), ("X", -30)], [("Z", -40)]), ([("Y", 70), ("X", -30)], [("Z", -40)]))
    pose["@root"] = (0, 0.05, -0.55)
    return pose


def build_animations():
    """The Brightvale skeleton with every humanoid clip (no mesh)."""
    arm = lp.armature("Armature", skeleton(RACES["BrightvaleHuman"]))
    lp.sample_action(arm, "Idle", 60, idle, loop=True)
    lp.sample_action(arm, "Walk", 30, walk, loop=True)
    lp.sample_action(arm, "Run", 20, run, loop=True)
    lp.sample_action(arm, "RunBack", 24, run_back, loop=True)
    lp.sample_action(arm, "StrafeLeft", 20, strafe(1), loop=True)
    lp.sample_action(arm, "StrafeRight", 20, strafe(-1), loop=True)
    lp.sample_action(arm, "Fall", 20, fall, loop=True)
    lp.keyframe_action(arm, "Jump", jump_keys())
    lp.keyframe_action(arm, "Attack1H", attack_one_hand())
    lp.keyframe_action(arm, "Attack2H", attack_two_hand())
    lp.keyframe_action(arm, "AttackUnarmed", attack_unarmed())
    lp.sample_action(arm, "CastLoop", 30, cast_loop, loop=True)
    lp.keyframe_action(arm, "CastRelease", cast_release())
    lp.keyframe_action(arm, "Hit", hit())
    lp.keyframe_action(arm, "Death", death())
    lp.sample_action(arm, "Sit", 60, sit, loop=True)
    return arm
