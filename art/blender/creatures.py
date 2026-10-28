"""
Creatures: generic rigs with their own clips (Idle, Walk, Run, Attack, Hit, Death).

Rotations are about the armature's rest axes like the humanoids (X = the model's left, -Y = forward,
Z = up). Material names can be recolored per display by the client (Fur, FurLight, Skin, Belly, ...).
"""

import math
import random

from mathutils import Vector

import beasts
import lowpoly as lp
import sculpt as sc

smooth = beasts.smooth


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
    """
    A lean grey wolf sculpted like the heroes: a deep chest and tucked waist, a shaggy ruff around the neck,
    a narrow muzzle with a black nose and amber eyes, tall ears, long legs that bend at the elbow and the
    hock, padded paws with claws, a bushy tail with a pale tip; shaded smoothly.
    """
    fur = lp.material("Fur", "#5f6268")
    light = lp.material("FurLight", "#9a9ca0")
    dark = lp.material("Dark", "#26262a")
    eyes = lp.material("EyesGlow", "#e8c84a", emission=0.6)
    rng = random.Random(3)
    mb = lp.MeshBuilder("WolfBody")

    # The trunk and neck, swept along the spine; a ruff of fur clumps blended on around the neck.
    spine = [(-0.72, 1.12), (-0.5, 0.95), (-0.3, 0.87), (-0.05, 0.83), (0.22, 0.8), (0.5, 0.81)]

    def spine_z(y):
        if y <= spine[0][0]:
            return spine[0][1]
        for (y0, z0), (y1, z1) in zip(spine, spine[1:]):
            if y <= y1:
                return z0 + (z1 - z0) * (y - y0) / (y1 - y0)
        return spine[-1][1]

    trunk = sc.union(
        sc.ellipsoid((0, -0.3, 0.83), (0.16, 0.25, 0.22)),
        sc.cone((0, -0.15, 0.84), (0, 0.25, 0.8), 0.17, 0.12),
        sc.ellipsoid((0, 0.3, 0.8), (0.14, 0.17, 0.15)),
        sc.ellipsoid((0, -0.36, 0.72), (0.12, 0.12, 0.13)),
        sc.cone((0, -0.4, 0.9), (0, -0.72, 1.13), 0.15, 0.09),
        k=0.06)
    trunk = sc.carve(trunk, (sc.ellipsoid((0, 0.08, 0.6), (0.3, 0.2, 0.12)), 0.06))
    clumps = []
    for i in range(14):
        a = math.pi * (-0.15 + 1.3 * (i % 7) / 6)
        y = -0.5 + (0.0 if i < 7 else 0.09) + rng.uniform(-0.02, 0.02)
        reach = 0.16 if i < 7 else 0.18
        clumps.append(sc.ellipsoid((math.cos(a) * reach, y, spine_z(y) + math.sin(a) * reach), (0.075, 0.085, 0.06)))
    trunk = sc.blend(trunk, *[(c, 0.015) for c in clumps])
    ys = [0.56 - (0.56 + 0.71) * i / 15 for i in range(16)]
    rows = sc.sweep(trunk, [Vector((0, y, spine_z(y))) for y in ys], 16, caps=(True, False), limit=0.8)
    weigh = beasts.along_y([("Neck", -0.6), ("Chest", -0.3), ("Spine", -0.05), ("Hips", 0.22)])
    sc.grid_mesh(mb, rows, fur, weigh, closed=True, inside=lambda c: Vector((0, c.y, spine_z(c.y))),
                 material_at=lambda c: light if c.z < spine_z(c.y) - 0.13 and abs(c.x) < 0.1 else None)

    # Tufts sticking out of the ruff.
    for i in range(12):
        a = math.pi * (-0.1 + 1.2 * i / 11)
        y = -0.47 + rng.uniform(-0.03, 0.03)
        origin = Vector((0, y, spine_z(y)))
        d = Vector((math.cos(a), 0, math.sin(a)))
        p = origin + d * (sc.Star(trunk, origin, limit=0.5).reach(origin, d) - 0.015)
        beasts.tufts(mb, [p], light if math.sin(a) < 0.2 else fur, "Chest", rng, size=0.1, sweep=(math.cos(a) * 0.6, 0.7, 0.2 + math.sin(a) * 0.4))

    # The head: skull, brow, cheek ruffs, a narrow muzzle and a black nose; ears, eyes and the jaw.
    head = sc.union(
        sc.ellipsoid((0, -0.72, 1.16), (0.11, 0.12, 0.1)),
        sc.cone((-0.05, -0.8, 1.21), (0.05, -0.8, 1.21), 0.035),
        sc.cone((0, -0.78, 1.13), (0, -0.99, 1.1), 0.068, 0.038),
        sc.ellipsoid((0.085, -0.7, 1.08), (0.06, 0.07, 0.06)),
        sc.ellipsoid((-0.085, -0.7, 1.08), (0.06, 0.07, 0.06)),
        sc.ellipsoid((0, -1.0, 1.115), (0.03, 0.025, 0.025)),
        k=0.03)

    def head_material(c):
        if c.y < -0.975 and c.z > 1.09:
            return dark
        return light if c.z < 1.09 or (c.y < -0.85 and c.z < 1.11) else None
    sc.star_mesh(mb, sc.Star(head, (0, -0.78, 1.14), limit=0.6), fur, "Head", 14, 22, material_at=head_material)
    for s in (1, -1):
        ear = [Vector(p) for p in ((s * 0.045, -0.67, 1.2), (s * 0.11, -0.69, 1.19), (s * 0.08, -0.64, 1.18), (s * 0.085, -0.7, 1.36))]
        mb.hull(ear, fur, "Head")
        middle = sum(ear, Vector()) / 4
        mb.hull([middle.lerp(p, 0.7) + Vector((0, -0.014, 0)) for p in (ear[0], ear[1], ear[3])] + [middle], dark, "Head")
        mb.sphere((s * 0.06, -0.83, 1.165), (0.022, 0.012, 0.014), eyes, "Head", segments=6, rings=4, smooth=True)
    jaw_from, jaw_to = Vector((0, -0.76, 1.05)), Vector((0, -0.96, 1.065))
    sc.sweep_mesh(mb, sc.chain([tuple(jaw_from), tuple(jaw_to)], [0.055, 0.03]), [jaw_from, jaw_from.lerp(jaw_to, 0.5), jaw_to], 8, light, "Jaw")

    # Legs: a muscled shoulder or thigh, a lean shank bending the other way at the hock, padded paws.
    for leg, (top, mid, low, tip, _) in WOLF_LEGS.items():
        back = leg == "Back"
        for side, x in (("L", 0.14), ("R", -0.14)):
            upper, lower, paw_bone = leg + "UpperLeg." + side, leg + "LowerLeg." + side, leg + "Paw." + side
            a = Vector((x * 0.8, top[0], top[1] + (0.0 if back else 0.06)))
            b = Vector((x, mid[0], mid[1]))
            c = Vector((x, low[0], low[1] + 0.03))
            muscle = sc.ellipsoid(tuple(a.lerp(b, 0.35)), (0.075, 0.12 if back else 0.09, (a - b).length * 0.5))
            shape = sc.blend(sc.chain([tuple(a), tuple(b)], [0.085 if back else 0.075, 0.045]), (muscle, 0.04),
                             (sc.chain([tuple(b), tuple(b.lerp(c, 0.5)), tuple(c)], [0.045, 0.036, 0.032]), 0.03))
            path = [a.lerp(b, f) for f in (0.0, 0.33, 0.66)] + [b.lerp(c, f) for f in (0.0, 0.33, 0.66, 1.0)]
            leg_rows = sc.sweep(shape, path, 10, caps=(True, True), up=(0, -1, 0), limit=0.5)

            def leg_weigh(p, upper=upper, lower=lower, b=b, c=c):
                # Handed over at the elbow or knee, by how far down the lower part a point lies.
                t = smooth(-0.03, 0.03, (p - b).dot((c - b).normalized()))
                return {upper: 1.0 - t, lower: t}
            sc.grid_mesh(mb, leg_rows, fur, leg_weigh, closed=True, inside=lambda q, path=path: min(path, key=lambda r: (r - q).length))
            # A padded paw on the ground with four toes and claws.
            px, py = x, low[0] - 0.04
            # The foot rises from the pad to the wrist or hock, where the lower leg ends.
            paw = sc.union(sc.ellipsoid((px, py, 0.035), (0.045, 0.06, 0.04)),
                           *[sc.sphere((px + k * 0.026, py - 0.055, 0.025), 0.022) for k in (-1.5, -0.5, 0.5, 1.5)], k=0.015)
            paw = sc.intersect(paw, sc.Shape(lambda q: -q[:, 2]))
            sc.star_mesh(mb, sc.Star(paw, (px, py - 0.01, 0.03), limit=0.3), light if not back else fur, paw_bone, 6, 10)
            foot = [c + Vector((0, 0, 0.01)), Vector((px, py + 0.01, 0.05))]
            mb.tube([(foot[0], 0.034), (foot[0].lerp(foot[1], 0.5), 0.033), (foot[1], 0.036)], fur, paw_bone, sides=8, smooth=True)
            for k in (-1.5, -0.5, 0.5, 1.5):
                base = Vector((px + k * 0.026, py - 0.07, 0.03))
                mb.tube([(base, 0.008), (base + Vector((0, -0.025, -0.02)), 0.002)], dark, paw_bone, sides=4)

    # A bushy tail: thick through the middle, a pale tip.
    tail = sc.chain([(0, 0.45, 0.84), (0, 0.6, 0.79), (0, 0.74, 0.7), (0, 0.86, 0.6), (0, 0.96, 0.46)], [0.045, 0.075, 0.085, 0.07, 0.02])
    tail_path = [Vector(p) for p in ((0, 0.45, 0.84), (0, 0.6, 0.79), (0, 0.74, 0.7), (0, 0.86, 0.6), (0, 0.95, 0.47))]

    def tail_weigh(p):
        t = smooth(0.68, 0.76, p.y)
        return {"Tail1": 1.0 - t, "Tail2": t}
    sc.grid_mesh(mb, sc.sweep(tail, tail_path, 9, caps=(True, True), limit=0.4), fur, tail_weigh, closed=True,
                 inside=lambda q: min(tail_path, key=lambda r: (r - q).length), material_at=lambda c: light if c.y > 0.86 else None)
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
    paint = lp.material("Paint", "#9a2e26")
    rng = random.Random(6)
    mb = lp.MeshBuilder("DummyBody")
    # A cross foot of beams with braces, the post, the crossbar for arms.
    for yaw in (0, 90):
        mb.box((0, 0, 0.07), (1.0 if yaw == 0 else 0.14, 0.14 if yaw == 0 else 1.0, 0.14), dark, "Base")
    for k in range(4):
        ang = math.pi / 2 * k
        mb.tube([((math.cos(ang) * 0.38, math.sin(ang) * 0.38, 0.12), 0.035), ((math.cos(ang) * 0.06, math.sin(ang) * 0.06, 0.55), 0.035)], dark, "Post", sides=4)
    mb.tube([((0, 0, 0.08), 0.085), ((0, 0, 1.95), 0.07)], wood, "Post", sides=7)
    mb.tube([((-0.68, 0, 1.45), 0.05), ((0.68, 0, 1.45), 0.05)], wood, "Post", sides=6)
    # A body of burlap stuffed with straw, bound with rope, a painted target on the chest.
    rings = []
    for z, r in ((0.92, 0.2), (1.0, 0.27), (1.2, 0.3), (1.42, 0.31), (1.58, 0.26), (1.64, 0.14)):
        rings.append([(math.cos(t) * r * rng.uniform(0.95, 1.05), math.sin(t) * r * 0.85, z) for t in (2 * math.pi * k / 10 for k in range(10))])
    mb.loft(rings, sack, "Post")
    for z in (1.08, 1.5):
        mb.frustum((0, 0, z - 0.03), (0, 0, z + 0.03), 0.31, 0.31, rope, "Post", sides=10)
    for r, mat, dy in ((0.15, paint, 0.0), (0.1, sack, 0.004), (0.05, paint, 0.008)):
        mb.frustum((0, -0.255 - dy, 1.3), (0, -0.265 - dy, 1.3), r, r, mat, "Post", sides=10)
    for _ in range(14):
        z = rng.uniform(0.9, 1.0)
        t = rng.uniform(0, 2 * math.pi)
        base = (math.cos(t) * 0.2, math.sin(t) * 0.17, z)
        mb.tube([(base, 0.012), ((base[0] * 1.5, base[1] * 1.5, z - rng.uniform(0.08, 0.16)), 0.004)], straw, "Post", sides=3)
    # Straw sleeves on the crossbar, tied at the ends.
    for sx in (1, -1):
        mb.tube([((sx * 0.25, 0, 1.45), 0.1), ((sx * 0.5, 0, 1.45), 0.09), ((sx * 0.62, 0, 1.45), 0.07)], sack, "Post", sides=7)
        mb.frustum((sx * 0.6, 0, 1.45), (sx * 0.63, 0, 1.45), 0.075, 0.075, rope, "Post", sides=7)
        for k in range(5):
            t = 2 * math.pi * k / 5
            mb.tube([((sx * 0.64, math.cos(t) * 0.04, 1.45 + math.sin(t) * 0.04), 0.012), ((sx * 0.75, math.cos(t) * 0.07, 1.45 + math.sin(t) * 0.06), 0.004)],
                    straw, "Post", sides=3)
    # A sack head with a painted face, tied at the neck, straw poking out on top.
    mb.sphere((0, 0, 1.86), (0.19, 0.18, 0.21), sack, "Post", segments=9, rings=6)
    mb.frustum((0, 0, 1.68), (0, 0, 1.72), 0.1, 0.1, rope, "Post", sides=8)
    for sx in (1, -1):
        mb.box((sx * 0.065, -0.17, 1.9), (0.045, 0.02, 0.03), dark, "Post", rotation=(0, sx * 45, 0))
        mb.box((sx * 0.065, -0.17, 1.9), (0.045, 0.02, 0.03), dark, "Post", rotation=(0, -sx * 45, 0))
    mb.box((0, -0.175, 1.79), (0.12, 0.02, 0.02), dark, "Post")
    for k in range(7):
        t = 2 * math.pi * k / 7
        mb.tube([((math.cos(t) * 0.05, math.sin(t) * 0.05, 2.04), 0.014), ((math.cos(t) * 0.12, math.sin(t) * 0.1, 2.14 + rng.uniform(0, 0.05)), 0.004)],
                straw, "Post", sides=3)
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
