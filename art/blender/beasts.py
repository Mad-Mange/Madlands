"""
More creatures for Hollowmere: one parametric quadruped (boar, bear, crocodile and the world boss
Gloomjaw), a spider and a floating crystal elemental (shardling). Same clip names and rotation
conventions as creatures.py.
"""

import math
import random

import lowpoly as lp


def cycle(t, phase=0.0):
    return math.sin(2 * math.pi * (t + phase))


# ---------------------------------------------------------------- quadrupeds

QUADS = {
    "Boar": dict(
        height=0.74, hip=0.7, length=0.95, width=0.44, depth=0.5, leg_x=0.15, leg_r=0.07, sprawl=0.0,
        neck=(0.14, 0.0), head_len=0.42, head_drop=0.12, jaw_drop=0.1, tail=(0.22, 0.12), head="boar",
        colors=dict(Fur="#5b4436", FurLight="#7d6553", Dark="#2a2420", Tusk="#ebe2c8"),
        walk=(18, 30, 2, 0.02), run=(30, 55, 6, 0.05), walk_speed=1.1, run_speed=3.8,
    ),
    "Bear": dict(
        height=1.2, hip=1.1, length=1.35, width=0.72, depth=0.78, leg_x=0.26, leg_r=0.13, sprawl=0.0,
        neck=(0.22, 0.05), head_len=0.5, head_drop=0.12, jaw_drop=0.14, tail=(0.12, 0.05), head="bear",
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
    m = {k: lp.material(k, v) for k, v in s["colors"].items()}
    mb = lp.MeshBuilder(name + "Body")
    L, H, Hh, W, depth = s["length"], s["height"], s["hip"], s["width"], s["depth"]
    hy, cy, legs = quad_points(s)
    mb.box((0, hy - L * 0.2, Hh), (W * 0.9, L * 0.45, depth * 0.9), m["Fur"], "Hips", top=(0.95, 0.9))
    mb.box((0, 0, (H + Hh) / 2), (W, L * 0.5, depth), m["Fur"], "Spine", top=(1.0, 1.0))
    mb.box((0, 0, (H + Hh) / 2 - depth * 0.48), (W * 0.75, L * 0.45, depth * 0.08), m["FurLight"], "Spine")
    mb.box((0, cy + L * 0.15, H - depth * 0.05), (W * 1.05, L * 0.4, depth * 1.05), m["Fur"], "Chest", top=(0.95, 0.9))
    nl, rise = s["neck"]
    mb.frustum((0, cy + 0.05, H), (0, cy - nl, H + rise), depth * 0.45, depth * 0.38, m["Fur"], "Neck", sides=6)
    HEADS[s["head"]](mb, m, s, (0, cy - nl, H + rise))
    for leg_name, (top, knee, ankle, tip) in legs.items():
        leg, side = leg_name.split(".")
        r = s["leg_r"] * (1.15 if leg == "Back" else 1.0)
        mb.frustum((top[0], top[1], top[2] + 0.05), knee, r, r * 0.8, m["Fur"], leg + "UpperLeg." + side, sides=5)
        mb.frustum(knee, ankle, r * 0.75, r * 0.65, m["Fur"], leg + "LowerLeg." + side, sides=5)
        mb.box(((ankle[0] + tip[0]) / 2, (ankle[1] + tip[1]) / 2, 0.035), (r * 1.6, r * 2.2 + 0.05, 0.07), m["Dark"], leg + "Paw." + side, top=(0.9, 0.8))
    tl, tdrop = s["tail"]
    tr = depth * (0.3 if tl > 1 else 0.08)
    mb.frustum((0, hy - 0.05, Hh), (0, hy + tl * 0.5, Hh - tdrop * 0.5), tr, tr * 0.7, m["Fur"], "Tail1", sides=5)
    mb.frustum((0, hy + tl * 0.5, Hh - tdrop * 0.5), (0, hy + tl, Hh - tdrop), tr * 0.7, 0.02, m["Fur"], "Tail2", sides=5)
    if s.get("crystals"):
        glow = lp.material("CrystalGlow", "#7ff2ff", emission=2.0)
        rng = random.Random(4)
        spots = [("Hips", hy - L * 0.2, Hh), ("Spine", 0.0, (H + Hh) / 2), ("Chest", cy + L * 0.15, H), ("Tail1", hy + tl * 0.25, Hh - tdrop * 0.2)]
        for bone, y, z in spots:
            for _ in range(3):
                x = rng.uniform(-W * 0.3, W * 0.3)
                yy = y + rng.uniform(-0.15, 0.15)
                top = z + depth * 0.5
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
    body = lp.material("Chitin", "#2e2b33")
    light = lp.material("ChitinLight", "#57505e")
    mark = lp.material("Marking", "#a8332a")
    eyes = lp.material("EyesGlow", "#ff5a3a", emission=1.5)
    mb = lp.MeshBuilder("SpiderBody")
    mb.sphere((0, -0.1, 0.5), (0.24, 0.3, 0.17), body, "Body", segments=8, rings=5)
    mb.sphere((0, 0.48, 0.62), (0.36, 0.45, 0.3), body, "Abdomen", segments=8, rings=6)
    mb.hull([(0.09, 0.4, 0.92), (-0.09, 0.4, 0.92), (0, 0.62, 0.9), (0, 0.3, 0.88), (0, 0.48, 0.95)], mark, "Abdomen")
    for sx in (1, -1):
        mb.box((sx * 0.07, -0.38, 0.56), (0.05, 0.03, 0.04), eyes, "Body")
        mb.box((sx * 0.12, -0.34, 0.6), (0.035, 0.03, 0.03), eyes, "Body")
        mb.hull([(sx * 0.05, -0.3, 0.45), (sx * 0.09, -0.3, 0.45), (sx * 0.07, -0.33, 0.48), (sx * 0.05, -0.4, 0.3)], light, "Fangs")
    for i, (y, sweep) in enumerate(SPIDER_LEGS, start=1):
        a = math.radians(sweep)
        for side, sx in (("L", 1), ("R", -1)):
            root = (sx * 0.16, y, 0.5)
            knee = (sx * (0.16 + 0.42 * math.cos(a)), y + 0.42 * math.sin(a), 0.85)
            foot = (sx * (0.16 + 0.95 * math.cos(a)), y + 0.95 * math.sin(a), 0.02)
            mb.frustum(root, knee, 0.045, 0.035, body, "LegUpper%d.%s" % (i, side), sides=4)
            mb.frustum(knee, foot, 0.035, 0.012, light, "LegLower%d.%s" % (i, side), sides=4)
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
