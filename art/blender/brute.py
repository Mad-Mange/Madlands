"""
Ruukhar the Slagborn, the boss of the Cinder Cut: a hunched brute in slag-iron plates cracked with molten
light, wearing the bleached skull of a great beast as a hood, a giant cleaver with a glowing edge in his
right hand and claws on his left. A generic rig with the creature clip names (Idle, Walk, Run, Attack,
CastLoop, CastRelease, Hit, Death) and the rotation conventions of creatures.py.
"""

import math
import random

import lowpoly as lp

COLORS = {
    "Slag": "#4a4146", "SlagLight": "#6a5d63", "Rust": "#6e3a2e", "Iron": "#2c2829", "Chain": "#5c5a60",
    "Ashen": "#9ca1aa", "AshenDark": "#6f747c", "Bone": "#ddd7c3", "BoneShadow": "#aaa290", "Fang": "#efe9d6",
    "Wrap": "#3a3230",
}


def mats():
    m = {name: lp.material(name, color) for name, color in COLORS.items()}
    m["MagmaGlow"] = lp.material("MagmaGlow", "#ff5a14", emission=4.0)
    m["EmberGlow"] = lp.material("EmberGlow", "#ffb030", emission=3.0)
    return m


def cycle(t, phase=0.0):
    return math.sin(2 * math.pi * (t + phase))


# Joints (meters; the model faces -Y, +Z up). A hunched brute: the shoulders ride higher than the head.
J = {
    "hips": (0, 0.2, 0.95), "spine": (0, 0.15, 1.25), "chest": (0, -0.02, 1.55), "chest_end": (0, -0.3, 1.85),
    "head": (0, -0.5, 1.66), "nose": (0, -0.72, 1.64),
}


def side(x, sx):
    return (x[0] * sx, x[1], x[2])


ARM = {"shoulder": (0.74, -0.08, 1.92), "elbow": (0.96, -0.28, 1.38), "wrist": (1.0, -0.5, 0.94), "tip": (1.02, -0.66, 0.76)}
LEG = {"hip": (0.36, 0.2, 0.95), "knee": (0.5, -0.14, 0.55), "ankle": (0.48, 0.1, 0.14), "toe": (0.5, -0.26, 0.05)}


def skeleton():
    bones = [
        ("Hips", J["hips"], J["spine"], None),
        ("Spine", J["spine"], J["chest"], "Hips"),
        ("Chest", J["chest"], J["chest_end"], "Spine"),
        ("Neck", (0, -0.3, 1.72), J["head"], "Chest"),
        ("Head", J["head"], J["nose"], "Neck"),
        ("Jaw", (0, -0.52, 1.58), (0, -0.72, 1.55), "Head"),
    ]
    for name, sx in (("L", 1), ("R", -1)):
        bones += [
            ("UpperArm." + name, side(ARM["shoulder"], sx), side(ARM["elbow"], sx), "Chest"),
            ("LowerArm." + name, side(ARM["elbow"], sx), side(ARM["wrist"], sx), "UpperArm." + name),
            ("Hand." + name, side(ARM["wrist"], sx), side(ARM["tip"], sx), "LowerArm." + name),
            ("Thigh." + name, side(LEG["hip"], sx), side(LEG["knee"], sx), "Hips"),
            ("Shin." + name, side(LEG["knee"], sx), side(LEG["ankle"], sx), "Thigh." + name),
            ("Foot." + name, side(LEG["ankle"], sx), side(LEG["toe"], sx), "Shin." + name),
        ]
    return bones


def plate(mb, center, size, mat, bone, rng, lumps=0.06, pts=18):
    """A lumpy, rounded armor plate or chunk: the hull of points spread over a squared-off ellipsoid."""
    cx, cy, cz = center
    sx, sy, sz = (s / 2 for s in size)
    points = []
    for i in range(pts):
        # Even-ish directions on a sphere, pushed toward the box corners so plates stay chunky.
        z = 1 - 2 * (i + 0.5) / pts
        r = math.sqrt(max(0.0, 1 - z * z))
        a = i * 2.39996 + rng.uniform(-0.2, 0.2)
        x, y = r * math.cos(a), r * math.sin(a)
        k = rng.uniform(1 - lumps, 1 + lumps)
        sq = 1.25
        points.append((cx + sx * max(-1.0, min(1.0, x * sq)) * k, cy + sy * max(-1.0, min(1.0, y * sq)) * k,
                       cz + sz * max(-1.0, min(1.0, z * sq)) * k))
    mb.hull(points, mat, bone)


