"""
More creatures for Hollowmere, sculpted like the heroes: quadrupeds on one parametric skeleton (boar,
bear, crocodile and the world boss Gloomjaw), a spider and a floating crystal elemental (shardling).
Same clip names and rotation conventions as creatures.py.
"""

import bisect
import math
import random

from mathutils import Vector

import lowpoly as lp
import sculpt as sc


def cycle(t, phase=0.0):
    return math.sin(2 * math.pi * (t + phase))


# ---------------------------------------------------------------- bodies of beasts


def smooth(a, b, x):
    """0 at a, 1 at b, smooth in between (a may be greater than b)."""
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


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
        walk=(18, 30, 2, 0.02), run=(30, 55, 6, 0.05), walk_speed=1.1, run_speed=3.8, slim=0.6, bulk=0.82,
    ),
    "Bear": dict(
        height=1.2, hip=1.1, length=1.35, width=0.72, depth=0.78, leg_x=0.26, leg_r=0.13, sprawl=0.0,
        neck=(0.22, 0.05), head_len=0.5, head_drop=0.12, jaw_drop=0.14, tail=(0.12, 0.05), head="bear", hump=1.28,
        colors=dict(Fur="#4a3a2c", FurLight="#6e5844", Dark="#1f1a16", Tusk="#e6dcc4"),
        walk=(16, 30, 3, 0.03), run=(28, 50, 7, 0.07), walk_speed=1.4, run_speed=4.5, slim=0.85, ruff=True,
    ),
    "Crocodile": dict(
        height=0.4, hip=0.42, length=1.5, width=0.6, depth=0.34, leg_x=0.26, leg_r=0.07, sprawl=0.24,
        neck=(0.18, 0.0), head_len=0.85, head_drop=0.06, jaw_drop=0.07, tail=(2.0, 0.25), head="croc",
        colors=dict(Fur="#4b5a34", FurLight="#a6a070", Dark="#26301a", Band="#38452a", Tusk="#e8e2c6"),
        walk=(22, 25, 1, 0.01), run=(32, 40, 2, 0.02), walk_speed=1.0, run_speed=3.0,
    ),
}
# The world boss: a crocodile grown over with moon-shard crystals (scaled up in the client).
QUADS["Gloomjaw"] = dict(QUADS["Crocodile"], colors=dict(Fur="#38452c", FurLight="#8c8a62", Dark="#1c2414", Band="#28321f", Tusk="#efe8cc"),
                         crystals=True)


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


# ---------------------------------------------------------------- sculpted quadrupeds

def _spine(s):
    """The line through the middle of the trunk, rump to neck: (y, z) pairs by increasing y."""
    L, H, Hh = s["length"], s["height"], s["hip"]
    hy, cy, _ = quad_points(s)
    nl, rise = s["neck"]
    mid_z = (H + Hh) / 2
    return [(cy - nl, H + rise), (cy, H - 0.01), (cy + 0.25 * L, H - 0.03), (0.0, mid_z - 0.03), (hy - 0.2 * L, Hh - 0.01), (hy + 0.06 * L, Hh)]


def _along(points, y):
    """The height at y along a line of (y, z) points by increasing y."""
    if y <= points[0][0]:
        return points[0][1]
    for (y0, z0), (y1, z1) in zip(points, points[1:]):
        if y <= y1:
            return z0 + (z1 - z0) * (y - y0) / (y1 - y0)
    return points[-1][1]


def _spine_z(s, y):
    return _along(_spine(s), y)


def trunk_sdf(s):
    """
    The trunk and neck: a deep chest tapering back to the hips, the withers (a bear's hump), haunches and
    rump, the brisket low in front, the belly tucked up before the hind legs, a thick neck.
    """
    L, H, Hh, W, depth = s["length"], s["height"], s["hip"], s["width"], s["depth"]
    hy, cy, _ = quad_points(s)
    hump = s.get("hump", 1.0)
    nl, rise = s["neck"]
    neck_end = (0, cy - nl, H + rise)
    wide = W / depth * 0.95
    body = sc.union(
        sc.cone((0, cy + 0.12 * L, H - 0.12 * depth), (0, hy - 0.22 * L, Hh - 0.03), 0.53 * depth, 0.4 * depth).scaled((wide, 1, 1)),
        sc.ellipsoid((0, cy + 0.25 * L, H + depth * (0.1 + 0.35 * (hump - 1))), (0.32 * W, 0.22 * L, 0.3 * depth)),
        sc.ellipsoid((0, hy - 0.18 * L, Hh), (0.44 * W, 0.24 * L, 0.42 * depth)),
        sc.ellipsoid((0, hy - 0.0 * L, Hh + 0.02), (0.36 * W, 0.13 * L, 0.34 * depth)),
        sc.ellipsoid((0, cy + 0.08 * L, H - depth * 0.35), (0.34 * W, 0.14 * L, 0.3 * depth)),
        sc.cone((0, cy + 0.1 * L, H - 0.03), (0, neck_end[1] + 0.03, neck_end[2] - 0.02), 0.38 * depth, 0.3 * depth),
        k=0.12 * depth)
    tuck = sc.ellipsoid((0, hy - 0.34 * L, Hh - depth * 0.78), (0.7 * W, 0.24 * L, 0.26 * depth))
    body = sc.carve(body, (tuck, 0.1 * depth))
    if s.get("ruff"):
        # Clumps of shaggy fur around the neck and over the hump, blended on like clay.
        rng = random.Random(7)
        clumps = []
        for i in range(16):
            a = math.pi * (0.05 + 0.9 * (i % 8) / 7)
            y = cy + (0.02 if i < 8 else 0.16) * L + rng.uniform(-0.03, 0.03)
            z = H + (0.02 if i < 8 else depth * 0.12)
            reach = depth * (0.4 if i < 8 else 0.42)
            c = (math.cos(a) * reach * wide, y, z + math.sin(a) * reach)
            clumps.append(sc.ellipsoid(c, (depth * 0.13, depth * 0.16, depth * 0.11)))
        body = sc.blend(body, *[(c, 0.04 * depth) for c in clumps])
    return body


