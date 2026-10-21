"""
More creatures for Hollowmere: one parametric quadruped (boar, bear, crocodile and the world boss
Gloomjaw), a spider and a floating crystal elemental (shardling). Same clip names and rotation
conventions as creatures.py.
"""

import math
import random

from mathutils import Vector

import lowpoly as lp


def cycle(t, phase=0.0):
    return math.sin(2 * math.pi * (t + phase))


# ---------------------------------------------------------------- bodies of beasts


def smooth(a, b, x):
    """0 at a, 1 at b, smooth in between (a may be greater than b)."""
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def across(y, z, half_w, half_h, top=1.0, bottom=1.0, n=12, square=0.25):
    """A ring of points around the Y axis at y, centered at height z: index 0 on top, then the left side."""
    e = 2.0 / (2.0 + 3.0 * square)
    out = []
    for i in range(n):
        t = 2 * math.pi * i / n
        s, c = math.sin(t), math.cos(t)
        out.append(Vector((math.copysign(abs(s) ** e, s) * half_w, y,
                           z + math.copysign(abs(c) ** e, c) * half_h * (top if c > 0 else bottom))))
    return out


def trunk(mb, sections, weigh, fur, belly, n=12, belly_cols=2):
    """
    A body along Y through cross-sections (y, z, half width, half height[, top, bottom]), closed at both
    ends; the underside (`belly_cols` columns to each side of the bottom) in the belly material.
    """
    rings = [across(*sec, n=n) for sec in sections]
    ys = [sec[0] for sec in sections]
    lo, hi = min(ys), max(ys)
    margin = (hi - lo) * 0.15

    def center_z(y):
        pts = sorted(sections, key=lambda sec: sec[0])
        for a, b in zip(pts, pts[1:]):
            if a[0] <= y <= b[0]:
                return a[1] + (b[1] - a[1]) * (y - a[0]) / (b[0] - a[0])
        return pts[0][1] if y < pts[0][0] else pts[-1][1]

    def inside(p):
        y = max(lo + margin, min(hi - margin, p.y))
        return Vector((0, y, center_z(y)))

    first, last = sections[0], sections[-1]
    step0 = (sections[1][0] - first[0]) * 0.25
    step1 = (last[0] - sections[-2][0]) * 0.25
    pole0 = [Vector((0, first[0] - step0, first[1]))]
    pole1 = [Vector((0, last[0] + step1, last[1]))]
    bottom = n // 2
    lower = list(range(bottom - belly_cols, bottom + belly_cols + 1))
    upper = list(range(bottom + belly_cols, n)) + list(range(0, bottom - belly_cols + 1))
    for cols, mat in ((upper, fur), (lower, belly)):
        rows = [pole0] + [[ring[i] for i in cols] for ring in rings] + [pole1]
        mb.grid(rows, mat, weigh, inside)


def along_y(bones):
    """Bone weights that hand a body over from one bone to the next along Y: [(bone, y), ...] in order of y."""
    def weigh(p):
        if p.y <= bones[0][1]:
            return {bones[0][0]: 1.0}
        for (a, ya), (b, yb) in zip(bones, bones[1:]):
            if p.y <= yb:
                t = smooth(ya, yb, p.y)
                return {a: 1.0 - t, b: t}
        return {bones[-1][0]: 1.0}
    return weigh


def leg(mb, points, radii, mat, bones, sides=7):
    """A leg through `points` (hip, knee, ankle) with a radius at each; each segment follows its bone."""
    for (a, b), (ra, rb), bone in zip(zip(points, points[1:]), zip(radii, radii[1:]), bones):
        mid = (Vector(a) + Vector(b)) / 2
        mb.tube([(a, ra), (mid, (ra + rb) / 2 * 1.04), (b, rb)], mat, bone, sides=sides)


def paw(mb, ankle, tip, width, mat, bone, claws=None, hoof=False):
    """A rounded paw (or a split hoof) from the ankle to the toe tip on the ground, with claws."""
    ax, ay, az = ankle
    tx, ty, _ = tip
    w = width / 2
    if hoof:
        for s in (1, -1):
            mb.hull([(ax + s * w * 0.1, ay, az + 0.02), (ax + s * w, ay, 0.06), (ax + s * w * 0.9, ty, 0.0), (ax + s * w * 0.15, ty + 0.01, 0.0),
                     (ax + s * w, ay + 0.02, 0.0), (ax + s * w * 0.1, ay + 0.02, 0.0)], mat, bone)
        return
    mb.hull([(ax + s * w * 0.8, ay + 0.02, 0.0) for s in (1, -1)] + [(ax + s * w * 0.7, ay, az * 0.8) for s in (1, -1)]
            + [(tx + s * w, (ay + ty) / 2, 0.0) for s in (1, -1)] + [(tx + s * w * 0.8, (ay + ty) / 2, w * 0.9) for s in (1, -1)]
            + [(tx + s * w * 0.6, ty, 0.0) for s in (1, -1)] + [(tx + s * w * 0.5, ty, w * 0.6) for s in (1, -1)], mat, bone)
    if claws is not None:
        for s in (-0.6, 0.0, 0.6):
            base = Vector((tx + s * w, ty + 0.01, w * 0.35))
            mb.tube([(base, w * 0.22), (base + Vector((s * w * 0.15, -w * 0.7, -w * 0.3)), 0.004)], claws, bone, sides=4)


