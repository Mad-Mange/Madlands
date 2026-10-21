"""
Creatures: generic rigs with their own clips (Idle, Walk, Run, Attack, Hit, Death).

Rotations are about the armature's rest axes like the humanoids (X = the model's left, -Y = forward,
Z = up). Material names can be recolored per display by the client (Fur, FurLight, Skin, Belly, ...).
"""

import math
import random

import beasts
import lowpoly as lp


def cycle(t, phase=0.0):
    return math.sin(2 * math.pi * (t + phase))


# ---------------------------------------------------------------- wolf

WOLF_LEGS = {
    # leg: (side x, shoulder/hip, elbow/knee, wrist/hock, paw tip, parent)
    "Front": ((-0.37, 0.85), (-0.36, 0.46), (-0.37, 0.08), (-0.47, 0.03), "Chest"),
    "Back": ((0.36, 0.82), (0.27, 0.46), (0.4, 0.12), (0.32, 0.03), "Hips"),
}


def wolf_skeleton():
    bones = [
        ("Hips", (0, 0.4, 0.8), (0, 0.1, 0.82), None),
        ("Spine", (0, 0.1, 0.82), (0, -0.25, 0.86), "Hips"),
        ("Chest", (0, -0.25, 0.86), (0, -0.45, 0.92), "Spine"),
        ("Neck", (0, -0.45, 0.95), (0, -0.66, 1.12), "Chest"),
        ("Head", (0, -0.66, 1.12), (0, -0.98, 1.1), "Neck"),
        ("Jaw", (0, -0.74, 1.03), (0, -0.96, 1.0), "Head"),
        ("Tail1", (0, 0.45, 0.84), (0, 0.72, 0.72), "Hips"),
        ("Tail2", (0, 0.72, 0.72), (0, 0.92, 0.52), "Tail1"),
    ]
    for leg, (top, mid, low, tip, parent) in WOLF_LEGS.items():
        for side, x in (("L", 0.14), ("R", -0.14)):
            bones += [
                (leg + "UpperLeg." + side, (x, top[0], top[1]), (x, mid[0], mid[1]), parent),
                (leg + "LowerLeg." + side, (x, mid[0], mid[1]), (x, low[0], low[1]), leg + "UpperLeg." + side),
                (leg + "Paw." + side, (x, low[0], low[1]), (x, tip[0], tip[1]), leg + "LowerLeg." + side),
            ]
    return bones