def _leg_sdf(s, part, top, knee, ankle):
    """A leg: the broad muscle of the shoulder or the ham at the top, the knee, a shin tapering to the ankle."""
    r = s["leg_r"]
    slim = s.get("slim", 0.8)
    top, knee, ankle = Vector(top), Vector(knee), Vector(ankle)
    upper_len = (top - knee).length
    # The muscle sits high on the body's side, so the leg grows out of the flank instead of hanging under it.
    bulk = s.get("bulk", 1.0)
    if part == "Back":
        muscle = sc.ellipsoid(tuple(top + Vector((0, 0.0, -upper_len * 0.38))), (r * 1.55 * bulk, r * 2.4 * bulk, upper_len * 0.62))
        upper = sc.chain([tuple(top), tuple(knee)], [r * 1.5, r * 0.95])
    else:
        muscle = sc.ellipsoid(tuple(top + Vector((0, 0.0, -upper_len * 0.38))), (r * 1.45 * bulk, r * 2.3 * bulk, upper_len * 0.6))
        upper = sc.chain([tuple(top), tuple(knee)], [r * 1.35, r * 0.95])
    lower = sc.chain([tuple(knee), tuple(knee.lerp(ankle, 0.45)), tuple(ankle)], [r * 0.92, r * 0.92 * slim + 0.08 * r, r * slim])
    return sc.blend(upper, (muscle, 0.06), (lower, 0.05))


def _head_sdf(s, base):
    """The head from the neck's end: a boar's long wedge with jowls and a flat snout, or a bear's round skull and muzzle."""
    x0, y0, z0 = base
    l = s["head_len"]
    if s["head"] == "boar":
        return sc.union(
            sc.ellipsoid((0, y0 - 0.06, z0 + 0.02), (0.15, 0.17, 0.16)),
            sc.cone((0, y0 - 0.1, z0 - 0.0), (0, y0 - l + 0.03, z0 - 0.09), 0.13, 0.07),
            sc.ellipsoid((0.1, y0 - 0.13, z0 - 0.08), (0.075, 0.12, 0.08)),
            sc.ellipsoid((-0.1, y0 - 0.13, z0 - 0.08), (0.075, 0.12, 0.08)),
            sc.cone((-0.08, y0 - 0.12, z0 + 0.1), (0.08, y0 - 0.12, z0 + 0.1), 0.05),
            sc.capped((0, y0 - l + 0.04, z0 - 0.09), (0, y0 - l - 0.01, z0 - 0.095), 0.085, 0.08, rounding=0.012),
            k=0.05)
    return sc.union(
        sc.ellipsoid((0, y0 - l * 0.28, z0 + 0.03), (0.22, 0.22, 0.2)),
        sc.cone((-0.12, y0 - l * 0.35, z0 + 0.1), (0.12, y0 - l * 0.35, z0 + 0.1), 0.07),
        sc.cone((0, y0 - l * 0.45, z0 - 0.02), (0, y0 - l * 0.95, z0 - 0.06), 0.13, 0.08),
        sc.ellipsoid((0.15, y0 - l * 0.28, z0 - 0.06), (0.1, 0.11, 0.1)),
        sc.ellipsoid((-0.15, y0 - l * 0.28, z0 - 0.06), (0.1, 0.11, 0.1)),
        sc.sphere((0.17, y0 - l * 0.12, z0 + 0.2), 0.07),
        sc.sphere((-0.17, y0 - l * 0.12, z0 + 0.2), 0.07),
        sc.ellipsoid((0, y0 - l * 0.98, z0 - 0.03), (0.06, 0.04, 0.045)),
        k=0.06)