def tufts(mb, points, mat, bone, rng, size=0.08, sweep=(0, 0.6, 0.4)):
    """Shaggy tufts of fur: a jagged little spike at each point, swept back and up."""
    for p in points:
        p = Vector(p)
        d = Vector(sweep).normalized()
        tip = p + d * size * rng.uniform(0.8, 1.3) + Vector((rng.uniform(-0.02, 0.02), 0, rng.uniform(-0.01, 0.02)))
        r = size * 0.35
        mb.hull([p + Vector((r, 0, 0)), p + Vector((-r, 0, 0)), p + Vector((0, -r, 0)), p + Vector((0, r * 0.5, -r * 0.6)), tip], mat, bone)


# ---------------------------------------------------------------- quadrupeds

QUADS = {
    "Boar": dict(
        height=0.74, hip=0.7, length=0.95, width=0.44, depth=0.5, leg_x=0.15, leg_r=0.07, sprawl=0.0,
        neck=(0.14, 0.0), head_len=0.42, head_drop=0.12, jaw_drop=0.1, tail=(0.22, 0.12), head="boar", hump=1.12,
        colors=dict(Fur="#5b4436", FurLight="#7d6553", Dark="#2a2420", Tusk="#ebe2c8"),
        walk=(18, 30, 2, 0.02), run=(30, 55, 6, 0.05), walk_speed=1.1, run_speed=3.8,
    ),
    "Bear": dict(
        height=1.2, hip=1.1, length=1.35, width=0.72, depth=0.78, leg_x=0.26, leg_r=0.13, sprawl=0.0,
        neck=(0.22, 0.05), head_len=0.5, head_drop=0.12, jaw_drop=0.14, tail=(0.12, 0.05), head="bear", hump=1.28,
        colors=dict(Fur="#4a3a2c", FurLight="#6e5844", Dark="#1f1a16", Tusk="#e6dcc4"),
        walk=(16, 30, 3, 0.03), run=(28, 50, 7, 0.07), walk_speed=1.4, run_speed=4.5,
    ),
    "Crocodile": dict(
        height=0.4, hip=0.42, length=1.5, width=0.6, depth=0.34, leg_x=0.26, leg_r=0.07, sprawl=0.24,
        neck=(0.18, 0.0), head_len=0.85, head_drop=0.06, jaw_drop=0.07, tail=(2.0, 0.25), head="croc",
        colors=dict(Fur="#4b5a34", FurLight="#a6a070", Dark="#26301a", Tusk="#e8e2c6"),
        walk=(22, 25, 1, 0.01), run=(32, 40, 2, 0.02), walk_speed=1.0, run_speed=3.0,
    ),
}
# The world boss: a crocodile grown over with moon-shard crystals (scaled up in the client).
QUADS["Gloomjaw"] = dict(QUADS["Crocodile"], colors=dict(Fur="#38452c", FurLight="#8c8a62", Dark="#1c2414", Tusk="#efe8cc"), crystals=True)


def quad_points(s):
    """Key points of the body: hips and chest centers and the four leg chains."""
    L, H, Hh, depth = s["length"], s["height"], s["hip"], s["depth"]
    hy, cy = L / 2, -L / 2
    legs = {}
    for leg, y, top in (("Front", cy + 0.1 * L, H - depth * 0.45), ("Back", hy - 0.1 * L, Hh - depth * 0.45)):
        for side, sx in (("L", 1), ("R", -1)):
            x = sx * s["leg_x"]
            out = sx * s["sprawl"]
            knee_z = top * (0.85 if s["sprawl"] else 0.5)
            knee = (x + out, y + (0.03 if leg == "Front" else -0.05), knee_z)
            ankle = (x + out * 1.25, y, 0.07)
            tip = (x + out * 1.3, y - 0.12 - s["leg_r"], 0.03)
            legs[leg + "." + side] = ((x, y, top), knee, ankle, tip)
    return hy, cy, legs