def wolf_body():
    """A lean wolf: deep chest and tucked belly, a ruff of fur, long legs with paws, a bushy tail."""
    fur = lp.material("Fur", "#5f6268")
    light = lp.material("FurLight", "#9a9ca0")
    dark = lp.material("Dark", "#26262a")
    eyes = lp.material("EyesGlow", "#e8c84a", emission=0.6)
    rng = random.Random(3)
    mb = lp.MeshBuilder("WolfBody")
    sections = [(-0.5, 0.92, 0.12, 0.15), (-0.42, 0.89, 0.16, 0.2), (-0.28, 0.86, 0.17, 0.21), (-0.1, 0.83, 0.15, 0.18, 1.0, 0.95),
                (0.1, 0.81, 0.12, 0.13, 1.0, 0.75), (0.28, 0.8, 0.14, 0.16), (0.42, 0.8, 0.12, 0.14), (0.5, 0.81, 0.06, 0.07)]
    beasts.trunk(mb, sections, beasts.along_y([("Chest", -0.3), ("Spine", -0.05), ("Hips", 0.22)]), fur, light)
    beasts.trunk(mb, [(-0.7, 1.1, 0.09, 0.1), (-0.6, 1.03, 0.11, 0.13), (-0.48, 0.95, 0.13, 0.16)],
                 beasts.along_y([("Neck", -0.62), ("Chest", -0.47)]), fur, light, n=10, belly_cols=1)
    # A ruff of fur around the neck.
    for i in range(12):
        a = math.pi * (-0.1 + 1.2 * i / 11)
        p = (math.cos(a) * 0.15, -0.5 + rng.uniform(-0.03, 0.03), 0.95 + math.sin(a) * 0.16)
        beasts.tufts(mb, [p], light if math.sin(a) < 0.2 else fur, "Chest", rng, size=0.09, sweep=(math.cos(a) * 0.6, 0.7, 0.2 + math.sin(a) * 0.4))
    # The head: skull, snout, nose, ears, eyes and a jaw.
    mb.hull([(x * s, y, z) for s in (1, -1) for x, y, z in
             ((0.11, -0.62, 1.06), (0.12, -0.66, 1.24), (0.08, -0.82, 1.22), (0.1, -0.82, 1.06), (0.04, -1.0, 1.12), (0.05, -1.0, 1.05))],
            fur, "Head")
    mb.box((0, -0.99, 1.11), (0.06, 0.05, 0.05), dark, "Head")
    for s in (1, -1):
        mb.hull([(s * 0.05, -0.66, 1.22), (s * 0.12, -0.7, 1.22), (s * 0.08, -0.64, 1.36), (s * 0.08, -0.72, 1.22)], fur, "Head")
        mb.box((s * 0.075, -0.83, 1.17), (0.03, 0.02, 0.02), eyes, "Head")
        mb.hull([(s * 0.1, -0.66, 1.05), (s * 0.15, -0.64, 1.12), (s * 0.13, -0.6, 1.02), (s * 0.12, -0.7, 1.08)], light, "Head")
    mb.box((0, -0.86, 1.01), (0.08, 0.22, 0.04), light, "Jaw")
    # Legs: a thick thigh or shoulder, a lean shank, a paw with claws.
    for leg, (top, mid, low, tip, _) in WOLF_LEGS.items():
        back = leg == "Back"
        for side, x in (("L", 0.14), ("R", -0.14)):
            beasts.leg(mb, [(x, top[0], top[1] + 0.03), (x, mid[0], mid[1]), (x, low[0], low[1] + 0.02)],
                       (0.1 if back else 0.08, 0.05, 0.035), fur, (leg + "UpperLeg." + side, leg + "LowerLeg." + side))
            beasts.paw(mb, (x, low[0], low[1]), (x, tip[0], 0.0), 0.085, light if not back else fur, leg + "Paw." + side, claws=dark)
    mb.tube([((0, 0.45, 0.84), 0.05), ((0, 0.6, 0.79), 0.08), ((0, 0.72, 0.72), 0.085)], fur, "Tail1", sides=7)
    mb.tube([((0, 0.72, 0.72), 0.085), ((0, 0.84, 0.62), 0.075), ((0, 0.95, 0.48), 0.02)], light, "Tail2", sides=7)
    return mb.finish()


def wolf_gait(t, phases, swing, lift, flex, bob, head):
    pose = {}
    for leg, phase in phases.items():
        c = math.cos(2 * math.pi * (t + phase))
        up = max(0.0, math.sin(2 * math.pi * (t + phase - 0.5)))
        part, side = leg.split(".")
        pose[part + "UpperLeg." + side] = [("X", -swing * c - lift * 0.2 * up)]
        pose[part + "LowerLeg." + side] = [("X", lift * up)]
        pose[part + "Paw." + side] = [("X", lift * 0.6 * up - 10 * c)]
    pose["Spine"] = [("X", flex * cycle(t))]
    pose["Chest"] = [("X", -flex * 0.6 * cycle(t))]
    pose["Neck"] = [("X", head * cycle(t, 0.25))]
    pose["Tail1"] = [("X", -10 + 8 * cycle(t)), ("Z", 6 * cycle(t, 0.25))]
    pose["@root"] = (0, 0, -bob * abs(cycle(t)))
    return pose


def wolf_idle(t):
    b = cycle(t)
    return {
        "Spine": [("X", 1.0 * b)],
        "Neck": [("X", 2 * b), ("Z", 6 * math.sin(math.pi * t))],
        "Head": [("X", -2 * b)],
        "Tail1": [("X", -12), ("Z", 12 * cycle(t, 0.1))],
        "Tail2": [("Z", 10 * cycle(t, 0.3))],
        "@root": (0, 0, -0.008 * (1 + b)),
    }


def wolf_attack():
    crouch = {"Hips": [("X", 6)], "Neck": [("X", -10)], "Head": [("X", -8)],
              "FrontUpperLeg.L": [("X", 12)], "FrontUpperLeg.R": [("X", 12)], "@root": (0, 0.06, -0.08)}
    bite = {"Spine": [("X", -6)], "Neck": [("X", 18)], "Head": [("X", 12)], "Jaw": [("X", 35)],
            "FrontUpperLeg.L": [("X", -30)], "FrontUpperLeg.R": [("X", -25)], "FrontLowerLeg.L": [("X", 30)],
            "BackUpperLeg.L": [("X", 20)], "BackUpperLeg.R": [("X", 18)], "@root": (0, -0.25, 0.04)}
    snap = dict(bite)
    snap["Jaw"] = [("X", 0)]
    return [(0, {}), (5, crouch), (9, bite), (11, snap), (18, {})]