def sculpted_quad_body(name, s):
    """
    A quadruped sculpted like the heroes: the trunk and neck swept along the spine and blended from
    clay-like masses (barrel, shoulders and hump, haunches), muscled legs that bend at the knee, the
    head with its muzzle, ears and jaw, paws with claws or split hooves, a tail; shaded smoothly, fur
    tufts and bristles on top.
    """
    m = {k: lp.material(k, v) for k, v in s["colors"].items()}
    eye = lp.material("Eye", "#120e0c")
    mb = lp.MeshBuilder(name + "Body")
    L, H, Hh, W, depth = s["length"], s["height"], s["hip"], s["width"], s["depth"]
    hy, cy, legs = quad_points(s)
    nl, rise = s["neck"]
    neck_end = (0, cy - nl, H + rise)
    rng = random.Random(len(name))
    boar = s["head"] == "boar"

    # The trunk and neck, swept along the spine from behind the rump into the head.
    trunk = trunk_sdf(s)
    ys = [hy + 0.08 * L - (hy + 0.08 * L - neck_end[1] + 0.06) * i / 15 for i in range(16)]
    path = [Vector((0, y, _spine_z(s, y))) for y in ys]
    rows = sc.sweep(trunk, path, 18, caps=(True, False), up=(0, 0, 1), limit=1.5)
    weigh = along_y([("Neck", neck_end[1] + nl * 0.35), ("Chest", cy + 0.14 * L), ("Spine", 0.0), ("Hips", hy - 0.26 * L)])

    def trunk_material(c):
        return m["FurLight"] if c.z < _spine_z(s, c.y) - depth * 0.3 and abs(c.x) < W * 0.32 else None
    sc.grid_mesh(mb, rows, m["Fur"], weigh, closed=True, material_at=trunk_material,
                 inside=lambda c: Vector((0, c.y, _spine_z(s, c.y))))
    surface = sc.Star(trunk, (0, 0, (H + Hh) / 2), limit=2.0)

    def top_at(y):
        """The top of the back at y."""
        origin = Vector((0, y, _spine_z(s, y)))
        return origin + Vector((0, 0, surface.reach(origin, (0, 0, 1))))

    # Legs: one sweep from inside the body down to the ankle, bending at the knee.
    for leg_name, (top, knee, ankle, tip) in legs.items():
        part, side = leg_name.split(".")
        upper, lower, paw_bone = part + "UpperLeg." + side, part + "LowerLeg." + side, part + "Paw." + side
        start = Vector((top[0] * 0.85, top[1], top[2] + depth * 0.3))
        knee_v, ankle_v = Vector(knee), Vector((ankle[0], ankle[1], ankle[2] + s["leg_r"] * 0.5))
        shape = _leg_sdf(s, part, start, knee_v, ankle_v)
        leg_path = [start.lerp(knee_v, f) for f in (0.0, 0.25, 0.5, 0.75)] + [knee_v.lerp(ankle_v, f) for f in (0.0, 0.33, 0.66, 1.0)]
        leg_rows = sc.sweep(shape, leg_path, 12, caps=(True, True), up=(0, -1, 0), limit=0.8)
        kz = knee[2]

        def leg_weigh(p, upper=upper, lower=lower, kz=kz):
            t = smooth(kz + 0.04, kz - 0.04, p.z)
            return {upper: 1.0 - t, lower: t}
        pts = leg_path
        sc.grid_mesh(mb, leg_rows, m["Fur"], leg_weigh, closed=True,
                     inside=lambda c, pts=pts: min(pts, key=lambda q: (q - c).length))
        r = s["leg_r"]
        if boar:
            # A split hoof: two rounded toes under the ankle and a dewclaw behind.
            ax, ay, az = ankle
            hoof = sc.union(sc.ellipsoid((ax, ay - r * 0.1, r * 0.75), (r * 0.6, r * 0.6, r * 0.6)),
                            *[sc.cone((ax + k * r * 0.32, ay + r * 0.3, r * 1.0), (ax + k * r * 0.38, ay - r * 0.75, r * 0.3), r * 0.48, r * 0.3)
                              for k in (-1, 1)], k=0.02)
            hoof = sc.intersect(hoof, sc.Shape(lambda q: -q[:, 2]))
            sc.star_mesh(mb, sc.Star(hoof, (ax, ay - r * 0.1, r * 0.5), limit=0.4), m["Dark"], paw_bone, 6, 10)
            for k in (-1, 1):
                mb.tube([((ax + k * r * 0.5, ay + r * 0.6, r * 1.2), r * 0.16), ((ax + k * r * 0.55, ay + r * 0.95, r * 0.75), 0.005)],
                        m["Dark"], paw_bone, sides=4)
        else:
            # A broad padded paw with the toes rounded on top, claws curving down in front.
            ax, ay, az = ankle
            w = r * 1.15
            foot = sc.union(sc.ellipsoid((ax, ay - r * 0.5, r * 0.55), (w, r * 1.3, r * 0.6)),
                            *[sc.sphere((ax + k * w * 0.62, ay - r * 1.45, r * 0.42), r * 0.42) for k in (-1, 0, 1)], k=0.04)
            foot = sc.intersect(foot, sc.Shape(lambda q: -q[:, 2]))
            sc.star_mesh(mb, sc.Star(foot, (ax, ay - r * 0.6, r * 0.45), limit=0.6), m["Fur"], paw_bone, 6, 10)
            for k in (-1, -0.33, 0.33, 1):
                base = Vector((ax + k * w * 0.7, ay - r * 1.75, r * 0.4))
                mb.tube([(base, r * 0.16), (base + Vector((0, -r * 0.45, -r * 0.25)), r * 0.09), (base + Vector((0, -r * 0.65, -r * 0.5)), 0.004)],
                        m["Dark"], paw_bone, sides=4)

    # The head and the jaw.
    head = _head_sdf(s, neck_end)
    x0, y0, z0 = neck_end
    l = s["head_len"]
    center = (0, y0 - l * (0.35 if boar else 0.4), z0 - 0.03)

    def head_material(c):
        if boar:
            return m["Dark"] if c.y < y0 - l + 0.035 else (m["FurLight"] if c.z < z0 - 0.12 else None)
        if c.y < y0 - l * 0.9 and c.z > z0 - 0.09:
            return m["Dark"]
        return m["FurLight"] if c.y < y0 - l * 0.5 else None
    sc.star_mesh(mb, sc.Star(head, center, limit=1.0), m["Fur"], "Head", 16, 24, material_at=head_material)
    jaw_from = Vector((0, y0 - l * 0.25, z0 - (0.13 if boar else 0.14)))
    jaw_to = Vector((0, y0 - l * (0.85 if boar else 0.9), z0 - (0.14 if boar else 0.13)))
    jaw = sc.chain([tuple(jaw_from), tuple(jaw_to)], [0.08 if boar else 0.1, 0.05 if boar else 0.06])
    sc.sweep_mesh(mb, jaw, [jaw_from, jaw_from.lerp(jaw_to, 0.5), jaw_to], 8, m["FurLight"], "Jaw")
    for sx in (1, -1):
        if boar:
            mb.sphere((sx * 0.105, y0 - l * 0.38, z0 + 0.06), 0.022, eye, "Head", segments=6, rings=4, smooth=True)
            # Ears: pointed and leaning out; nostrils in the snout; tusks curling up out of the jaw.
            ear = [(sx * 0.09, y0 - 0.02, z0 + 0.14), (sx * 0.15, y0 + 0.02, z0 + 0.13), (sx * 0.12, y0 - 0.04, z0 + 0.12),
                   (sx * 0.2, y0 + 0.07, z0 + 0.3)]
            mb.hull(ear, m["Fur"], "Head")
            mb.sphere((sx * 0.032, y0 - l - 0.012, z0 - 0.085), 0.018, eye, "Head", segments=5, rings=3)
            tusk = [Vector((sx * 0.07, y0 - l + 0.1, z0 - 0.14)), Vector((sx * 0.12, y0 - l + 0.04, z0 - 0.08)),
                    Vector((sx * 0.13, y0 - l + 0.06, z0 + 0.0))]
            mb.tube([(tusk[0], 0.022), (tusk[1], 0.016), (tusk[2], 0.003)], m["Tusk"], "Jaw", sides=5, smooth=True)
        else:
            mb.sphere((sx * 0.085, y0 - l * 0.55, z0 + 0.07), 0.025, eye, "Head", segments=6, rings=4, smooth=True)
            mb.sphere((sx * 0.17, y0 - l * 0.12 - 0.035, z0 + 0.2), (0.04, 0.02, 0.045), m["Dark"], "Head", segments=6, rings=4, smooth=True)

    # The tail.
    tl, tdrop = s["tail"]
    tr = depth * 0.08
    if boar:
        curl = [(0, hy + 0.04 * L, Hh + 0.02), (0, hy + 0.08 * L + 0.04, Hh - 0.04), (0.03, hy + 0.08 * L + 0.08, Hh - 0.12), (0.0, hy + 0.08 * L + 0.06, Hh - 0.2)]
        mb.tube([(curl[0], 0.03), (curl[1], 0.022)], m["Fur"], "Tail1", sides=5, smooth=True)
        mb.tube([(curl[1], 0.022), (curl[2], 0.016), (curl[3], 0.006)], m["Fur"], "Tail2", sides=5, smooth=True)
        mb.hull([(0, curl[3][1] + dx, curl[3][2] + dz) for dx, dz in ((0.03, 0), (-0.03, 0), (0, -0.08))] + [(0.03, curl[3][1], curl[3][2] - 0.05)],
                m["Dark"], "Tail2")
    else:
        mb.tube([((0, hy + 0.04 * L, Hh), tr * 1.6), ((0, hy + 0.08 * L + tl * 0.6, Hh - tdrop), tr)], m["Fur"], "Tail1", sides=6, smooth=True)

    # Fur on top: a bristly dark ridge down a boar's back, shaggy tufts on a bear's haunches.
    if boar:
        for i in range(12):
            y = cy + 0.02 + (hy - cy) * 0.75 * i / 11
            p = top_at(y) - Vector((0, 0, 0.02))
            bone = "Chest" if y < cy + 0.25 * L else ("Spine" if y < hy - 0.25 * L else "Hips")
            tufts(mb, [p], m["Dark"], bone, rng, size=0.15 - 0.007 * i, sweep=(0, 0.7, 0.7))
    else:
        for i in range(10):
            a = math.pi * (0.15 + 0.7 * i / 9)
            y = hy - 0.1 * L + rng.uniform(-0.06, 0.06)
            origin = Vector((0, y, _spine_z(s, y)))
            d = Vector((math.cos(a), 0, math.sin(a)))
            p = origin + d * (surface.reach(origin, d) - 0.02)
            tufts(mb, [p], m["FurLight"], "Hips", rng, size=0.08, sweep=(math.cos(a) * 0.4, 0.7, 0.2))
    return mb.finish()