def seam(mb, a, b, mat, bone, width=0.035):
    """A glowing crack between two points with a kink in the middle, standing a little proud of the armor."""
    mid = ((a[0] + b[0]) / 2 + 0.02, (a[1] + b[1]) / 2 - 0.01, (a[2] + b[2]) / 2 + 0.03)
    mb.frustum(a, mid, width, width * 0.8, mat, bone, sides=4)
    mb.frustum(mid, b, width * 0.8, width * 0.4, mat, bone, sides=4)


def spike(mb, base, tip, radius, mat, bone, sides=5):
    mb.frustum(base, tip, radius, 0.005, mat, bone, sides=sides)


def body(m):
    rng = random.Random(21)
    mb = lp.MeshBuilder("SlagbornBody")

    # Hips: a heavy belt with tassets hanging over the thighs, a dark loincloth in front.
    plate(mb, (0, 0.18, 1.0), (1.05, 0.75, 0.36), m["Slag"], "Hips", rng)
    for x, y, tilt in ((0.38, -0.16, 10), (-0.38, -0.16, -10), (0.58, 0.18, 28), (-0.58, 0.18, -28)):
        mb.box((x, y, 0.76), (0.38, 0.08, 0.44), m["Slag"], "Hips", top=(1.12, 1.0), rotation=(-14 if y < 0 else 0, tilt, 0))
        seam(mb, (x - 0.14, y - 0.07, 0.68), (x + 0.12, y - 0.07, 0.84), m["MagmaGlow"], "Hips", 0.02)
    mb.box((0, -0.2, 0.76), (0.32, 0.05, 0.52), m["Wrap"], "Hips", top=(1.2, 1.0))

    # Belly and chest: a barrel of plates leaning far forward, glowing cracks between them, a hump behind.
    plate(mb, (0, 0.08, 1.3), (1.1, 0.9, 0.5), m["Slag"], "Spine", rng)
    seam(mb, (-0.46, -0.36, 1.22), (0.44, -0.38, 1.28), m["MagmaGlow"], "Spine")
    plate(mb, (0, -0.12, 1.66), (1.5, 1.05, 0.62), m["Slag"], "Chest", rng, lumps=0.1)
    plate(mb, (0, 0.28, 1.92), (1.2, 0.8, 0.55), m["SlagLight"], "Chest", rng)
    seam(mb, (-0.55, -0.62, 1.55), (0.5, -0.62, 1.62), m["MagmaGlow"], "Chest")
    seam(mb, (0.32, -0.62, 1.44), (0.5, -0.58, 1.82), m["MagmaGlow"], "Chest", 0.025)
    seam(mb, (-0.3, -0.62, 1.42), (-0.48, -0.58, 1.8), m["MagmaGlow"], "Chest", 0.025)
    for x in (-0.5, -0.2, 0.2, 0.5):
        mb.sphere((x, -0.64, 1.5), 0.04, m["SlagLight"], "Chest", segments=5, rings=3)
    # A high collar of jagged plates around the low-slung face, chains hanging from it.
    for sx in (1, -1):
        mb.hull([(sx * 0.16, -0.6, 1.48), (sx * 0.46, -0.5, 1.5), (sx * 0.44, -0.42, 2.08), (sx * 0.22, -0.6, 1.94),
                 (sx * 0.32, -0.5, 2.18), (sx * 0.18, -0.52, 1.5)], m["Slag"], "Chest")
        seam(mb, (sx * 0.22, -0.6, 1.56), (sx * 0.28, -0.56, 1.92), m["EmberGlow"], "Chest", 0.018)
        for i in range(5):
            mb.sphere((sx * (0.5 + i * 0.02), -0.42 + i * 0.03, 1.74 - i * 0.07), 0.045, m["Chain"], "Chest", segments=5, rings=3)

    # Head: a small ashen face deep in the collar, pointed ears, ember eyes and a fanged underbite.
    hz = J["head"][2]
    mb.hull([(sx * 0.15, -0.46, hz + 0.09) for sx in (1, -1)] + [(sx * 0.16, -0.48, hz - 0.07) for sx in (1, -1)]
            + [(sx * 0.1, -0.7, hz + 0.05) for sx in (1, -1)] + [(sx * 0.09, -0.7, hz - 0.05) for sx in (1, -1)], m["Ashen"], "Head")
    for sx in (1, -1):
        mb.hull([(sx * 0.13, -0.48, hz + 0.05), (sx * 0.15, -0.52, hz + 0.02), (sx * 0.32, -0.44, hz + 0.14)], m["AshenDark"], "Head")
        mb.box((sx * 0.06, -0.7, hz + 0.03), (0.05, 0.02, 0.025), m["EmberGlow"], "Head")
        spike(mb, (sx * 0.06, -0.7, hz - 0.07), (sx * 0.07, -0.73, hz + 0.05), 0.024, m["Fang"], "Jaw", sides=4)
    mb.hull([(sx * 0.13, -0.48, hz - 0.07) for sx in (1, -1)] + [(sx * 0.1, -0.72, hz - 0.09) for sx in (1, -1)]
            + [(sx * 0.12, -0.5, hz - 0.13) for sx in (1, -1)] + [(sx * 0.08, -0.7, hz - 0.15) for sx in (1, -1)], m["AshenDark"], "Jaw")

    # The skull hood: the bleached skull of a great beast tied over head and shoulders, its upper jaw
    # framing the face, horn-like crests sweeping back.
    mb.hull([(sx * 0.46, -0.32, 1.86) for sx in (1, -1)] + [(sx * 0.38, 0.3, 2.06) for sx in (1, -1)]
            + [(sx * 0.32, -0.12, 2.4) for sx in (1, -1)] + [(sx * 0.22, 0.38, 2.34) for sx in (1, -1)]
            + [(0, -0.42, 2.28), (0, 0.5, 2.2)], m["Bone"], "Chest")
    mb.hull([(sx * 0.4, -0.34, 1.94) for sx in (1, -1)] + [(sx * 0.32, -0.84, 1.9) for sx in (1, -1)]
            + [(sx * 0.36, -0.34, 1.76) for sx in (1, -1)] + [(sx * 0.28, -0.82, 1.78) for sx in (1, -1)], m["Bone"], "Chest")
    for sx in (1, -1):
        mb.box((sx * 0.4, -0.28, 2.14), (0.08, 0.22, 0.15), m["Iron"], "Chest", rotation=(0, sx * -10, 0))   # eye sockets
        mb.box((sx * 0.36, -0.58, 1.92), (0.06, 0.12, 0.08), m["Iron"], "Chest")
        spike(mb, (sx * 0.24, 0.2, 2.3), (sx * 0.56, 0.85, 2.85), 0.12, m["Bone"], "Chest", sides=5)
        spike(mb, (sx * 0.32, -0.05, 2.32), (sx * 0.62, 0.1, 2.72), 0.08, m["BoneShadow"], "Chest", sides=4)
        for i in range(4):
            y = -0.8 + i * 0.12
            spike(mb, (sx * (0.27 - i * 0.01), y, 1.78), (sx * (0.27 - i * 0.01), y - 0.02, 1.6), 0.032, m["Fang"], "Chest", sides=4)
        mb.box((sx * 0.44, -0.12, 1.84), (0.05, 0.32, 0.06), m["Wrap"], "Chest", rotation=(0, 0, sx * 25))   # lashings
    for i in range(3):
        x = -0.11 + i * 0.11
        spike(mb, (x, -0.84, 1.78), (x, -0.86, 1.62), 0.03, m["Fang"], "Chest", sides=4)

    for name, sx in (("L", 1), ("R", -1)):
        sh, el, wr, tip = (side(ARM[k], sx) for k in ("shoulder", "elbow", "wrist", "tip"))
        # Pauldrons: great curved plates riding above the head, spikes on top, ember slits on the left one.
        plate(mb, (sx * 0.86, -0.06, 2.06), (0.78, 0.86, 0.52), m["Slag"], "UpperArm." + name, rng, lumps=0.12)
        plate(mb, (sx * 1.06, -0.1, 1.8), (0.34, 0.74, 0.5), m["SlagLight"], "UpperArm." + name, rng)
        spike(mb, (sx * 0.84, 0.0, 2.28), (sx * 0.98, 0.12, 2.7), 0.12, m["Slag"], "UpperArm." + name)
        spike(mb, (sx * 1.02, -0.22, 2.2), (sx * 1.24, -0.24, 2.42), 0.08, m["SlagLight"], "UpperArm." + name)
        if sx > 0:
            for i in range(3):
                mb.hull([(0.74 + i * 0.12, -0.5, 2.02), (0.79 + i * 0.12, -0.5, 2.02), (0.765 + i * 0.12, -0.52, 2.16)], m["EmberGlow"], "UpperArm.L")
        mb.frustum(sh, el, 0.2, 0.17, m["Ashen"], "UpperArm." + name, sides=6)
        # Forearms in vambraces, a claw on the left hand, a fist on the right.
        plate(mb, ((el[0] + wr[0]) / 2, (el[1] + wr[1]) / 2, (el[2] + wr[2]) / 2), (0.38, 0.4, 0.52), m["Slag"], "LowerArm." + name, rng)
        seam(mb, (el[0], el[1] - 0.19, el[2] - 0.08), (wr[0], wr[1] - 0.17, wr[2] + 0.1), m["MagmaGlow"], "LowerArm." + name, 0.022)
        mb.sphere((wr[0], wr[1] - 0.05, wr[2] - 0.08), (0.15, 0.14, 0.13), m["Ashen"], "Hand." + name, segments=6, rings=4)
        if name == "L":
            for i in range(4):
                a = math.radians(-35 + i * 23)
                base = (wr[0] + math.sin(a) * 0.09, wr[1] - 0.12, wr[2] - 0.12)
                mid = (base[0] + math.sin(a) * 0.05, base[1] - 0.1, base[2] - 0.12)
                mb.frustum(base, mid, 0.04, 0.034, m["Ashen"], "Hand.L", sides=4)
                spike(mb, mid, (mid[0], mid[1] - 0.06, mid[2] - 0.14), 0.032, m["Fang"], "Hand.L", sides=4)
        # Legs: armored thighs, knee cops with a spike, greaves, heavy sabatons.
        hp, kn, an, to = (side(LEG[k], sx) for k in ("hip", "knee", "ankle", "toe"))
        plate(mb, ((hp[0] + kn[0]) / 2, (hp[1] + kn[1]) / 2, (hp[2] + kn[2]) / 2 + 0.05), (0.46, 0.5, 0.52), m["Slag"], "Thigh." + name, rng)
        plate(mb, (kn[0], kn[1] - 0.12, kn[2]), (0.34, 0.24, 0.3), m["SlagLight"], "Shin." + name, rng)
        spike(mb, (kn[0], kn[1] - 0.22, kn[2]), (kn[0], kn[1] - 0.42, kn[2] + 0.05), 0.07, m["SlagLight"], "Shin." + name, sides=4)
        plate(mb, ((kn[0] + an[0]) / 2, (kn[1] + an[1]) / 2, (kn[2] + an[2]) / 2), (0.42, 0.46, 0.46), m["Slag"], "Shin." + name, rng)
        seam(mb, (kn[0] - sx * 0.08, kn[1] - 0.22, kn[2] - 0.1), (an[0], an[1] - 0.2, an[2] + 0.12), m["MagmaGlow"], "Shin." + name, 0.022)
        mb.box((an[0], (an[1] + to[1]) / 2 - 0.04, 0.1), (0.42, 0.62, 0.2), m["Slag"], "Foot." + name, top=(0.85, 0.75))
        for dx in (-0.12, 0.0, 0.12):
            spike(mb, (an[0] + dx, to[1] - 0.06, 0.07), (an[0] + dx * 1.2, to[1] - 0.2, 0.05), 0.045, m["Iron"], "Foot." + name, sides=4)

    cleaver(mb, m, side(ARM["wrist"], -1), "Hand.R")
    return mb.finish()