def quad_skeleton(s):
    L, H, Hh = s["length"], s["height"], s["hip"]
    hy, cy, legs = quad_points(s)
    mid_z = (H + Hh) / 2 + 0.02
    nl, rise = s["neck"]
    neck_end = (0, cy - nl, H + rise)
    head_end = (0, neck_end[1] - s["head_len"], neck_end[2] - s["head_drop"])
    jd = s["jaw_drop"]
    tl, tdrop = s["tail"]
    bones = [
        ("Hips", (0, hy, Hh), (0, hy - L * 0.4, Hh + 0.02), None),
        ("Spine", (0, hy - L * 0.4, Hh + 0.02), (0, cy + L * 0.2, mid_z), "Hips"),
        ("Chest", (0, cy + L * 0.2, mid_z), (0, cy, H), "Spine"),
        ("Neck", (0, cy, H), neck_end, "Chest"),
        ("Head", neck_end, head_end, "Neck"),
        ("Jaw", (0, neck_end[1] - s["head_len"] * 0.1, neck_end[2] - jd), (0, head_end[1], head_end[2] - jd), "Head"),
        ("Tail1", (0, hy + 0.02, Hh), (0, hy + tl * 0.5, Hh - tdrop * 0.5), "Hips"),
        ("Tail2", (0, hy + tl * 0.5, Hh - tdrop * 0.5), (0, hy + tl, Hh - tdrop), "Tail1"),
    ]
    for name, (top, knee, ankle, tip) in legs.items():
        leg, side = name.split(".")
        parent = "Chest" if leg == "Front" else "Hips"
        bones += [
            (leg + "UpperLeg." + side, top, knee, parent),
            (leg + "LowerLeg." + side, knee, ankle, leg + "UpperLeg." + side),
            (leg + "Paw." + side, ankle, tip, leg + "LowerLeg." + side),
        ]
    return bones


def quad_body(name, s):
    """
    A rounded body through cross-sections (rump, haunch, a tucked belly, the shoulders with an optional
    hump, the chest) with a pale belly, a neck, muscled legs, paws or hooves, a tail and the head.
    """
    m = {k: lp.material(k, v) for k, v in s["colors"].items()}
    mb = lp.MeshBuilder(name + "Body")
    L, H, Hh, W, depth = s["length"], s["height"], s["hip"], s["width"], s["depth"]
    hy, cy, legs = quad_points(s)
    hump = s.get("hump", 1.0)
    rng = random.Random(len(name))
    sections = [
        (cy - 0.04 * L, H, 0.3 * W, 0.33 * depth),
        (cy + 0.08 * L, H - 0.03, 0.47 * W, 0.48 * depth),
        (cy + 0.28 * L, H - 0.05, 0.5 * W, 0.5 * depth, hump, 1.0),
        (0.0, (H + Hh) / 2 - 0.03, 0.46 * W, 0.44 * depth, 1.0, 0.86),
        (hy - 0.22 * L, Hh - 0.02, 0.48 * W, 0.47 * depth),
        (hy - 0.04 * L, Hh, 0.42 * W, 0.42 * depth),
        (hy + 0.06 * L, Hh - 0.02, 0.18 * W, 0.18 * depth),
    ]
    trunk(mb, sections, along_y([("Chest", cy + 0.18 * L), ("Spine", 0.0), ("Hips", hy - 0.28 * L)]), m["Fur"], m["FurLight"])
    nl, rise = s["neck"]
    neck_end = (0, cy - nl, H + rise)
    trunk(mb, [(neck_end[1] - 0.04, neck_end[2] - 0.02, 0.27 * W, 0.3 * depth), ((neck_end[1] + cy) / 2, (neck_end[2] + H) / 2, 0.3 * W, 0.34 * depth),
               (cy + 0.04, H - 0.02, 0.32 * W, 0.36 * depth)],
          along_y([("Neck", neck_end[1] + nl * 0.3), ("Chest", cy)]), m["Fur"], m["FurLight"], n=10, belly_cols=1)
    HEADS[s["head"]](mb, m, s, neck_end)
    hoof = s["head"] == "boar"
    for leg_name, (top, knee, ankle, tip) in legs.items():
        part, side = leg_name.split(".")
        r = s["leg_r"]
        radii = (r * 1.7, r * 1.1, r * 0.85) if part == "Back" else (r * 1.5, r * 1.0, r * 0.85)
        bones = (part + "UpperLeg." + side, part + "LowerLeg." + side)
        inset = 0.0 if s["sprawl"] else (abs(top[0]) - max(0.0, W * 0.46 - radii[0])) * (1 if top[0] > 0 else -1)
        leg(mb, [(top[0] - inset, top[1], top[2] + 0.04), knee, (ankle[0], ankle[1], ankle[2] + r * 0.4)], radii, m["Fur"], bones)
        paw(mb, ankle, tip, r * 2.1, m["Dark"] if hoof else m["Fur"], part + "Paw." + side, claws=None if hoof else m["Dark"], hoof=hoof)
    tl, tdrop = s["tail"]
    tr = depth * (0.3 if tl > 1 else 0.08)
    mb.tube([((0, hy - 0.05, Hh), tr), ((0, hy + tl * 0.5, Hh - tdrop * 0.5), tr * 0.75)], m["Fur"], "Tail1", sides=6)
    mb.tube([((0, hy + tl * 0.5, Hh - tdrop * 0.5), tr * 0.75), ((0, hy + tl, Hh - tdrop), 0.02)], m["Fur"], "Tail2", sides=6)
    if s["head"] == "boar":
        # A bristly dark ridge down the back.
        ridge = [(0, y, (H if y < 0 else Hh) + depth * 0.45 * (hump if y < 0 else 1.0) - 0.02) for y in [cy + 0.05 + (hy - cy) * 0.6 * i / 9 for i in range(10)]]
        for i, p in enumerate(ridge):
            bone = "Chest" if p[1] < cy + 0.25 * L else "Spine"
            tufts(mb, [p], m["Dark"], bone, rng, size=0.09 - 0.004 * i, sweep=(0, 0.7, 0.7))
    if s["head"] == "bear":
        # Shaggy fur over the hump and around the neck.
        for i in range(14):
            a = math.pi * (0.15 + 0.7 * i / 13)
            p = (math.cos(a) * W * 0.48, cy + 0.12 * L + rng.uniform(-0.06, 0.06), H - 0.05 + math.sin(a) * depth * 0.5 * hump)
            tufts(mb, [p], m["Fur"], "Chest", rng, size=0.11, sweep=(math.cos(a) * 0.5, 0.6, 0.3))
    if s.get("crystals"):
        glow = lp.material("CrystalGlow", "#7ff2ff", emission=2.0)
        rng = random.Random(4)
        spots = [("Hips", hy - L * 0.2, Hh), ("Spine", 0.0, (H + Hh) / 2), ("Chest", cy + L * 0.15, H), ("Tail1", hy + tl * 0.25, Hh - tdrop * 0.2)]
        for bone, y, z in spots:
            for _ in range(3):
                x = rng.uniform(-W * 0.3, W * 0.3)
                yy = y + rng.uniform(-0.15, 0.15)
                top = z + depth * 0.4
                h = rng.uniform(0.25, 0.5)
                mb.hull([(x + 0.06, yy, top - 0.05), (x - 0.06, yy, top - 0.05), (x, yy + 0.06, top - 0.05), (x, yy - 0.06, top - 0.05),
                         (x + rng.uniform(-0.1, 0.1), yy + rng.uniform(-0.1, 0.1), top + h)], glow, bone)
    return mb.finish()