def wolf_hit():
    flinch = {"Neck": [("X", -16)], "Head": [("X", -8)], "Spine": [("X", -4)], "@root": (0, 0.05, 0)}
    return [(0, {}), (3, flinch), (9, {})]


def wolf_death():
    stagger = {"Neck": [("X", -20)], "Hips": [("Y", 25)], "@root": (0, 0, -0.15)}
    down = {"Hips": [("Y", 88)], "Neck": [("X", -25), ("Z", 10)], "Head": [("X", -10)], "Jaw": [("X", 15)],
            "FrontUpperLeg.L": [("X", -20)], "FrontUpperLeg.R": [("X", -30)], "BackUpperLeg.L": [("X", 25)],
            "BackUpperLeg.R": [("X", 15)], "Tail1": [("X", 20)], "@root": (0, 0, -0.58)}
    return [(0, {}), (8, stagger), (18, down), (30, down)]


def build_wolf():
    arm = lp.armature("Armature", wolf_skeleton())
    lp.bind(wolf_body(), arm)
    walk = {"Back.L": 0.0, "Front.L": 0.25, "Back.R": 0.5, "Front.R": 0.75}
    run = {"Front.L": 0.0, "Front.R": 0.12, "Back.L": 0.5, "Back.R": 0.62}
    lp.sample_action(arm, "Idle", 60, wolf_idle, loop=True)
    lp.sample_action(arm, "Walk", 30, lambda t: wolf_gait(t, walk, 22, 40, 2, 0.02, 4), loop=True)
    lp.sample_action(arm, "Run", 16, lambda t: wolf_gait(t, run, 38, 70, 9, 0.06, 6), loop=True)
    lp.keyframe_action(arm, "Attack", wolf_attack())
    lp.keyframe_action(arm, "Hit", wolf_hit())
    lp.keyframe_action(arm, "Death", wolf_death())
    return arm


# ---------------------------------------------------------------- frog

def frog_skeleton():
    bones = [
        ("Body", (0, 0.15, 0.3), (0, -0.12, 0.34), None),
        ("Head", (0, -0.12, 0.34), (0, -0.4, 0.38), "Body"),
        ("Jaw", (0, -0.14, 0.26), (0, -0.4, 0.26), "Head"),
    ]
    for side, s in (("L", 1), ("R", -1)):
        bones += [
            ("FrontLeg." + side, (s * 0.18, -0.12, 0.24), (s * 0.24, -0.2, 0.05), "Body"),
            ("BackThigh." + side, (s * 0.2, 0.12, 0.24), (s * 0.34, -0.04, 0.18), "Body"),
            ("BackShin." + side, (s * 0.34, -0.04, 0.18), (s * 0.31, 0.2, 0.08), "BackThigh." + side),
            ("BackFoot." + side, (s * 0.31, 0.2, 0.08), (s * 0.36, -0.02, 0.02), "BackShin." + side),
        ]
    return bones