def cleaver(mb, m, grip, bone):
    """The great cleaver: a wrapped haft held forward, a slab of slag iron with a glowing toothed edge."""
    gx, gy, gz = grip
    gz -= 0.08
    front = gy - 1.0
    mb.frustum((gx, gy + 0.25, gz), (gx, front, gz), 0.05, 0.05, m["Wrap"], bone, sides=6)
    for i in range(5):
        y = gy + 0.2 - i * 0.22
        mb.frustum((gx, y, gz), (gx, y - 0.05, gz), 0.058, 0.058, m["Iron"], bone, sides=6)
    mb.frustum((gx, gy + 0.3, gz + 0.06), (gx, gy + 0.3, gz - 0.06), 0.08, 0.08, m["Iron"], bone, sides=6, cap_a=False, cap_b=False)
    # The blade hangs below the front of the haft.
    top, bottom = gz + 0.12, gz - 0.62
    y0, y1 = front + 0.12, front - 0.82
    mb.add([(gx - 0.035, y0, top), (gx - 0.035, y1, top + 0.06), (gx - 0.035, y1 - 0.08, bottom), (gx - 0.035, y0, bottom + 0.12),
            (gx + 0.035, y0, top), (gx + 0.035, y1, top + 0.06), (gx + 0.035, y1 - 0.08, bottom), (gx + 0.035, y0, bottom + 0.12)],
           [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)], m["Slag"], bone, closed=True)
    mb.box((gx, (y0 + y1) / 2, top - 0.03), (0.09, abs(y1 - y0) + 0.04, 0.07), m["Rust"], bone)
    # A notch near the spine and the glowing teeth along the edge.
    mb.box((gx, y1 + 0.25, top - 0.12), (0.08, 0.18, 0.14), m["Iron"], bone)
    for i in range(7):
        t = i / 6
        y = y0 - 0.05 + (y1 - 0.08 - y0 + 0.05) * t
        z = bottom + 0.12 * (1 - t) + 0.02
        mb.hull([(gx - 0.03, y - 0.06, z + 0.08), (gx + 0.03, y - 0.06, z + 0.08), (gx - 0.03, y + 0.06, z + 0.08), (gx + 0.03, y + 0.06, z + 0.08),
                 (gx, y, z - 0.1)], m["MagmaGlow"], bone)
    mb.box((gx, (y0 + y1) / 2, bottom + 0.14), (0.08, abs(y1 - y0), 0.1), m["EmberGlow"], bone, rotation=(-7, 0, 0))