def head_boar(mb, m, s, base):
    x0, y0, z0 = base
    l = s["head_len"]
    pts = [(sx * 0.15, y0 + 0.02, z0 + 0.12) for sx in (1, -1)] + [(sx * 0.16, y0, z0 - 0.15) for sx in (1, -1)]
    pts += [(sx * 0.08, y0 - l, z0 - 0.05) for sx in (1, -1)] + [(sx * 0.08, y0 - l, z0 - 0.17) for sx in (1, -1)]
    mb.hull(pts, m["Fur"], "Head")
    mb.box((0, y0 - l - 0.01, z0 - 0.11), (0.15, 0.03, 0.12), m["Dark"], "Head")
    for sx in (1, -1):
        mb.hull([(sx * 0.07, y0 - l + 0.1, z0 - 0.15), (sx * 0.09, y0 - l + 0.12, z0 - 0.13), (sx * 0.075, y0 - l + 0.08, z0 - 0.12),
                 (sx * 0.12, y0 - l - 0.02, z0 + 0.02)], m["Tusk"], "Jaw")
        mb.hull([(sx * 0.1, y0 + 0.02, z0 + 0.1), (sx * 0.16, y0 + 0.04, z0 + 0.1), (sx * 0.14, y0 + 0.06, z0 + 0.24)], m["Fur"], "Head")
        mb.box((sx * 0.1, y0 - l * 0.45, z0 + 0.05), (0.03, 0.02, 0.025), m["Dark"], "Head")
    mb.box((0, y0 + 0.1, z0 + 0.18), (0.05, 0.4, 0.08), m["Dark"], "Neck")  # a bristly ridge


def head_bear(mb, m, s, base):
    x0, y0, z0 = base
    l = s["head_len"]
    mb.sphere((0, y0 - l * 0.35, z0), (0.24, 0.26, 0.22), m["Fur"], "Head", segments=8, rings=5)
    mb.hull([(sx * 0.1, y0 - l * 0.55, z0 + 0.02) for sx in (1, -1)] + [(sx * 0.1, y0 - l * 0.55, z0 - 0.14) for sx in (1, -1)]
            + [(sx * 0.07, y0 - l * 1.0, z0 - 0.04) for sx in (1, -1)] + [(sx * 0.07, y0 - l * 1.0, z0 - 0.14) for sx in (1, -1)],
            m["FurLight"], "Head")
    mb.box((0, y0 - l * 1.0, z0 - 0.04), (0.09, 0.04, 0.06), m["Dark"], "Head")
    for sx in (1, -1):
        mb.sphere((sx * 0.17, y0 - l * 0.25, z0 + 0.2), (0.06, 0.04, 0.06), m["Fur"], "Head", segments=6, rings=4)
        mb.box((sx * 0.1, y0 - l * 0.6, z0 + 0.09), (0.035, 0.02, 0.03), m["Dark"], "Head")
    mb.box((0, y0 - l * 0.8, z0 - 0.18), (0.12, 0.3, 0.04), m["FurLight"], "Jaw")