# ---------------------------------------------------------------- crocodiles

def _croc_spine(s):
    """The line through the middle of a crocodile's neck, body and tail: (y, z) pairs by increasing y."""
    H, Hh = s["height"], s["hip"]
    hy, cy, _ = quad_points(s)
    nl, rise = s["neck"]
    tl, tdrop = s["tail"]
    return [(cy - nl - 0.08, H + rise), (cy, H), (0.0, (H + Hh) / 2), (hy, Hh), (hy + tl * 0.5, Hh - tdrop * 0.5), (hy + tl, Hh - tdrop)]


def croc_body(name, s):
    """
    A crocodile sculpted like the other beasts: a long low body, broad over the shoulders and hips, running
    into a tall flat tail banded dark; rows of keeled scutes down the back that close into a single crest
    on the tail; sprawling legs with clawed toes, webbed behind; a long flat snout over a narrower jaw,
    their teeth interlocking along the lips, eyes and nostrils raised on knobs. Gloomjaw grows clusters of
    glowing moon-shard crystals out of its back.
    """
    m = {k: lp.material(k, v) for k, v in s["colors"].items()}
    mouth = lp.material("Mouth", "#c8a882")
    eye_mat = lp.material("Eye", "#c8b440")
    pupil = lp.material("Pupil", "#120e0c")
    mb = lp.MeshBuilder(name + "Body")
    L, H, Hh, W, depth = s["length"], s["height"], s["hip"], s["width"], s["depth"]
    hy, cy, legs = quad_points(s)
    nl, rise = s["neck"]
    neck_end = Vector((0, cy - nl, H + rise))
    tl, tdrop = s["tail"]
    spine = _croc_spine(s)

    def spine_z(y):
        return _along(spine, y)

    def half_height(y):
        if y < hy - 0.1:
            return depth * 0.47
        t = min(1.0, (y - hy + 0.1) / (tl + 0.1))
        return depth * 0.5 * (1 - t) + 0.012

    # Neck, body and tail as one shape, swept along the spine from behind the head to the tail's tip.
    wide = W / depth * 0.95
    body = sc.union(
        sc.cone((0, cy + 0.03, H), (0, hy - 0.05, Hh), depth * 0.47).scaled((wide, 1, 1)),
        sc.ellipsoid((0, 0.0, (H + Hh) / 2 - depth * 0.12), (W * 0.45, L * 0.4, depth * 0.42)),
        sc.cone((0, cy + 0.05, H), (neck_end.x, neck_end.y - 0.08, neck_end.z), depth * 0.44, depth * 0.4).scaled((wide * 0.85, 1, 1)),
        sc.cone((0, hy - 0.15, Hh), (0, hy + tl, Hh - tdrop), depth * 0.5, 0.012).scaled((0.78, 1, 1)),
        sc.ellipsoid((0, hy + 0.1, Hh), (W * 0.36, 0.35, depth * 0.45)),
        # Shoulders and hips bulging where the legs come out.
        *[sc.ellipsoid((sx * W * 0.4, y, z - 0.06), (0.11, 0.15, 0.1)) for sx in (1, -1) for y, z in ((cy + 0.1 * L, H), (hy - 0.1 * L, Hh))],
        k=0.08)
    ys = [neck_end.y - 0.07 + (hy + tl - 0.03 + 0.07 - neck_end.y) * (i / 33) ** 1.15 for i in range(34)]
    path = [Vector((0, y, spine_z(y))) for y in ys]
    rows = sc.sweep(body, path, 16, caps=(True, True), up=(0, 0, 1), limit=1.0)
    weigh = along_y([("Neck", neck_end.y + 0.1), ("Chest", cy + 0.1), ("Spine", 0.0), ("Hips", hy - 0.25), ("Tail1", hy + 0.25),
                     ("Tail2", hy + tl * 0.55)])

    def body_material(c):
        below = spine_z(c.y) - c.z
        if below > half_height(c.y) * 0.35 and (abs(c.x) < W * 0.36 or below > half_height(c.y) * 0.7):
            return m["FurLight"]
        if cy < c.y < hy + 0.1 and c.z > spine_z(c.y) + half_height(c.y) * 0.4:
            return m["Band"]  # dark between the plates of the back
        # Dark bands across the tail, two rows wide, so their edges follow the rows.
        i = max(0, bisect.bisect(ys, c.y) - 1)
        if ys[i] > hy + 0.15 and (i // 2) % 2 == 1:
            return m["Band"]
        return None
    sc.grid_mesh(mb, rows, m["Fur"], weigh, closed=True, material_at=body_material,
                 inside=lambda c: Vector((0, min(max(c.y, ys[1]), ys[-2]), spine_z(min(max(c.y, ys[1]), ys[-2])))))

    def surface(x, y):
        """The top of the back at (x, y) and the normal there."""
        star = sc.Star(body, (x, y, spine_z(y)), limit=1.0)
        p = star.center + Vector((0, 0, star.reach(star.center, (0, 0, 1))))
        return p, star.normal(p)

    def scute(x, y, size, height, mat=None):
        """A keeled bony plate: a low rounded base with a ridge along its middle."""
        p, n = surface(x, y)
        side = Vector((1, 0, 0)) - n * n.x
        side.normalize()
        fwd = n.cross(side)
        base = [p - n * 0.01 + side * sx * size * 0.6 + fwd * sy * size for sx in (-1, 1) for sy in (-1, 1)]
        base += [p + side * sx * size * 0.45 + fwd * sy * size * 0.7 + n * height * 0.35 for sx in (-1, 1) for sy in (-1, 1)]
        keel = [p + n * height + fwd * sy * size * 0.45 for sy in (-1, 1)]
        w = weigh(p)
        mb.hull(base + keel, mat or m["Dark"], lambda q, w=w: w)

    # Armor: two big pairs of scutes behind the head, six rows of plates down the back close together, two
    # rows of tall scutes down the tail joining into one crest; bony knobs along the flanks.
    for sx in (1, -1):
        scute(sx * 0.07, neck_end.y + 0.12, 0.05, 0.045)
        scute(sx * 0.07, neck_end.y + 0.24, 0.05, 0.045)
    n_back = 13
    step = (hy - cy - 0.05) / (n_back - 1)
    for k in range(n_back):
        y = cy + 0.1 + step * k
        for x in (-0.2, -0.12, -0.045, 0.045, 0.12, 0.2):
            outer = abs(x) > 0.15
            scute(x * W / 0.6, y, step * 0.46, 0.022 if outer else 0.03, m["Fur"])
        for sx in (1, -1):
            if k % 2 == 0 and 0 < k < n_back - 1:
                p0 = Vector((0, y + step * 0.5, spine_z(y + step * 0.5)))
                d = Vector((sx, 0, 0.35)).normalized()
                p = p0 + d * sc.Star(body, p0, limit=1.0).reach(p0, d)
                mb.hull([p - d * 0.02 + Vector((0, dy, dz)) for dy in (-0.035, 0.035) for dz in (-0.025, 0.025)] + [p + d * 0.022],
                        m["Band"], lambda q, w=weigh(p): w)
    n_tail = 18
    for k in range(n_tail):
        t = k / (n_tail - 1)
        y = hy + 0.15 + (tl - 0.25) * t
        size = 0.05 * (1 - t * 0.55)
        if t < 0.45:
            for sx in (1, -1):
                scute(sx * 0.055 * (1 - t / 0.45 * 0.7), y, size, size * (1.4 + t))
        else:
            scute(0.0, y, size, size * 1.8)

    # The upper jaw: a broad skull with the cheeks, eyes and nostrils raised on knobs, a long flat snout
    # with the lip bulging over the big teeth; its flat palate is the mouth's roof.
    l = s["head_len"]
    x0, y0, z0 = neck_end
    lip = z0 - 0.07
    tip = y0 - l
    upper = sc.union(
        sc.ellipsoid((0, y0 - 0.09, z0 + 0.01), (0.19, 0.16, 0.085)),
        sc.cone((0, y0 - 0.15, z0 - 0.02), (0, tip + 0.06, lip + 0.025), 0.07, 0.045).scaled((2.3, 1, 1)),
        sc.ellipsoid((0, tip + 0.05, lip + 0.05), (0.065, 0.055, 0.042)),
        *[sc.ellipsoid((sx * 0.09, y0 - 0.1, z0 + 0.075), (0.05, 0.065, 0.04)) for sx in (1, -1)],
        *[sc.ellipsoid((sx * 0.15, y0 - 0.06, z0 - 0.03), (0.07, 0.1, 0.06)) for sx in (1, -1)],
        *[sc.ellipsoid((sx * 0.12, y0 - 0.32, lip + 0.02), (0.04, 0.09, 0.035)) for sx in (1, -1)],
        *[sc.ellipsoid((sx * 0.085, tip + 0.17, lip + 0.02), (0.035, 0.06, 0.03)) for sx in (1, -1)],
        k=0.04)
    upper = sc.intersect(upper, sc.Shape(lambda q: lip - q[:, 2]), k=0.008)
    head_path = [Vector((0, y, z0 + (lip + 0.03 - z0) * (y0 - y) / l)) for y in (y0 + 0.02, y0 - 0.1, y0 - 0.25, y0 - 0.4, y0 - 0.55, y0 - 0.7, tip + 0.04)]
    sc.sweep_mesh(mb, upper, head_path, 16, m["Fur"], "Head",
                  material_at=lambda c: mouth if c.z < lip + 0.006 else None)
    # The lower jaw: narrower, with the throat behind it; its flat top is the mouth's floor.
    lower = sc.union(
        sc.cone((0, y0 - 0.06, lip - 0.03), (0, tip + 0.05, lip - 0.012), 0.055, 0.032).scaled((2.7, 1, 1)),
        sc.ellipsoid((0, y0 - 0.02, lip - 0.03), (0.16, 0.13, 0.07)),
        sc.ellipsoid((0, tip + 0.04, lip - 0.012), (0.06, 0.04, 0.025)),
        k=0.04)
    lower = sc.intersect(lower, sc.Shape(lambda q: q[:, 2] - lip), k=0.006)
    jaw_path = [Vector((0, y, lip - 0.025 + 0.012 * (y0 - y) / l)) for y in (y0 + 0.02, y0 - 0.15, y0 - 0.35, y0 - 0.55, tip + 0.05)]

    def jaw_material(c):
        if c.z > lip - 0.006:
            return mouth
        return m["FurLight"] if c.z < lip - 0.04 or abs(c.x) < 0.05 else None
    sc.sweep_mesh(mb, lower, jaw_path, 14, m["Fur"], "Jaw", material_at=jaw_material)

    # Teeth along both lips, the lower ones fitting between the upper; a big lower fang outside the snout.
    head_star = sc.Star(upper, (0, y0 - 0.2, lip + 0.03), limit=1.2)
    jaw_star = sc.Star(lower, (0, y0 - 0.2, lip - 0.02), limit=1.2)
    for sx in (1, -1):
        for k in range(10):
            y = y0 - 0.16 - (l - 0.22) * k / 9
            o = Vector((0, y, lip + 0.012))
            rim = o + Vector((sx, 0, 0)) * (head_star.reach(o, (sx, 0, 0)) - 0.012)
            mb.tube([(rim, 0.011), (rim + Vector((sx * 0.004, 0, -0.035)), 0.002)], m["Tusk"], "Head", sides=4)
            y2 = y - (l - 0.22) / 18
            o2 = Vector((0, y2, lip - 0.01))
            rim2 = o2 + Vector((sx, 0, 0)) * (jaw_star.reach(o2, (sx, 0, 0)) - 0.01)
            mb.tube([(rim2, 0.01), (rim2 + Vector((sx * 0.006, 0, 0.032)), 0.002)], m["Tusk"], "Jaw", sides=4)
        o = Vector((0, tip + 0.2, lip - 0.01))
        fang = o + Vector((sx, 0, 0)) * (jaw_star.reach(o, (sx, 0, 0)) - 0.005)
        mb.tube([(fang, 0.014), (fang + Vector((sx * 0.012, 0, 0.055)), 0.002)], m["Tusk"], "Jaw", sides=4)
        # An eye on its knob: yellow, a slit pupil; a nostril on the snout's knob.
        eye = Vector((sx * 0.105, y0 - 0.115, z0 + 0.085))
        mb.sphere(tuple(eye), 0.026, eye_mat, "Head", segments=8, rings=5, smooth=True)
        look = Vector((sx * 0.8, -0.6, 0.15)).normalized()
        mb.sphere(tuple(eye + look * 0.024), (0.005, 0.006, 0.018), pupil, "Head", segments=6, rings=4, smooth=True,
                  rotation=(0, 0, math.degrees(math.atan2(look.x, -look.y))))
        mb.sphere((sx * 0.025, tip + 0.045, lip + 0.09), 0.012, pupil, "Head", segments=5, rings=3)

    # Legs sprawling out of the flanks: the upper leg out to the elbow or knee, the lower leg down to the
    # foot; toes splayed with dark claws, webbed between them on the hind feet.
    for leg_name, (top, knee, ankle, toe) in legs.items():
        part, side = leg_name.split(".")
        sx = 1 if side == "L" else -1
        back = part == "Back"
        r = s["leg_r"]
        upper_bone, lower_bone, paw_bone = part + "UpperLeg." + side, part + "LowerLeg." + side, part + "Paw." + side
        start = Vector((top[0] * 0.6, top[1], top[2] + 0.06))
        knee_v, ankle_v = Vector(knee), Vector((ankle[0], ankle[1], ankle[2] + 0.02))
        thigh = sc.blend(sc.chain([tuple(start), tuple(knee_v)], [r * (1.7 if back else 1.5), r * 1.0]),
                         (sc.ellipsoid(tuple(start.lerp(knee_v, 0.4)), (r * 1.6, r * (1.9 if back else 1.5), r * 1.3)), 0.03))
        sc.sweep_mesh(mb, thigh, [start.lerp(knee_v, f) for f in (0.0, 0.5, 1.0)], 7, m["Fur"], upper_bone)
        shin = sc.chain([tuple(knee_v), tuple(knee_v.lerp(ankle_v, 0.5)), tuple(ankle_v)], [r * 1.0, r * 0.85, r * 0.75])
        sc.sweep_mesh(mb, shin, [knee_v, ankle_v], 6, m["Fur"], lower_bone)
        ax, ay, _ = ankle
        toes = 4 if back else 5
        spread = (-55, 25) if back else (-60, 50)
        heel = Vector((ax, ay + 0.02, 0.03))
        palm = sc.union(sc.ellipsoid((ax, ay - 0.03, 0.035), (r * 0.9, r * 1.2, 0.035)), sc.sphere(tuple(heel), 0.035), k=0.02)
        palm = sc.intersect(palm, sc.Shape(lambda q: -q[:, 2]))
        sc.star_mesh(mb, sc.Star(palm, (ax, ay - 0.02, 0.025), limit=0.3), m["Fur"], paw_bone, 4, 8)
        tips = []
        for k in range(toes):
            a = math.radians(spread[0] + (spread[1] - spread[0]) * k / (toes - 1))
            length = (0.13 if back else 0.1) * (1.0 - 0.25 * abs(k - (toes - 1) / 2) / ((toes - 1) / 2))
            base = Vector((ax, ay - 0.04, 0.025))
            d = Vector((sx * math.sin(a), -math.cos(a), 0))
            end = base + d * length + Vector((0, 0, -0.012))
            tips.append(end)
            mb.tube([(base, 0.018), (end, 0.01)], m["Fur"], paw_bone, sides=4, smooth=True)
            mb.tube([(end, 0.009), (end + d * 0.03 + Vector((0, 0, -0.012)), 0.002)], m["Dark"], paw_bone, sides=3)
        if back:
            root = Vector((ax, ay - 0.04, 0.02))
            for a_, b_ in zip(tips, tips[1:]):
                web = [root, root.lerp(a_, 0.85), root.lerp((a_ + b_) / 2, 0.6), root.lerp(b_, 0.85)]
                mb.add(web, [(0, 1, 2), (0, 2, 3)], m["Band"], paw_bone, double_sided=True)

    if s.get("crystals"):
        # Clusters of six-sided crystals breaking out of the back and the tail, their points brighter.
        glow = lp.material("CrystalGlow", "#30c8ec", emission=2.0)
        tip_mat = lp.material("CrystalGlowTip", "#9af0ff", emission=2.5)
        rng = random.Random(4)

        def crystal(base, axis, height, radius):
            axis = axis.normalized()
            u = axis.cross(Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))).normalized()
            v = axis.cross(u)
            twist = rng.uniform(0, math.pi)
            verts = []
            for h, rr in ((-0.08, radius), (height * 0.75, radius * 0.92)):
                for i in range(6):
                    a = twist + 2 * math.pi * i / 6
                    verts.append(base + axis * h + (u * math.cos(a) + v * math.sin(a)) * rr)
            verts.append(base + axis * height)
            side_faces = [(i, (i + 1) % 6, 6 + (i + 1) % 6, 6 + i) for i in range(6)]
            tip_faces = [(6 + i, 6 + (i + 1) % 6, 12) for i in range(6)]
            w = weigh(base)
            mb.add_faces(verts, {glow: side_faces, tip_mat: tip_faces}, lambda q, w=w: w, closed=True)

        # Each cluster: one big crystal, smaller ones fanning out around its foot.
        for x, y, scale in ((0.04, cy + 0.15, 0.8), (-0.08, cy + 0.55, 1.0), (0.1, 0.0, 0.85), (-0.05, hy - 0.2, 1.0), (0.03, hy + 0.5, 0.65),
                            (-0.02, hy + 0.95, 0.45)):
            p, n = surface(x, y)
            crystal(p, n + Vector((x * 2.0, rng.uniform(-0.15, 0.15), 0)), rng.uniform(0.5, 0.6) * scale, 0.07 * scale)
            for k in range(4):
                a = 2 * math.pi * k / 4 + rng.uniform(-0.4, 0.4)
                out = Vector((math.cos(a), math.sin(a), 0))
                q, nq = surface(x + out.x * 0.07 * scale, y + out.y * 0.09 * scale)
                crystal(q, nq + out * 0.9, rng.uniform(0.15, 0.3) * scale, rng.uniform(0.03, 0.045) * scale)
    return mb.finish()