# ---------------------------------------------------------------- animations

def gait(t, swing, lift, sway, bob, arms):
    c = cycle(t)
    up_l = max(0.0, cycle(t, 0.25))
    up_r = max(0.0, cycle(t, 0.75))
    return {
        "Thigh.L": [("X", -swing * c)], "Thigh.R": [("X", swing * c)],
        "Shin.L": [("X", lift * up_l)], "Shin.R": [("X", lift * up_r)],
        "Foot.L": [("X", -8 * c)], "Foot.R": [("X", 8 * c)],
        "Hips": [("Z", sway * c)], "Spine": [("Z", -sway * 0.6 * c), ("X", 3)], "Chest": [("Z", -sway * 0.4 * c)],
        "UpperArm.L": [("X", arms * c)], "UpperArm.R": [("X", -arms * 0.5 * c)],
        "Head": [("X", 3 * cycle(t, 0.5))],
        "@root": (0, 0, -bob * abs(c)),
    }


def idle(t):
    b = cycle(t)
    return {"Spine": [("X", 2 * b)], "Chest": [("X", -1.5 * b), ("Z", 2 * cycle(t, 0.3))], "Head": [("X", 3 * b), ("Z", 6 * cycle(t, 0.15))],
            "Jaw": [("X", 4 + 4 * b)], "UpperArm.L": [("X", 3 * b)], "UpperArm.R": [("X", 2 * b)], "@root": (0, 0, -0.012 * (1 + b))}