def head_croc(mb, m, s, base):
    x0, y0, z0 = base
    l = s["head_len"]
    mb.hull([(sx * 0.2, y0 + 0.05, z0 + 0.08) for sx in (1, -1)] + [(sx * 0.22, y0, z0 - 0.06) for sx in (1, -1)]
            + [(sx * 0.07, y0 - l, z0 - 0.0) for sx in (1, -1)] + [(sx * 0.08, y0 - l, z0 - 0.06) for sx in (1, -1)],
            m["Fur"], "Head")
    mb.hull([(sx * 0.19, y0 - 0.05, z0 - 0.07) for sx in (1, -1)] + [(sx * 0.07, y0 - l + 0.02, z0 - 0.07) for sx in (1, -1)]
            + [(sx * 0.16, y0 - 0.05, z0 - 0.14) for sx in (1, -1)] + [(sx * 0.06, y0 - l + 0.04, z0 - 0.11) for sx in (1, -1)],
            m["FurLight"], "Jaw")
    for sx in (1, -1):
        mb.sphere((sx * 0.11, y0 - 0.08, z0 + 0.1), (0.05, 0.06, 0.04), m["Fur"], "Head", segments=6, rings=4)
        mb.box((sx * 0.12, y0 - 0.12, z0 + 0.11), (0.03, 0.02, 0.02), m["Tusk"], "Head")
        for i in range(4):
            mb.hull([(sx * 0.075, y0 - l * (0.3 + i * 0.17), z0 - 0.065), (sx * 0.075, y0 - l * (0.3 + i * 0.17) - 0.04, z0 - 0.065),
                     (sx * 0.08, y0 - l * (0.3 + i * 0.17) - 0.02, z0 - 0.11)], m["Tusk"], "Head")


HEADS = {"boar": head_boar, "bear": head_bear, "croc": head_croc}


def quad_gait(s, t, phases, swing, lift, flex, bob):
    pose = {}
    sprawl = s["sprawl"] > 0
    for leg, phase in phases.items():
        c = math.cos(2 * math.pi * (t + phase))
        up = max(0.0, math.sin(2 * math.pi * (t + phase - 0.5)))
        part, side = leg.split(".")
        if sprawl:
            # Sprawling legs swing around the vertical axis and lift sideways.
            upper = [("Z", -swing * c), ("Y", -lift * 0.5 * up)]
            lower = [("Y", lift * 0.3 * up)]
        else:
            upper = [("X", -swing * c - lift * 0.2 * up)]
            lower = [("X", lift * up)]
        paw = [("X", -8 * c)]
        if side == "R":
            upper, lower, paw = lp.mirror(upper), lp.mirror(lower), lp.mirror(paw)
        pose[part + "UpperLeg." + side] = upper
        pose[part + "LowerLeg." + side] = lower
        pose[part + "Paw." + side] = paw
    if sprawl:
        # A crocodile walks with its whole body swinging side to side.
        pose["Hips"] = [("Z", 6 * cycle(t))]
        pose["Spine"] = [("Z", -8 * cycle(t))]
        pose["Chest"] = [("Z", -4 * cycle(t))]
        pose["Tail1"] = [("Z", 14 * cycle(t, 0.2))]
        pose["Tail2"] = [("Z", 18 * cycle(t, 0.35))]
    else:
        pose["Spine"] = [("X", flex * cycle(t))]
        pose["Chest"] = [("X", -flex * 0.6 * cycle(t))]
        pose["Tail1"] = [("Z", 10 * cycle(t, 0.25))]
    pose["Neck"] = [("X", 3 * cycle(t, 0.25))]
    pose["@root"] = (0, 0, -bob * abs(cycle(t)))
    return pose


def quad_idle(s):
    def pose_at(t):
        b = cycle(t)
        return {"Spine": [("X", 1.0 * b)], "Neck": [("X", 2 * b), ("Z", 5 * math.sin(math.pi * t))], "Jaw": [("X", 1 + b)],
                "Tail1": [("Z", 10 * cycle(t, 0.1))], "Tail2": [("Z", 8 * cycle(t, 0.3))], "@root": (0, 0, -0.006 * (1 + b))}
    return pose_at


def quad_attack(s):
    reach = s["length"] * 0.2
    crouch = {"Neck": [("X", -10)], "Head": [("X", -6)], "@root": (0, reach * 0.3, -0.04)}
    bite = {"Spine": [("X", -4)], "Neck": [("X", 14)], "Head": [("X", 10)], "Jaw": [("X", 35)], "@root": (0, -reach, 0.02)}
    if s["sprawl"]:
        bite["Neck"] = [("Z", 15)]
        bite["Head"] = [("X", -10)]
    snap = dict(bite)
    snap["Jaw"] = [("X", 0)]
    return [(0, {}), (5, crouch), (9, bite), (11, snap), (19, {})]


def quad_death(s):
    if s["sprawl"]:
        # Rolls onto its back.
        over = {"Hips": [("Y", 170)], "@root": (0, 0, s["depth"] * 0.6), "Jaw": [("X", 20)]}
        return [(0, {}), (8, {"Hips": [("Y", 80)], "@root": (0, 0, s["depth"] * 0.4)}), (16, over), (30, over)]
    lie = s["hip"] - s["width"] * 0.45
    down = {"Hips": [("Y", 86)], "Neck": [("X", -20), ("Z", 10)], "Jaw": [("X", 12)], "@root": (0, 0, -lie)}
    for leg in ("FrontUpperLeg", "BackUpperLeg"):
        down[leg + ".L"] = [("X", -15)]
        down[leg + ".R"] = [("X", 20)]
    return [(0, {}), (8, {"Neck": [("X", -18)], "Hips": [("Y", 20)], "@root": (0, 0, -lie * 0.3)}), (18, down), (30, down)]