def frog_body():
    """
    A fat swamp frog: a squat warty body with a pale throat and belly, a wide mouth, bulging eyes on top,
    muscled hind legs folded at its sides, and splayed webbed toes.
    """
    skin = lp.material("Skin", "#5b8a3a")
    belly = lp.material("Belly", "#cfc47e")
    spots = lp.material("Spots", "#3d6127")
    eyes = lp.material("Eyes", "#e6d36a")
    pupil = lp.material("Pupil", "#151515")
    rng = random.Random(8)
    mb = lp.MeshBuilder("FrogBody")
    # Body: a lumpy hull, heavier at the back, the belly a flat pale hull underneath.
    body = []
    for z, rx, ry, y0 in ((0.12, 0.24, 0.3, 0.04), (0.22, 0.32, 0.38, 0.03), (0.34, 0.3, 0.34, 0.04), (0.44, 0.2, 0.24, 0.06), (0.49, 0.1, 0.12, 0.08)):
        for k in range(10):
            t = 2 * math.pi * k / 10
            body.append((math.cos(t) * rx, y0 + math.sin(t) * ry * (1.12 if math.sin(t) > 0 else 1.0), z))
    mb.hull(body, skin, "Body")
    mb.hull([(math.cos(t) * 0.25, 0.04 + math.sin(t) * 0.31, z) for t in (2 * math.pi * k / 10 for k in range(10)) for z in (0.1, 0.16)],
            belly, "Body")
    # Warts and dark spots on the back.
    for _ in range(12):
        t = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.05, 0.24)
        x, y = math.cos(t) * r, 0.05 + math.sin(t) * r * 1.2
        z = 0.48 - (r / 0.3) ** 2 * 0.2
        mb.sphere((x, y, z), rng.uniform(0.025, 0.045), spots if rng.random() < 0.6 else skin, "Body", segments=5, rings=3)
    # Head: a wide flat skull with a broad mouth line, a pale throat on the jaw, bulging eyes.
    head = [(sx * x, y, z) for sx in (1, -1) for x, y, z in
            ((0.24, -0.1, 0.28), (0.25, -0.14, 0.4), (0.18, -0.34, 0.33), (0.1, -0.42, 0.3), (0.0, -0.44, 0.31), (0.12, -0.14, 0.46),
             (0.2, -0.3, 0.27), (0.08, -0.4, 0.27))]
    mb.hull(head, skin, "Head")
    mb.hull([(sx * x, y, z) for sx in (1, -1) for x, y, z in
             ((0.23, -0.12, 0.26), (0.19, -0.3, 0.26), (0.08, -0.41, 0.26), (0.0, -0.43, 0.26), (0.2, -0.12, 0.17), (0.14, -0.28, 0.17),
              (0.05, -0.36, 0.19))], belly, "Jaw")
    mouth = [(0.245, -0.12, 0.272), (0.2, -0.3, 0.272), (0.09, -0.415, 0.272), (0.0, -0.44, 0.272), (-0.09, -0.415, 0.272), (-0.2, -0.3, 0.272),
             (-0.245, -0.12, 0.272)]
    mb.tube([(pt, 0.008) for pt in mouth], pupil, "Head", sides=4)
    for sx in (1, -1):
        mb.sphere((sx * 0.14, -0.2, 0.47), 0.085, skin, "Head", segments=7, rings=4)
        mb.sphere((sx * 0.15, -0.22, 0.49), 0.07, eyes, "Head", segments=7, rings=5)
        mb.box((sx * 0.155, -0.29, 0.5), (0.04, 0.012, 0.022), pupil, "Head")
        mb.sphere((sx * 0.05, -0.43, 0.33), 0.012, pupil, "Head", segments=4, rings=3)
    # Legs.
    for side, sx in (("L", 1), ("R", -1)):
        mb.tube([((sx * 0.18, -0.12, 0.26), 0.055), ((sx * 0.22, -0.17, 0.14), 0.045), ((sx * 0.24, -0.2, 0.05), 0.035)], skin, "FrontLeg." + side, sides=6)
        for k in range(3):
            a = math.radians(-40 + 40 * k)
            tip = (sx * 0.24 + sx * math.sin(a) * 0.1, -0.2 - math.cos(a) * 0.1, 0.012)
            mb.tube([((sx * 0.24, -0.2, 0.03), 0.02), (tip, 0.012)], skin, "FrontLeg." + side, sides=4)
            mb.sphere(tip, 0.018, belly, "FrontLeg." + side, segments=4, rings=3)
        mb.tube([((sx * 0.18, 0.14, 0.26), 0.11), ((sx * 0.28, 0.04, 0.22), 0.1), ((sx * 0.34, -0.04, 0.18), 0.065)], skin, "BackThigh." + side, sides=7)
        mb.tube([((sx * 0.34, -0.04, 0.18), 0.06), ((sx * 0.33, 0.08, 0.13), 0.05), ((sx * 0.31, 0.2, 0.08), 0.035)], skin, "BackShin." + side, sides=6)
        # A long webbed foot with four toes.
        heel = (sx * 0.31, 0.2, 0.05)
        toes = []
        for k in range(4):
            a = math.radians(-30 + 20 * k)
            toes.append((sx * 0.33 + sx * math.sin(a) * 0.13, 0.0 - math.cos(a) * 0.08, 0.015))
        mb.add([heel] + toes, [(0, 1, 2), (0, 2, 3), (0, 3, 4)], skin, "BackFoot." + side, double_sided=True)
        for tip in toes:
            mb.sphere(tip, 0.02, belly, "BackFoot." + side, segments=4, rings=3)
    return mb.finish()


def frog_idle(t):
    b = cycle(t)
    return {"Jaw": [("X", 2 + 2 * b)], "Body": [("X", 1.5 * b)], "@root": (0, 0, 0.005 * b)}