def attack():
    wind = {"UpperArm.R": [("X", -150), ("Y", -15)], "LowerArm.R": [("X", -35)], "Spine": [("X", -8)], "Chest": [("X", -8)],
            "Head": [("X", -8)], "UpperArm.L": [("X", 20), ("Y", -20)], "@root": (0, 0.1, 0)}
    chop = {"UpperArm.R": [("X", -15)], "LowerArm.R": [("X", -10)], "Spine": [("X", 18)], "Chest": [("X", 12)],
            "Head": [("X", 10)], "Jaw": [("X", 20)], "UpperArm.L": [("X", -10)], "@root": (0, -0.25, -0.08)}
    return [(0, {}), (10, wind), (14, chop), (17, chop), (28, {})]


def cast_loop(t):
    shake = 2 * math.sin(2 * math.pi * t * 6)
    return {"UpperArm.R": [("X", -155 + shake), ("Y", -10)], "LowerArm.R": [("X", -25)], "UpperArm.L": [("X", -100), ("Y", -35)],
            "LowerArm.L": [("X", -20)], "Spine": [("X", -10 + shake * 0.5)], "Chest": [("X", -8)], "Head": [("X", -20)], "Jaw": [("X", 30)],
            "@root": (0, 0.08, 0.02)}


def cast_release():
    slam = {"UpperArm.R": [("X", 5)], "LowerArm.R": [("X", -5)], "UpperArm.L": [("X", -30), ("Y", -25)], "Spine": [("X", 30)],
            "Chest": [("X", 15)], "Head": [("X", 10)], "Jaw": [("X", 25)], "Thigh.L": [("X", -25)], "Thigh.R": [("X", -25)],
            "Shin.L": [("X", 40)], "Shin.R": [("X", 40)], "@root": (0, -0.2, -0.22)}
    return [(0, cast_loop(0)), (5, slam), (11, slam), (20, {})]