def build_quad(name):
    s = QUADS[name]
    arm = lp.armature("Armature", quad_skeleton(s))
    lp.bind(quad_body(name, s), arm)
    four_beat = {"Back.L": 0.0, "Front.L": 0.25, "Back.R": 0.5, "Front.R": 0.75}
    gallop = {"Front.L": 0.0, "Front.R": 0.12, "Back.L": 0.5, "Back.R": 0.62}
    trot = {"Front.L": 0.0, "Back.R": 0.0, "Front.R": 0.5, "Back.L": 0.5}
    sw, li, fl, bo = s["walk"]
    lp.sample_action(arm, "Idle", 60, quad_idle(s), loop=True)
    lp.sample_action(arm, "Walk", 30, lambda t: quad_gait(s, t, trot if s["sprawl"] else four_beat, sw, li, fl, bo), loop=True)
    sw, li, fl, bo = s["run"]
    lp.sample_action(arm, "Run", 18, lambda t: quad_gait(s, t, trot if s["sprawl"] else gallop, sw, li, fl, bo), loop=True)
    lp.keyframe_action(arm, "Attack", quad_attack(s))
    lp.keyframe_action(arm, "Hit", [(0, {}), (3, {"Neck": [("X", -14)], "Spine": [("X", -3)]}), (9, {})])
    lp.keyframe_action(arm, "Death", quad_death(s))
    return arm


# ---------------------------------------------------------------- spider

SPIDER_LEGS = [(-0.2, -35), (-0.07, -12), (0.06, 12), (0.18, 35)]  # (y on the body, sweep in degrees)


def spider_skeleton():
    bones = [
        ("Body", (0, 0.12, 0.5), (0, -0.28, 0.5), None),
        ("Abdomen", (0, 0.12, 0.52), (0, 0.75, 0.62), "Body"),
        ("Fangs", (0, -0.28, 0.45), (0, -0.38, 0.35), "Body"),
    ]
    for i, (y, sweep) in enumerate(SPIDER_LEGS, start=1):
        a = math.radians(sweep)
        for side, sx in (("L", 1), ("R", -1)):
            root = (sx * 0.16, y, 0.5)
            knee = (sx * (0.16 + 0.42 * math.cos(a)), y + 0.42 * math.sin(a), 0.85)
            foot = (sx * (0.16 + 0.95 * math.cos(a)), y + 0.95 * math.sin(a), 0.02)
            bones += [("LegUpper%d.%s" % (i, side), root, knee, "Body"), ("LegLower%d.%s" % (i, side), knee, foot, "LegUpper%d.%s" % (i, side))]
    return bones