def build_quad(name):
    s = QUADS[name]
    arm = lp.armature("Armature", quad_skeleton(s))
    lp.bind(croc_body(name, s) if s["head"] == "croc" else sculpted_quad_body(name, s), arm)
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


def hairs(mb, star, count, mat, bone, rng, length, where, sweep=(0, 0.6, 0.3)):
    """Stiff bristles standing out of a star's surface, leaning along `sweep`, where `where(direction)` allows."""
    dirs = []
    while len(dirs) < count:
        d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
        if where(d):
            dirs.append(d)
    pts = star.points(dirs)
    for p, n in zip(pts, star.normals(pts)):
        tip = p + (n + Vector(sweep)).normalized() * length * rng.uniform(0.7, 1.2)
        mb.tube([(p - n * 0.004, length * 0.12), (tip, 0.002)], mat, bone, sides=3)


def spider_body():
    """
    A big forest spider: a ridged carapace raised into the head, eight glowing eyes in two rows, swollen
    jaws with curved fangs and two feelers; a narrow waist; a swollen bristly abdomen with a red
    hourglass; legs jointed at hip and knee, thickest at the thigh, banded pale at every joint.
    """
    body = lp.material("Chitin", "#2e2b33")
    light = lp.material("ChitinLight", "#57505e")
    mark = lp.material("Marking", "#a8332a")
    eyes = lp.material("EyesGlow", "#ff5a3a", emission=1.5)
    fang = lp.material("Fang", "#1a1418")
    rng = random.Random(13)
    mb = lp.MeshBuilder("SpiderBody")

    # The carapace: a flat oval raised into the head in front, bulging over the hips of the legs, a pit on
    # top with paler grooves running out from it.
    thorax = sc.union(
        sc.ellipsoid((0, -0.06, 0.52), (0.21, 0.26, 0.11)),
        sc.ellipsoid((0, -0.25, 0.57), (0.13, 0.12, 0.09)),
        *[sc.sphere((sx * 0.17, y, 0.5), 0.05) for y, _ in SPIDER_LEGS for sx in (1, -1)],
        sc.cone((0, 0.12, 0.53), (0, 0.26, 0.56), 0.055, 0.06),
        k=0.04)
    thorax = sc.carve(thorax, (sc.ellipsoid((0, 0.0, 0.64), (0.035, 0.05, 0.03)), 0.02))
    t_center = Vector((0, -0.08, 0.52))
    t_star = sc.Star(thorax, t_center, limit=0.6)

    def thorax_material(c):
        if c.z < 0.49:
            return light
        a = math.atan2(c.x, c.y)
        return light if c.z > 0.56 and c.y > -0.22 and math.cos(6 * a) > 0.8 else None
    sc.grid_mesh(mb, t_star.grid(14, 24), body, "Body", closed=True, material_at=thorax_material, inside=lambda c: t_center)

    # Eyes: a big pair in front, a smaller pair beside them, four more in a row on top.
    for sx in (1, -1):
        for d, r in (((sx * 0.25, -1.0, 0.45), 0.034), ((sx * 0.6, -1.0, 0.42), 0.024), ((sx * 0.22, -0.7, 0.9), 0.022), ((sx * 0.55, -0.6, 0.75), 0.02)):
            p = t_star.points([d])[0]
            mb.sphere(tuple(p), r, eyes, "Body", segments=8, rings=5, smooth=True)
        # A swollen jaw hanging under the head with a fang curving in under it, and a short feeler beside it.
        jaw = [Vector((sx * 0.05, -0.3, 0.51)), Vector((sx * 0.055, -0.38, 0.45)), Vector((sx * 0.045, -0.4, 0.39))]
        sc.sweep_mesh(mb, sc.chain([tuple(q) for q in jaw], [0.052, 0.046, 0.034]), jaw, 8, body, "Fangs",
                      material_at=lambda c: light if c.y < -0.41 else None)
        mb.tube([((sx * 0.045, -0.41, 0.38), 0.018), ((sx * 0.03, -0.43, 0.33), 0.011), ((sx * 0.0, -0.41, 0.3), 0.002)], fang, "Fangs", sides=5, smooth=True)
        palp = [Vector((sx * 0.1, -0.3, 0.48)), Vector((sx * 0.16, -0.43, 0.46)), Vector((sx * 0.17, -0.5, 0.36)), Vector((sx * 0.15, -0.5, 0.29))]
        sc.sweep_mesh(mb, sc.chain([tuple(q) for q in palp], [0.022, 0.02, 0.018, 0.022]), palp, 6, body, "Body",
                      material_at=lambda c: light if c.z < 0.31 else None)

    # The abdomen: swollen, tapering behind to the spinnerets, a red hourglass on its back, bristles all over.
    abdomen = sc.union(
        sc.ellipsoid((0, 0.47, 0.66), (0.33, 0.4, 0.3)),
        sc.ellipsoid((0, 0.72, 0.6), (0.2, 0.18, 0.19)),
        sc.cone((0, 0.24, 0.56), (0, 0.32, 0.6), 0.06, 0.12),
        sc.cone((0, 0.84, 0.53), (0, 0.9, 0.5), 0.05, 0.025),
        k=0.06)
    abdomen = sc.lumpy(abdomen, 0.008, 0.18, 13)
    a_center = Vector((0, 0.48, 0.65))
    a_star = sc.Star(abdomen, a_center, limit=0.8)
    sc.grid_mesh(mb, a_star.grid(16, 26), body, "Abdomen", closed=True, inside=lambda c: a_center,
                 material_at=lambda c: light if c.z < 0.45 or (c.z < 0.6 and abs(c.x) < 0.12) else None)
    # The hourglass, drawn on a plane above the back and dropped onto it.
    top = Vector((0, 0.44, 1.2))
    hourglass = [(0.17, -0.22), (0.0, -0.17), (-0.17, -0.22), (-0.035, -0.035), (-0.035, 0.035), (-0.17, 0.22), (0.0, 0.17), (0.17, 0.22),
                 (0.035, 0.035), (0.035, -0.035)]
    sc.decal(mb, a_star, top - a_center, [top + Vector((x, y, 0)) - a_center for x, y in hourglass], mark, "Abdomen", lift=0.004)
    hairs(mb, a_star, 70, light, "Abdomen", rng, 0.085, lambda d: d.z > -0.3 and abs(d.x) > 0.25 or d.y > 0.35 and d.z > -0.3)

    # Legs: the thigh arching up from the hip to the knee, the shin down to the foot; a pale band at the
    # knee and the ankle, bristles along the shin.
    for i, (y, sweep_deg) in enumerate(SPIDER_LEGS, start=1):
        ang = math.radians(sweep_deg)
        for side, sx in (("L", 1), ("R", -1)):
            root = Vector((sx * 0.16, y, 0.5))
            knee = Vector((sx * (0.16 + 0.42 * math.cos(ang)), y + 0.42 * math.sin(ang), 0.85))
            foot = Vector((sx * (0.16 + 0.95 * math.cos(ang)), y + 0.95 * math.sin(ang), 0.02))
            upper, lower = "LegUpper%d.%s" % (i, side), "LegLower%d.%s" % (i, side)
            hip = root.lerp(knee, 0.12)
            thigh = sc.blend(sc.chain([tuple(root), tuple(root.lerp(knee, 0.45)), tuple(knee)], [0.045, 0.05, 0.036]),
                             (sc.sphere(tuple(hip), 0.05), 0.02), (sc.sphere(tuple(knee), 0.045), 0.015))
            sc.sweep_mesh(mb, thigh, [root, root.lerp(knee, 0.5), knee], 6, body, upper,
                          material_at=lambda c, knee=knee: light if (c - knee).length < 0.05 else None)
            stations = (0.0, 0.08, 0.4, 0.47, 0.75, 0.82, 1.0)
            path = [knee.lerp(foot, f) for f in stations]
            radii = [0.038, 0.034, 0.028, 0.03, 0.02, 0.019, 0.007]
            shin = sc.chain([tuple(q) for q in path], radii)

            def shin_material(c, knee=knee, foot=foot):
                f = (c - knee).dot((foot - knee).normalized()) / (foot - knee).length
                return light if 0.4 < f < 0.47 or 0.75 < f < 0.82 else None
            sc.sweep_mesh(mb, shin, path, 5, body, lower, material_at=shin_material)
            for k in range(3):
                p = knee.lerp(foot, 0.12 + 0.12 * k)
                out = Vector((sx, 0, 0.4)).normalized()
                mb.tube([(p, 0.008), (p + (out + Vector((0, 0, -0.3))) * 0.06, 0.002)], light, lower, sides=3)
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
    glow = lp.material("CrystalGlow", "#30c8ec", emission=2.5)
    core = lp.material("CoreGlow", "#d8ffff", emission=4.0)
    rng = random.Random(21)
    mb = lp.MeshBuilder("ShardlingBody")

    def shard(base, tip, width, mat, bone, facets=5):
        """A crystal: a prism from `base` widening a little, then to a point at `tip`."""
        base, tip = Vector(base), Vector(tip)
        axis = (tip - base).normalized()
        ref = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((0, 1, 0))
        u = axis.cross(ref).normalized()
        v = axis.cross(u)
        shoulder = base + (tip - base) * 0.7
        pts = [tip]
        for k in range(facets):
            t = 2 * math.pi * k / facets
            d = u * math.cos(t) + v * math.sin(t)
            pts += [base + d * width * 0.8, shoulder + d * width]
        mb.hull(pts, mat, bone)

    def chunk(center, radii, mat, bone, seed, tilt=(0, 0, 0), slab=True):
        """A broken block of rock (or a round stone): chipped and lumpy, shaded flat."""
        base = sc.box((0, 0, 0), radii, radius=min(radii) * 0.45) if slab else sc.ellipsoid((0, 0, 0), radii)
        shape = sc.lumpy(base, min(radii) * 0.18, max(radii) * 1.1, seed)
        rows = sc.Star(shape, (0, 0, 0), limit=1.0).grid(7, 10)
        rows = [sc.rotate_points(row, (0, 0, 0), tilt) for row in rows]
        rows = [[p + Vector(center) for p in row] for row in rows]
        sc.grid_mesh(mb, rows, mat, bone, closed=True, smooth=False, inside=lambda p: Vector(center))

    # The heart: a bright core crystal in a cage of rock slabs, open at the front.
    shard((0, 0, 0.85), (0, -0.02, 1.25), 0.11, core, "Body", facets=6)
    shard((0, 0, 1.0), (0, -0.02, 0.62), 0.09, core, "Body", facets=6)
    for k in range(8):
        t = 2 * math.pi * k / 8
        if math.cos(t) > 0.5:
            continue  # open at the front (t = 0 is -Y)
        out = Vector((math.sin(t), -math.cos(t), 0))
        c = Vector((0, 0, 1.0)) + out * 0.31
        chunk(c, (0.06, 0.085, 0.24), rock if k % 2 else rock2, "Body", 30 + k, tilt=(rng.uniform(-8, 8), rng.uniform(-8, 8), math.degrees(t)))
    # Shoulders of rock with crystals breaking out, and a crown of crystals for a head over a stone brow.
    for sx in (1, -1):
        chunk((sx * 0.38, -0.02, 1.3), (0.15, 0.13, 0.11), rock, "Body", 40 + sx, tilt=(0, sx * 15, 0))
        for k in range(3):
            shard((sx * (0.32 + 0.06 * k), 0.02 * k, 1.38), (sx * (0.42 + 0.12 * k), 0.1 + 0.08 * k, 1.7 + 0.12 * (1 - k)), 0.05, glow, "Body")
    chunk((0, -0.02, 1.47), (0.14, 0.12, 0.08), rock2, "Head", 50)
    mb.box((0, -0.15, 1.47), (0.18, 0.03, 0.035), core, "Head")
    for k in range(5):
        t = math.pi * (0.15 + 0.7 * k / 4)
        shard((math.cos(t) * 0.08, 0.02, 1.52), (math.cos(t) * 0.22, 0.06, 1.72 + 0.12 * math.sin(t)), 0.04, glow, "Head")
    # Arms: chunks of rock hanging below the shoulders, crystal fists.
    for sx in (1, -1):
        side = "L" if sx > 0 else "R"
        chunk((sx * 0.53, 0.0, 1.06), (0.08, 0.08, 0.15), rock2, "ArmUpper." + side, 60 + sx, tilt=(0, sx * -30, 0))
        chunk((sx * 0.65, -0.05, 0.74), (0.075, 0.075, 0.13), rock, "ArmLower." + side, 70 + sx, tilt=(10, sx * -8, 0))
        for k in range(3):
            shard((sx * 0.66, -0.1 + 0.06 * k, 0.6), (sx * (0.66 + 0.04 * (k - 1)), -0.2 + 0.08 * k, 0.4), 0.04, glow, "ArmLower." + side)
    # Below: a tail of shards tapering to a point over the ground, small stones floating around it.
    shard((0, 0, 0.7), (0, 0.02, 0.12), 0.15, glow, "Base", facets=6)
    for k in range(3):
        t = 2 * math.pi * k / 3 + 0.4
        shard((math.cos(t) * 0.12, math.sin(t) * 0.12, 0.72), (math.cos(t) * 0.22, math.sin(t) * 0.22, 0.35), 0.06, rock, "Base")
    for k in range(4):
        t = 2 * math.pi * k / 4 + 0.8
        chunk((math.cos(t) * 0.42, math.sin(t) * 0.42, 0.55 + rng.uniform(-0.05, 0.05)), (0.06, 0.055, 0.05), rock2, "Base", 80 + k, slab=False)
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