def death():
    stagger = {"Spine": [("X", -14)], "Chest": [("X", -10)], "Head": [("X", -25)], "Jaw": [("X", 30)], "UpperArm.R": [("X", 30)],
               "UpperArm.L": [("X", 25), ("Y", -30)], "@root": (0, 0.2, 0)}
    knees = {"Thigh.L": [("X", -80)], "Thigh.R": [("X", -80)], "Shin.L": [("X", 100)], "Shin.R": [("X", 100)], "Spine": [("X", 10)],
             "Head": [("X", 15)], "UpperArm.R": [("X", 10)], "UpperArm.L": [("X", 10)], "@root": (0, 0, -0.5)}
    down = {"Hips": [("X", 75)], "Thigh.L": [("X", -70)], "Thigh.R": [("X", -60)], "Shin.L": [("X", 60)], "Shin.R": [("X", 50)],
            "Spine": [("X", 10)], "Head": [("X", -20), ("Z", 20)], "UpperArm.R": [("X", -60)], "UpperArm.L": [("X", -50), ("Y", -30)],
            "Jaw": [("X", 15)], "@root": (0, -0.5, -0.6)}
    return [(0, {}), (8, stagger), (18, knees), (30, down), (40, down)]


def build_slagborn():
    m = mats()
    arm = lp.armature("Armature", skeleton())
    lp.bind(body(m), arm)
    lp.sample_action(arm, "Idle", 60, idle, loop=True)
    lp.sample_action(arm, "Walk", 32, lambda t: gait(t, 24, 35, 7, 0.06, 14), loop=True)
    lp.sample_action(arm, "Run", 20, lambda t: gait(t, 36, 55, 9, 0.1, 24), loop=True)
    lp.keyframe_action(arm, "Attack", attack())
    lp.sample_action(arm, "CastLoop", 24, cast_loop, loop=True)
    lp.keyframe_action(arm, "CastRelease", cast_release())
    lp.keyframe_action(arm, "Hit", [(0, {}), (3, {"Chest": [("X", -8)], "Head": [("X", -12)], "Spine": [("X", -4)]}), (10, {})])
    lp.keyframe_action(arm, "Death", death())
    return arm