def spider_body():
    """
    A big forest spider: a hairy cephalothorax with a cluster of glowing eyes and curved fangs, a swollen
    abdomen with a red marking and bristles, and jointed legs with knobbly knees, banded shins and claws.
    """
    body = lp.material("Chitin", "#2e2b33")
    light = lp.material("ChitinLight", "#57505e")
    mark = lp.material("Marking", "#a8332a")
    eyes = lp.material("EyesGlow", "#ff5a3a", emission=1.5)
    rng = random.Random(13)
    mb = lp.MeshBuilder("SpiderBody")
    trunk(mb, [(-0.4, 0.5, 0.1, 0.07), (-0.32, 0.52, 0.2, 0.13), (-0.12, 0.54, 0.25, 0.16), (0.08, 0.53, 0.2, 0.13), (0.14, 0.52, 0.1, 0.08)],
          lambda q: {"Body": 1.0}, body, light, n=10, belly_cols=2)
    trunk(mb, [(0.12, 0.56, 0.1, 0.1), (0.22, 0.6, 0.3, 0.26), (0.45, 0.66, 0.38, 0.33), (0.7, 0.66, 0.3, 0.28), (0.86, 0.62, 0.12, 0.12)],
          lambda q: {"Abdomen": 1.0}, body, light, n=12, belly_cols=2)
    # The marking: a red hourglass on the back of the abdomen.
    for y0, y1, w0, w1 in ((0.28, 0.45, 0.13, 0.03), (0.45, 0.62, 0.03, 0.13)):
        mb.hull([(sx * w, y, 0.995 - 0.25 * ((y - 0.45) / 0.45) ** 2) for sx in (1, -1) for y, w in ((y0, w0), (y1, w1))]
                + [(0, (y0 + y1) / 2, 1.005)], mark, "Abdomen")
    for _ in range(16):
        t = rng.uniform(0.2, math.pi - 0.2)
        y = rng.uniform(0.25, 0.8)
        r = 0.36 * math.sqrt(max(0.0, 1 - ((y - 0.47) / 0.42) ** 2))
        base = (math.cos(t) * r, y, 0.66 + math.sin(t) * r * 0.9)
        tufts(mb, [base], light, "Abdomen", rng, size=0.07, sweep=(math.cos(t) * 0.3, 0.8, math.sin(t) * 0.4))
    # Eyes: two big, four small, glowing; fangs curving down.
    for sx in (1, -1):
        mb.sphere((sx * 0.06, -0.39, 0.58), 0.04, eyes, "Body", segments=6, rings=4)
        mb.sphere((sx * 0.12, -0.35, 0.61), 0.026, eyes, "Body", segments=5, rings=3)
        mb.sphere((sx * 0.04, -0.37, 0.64), 0.022, eyes, "Body", segments=5, rings=3)
        mb.tube([((sx * 0.06, -0.38, 0.47), 0.035), ((sx * 0.05, -0.47, 0.4), 0.026), ((sx * 0.03, -0.45, 0.31), 0.004)], light, "Fangs", sides=5)
        mb.tube([((sx * 0.1, -0.36, 0.5), 0.025), ((sx * 0.16, -0.48, 0.46), 0.02), ((sx * 0.13, -0.55, 0.38), 0.012)], body, "Body", sides=4)
    for i, (y, sweep) in enumerate(SPIDER_LEGS, start=1):
        ang = math.radians(sweep)
        for side, sx in (("L", 1), ("R", -1)):
            root = (sx * 0.16, y, 0.5)
            knee = (sx * (0.16 + 0.42 * math.cos(ang)), y + 0.42 * math.sin(ang), 0.85)
            foot = (sx * (0.16 + 0.95 * math.cos(ang)), y + 0.95 * math.sin(ang), 0.02)
            upper, lower = "LegUpper%d.%s" % (i, side), "LegLower%d.%s" % (i, side)
            mid = tuple(a + (b - a) * 0.5 for a, b in zip(root, knee))
            mb.tube([(root, 0.05), (mid, 0.055), (knee, 0.04)], body, upper, sides=5)
            mb.sphere(knee, 0.055, light, upper, segments=6, rings=4)
            shin = [tuple(a + (b - a) * f for a, b in zip(knee, foot)) for f in (0.0, 0.35, 0.36, 0.42, 0.43, 0.8, 1.0)]
            mats_ = [body, body, light, light, body, body]
            for k in range(len(shin) - 1):
                ra = 0.04 * (1 - 0.7 * k / 6)
                rb = 0.04 * (1 - 0.7 * (k + 1) / 6)
                mb.tube([(shin[k], ra), (shin[k + 1], rb)], mats_[k], lower, sides=5)
            tufts(mb, [tuple(a + (b - a) * 0.2 for a, b in zip(knee, foot))], light, lower, rng, size=0.05, sweep=(sx * 0.5, 0, 0.6))
    return mb.finish()


def spider_gait(t, swing, lift, bob):
    pose = {}
    for i in range(1, 5):
        for side in ("L", "R"):
            group = (i % 2 == 1) == (side == "L")
            c = math.cos(2 * math.pi * (t + (0 if group else 0.5)))
            up = max(0.0, math.sin(2 * math.pi * (t + (0 if group else 0.5) - 0.5)))
            upper = [("Z", -swing * c), ("Y", -lift * up)]
            lower = [("Y", lift * 0.5 * up)]
            if side == "R":
                upper, lower = lp.mirror(upper), lp.mirror(lower)
            pose["LegUpper%d.%s" % (i, side)] = upper
            pose["LegLower%d.%s" % (i, side)] = lower
    pose["Abdomen"] = [("X", 3 * cycle(t, 0.25))]
    pose["@root"] = (0, 0, -bob * abs(cycle(t)))
    return pose


def build_spider():
    arm = lp.armature("Armature", spider_skeleton())
    lp.bind(spider_body(), arm)
    lp.sample_action(arm, "Idle", 60, lambda t: {"Abdomen": [("X", 2 * cycle(t))], "Fangs": [("X", 4 * cycle(t, 0.3))],
                                                    "@root": (0, 0, 0.01 * cycle(t))}, loop=True)
    lp.sample_action(arm, "Walk", 20, lambda t: spider_gait(t, 14, 22, 0.01), loop=True)
    lp.sample_action(arm, "Run", 12, lambda t: spider_gait(t, 20, 30, 0.02), loop=True)
    rear = {"Body": [("X", -22)], "Abdomen": [("X", 15)], "Fangs": [("X", -25)], "@root": (0, 0.1, 0.12)}
    for side in ("L", "R"):
        rear["LegUpper1.%s" % side] = lp.mirror([("Y", -40)]) if side == "R" else [("Y", -40)]
    strike = {"Body": [("X", 8)], "Fangs": [("X", 30)], "@root": (0, -0.25, 0)}
    lp.keyframe_action(arm, "Attack", [(0, {}), (6, rear), (10, strike), (18, {})])
    lp.keyframe_action(arm, "Hit", [(0, {}), (3, {"Body": [("X", -10)], "@root": (0, 0.06, 0)}), (9, {})])
    curl = {"@root": (0, 0, -0.32), "Abdomen": [("X", -10)]}
    for i in range(1, 5):
        for side in ("L", "R"):
            upper, lower = [("Y", -55)], [("Y", 110)]
            if side == "R":
                upper, lower = lp.mirror(upper), lp.mirror(lower)
            curl["LegUpper%d.%s" % (i, side)] = upper
            curl["LegLower%d.%s" % (i, side)] = lower
    lp.keyframe_action(arm, "Death", [(0, {}), (10, curl), (30, curl)])
    return arm