def frog_hop(height):
    def pose_at(t):
        # 0..0.35 crouch, 0.35..0.75 in the air (legs stretched back), then landing.
        if t < 0.35:
            k = t / 0.35
            return {"Body": [("X", 8 * k)], "BackThigh.L": [("X", 10 * k)], "BackThigh.R": [("X", 10 * k)], "@root": (0, 0, -0.04 * k)}
        if t < 0.75:
            k = math.sin(math.pi * (t - 0.35) / 0.4)
            stretch = {"Body": [("X", -12 * k)], "@root": (0, 0, height * k)}
            for side in ("L", "R"):
                stretch["BackThigh." + side] = [("X", 55 * k)]
                stretch["BackShin." + side] = [("X", -40 * k)]
                stretch["FrontLeg." + side] = [("X", -30 * k)]
            return stretch
        k = 1 - (t - 0.75) / 0.25
        return {"Body": [("X", 6 * k)], "@root": (0, 0, -0.03 * k)}
    return pose_at


def frog_attack():
    lunge = {"Body": [("X", -18)], "Head": [("X", 10)], "Jaw": [("X", 40)], "@root": (0, -0.15, 0.08)}
    return [(0, {}), (5, lunge), (9, {"Body": [("X", -8)], "Jaw": [("X", 0)], "@root": (0, -0.08, 0.03)}), (15, {})]


def frog_death():
    flip = {"Body": [("Y", 175)], "@root": (0, 0, 0.12)}
    for side in ("L", "R"):
        flip["BackThigh." + side] = [("X", 30)]
        flip["FrontLeg." + side] = [("X", -40)]
    return [(0, {}), (6, {"Body": [("Y", 60)], "@root": (0, 0, 0.2)}), (14, flip), (30, flip)]


def build_frog():
    arm = lp.armature("Armature", frog_skeleton())
    lp.bind(frog_body(), arm)
    lp.sample_action(arm, "Idle", 45, frog_idle, loop=True)
    lp.sample_action(arm, "Walk", 24, frog_hop(0.12), loop=True)
    lp.sample_action(arm, "Run", 16, frog_hop(0.22), loop=True)
    lp.keyframe_action(arm, "Attack", frog_attack())
    lp.keyframe_action(arm, "Hit", [(0, {}), (3, {"Body": [("X", 12)], "@root": (0, 0.05, 0)}), (9, {})])
    lp.keyframe_action(arm, "Death", frog_death())
    return arm


# ---------------------------------------------------------------- training dummy

def build_dummy():
    arm = lp.armature("Armature", [
        ("Base", (0, 0, 0), (0, 0, 0.15), None),
        ("Post", (0, 0, 0.15), (0, 0, 2.0), "Base"),
    ])
    wood = lp.material("Wood", "#74502d")
    dark = lp.material("DarkWood", "#5a3d22")
    straw = lp.material("Straw", "#d2b56a")
    sack = lp.material("Sack", "#dccb8e")
    rope = lp.material("Rope", "#8a6f45")
    mb = lp.MeshBuilder("DummyBody")
    mb.frustum((0, 0, 0), (0, 0, 0.1), 0.45, 0.4, dark, "Base", sides=8)
    mb.frustum((0, 0, 0.08), (0, 0, 1.9), 0.08, 0.07, wood, "Post", sides=6)
    mb.box((0, 0, 1.45), (1.3, 0.1, 0.1), wood, "Post")
    mb.frustum((0, 0, 0.95), (0, 0, 1.6), 0.26, 0.3, straw, "Post", sides=8)
    mb.frustum((0, 0, 1.1), (0, 0, 1.16), 0.28, 0.28, rope, "Post", sides=8)
    mb.sphere((0, 0, 1.85), (0.2, 0.2, 0.22), sack, "Post", segments=8, rings=5)
    for s in (1, -1):
        mb.frustum((s * 0.62, 0, 1.45), (s * 0.7, 0, 1.45), 0.08, 0.1, straw, "Post", sides=6)
    return arm, mb.finish()


def build_training_dummy():
    arm, body = build_dummy()
    lp.bind(body, arm)
    lp.keyframe_action(arm, "Idle", [(0, {}), (30, {})], loop=True)
    lp.keyframe_action(arm, "Hit", [(0, {}), (3, {"Post": [("X", -12)]}), (8, {"Post": [("X", 7)]}),
                                    (13, {"Post": [("X", -3)]}), (18, {})])
    fallen = {"Post": [("X", -84)]}
    lp.keyframe_action(arm, "Death", [(0, {}), (6, {"Post": [("X", -20)]}), (16, fallen), (20, {"Post": [("X", -78)]}), (30, fallen)])
    return arm