# ---------------------------------------------------------------- shardling

def build_shardling():
    arm = lp.armature("Armature", [
        ("Base", (0, 0, 0.3), (0, 0, 0.6), None),
        ("Body", (0, 0, 0.6), (0, 0, 1.35), "Base"),
        ("Head", (0, 0, 1.35), (0, 0, 1.7), "Body"),
        ("ArmUpper.L", (0.38, 0, 1.25), (0.62, -0.05, 0.9), "Body"),
        ("ArmLower.L", (0.62, -0.05, 0.9), (0.66, -0.15, 0.45), "ArmUpper.L"),
        ("ArmUpper.R", (-0.38, 0, 1.25), (-0.62, -0.05, 0.9), "Body"),
        ("ArmLower.R", (-0.62, -0.05, 0.9), (-0.66, -0.15, 0.45), "ArmUpper.R"),
    ])
    rock = lp.material("StoneDark", "#4f4c58")
    rock2 = lp.material("Stone", "#6e6a78")
    glow = lp.material("CrystalGlow", "#7ff2ff", emission=2.5)
    rng = random.Random(21)
    mb = lp.MeshBuilder("ShardlingBody")
    mb.hull([(rng.uniform(-0.4, 0.4), rng.uniform(-0.28, 0.28), rng.uniform(0.75, 1.4)) for _ in range(14)], rock, "Body")
    mb.hull([(0.0, 0.0, 0.15), (0.16, 0.05, 0.75), (-0.14, 0.08, 0.75), (0.02, -0.15, 0.75)], glow, "Base")
    mb.hull([(rng.uniform(-0.17, 0.17), rng.uniform(-0.15, 0.15), rng.uniform(1.38, 1.7)) for _ in range(10)], rock2, "Head")
    mb.box((0, -0.15, 1.55), (0.2, 0.03, 0.04), glow, "Head")
    for sx in (1, -1):
        side = "L" if sx > 0 else "R"
        mb.hull([(sx * 0.38, 0, 1.25), (sx * 0.5, 0.1, 1.15), (sx * 0.62, -0.05, 0.9), (sx * 0.5, -0.12, 1.1), (sx * 0.45, 0.05, 0.95)], rock2, "ArmUpper." + side)
        mb.hull([(sx * 0.62, -0.05, 0.9), (sx * 0.75, 0.05, 0.6), (sx * 0.55, 0.05, 0.62), (sx * 0.7, -0.2, 0.42), (sx * 0.6, -0.15, 0.5)], rock, "ArmLower." + side)
        for z in (1.3, 1.1):
            x = sx * rng.uniform(0.15, 0.32)
            mb.hull([(x + 0.05, 0.1, z), (x - 0.05, 0.1, z), (x, 0.18, z), (x + sx * 0.15, 0.35, z + 0.35)], glow, "Body")
    lp.bind(mb.finish(), arm)

    def float_pose(t, lean, speed_bob):
        b = cycle(t)
        return {"Base": [("X", lean)], "Body": [("Z", 4 * b)], "ArmUpper.L": [("X", -lean * 0.5 + 6 * b)],
                "ArmUpper.R": [("X", -lean * 0.5 - 6 * b)], "@root": (0, 0, 0.08 * b * speed_bob)}
    lp.sample_action(arm, "Idle", 60, lambda t: float_pose(t, 0, 1), loop=True)
    lp.sample_action(arm, "Walk", 30, lambda t: float_pose(t, 10, 0.6), loop=True)
    lp.sample_action(arm, "Run", 20, lambda t: float_pose(t, 18, 0.4), loop=True)
    raise_ = {"ArmUpper.L": [("X", -110)], "ArmUpper.R": [("X", -110)], "Body": [("X", -10)]}
    slam = {"ArmUpper.L": [("X", -20)], "ArmUpper.R": [("X", -20)], "Body": [("X", 15)], "@root": (0, -0.15, -0.1)}
    lp.keyframe_action(arm, "Attack", [(0, {}), (7, raise_), (11, slam), (20, {})])
    lp.keyframe_action(arm, "Hit", [(0, {}), (3, {"Body": [("X", -12)]}), (9, {})])
    down = {"Base": [("X", 70)], "Body": [("X", 20)], "ArmUpper.L": [("Y", 40)], "ArmUpper.R": [("Y", -40)], "@root": (0, 0, -0.35)}
    lp.keyframe_action(arm, "Death", [(0, {}), (10, {"@root": (0, 0, -0.2)}), (18, down), (30, down)])
    return arm
