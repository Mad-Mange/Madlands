"""
Humanoid races and the shared humanoid animation set.

Every race uses the same bone names (Unity's humanoid names, so Unity maps the avatar by itself) in a
T-pose. Animations are made once on the Brightvale Human skeleton and exported on their own
(HumanoidAnimations.fbx); Unity retargets them to every race.

A race's model is several meshes on one armature: the body (<Race>Body), the hair on the scalp
(<Race>Hair), the beard (<Race>Beard) and the gear pieces (gear.py), each in three levels of detail
(_LOD0 .. _LOD2: the same shapes sampled coarser, small parts left out).

Material names are the gear regions the client tints: Skin, Hair (the hair on the scalp, hidden under
helmets and hoods), Beard (beard and brows, tinted with the hair), Eyes, Chest (torso and sleeves), Arms
(forearms), Hands, Waist, Legs (hips and thighs: hidden under robes) with LegsLower (knees and shins) and
Feet. A region's "Trim" material (ChestTrim, FeetTrim ...) is its darker second tone: collars, cuffs, soles.
BuckleMetal keeps its color.

The style is stylized and heroic, after classic fantasy MMOs: broad shoulders from big deltoids, a
tapering torso, chunky fists and boots, a strong brow, a square jaw. Heads, hands, hair and beards are
sculpted like clay (sculpt.py) and shaded smoothly; cloth and limbs are lofts through cross-sections.
"""

import math

import numpy as np
from mathutils import Vector

import lowpoly as lp
import sculpt as sc

# Joint heights and widths in meters. Z up, the model faces -Y, +X is its left.
# Body cross-sections are (z, width, depth[, front, back]), bottom to top (see lp.ring).
RACES = {
    "BrightvaleHuman": dict(
        ankle=0.09, knee=0.5, hip=0.93, spine=1.06, chest=1.24, neck=1.47, head=1.57, top=1.84,
        leg_x=0.105, shoulder_z=1.43, clavicle_x=0.05, shoulder_x=0.215, elbow_x=0.48, wrist_x=0.72, hand=0.14,
        foot=(0.13, 0.27, 0.1),
        pelvis=[(0.83, 0.22, 0.17), (0.91, 0.31, 0.215), (0.99, 0.31, 0.215), (1.05, 0.29, 0.205)],
        belly=[(0.99, 0.29, 0.205), (1.11, 0.30, 0.215), (1.22, 0.31, 0.225)],
        torso=[(1.16, 0.31, 0.22), (1.26, 0.345, 0.24, 1.12), (1.36, 0.36, 0.24, 1.08), (1.445, 0.33, 0.21), (1.505, 0.2, 0.15)],
        collar=[(1.47, 0.215, 0.17), (1.52, 0.165, 0.135)],
        tunic=[(0.89, 0.47, 0.3), (1.0, 0.315, 0.225)],
        belt=[(0.995, 0.325, 0.235), (1.065, 0.32, 0.23)],
        buckle=(0.06, 0.05),
        neck_r=0.077, deltoid=0.074, upper_arm=(0.07, 0.076, 0.058), forearm=(0.057, 0.064, 0.05),
        thigh=(0.108, 0.1, 0.075), shin=(0.072, 0.08, 0.06), boot=(0.077, 0.093), boot_top=0.31,
        head_shape=dict(w=1.03, d=1.0, h=0.97, nose=0.9, brow=1.1, ear=0.95, jaw=1.06, eye=1.05),
        hair="swept", beard="short",
        colors=dict(Skin="#e3b08a", Eyes="#1d1b22", EyeWhite="#e6dccb", Chest="#8c6e4b", ChestTrim="#5e4832", Arms="#6e5238",
                    ArmsTrim="#4a3624", Hands="#e3b08a", Waist="#4b3424", WaistTrim="#33241a", BuckleMetal="#c9a24a",
                    Legs="#4e5868", LegsLower="#4e5868", Feet="#3e2c20", FeetTrim="#2a1e16", Beard="#5b3920", Hair="#5b3920"),
    ),
    # Stocky mountain folk: short legs, a barrel chest, huge fists and a braided beard.
    "Korrin": dict(
        ankle=0.08, knee=0.35, hip=0.66, spine=0.78, chest=0.95, neck=1.15, head=1.22, top=1.5,
        leg_x=0.13, shoulder_z=1.1, clavicle_x=0.06, shoulder_x=0.28, elbow_x=0.53, wrist_x=0.75, hand=0.155,
        foot=(0.16, 0.29, 0.11),
        pelvis=[(0.56, 0.3, 0.22), (0.63, 0.42, 0.29), (0.72, 0.44, 0.31), (0.79, 0.42, 0.31)],
        belly=[(0.74, 0.44, 0.31), (0.84, 0.47, 0.34, 1.15), (0.96, 0.46, 0.34)],
        torso=[(0.92, 0.44, 0.33), (1.0, 0.45, 0.35, 1.1), (1.07, 0.42, 0.32), (1.13, 0.34, 0.26), (1.18, 0.2, 0.17)],
        collar=[(1.14, 0.24, 0.2), (1.19, 0.18, 0.15)],
        tunic=[(0.59, 0.56, 0.4), (0.73, 0.455, 0.33)],
        belt=[(0.725, 0.465, 0.345), (0.815, 0.455, 0.335)],
        buckle=(0.09, 0.075),
        neck_r=0.085, deltoid=0.094, upper_arm=(0.088, 0.094, 0.075), forearm=(0.076, 0.084, 0.066),
        thigh=(0.12, 0.112, 0.085), shin=(0.085, 0.09, 0.072), boot=(0.092, 0.11), boot_top=0.24,
        head_shape=dict(w=1.16, d=1.08, h=1.04, nose=1.25, nose_w=1.4, brow=1.5, ear=1.1, jaw=1.12, eye=0.9),
        hair="topknot", beard="braided",
        colors=dict(Skin="#d49a78", Eyes="#1d1b22", EyeWhite="#e6dccb", Chest="#6f7a5a", ChestTrim="#4a5240", Arms="#5a4632",
                    ArmsTrim="#3e3022", Hands="#d49a78", Waist="#4b3424", WaistTrim="#33241a", BuckleMetal="#c9a24a",
                    BeardMetal="#d0a848", Legs="#5e4a3a", LegsLower="#5e4a3a",
                    Feet="#3e2c20", FeetTrim="#2a1e16", Beard="#a4512a", Hair="#a4512a", HairTrim="#6a3a24"),
    ),
    # The fish-folk of the Reedfen: a broad fish head with bulging, glowing eyes and a fin crest, long arms,
    # webbed bare feet, fins down the back and on the forearms. Their fins use the Hair material (tinted with it).
    "Mireling": dict(
        ankle=0.07, knee=0.34, hip=0.64, spine=0.76, chest=0.92, neck=1.1, head=1.15, top=1.42,
        leg_x=0.12, shoulder_z=1.06, clavicle_x=0.055, shoulder_x=0.25, elbow_x=0.51, wrist_x=0.77, hand=0.15,
        foot=(0.19, 0.3, 0.07),
        pelvis=[(0.54, 0.26, 0.2), (0.61, 0.37, 0.27), (0.7, 0.39, 0.29), (0.77, 0.38, 0.29)],
        belly=[(0.72, 0.39, 0.29), (0.82, 0.42, 0.33, 1.2), (0.94, 0.41, 0.31)],
        torso=[(0.9, 0.39, 0.3), (0.98, 0.41, 0.31, 1.1), (1.04, 0.38, 0.29), (1.09, 0.3, 0.24), (1.14, 0.19, 0.17)],
        collar=[(1.1, 0.21, 0.19), (1.15, 0.17, 0.15)],
        tunic=[(0.58, 0.42, 0.32), (0.71, 0.4, 0.3)],
        belt=[(0.705, 0.41, 0.31), (0.765, 0.4, 0.305)],
        buckle=(0.06, 0.05),
        neck_r=0.08, deltoid=0.078, upper_arm=(0.072, 0.078, 0.06), forearm=(0.062, 0.068, 0.054),
        thigh=(0.11, 0.1, 0.076), shin=(0.075, 0.08, 0.06), boot=(0.08, 0.09), boot_top=0.2,
        head_shape=dict(w=1.3, d=1.25, h=1.15, nose=1.0, brow=1.0, ear=1.0, jaw=1.0, eye=1.0),
        hair="none", beard=None, head_style="fish", barefoot=True, fins=True, gear=("RobeSkirt", "RobeCuffs"),
        colors=dict(Skin="#4f8a7a", Eyes="#141814", EyeGlow="#f0e070", Belly="#b8c99a", Chest="#4f8a7a", ChestTrim="#3a6a5c",
                    Arms="#4f8a7a", ArmsTrim="#3a6a5c", Hands="#4f8a7a", Waist="#5a6a30", WaistTrim="#3e4a22", BuckleMetal="#c9c2a0",
                    Legs="#6a9a80", LegsLower="#4f8a7a", Feet="#3f6a5a", FeetTrim="#2f5048", Hair="#2f5a4a"),
    ),
}

SIDES = (("Left", 1.0), ("Right", -1.0))
# The top of the skull above the head joint (head-relative, before the race's head scale).
SKULL_TOP = 0.255

# The fish head (head_style "fish"): a broad skull drawn into a snout with a wide mouth, a pale lower jaw.
FISH_SKULL = [(0, 0.02, 0.2), (0.085, -0.02, 0.19), (0.1, 0.07, 0.17), (0, 0.13, 0.13), (0.08, 0.11, 0.06),
              (0, 0.09, -0.01), (0.125, 0.0, 0.11), (0.12, 0.03, 0.03), (0, -0.1, 0.17), (0.075, -0.12, 0.145),
              (0, -0.165, 0.1), (0.06, -0.16, 0.085), (0, -0.185, 0.052), (0.1, -0.13, 0.048), (0.115, -0.07, 0.05)]
FISH_JAW = [(0, -0.178, 0.036), (0.098, -0.128, 0.036), (0.112, -0.06, 0.034), (0, -0.16, -0.008), (0.07, -0.1, -0.04),
            (0, -0.07, -0.058), (0.08, 0.0, -0.025)]
FISH_MOUTH = [(0.1, -0.128, 0.042), (0.06, -0.162, 0.042), (0.0, -0.181, 0.043)]
# The crest from the brow over the crown to the nape: (base point, spine length), between spines a lower notch.
FISH_CREST = [((0, -0.09, 0.172), 0.06), ((0, -0.04, 0.198), 0.12), ((0, 0.02, 0.205), 0.09), ((0, 0.07, 0.186), 0.13),
              ((0, 0.11, 0.142), 0.085), ((0, 0.13, 0.082), 0.1), ((0, 0.11, 0.02), 0.05)]
FISH_GILLS = [((0.112, -0.045, 0.085), 0.05), ((0.12, -0.005, 0.065), 0.085), ((0.118, 0.035, 0.035), 0.06),
              ((0.108, 0.07, 0.005), 0.075), ((0.09, 0.09, -0.02), 0.04)]


def interp(table, x):
    """Piecewise linear through (x, y) pairs sorted by x."""
    if x <= table[0][0]:
        return table[0][1]
    for (x0, y0), (x1, y1) in zip(table, table[1:]):
        if x <= x1:
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0)
    return table[-1][1]


def smooth(a, b, x):
    """0 at a, 1 at b, smooth in between (a may be greater than b)."""
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


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
    # The top of the skull (under the hair): a marker for things worn on the head.
    skull_top = r["head"] + SKULL_TOP * r["head_shape"]["h"]
    bones.append(("HeadTop", (0, 0, skull_top), (0, 0, skull_top + 0.05), "Head"))
    # A cloak's hem hangs from this bone, behind the shoulders: the client swings it back while running.
    z = r["shoulder_z"]
    back = extent(r, z - 0.06)[2] + 0.03
    bones.append(("Cloak", (0, back, z), (0, back, z - 0.3), "Chest"))
    return bones


def _rings(sections, sides=8, square=0.3):
    """lp.ring for each (z, width, depth[, front, back]) section."""
    out = []
    for section in sections:
        z, w, d = section[:3]
        front = section[3] if len(section) > 3 else 1.0
        back = section[4] if len(section) > 4 else 1.0
        out.append(lp.ring(z, w, d, sides, front, back, square=square))
    return out


def extent(r, z, keys=("pelvis", "belly", "torso", "collar", "tunic", "belt")):
    """The trunk at height z over all its sections (or those of `keys`): (half width, front, back); the front is negative Y."""
    half, front, back = 0.0, 0.0, 0.0
    for key in keys:
        sections = r[key]
        for a, b in zip(sections, sections[1:]):
            if not a[0] <= z <= b[0]:
                continue
            t = (z - a[0]) / (b[0] - a[0])
            lerp = [p + (q - p) * t for p, q in zip(a + (1.0, 1.0)[len(a) - 3:], b + (1.0, 1.0)[len(b) - 3:])]
            half = max(half, lerp[1] / 2)
            front = min(front, -lerp[2] / 2 * lerp[3])
            back = max(back, lerp[2] / 2 * lerp[4])
    return half, front, back


# ---------------------------------------------------------------- trunk


def _trunk_ring(z, half, front, back, sides, square=0.3, bumps=()):
    """
    A superellipse cross-section: `half` wide to each side, `front` (negative Y) and `back` deep, pushed out
    by `bumps`: (z, height, angle from the front in degrees on both sides, angular width, amount) like pecs.
    """
    e = 2.0 / (2.0 + 3.0 * square)
    out = []
    for i in range(sides):
        t = 2 * math.pi * (i + 0.5) / sides
        c, s = math.cos(t), math.sin(t)
        p = Vector((math.copysign(abs(c) ** e, c) * half, math.copysign(abs(s) ** e, s) * (back if s > 0 else -front), z))
        around = abs(math.degrees(math.atan2(p.x, -p.y)))
        push = sum(amount * math.exp(-((z - bz) / bh) ** 2) * math.exp(-((around - ba) / bw) ** 2) for bz, bh, ba, bw, amount in bumps)
        flat = Vector((p.x, p.y, 0))
        out.append(p + flat.normalized() * push if flat.length > 1e-6 else p)
    return out


def trunk_bumps(r):
    """
    Pecs over the chest, shoulder blades behind, a little tapering in at the small of the back: (z, height,
    angle from the front in degrees on both sides, angular width, amount) for _trunk_ring (and gear over it).
    """
    sz, ch = r["shoulder_z"], r["chest"]
    return ((ch + (sz - ch) * 0.42, 0.055, 32, 26, 0.014), (ch + (sz - ch) * 0.55, 0.06, 150, 22, 0.01),
            (r["spine"] + 0.04, 0.05, 180, 30, -0.008))


def _torso(mb, r, m):
    """
    The trunk as one smooth surface through all its sections: pelvis (Legs), belly and chest (Chest), the
    collar (ChestTrim); the tunic's skirt with a hem, the belt, buckle and pouches.
    """
    keys = ("pelvis", "belly", "torso", "collar")
    z0, z1 = r["pelvis"][0][0], r["collar"][-1][0]
    count = lp.res(18, 7)
    levels = [z0 + (z1 - z0) * i / count for i in range(count + 1)]
    raw = [extent(r, z, keys) for z in levels]
    # A light smoothing of the profile (the sections meet at corners).
    prof = []
    for i in range(len(raw)):
        a, b, c = raw[max(i - 1, 0)], raw[i], raw[min(i + 1, len(raw) - 1)]
        prof.append(tuple((a[k] + 2 * b[k] + c[k]) / 4 for k in range(3)) if 0 < i < len(raw) - 1 else b)
    n = lp.res(16, 8)
    bumps = trunk_bumps(r)
    rows = [[Vector((0, 0, z0 - 0.025))]]
    rows += [_trunk_ring(z, h, f, bk, n, bumps=bumps) for z, (h, f, bk) in zip(levels, prof)]
    rows.append([Vector((0, 0.01, z1 + 0.01))])
    belly0, torso0, collar0 = r["belly"][0][0], r["torso"][0][0], r["collar"][0][0]

    def material_at(c):
        return m["Legs"] if c.z < belly0 + 0.01 else m["ChestTrim"] if c.z > collar0 + 0.012 else None

    def weigh(p):
        a = smooth(belly0, belly0 + 0.06, p.z)
        b = smooth(torso0, torso0 + 0.06, p.z)
        return {"Hips": 1.0 - a, "Spine": a * (1.0 - b), "Chest": a * b}
    sc.grid_mesh(mb, rows, m["Chest"], weigh, closed=True, material_at=material_at, inside=lambda p: Vector((0, 0.0, p.z)))
    # The tunic's skirt flares out under the belt; a darker hem band at the bottom.
    tunic = r["tunic"]
    tz, tw, td = tunic[0][:3]
    hem = [(tz - 0.004, tw * 1.015, td * 1.02), (tz + 0.028, tw * 0.985, td * 0.985)]
    top, lx = tunic[-1][0], r["leg_x"]

    def skirt(p):
        down = smooth(top, tz, p.z) * 0.55
        left = smooth(-lx * 0.9, lx * 0.9, p.x)
        return {"Hips": 1.0 - down, "LeftUpperLeg": down * left, "RightUpperLeg": down * (1.0 - left)}
    mb.loft(_rings(tunic, n, square=0.15), m["Chest"], skirt, cap_a=False, cap_b=False, smooth=True)
    mb.loft(_rings(hem, n, square=0.15), m["ChestTrim"], skirt, cap_a=False, cap_b=False, smooth=True)
    _belt(mb, r, m)


def _belt(mb, r, m):
    """A broad leather belt with stitched edges, a framed buckle, and pouches on both hips."""
    n = lp.res(18, 8)
    belt = _rings(r["belt"], n, square=0.35)
    mb.loft(belt, m["Waist"], "Hips", smooth=True)
    (zb, wb, db), (zt, wt, dt) = r["belt"][0][:3], r["belt"][1][:3]
    for zz, w, d in ((zb + 0.004, wb, db), (zt - 0.004, wt, dt)):
        edge = lp.ring(zz, w * 1.012, d * 1.016, n, square=0.35)
        mb.loft([[(x, y, zz - 0.004) for x, y, _ in edge], [(x, y, zz + 0.004) for x, y, _ in edge]], m["WaistTrim"], "Hips",
                cap_a=False, cap_b=False, smooth=True)
    bw, bh = r["buckle"]
    z = (zb + zt) / 2
    front = min(p[1] for p in belt[0]) - 0.006
    # The buckle: a raised frame around a plate, a prong across it.
    t = bw * 0.16
    for cx, cz, sx, sz in ((0, bh / 2 - t / 2, bw, t), (0, -bh / 2 + t / 2, bw, t), (bw / 2 - t / 2, 0, t, bh), (-bw / 2 + t / 2, 0, t, bh)):
        mb.box((cx, front - 0.004, z + cz), (sx, 0.014, sz), m["BuckleMetal"], "Hips")
    mb.box((0, front, z), (bw * 0.8, 0.01, bh * 0.8), m["Waist"], "Hips")
    mb.box((0, front - 0.008, z), (bw * 0.12, 0.012, bh * 0.9), m["BuckleMetal"], "Hips")
    # Pouches on both hips, a little to the front: a body, a flap and a stud.
    half = (wb + wt) / 4
    for s, a in ((1, 52), (-1, 64)):
        rad = math.radians(a)
        px, py = s * half * math.sin(rad) * 1.04, -(db / 2) * math.cos(rad) * 1.05
        out = Vector((s * math.sin(rad), -math.cos(rad), 0))
        center = Vector((px, py, z - bh * 0.35)) + out * 0.022
        pw, pd, ph = bw * 0.95, bw * 0.55, bh * 1.35
        mb.box(center, (pw, pd, ph), m["Waist"], "Hips", top=(1.0, 0.9), rotation=(0, 0, math.degrees(math.atan2(out.x, -out.y))))
        flap = center + out * (pd * 0.5 + 0.002) + Vector((0, 0, ph * 0.28))
        mb.box(flap, (pw * 1.06, 0.01, ph * 0.5), m["WaistTrim"], "Hips", rotation=(0, 0, math.degrees(math.atan2(out.x, -out.y))))
        mb.sphere(flap + out * 0.007 + Vector((0, 0, -ph * 0.16)), 0.007, m["BuckleMetal"], "Hips", segments=lp.res(6, 4), rings=4)


# ---------------------------------------------------------------- head


class Head:
    """
    A race's sculpted head. The shapes are made in head-relative units (z up from the head joint, -Y the
    front) and mapped to the model by the race's head scale; `star` is the skull and face seen from
    inside, for fitting hair, beards and gear around it.
    """

    def __init__(self, r):
        s = r["head_shape"]
        self.r = r
        self.s = s
        self.w, self.d, self.h = s["w"], s["d"], s["h"]
        self.base = r["head"]
        self.shape = head_sdf(s)
        self.star = sc.Star(self.shape, (0, -0.01, 0.12))

    def at(self, p):
        """Head-relative to model space."""
        return Vector((p[0] * self.w, p[1] * self.d, self.base + p[2] * self.h))

    def rows_at(self, rows):
        return [[self.at(p) for p in row] for row in rows]

    def surface(self, x, z, offset=0.0, toward=-1.0):
        """The face's surface point (and normal) straight in front of (x, z) (behind it with toward = 1)."""
        origin = Vector((x, -0.02, z))
        d = Vector((0, toward, 0))
        p = origin + d * self.star.reach(origin, d)
        n = self.star.normals([p])[0]
        return p + n * offset, n


def head_sdf(s):
    """The skull and face: cranium, forehead, a square jaw with a strong chin, cheekbones, a brow ridge, lips; eye sockets carved in."""
    j, b = s["jaw"], s["brow"]
    cranium = sc.ellipsoid((0, 0.018, 0.15), (0.096, 0.115, 0.108))
    forehead = sc.ellipsoid((0, -0.045, 0.18), (0.076, 0.062, 0.056))
    midface = sc.ellipsoid((0, -0.065, 0.1), (0.064, 0.058, 0.07))
    eyeline = sc.ellipsoid((0, -0.07, 0.136), (0.086, 0.05, 0.034))
    jaw = sc.union(*[sc.cone((k * 0.078 * j, 0.004, 0.05), (k * 0.031 * j, -0.095, 0.012), 0.021 * j, 0.021 * j) for k in (1, -1)])
    chin = sc.ellipsoid((0, -0.1, 0.012), (0.037 * j, 0.021, 0.025))
    jaw_fill = sc.ellipsoid((0, -0.04, 0.045), (0.068 * j, 0.07, 0.045))
    cheek = sc.ellipsoid((0.062, -0.088, 0.11), (0.029, 0.022, 0.018)).symmetric()
    ridge = 0.014 * (1 + 0.45 * (b - 1))
    ahead = 0.004 * (b - 1)
    brow = sc.union(*[sc.cone((0, -0.112 - ahead, 0.161), (k * 0.066, -0.1 - ahead, 0.17), ridge, ridge * 0.8) for k in (1, -1)])
    upper_lip = sc.ellipsoid((0, -0.112, 0.058), (0.028, 0.014, 0.014))
    lower_lip = sc.ellipsoid((0, -0.108, 0.037), (0.022, 0.013, 0.01))
    base = sc.blend(cranium, (forehead, 0.03), (midface, 0.035), (eyeline, 0.02), (jaw, 0.03), (jaw_fill, 0.03), (chin, 0.02), (cheek, 0.02),
                    (brow, 0.016), (upper_lip, 0.01), (lower_lip, 0.008))
    socket = sc.ellipsoid((0.04, -0.126, 0.134), (0.024, 0.02, 0.016)).symmetric()
    mouth = sc.ellipsoid((0, -0.126, 0.047), (0.025, 0.02, 0.003))
    temple = sc.ellipsoid((0.1, -0.055, 0.165), (0.018, 0.04, 0.04)).symmetric()
    under_cheek = sc.ellipsoid((0.066, -0.1, 0.07), (0.018, 0.016, 0.02)).symmetric()
    return sc.carve(base, (socket, 0.01), (mouth, 0.004), (temple, 0.02), (under_cheek, 0.02))


NOSE_ROOT = Vector((0, -0.12, 0.15))


def nose_sdf(s):
    """A straight nose with a rounded tip and wings, scaled about its root by `nose`."""
    k, kw = s["nose"], s["nose"] * s.get("nose_w", 1.0)

    def at(p):
        q = NOSE_ROOT + (Vector(p) - NOSE_ROOT) * k
        return (p[0] * kw, q.y, q.z)
    bridge = sc.cone(at((0, -0.113, 0.15)), at((0, -0.146, 0.094)), 0.0095 * (k + kw) / 2, 0.0135 * kw)
    tip = sc.ellipsoid(at((0, -0.145, 0.092)), (0.013 * kw, 0.015 * k, 0.014 * (k + kw) / 2))
    wings = sc.ellipsoid(at((0.0145, -0.13, 0.087)), (0.0105 * kw, 0.011 * k, 0.009 * kw)).symmetric()
    return sc.blend(bridge, (tip, 0.01 * k), (wings, 0.008 * k)), Vector(at((0, -0.128, 0.108)))


def ear_sdf(s, side):
    """An ear with a hollow, pointing a little back; `ear` scales it out from where it joins the head."""
    k = s["ear"]
    root = Vector((side * 0.092, 0.014, 0.122))

    def at(p):
        return tuple(root + (Vector((side * p[0], p[1], p[2])) - root) * k)
    outer = sc.ellipsoid(at((0.1, 0.014, 0.122)), (0.014 * k, 0.024 * k, 0.04 * k))
    bowl = sc.ellipsoid(at((0.115, 0.01, 0.124)), (0.009 * k, 0.015 * k, 0.026 * k))
    return sc.carve(outer, (bowl, 0.004)), Vector(at((0.1, 0.014, 0.122)))


def _head(mb, r, m):
    """The sculpted head: skull and face, nose, ears, eyes, brows; the neck below."""
    n = lp.res(10, 6)
    nr = r["neck_r"]
    mb.tube([((0, 0.012, r["neck"] - 0.05), (nr * 1.6, nr * 1.15)), ((0, 0.01, r["neck"] + 0.015), (nr * 1.12, nr * 1.02)),
             ((0, 0.012, r["head"] + 0.06), nr * 0.92)], m["Skin"], "Neck", sides=n, smooth=True)
    if r.get("head_style") == "fish":
        _fish_head(mb, r, m, head_at(r))
        return
    head = Head(r)
    s = r["head_shape"]
    rows = head.star.grid(lp.res(20, 8), lp.res(28, 10), focus=(math.radians(100), 0.6))
    sc.grid_mesh(mb, head.rows_at(rows), m["Skin"], "Head", closed=True, inside=lambda p: head.at(head.star.center))
    nose, center = nose_sdf(s)
    star = sc.Star(nose, center)
    sc.grid_mesh(mb, head.rows_at(star.grid(lp.res(9, 4), lp.res(12, 6))), m["Skin"], "Head", closed=True,
                 inside=lambda p: head.at(center))
    for side in (1, -1):
        ear, center = ear_sdf(s, side)
        star = sc.Star(ear, center)
        sc.grid_mesh(mb, head.rows_at(star.grid(lp.res(6, 4), lp.res(9, 5))), m["Skin"], "Head", closed=True,
                     inside=lambda p, c=center: head.at(c))
        _eye(mb, head, m, side)
        _brow(mb, head, m, side)


def _eye(mb, head, m, side):
    """A white almond in the socket, a large dark iris, a heavy upper lid; far away just a dark spot."""
    e = head.s.get("eye", 1.0)
    white = head.at((side * 0.039, -0.111, 0.134))
    if lp.DETAIL < 0.5:
        mb.sphere(white + Vector((0, -0.004, 0)), (0.012 * e * head.w, 0.006, 0.006 * e * head.h), m["Eyes"], "Head",
                  segments=5, rings=3, smooth=True)
        return
    mb.sphere(white, (0.0165 * e * head.w, 0.0085 * head.d, 0.0095 * e * head.h), m["EyeWhite"], "Head",
              segments=lp.res(10, 6), rings=lp.res(6, 4), smooth=True)
    iris = head.at((side * 0.0375, -0.1182, 0.1333))
    mb.sphere(iris, (0.0088 * e * head.w, 0.0032 * head.d, 0.0092 * e * head.h), m["Eyes"], "Head",
              segments=lp.res(10, 5), rings=lp.res(5, 3), smooth=True)
    # The upper lid, from the inner to the outer corner over the top of the eye.
    lid = [head.at((side * x, -0.1175 + 0.005 * abs(x - 0.039) / 0.017, 0.1405 + z)) for x, z in
           ((0.023, -0.003), (0.031, 0.0005), (0.04, 0.0015), (0.049, 0.0), (0.056, -0.004))]
    mb.tube([(q, (0.0032, 0.0035)) for q in lid], m["Skin"], "Head", sides=lp.res(6, 4), smooth=True)


def _brow(mb, head, m, side):
    """A thick brow on the brow ridge, its inner end lower (a scowl); bushier with a heavier brow."""
    b = head.s["brow"]
    path, radii = [], []
    for x, z, ru, rv in ((0.017, 0.155, 0.0045, 0.0062), (0.042, 0.164, 0.0055, 0.0085), (0.069, 0.166, 0.0035, 0.005)):
        p, normal = head.surface(side * x, z, offset=0.0035)
        path.append(head.at(p))
        radii.append((ru * b, rv * (1 + 0.6 * (b - 1))))
    if b > 1.2:
        # Bushy brows flare out at the ends.
        tip, _ = head.surface(side * 0.088, 0.176, offset=0.008)
        path.append(head.at(tip))
        radii.append((0.003, 0.004))
    mb.tube(list(zip(path, radii)), m["Beard"], "Head", sides=lp.res(6, 4), smooth=True)


def head_at(r):
    """Head-relative coordinates (the tables above) to model space for a race."""
    s = r["head_shape"]
    return lambda x, y, z: (x * s["w"], y * s["d"], r["head"] + z * s["h"])


def fin(mb, spines, center, mat, bone, sweep=(0, 0, 0), notch=0.45):
    """
    A spiny fin (both sides): `spines` are (base, length) along its root; each spine points away from
    `center`, swept by `sweep`, with a notch between neighbours (`notch` of the shorter one's length).
    """
    def tip(base, length):
        d = (Vector(base) - Vector(center)).normalized() + Vector(sweep)
        return Vector(base) + d.normalized() * length

    roots, edge = [], []
    for k, (base, length) in enumerate(spines):
        roots.append(Vector(base))
        edge.append(tip(base, length))
        if k + 1 < len(spines):
            nb, nl = spines[k + 1]
            mid = (Vector(base) + Vector(nb)) / 2
            roots.append(mid)
            edge.append(tip(mid, min(length, nl) * notch))
    n = len(roots)
    faces = [(i, i + 1, n + i + 1, n + i) for i in range(n - 1)]
    mb.add(roots + edge, faces, mat, bone, double_sided=True)


def _fish_head(mb, r, m, at):
    """A mireling's head: snout and wide mouth, a pale jaw, bulging glowing eyes, a crest and gill fins."""
    mb.hull([at(*p) for p in lp.mirrored(FISH_SKULL)], m["Skin"], "Head")
    mb.hull([at(*p) for p in lp.mirrored(FISH_JAW)], m["Belly"], "Head")
    mouth = [at(*p) for p in FISH_MOUTH] + [at(-x, y, z) for x, y, z in reversed(FISH_MOUTH[:-1])]
    mb.tube([(p, 0.007) for p in mouth], m["Eyes"], "Head", sides=4)
    w = r["head_shape"]["w"]
    for side in (1, -1):
        eye = at(side * 0.09, -0.105, 0.135)
        mb.sphere(eye, 0.038 * w, m["EyeGlow"], "Head", segments=lp.res(8, 5), rings=lp.res(5, 3), smooth=True)
        mb.box(Vector(eye) + Vector((side * 0.012, -0.033 * w, 0)), (0.009, 0.006, 0.034), m["Eyes"], "Head")
        mb.hull([at(side * x, y, z) for x, y, z in ((0.05, -0.125, 0.165), (0.125, -0.1, 0.16), (0.11, -0.08, 0.185),
                                                    (0.06, -0.11, 0.185), (0.09, -0.13, 0.17))], m["Skin"], "Head")
        fin(mb, [(at(side * x, y, z), l) for (x, y, z), l in FISH_GILLS], at(0, 0.0, 0.06), m["Hair"], "Head", sweep=(0, 0.9, 0.1))
    fin(mb, [(at(*p), l) for p, l in FISH_CREST], at(0, 0.0, 0.07), m["Hair"], "Head", sweep=(0, 0.5, 0))


def _body_fins(mb, r, m):
    """A mireling's fins down the back and along the back of the forearms (material Hair)."""
    z = r["shoulder_z"]
    spines = []
    for k in range(7):
        zz = z + 0.02 - (z - r["spine"]) * k / 6
        back = extent(r, zz)[2]
        spines.append(((0, back - 0.01, zz), 0.1 - 0.008 * k if k % 2 == 0 else 0.07 - 0.005 * k))

    def weigh(p):
        t = max(0.0, min(1.0, (p.z - r["chest"] + 0.03) / 0.06))
        return {"Chest": t, "Spine": 1.0 - t}

    fin(mb, spines, (0, -0.2, r["chest"]), m["Hair"], weigh, sweep=(0, 0.2, -0.5))
    ex, wx, fa = r["elbow_x"], r["wrist_x"], r["forearm"]
    for side, s in SIDES:
        spines = [((s * (ex + (wx - ex) * f), fa[1] * 0.8, z + 0.01), l) for f, l in ((0.1, 0.05), (0.35, 0.08), (0.6, 0.06), (0.82, 0.04))]
        fin(mb, spines, (s * (ex + wx) / 2, 0, z), m["Hair"], side + "LowerArm", sweep=(-s * 0.6, 0, 0))


# ---------------------------------------------------------------- hair and beards


def _cap_offset_fn(style):
    """How far the hair stands off the skull at polar angle theta (degrees) and angle around t (degrees)."""
    if style == "swept":
        return lambda th, t: 0.007 + 0.017 * (1 - smooth(25, 80, th)) + 0.006 * (1 - smooth(0, 60, abs(t))) * (1 - smooth(30, 60, th))
    return lambda th, t: 0.004


SWEPT_HAIRLINE = [(0, 36), (25, 40), (45, 50), (62, 66), (74, 94), (84, 80), (105, 82), (140, 106), (180, 116)]


def _angles(d):
    """A head-relative direction's polar angle and angle around, in degrees."""
    d = Vector(d).normalized()
    return math.degrees(math.acos(max(-1.0, min(1.0, d.z)))), math.degrees(math.atan2(d.x, -d.y))


def _cap(mb, head, mat, hairline, offset, rings, segments):
    """
    Hair over the scalp from the crown down to `hairline` (polar angle by angle around, degrees), standing
    `offset(theta, t)` off the skull, with a rim down to the skin along the hairline.
    """
    rows_dirs = [[sc.direction(0.0, 0.0)]]
    ts = [360.0 * i / segments for i in range(segments)]
    ts = [t if t <= 180 else t - 360 for t in ts]
    for k in range(1, rings + 1):
        rows_dirs.append([sc.direction(math.radians(interp(hairline, abs(t)) * k / rings), math.radians(t)) for t in ts])
    flat = [d for row in rows_dirs for d in row]
    pts = head.star.points(flat)
    normals = head.star.normals(pts)
    lifted = []
    for p, n, d in zip(pts, normals, flat):
        th, t = _angles(d)
        lifted.append(p + n * offset(th, t))
    rows, i = [], 0
    for row in rows_dirs:
        rows.append(lifted[i:i + len(row)])
        i += len(row)
    edge = pts[len(flat) - segments:]
    rows.append([p + n * 0.0015 for p, n in zip(edge, normals[len(flat) - segments:])])
    sc.grid_mesh(mb, head.rows_at(rows), mat, "Head", closed=True, inside=lambda p: head.at(head.star.center))
    return rows


def _bezier_dirs(a, b, c, n):
    """Directions along a quadratic curve over the sphere of directions (degree pairs theta, t)."""
    va, vb, vc = (sc.direction(math.radians(th), math.radians(t)) for th, t in (a, b, c))
    out = []
    for i in range(n + 1):
        u = i / n
        out.append(((1 - u) ** 2 * va + 2 * u * (1 - u) * vb + u * u * vc).normalized())
    return out


def _lock(mb, head, mat, dirs, base, width, thick, lift, bone="Head"):
    """
    A lock of hair along directions over the head: standing on the hair below (`base(theta, t)` off the
    skull), widest a third of the way, tapering to a tip that lifts off by `lift`. Smooth, flattened.
    """
    pts = head.star.points(dirs)
    normals = head.star.normals(pts)
    n = len(pts)
    path, ups = [], []
    for k, (p, nr, d) in enumerate(zip(pts, normals, dirs)):
        u = k / (n - 1)
        th, t = _angles(d)
        path.append(p + nr * (base(th, t) + thick * 0.35 + lift * u * u))
        ups.append(nr)
    rings = []
    sides = lp.res(6, 4)
    for k in range(n):
        u = k / (n - 1)
        a, b = path[max(k - 1, 0)], path[min(k + 1, n - 1)]
        tangent = (b - a).normalized()
        lateral = ups[k].cross(tangent).normalized()
        up = tangent.cross(lateral).normalized()
        taper = math.sin(math.pi * min(1.0, 0.25 + u * 0.75)) ** 0.7 if u > 0 else 0.75
        w, h = width * taper / 2, thick * max(taper, 0.2) / 2
        rings.append([head.at(path[k] + lateral * (math.cos(x) * w) + up * (math.sin(x) * h))
                      for x in (2 * math.pi * (j + 0.5) / sides for j in range(sides))])
    mb.loft(rings, mat, bone, cap_a=True, cap_b=True, smooth=True)


# Locks of the swept hair: (root, bend, tip) as (polar angle, angle around) in degrees, width, thickness, lift.
# Parted on the right (negative angles), swept up, back and over to the left; two locks fall on the brow.
SWEPT_LOCKS = [
    ((40, -16), (14, 40), (42, 150), 0.05, 0.018, 0.012),
    ((38, 6), (18, 70), (55, 135), 0.05, 0.018, 0.012),
    ((42, 26), (34, 75), (66, 118), 0.046, 0.016, 0.01),
    ((48, 44), (50, 84), (78, 112), 0.04, 0.014, 0.008),
    ((44, -34), (36, -72), (62, -118), 0.042, 0.014, 0.006),
    ((56, -52), (66, -88), (82, -116), 0.036, 0.012, 0.004),
    ((14, 175), (52, 178), (96, 180), 0.05, 0.016, 0.006),
    ((20, 128), (54, 140), (92, 150), 0.046, 0.016, 0.006),
    ((22, -132), (56, -146), (94, -160), 0.046, 0.016, 0.006),
    ((30, 100), (60, 104), (86, 112), 0.04, 0.014, 0.006),
    ((30, -96), (62, -102), (86, -110), 0.04, 0.014, 0.004),
    ((22, 18), (36, -6), (56, -20), 0.034, 0.012, 0.012),
    ((26, -2), (40, -22), (54, -38), 0.03, 0.011, 0.01),
]


def _swept_hair(mb, head, m):
    """Thick hair parted on one side and swept back, short over the ears, two locks falling on the brow."""
    base = _cap_offset_fn("swept")
    _cap(mb, head, m["Hair"], SWEPT_HAIRLINE, base, lp.res(9, 4), lp.res(26, 10))
    steps = lp.res(7, 3)
    for root, bend, tip, width, thick, lift in SWEPT_LOCKS:
        _lock(mb, head, m["Hair"], _bezier_dirs(root, bend, tip, steps), base, width, thick, lift)


def _strip_dir(a, b):
    """A direction over the top of the head: a from the front (0) over the crown (90) to the back (180), b to the side."""
    a, b = math.radians(a), math.radians(b)
    return Vector((math.sin(b), -math.cos(b) * math.cos(a), math.cos(b) * math.sin(a)))


def _topknot_hair(mb, head, m):
    """
    The sides shaved to a dark stubble (HairTrim); a crest of hair from the brow back over the crown in
    thick locks, gathered into a knot bound with a ring.
    """
    _cap(mb, head, m["HairTrim"], SWEPT_HAIRLINE, lambda th, t: 0.0025, lp.res(7, 3), lp.res(22, 8))
    cols, rows_n = lp.res(10, 4), lp.res(12, 5)
    half = 32.0
    dirs = [[_strip_dir(30 + 120 * k / rows_n, -half + 2 * half * i / cols) for i in range(cols + 1)] for k in range(rows_n + 1)]
    flat = [d for row in dirs for d in row]
    pts = head.star.points(flat)
    normals = head.star.normals(pts)
    rows, i = [], 0
    for k, row in enumerate(dirs):
        out = []
        for j in range(len(row)):
            b = -half + 2 * half * j / cols
            bell = max(0.0, math.cos(b / half * math.pi / 2)) ** 0.5
            front = smooth(0, 3.5, k)
            out.append(pts[i + j] + normals[i + j] * (0.004 + 0.022 * bell * front))
        rows.append(out)
        i += len(row)
    sc.grid_mesh(mb, head.rows_at(rows), m["Hair"], "Head", inside=lambda p: head.at(head.star.center))
    # Thick locks swept back over the crest into the knot.
    steps = lp.res(8, 4)
    for b, width, lift in ((-20, 0.04, 0.004), (-7, 0.046, 0.008), (7, 0.046, 0.008), (20, 0.04, 0.004)):
        lock_dirs = [_strip_dir(40 + 100 * u / steps, b * (1 - 0.7 * u / steps)) for u in range(steps + 1)]
        _lock(mb, head, m["Hair"], lock_dirs, lambda th, t: 0.012, width, 0.016, lift)
    # The knot at the back of the crown, bound with a metal ring.
    root = head.star.points([_strip_dir(140, 0)])[0]
    up = head.star.normals([root])[0]
    ring_c = root + up * 0.026
    knot = root + up * 0.07
    mb.tube([(head.at(root + up * 0.004), 0.036 * head.w), (head.at(ring_c), 0.031 * head.w), (head.at(knot - up * 0.03), 0.032 * head.w)],
            m["Hair"], "Head", sides=lp.res(10, 6), smooth=True)
    mb.tube([(head.at(ring_c - up * 0.01), 0.035 * head.w), (head.at(ring_c + up * 0.01), 0.035 * head.w)], m["BeardMetal"], "Head",
            sides=lp.res(10, 6))
    shape = sc.union(sc.ellipsoid(knot, (0.048, 0.044, 0.042)), sc.ellipsoid(knot + up * 0.028 + Vector((0, 0.01, 0)), (0.032, 0.03, 0.03)),
                     k=0.018)
    star = sc.Star(shape, knot)
    sc.grid_mesh(mb, head.rows_at(star.grid(lp.res(9, 4), lp.res(12, 6))), m["Hair"], "Head", closed=True,
                 inside=lambda p: head.at(knot))


def _surface_lock(mb, head, star, mat, targets, width, thick, offset=0.002):
    """A lock of beard over a sculpted surface: through points in front of (x, z) targets, tapering at both ends."""
    dirs = [Vector((x, -0.3, z)) - star.center for x, z in targets]
    pts = star.points(dirs)
    normals = star.normals(pts)
    n = len(pts)
    rings = []
    sides = lp.res(6, 4)
    for k in range(n):
        u = k / (n - 1)
        a, b = pts[max(k - 1, 0)], pts[min(k + 1, n - 1)]
        tangent = (b - a).normalized()
        lateral = normals[k].cross(tangent).normalized()
        up = tangent.cross(lateral).normalized()
        taper = max(0.12, math.sin(math.pi * (0.15 + 0.85 * u)) ** 0.6)
        center = pts[k] + normals[k] * (offset + thick * 0.3)
        rings.append([head.at(center + lateral * (math.cos(x) * width * taper / 2) + up * (math.sin(x) * thick * taper / 2))
                      for x in (2 * math.pi * (j + 0.5) / sides for j in range(sides))])
    mb.loft(rings, mat, "Head", smooth=True)


def _short_beard(mb, head, m):
    """
    A short full beard over the jaw and chin up to the sideburns, the lips left free: the face grown a
    little where the beard is, so its edges stand off the skin; a mustache over the upper lip.
    """
    xs = [0.0, 0.018, 0.03, 0.045, 0.07, 0.09, 0.105]
    zs = [0.027, 0.029, 0.05, 0.074, 0.094, 0.118, 0.152]

    def mask(p):
        return np.maximum.reduce([p[:, 2] - np.interp(np.abs(p[:, 0]), xs, zs), p[:, 1] - 0.025, -0.035 - p[:, 2]])

    def grown(p):
        # Thin at its edge on the cheeks, fuller lower down.
        below = np.interp(np.abs(p[:, 0]), xs, zs) - p[:, 2]
        return head.shape.f(p) - (0.002 + 0.007 * np.clip(below / 0.03, 0.0, 1.0))
    shape = sc.intersect(sc.Shape(grown), sc.Shape(mask), k=0.003)
    star = sc.Star(shape, (0, -0.07, 0.016))
    rows = star.grid(lp.res(14, 8), lp.res(22, 12), focus=(math.radians(100), 0.3))
    sc.grid_mesh(mb, head.rows_at(rows), m["Beard"], "Head", closed=True, inside=lambda p: head.at(star.center))
    if lp.DETAIL < 0.5:
        return
    path, radii = [], []
    for x, z, ru, rv in ((-0.036, 0.044, 0.0035, 0.004), (-0.026, 0.058, 0.005, 0.0065), (-0.01, 0.066, 0.0055, 0.0075),
                         (0.01, 0.066, 0.0055, 0.0075), (0.026, 0.058, 0.005, 0.0065), (0.036, 0.044, 0.0035, 0.004)):
        q, _ = head.surface(x, z, offset=0.0035)
        path.append(head.at(q))
        radii.append((ru, rv))
    mb.tube(list(zip(path, radii)), m["Beard"], "Head", sides=lp.res(6, 4), smooth=True)


def _cheek_line(x):
    """Where the big beard starts on the face (head-relative z by |x|)."""
    return interp([(0.0, 0.074), (0.03, 0.062), (0.06, 0.088), (0.09, 0.122), (0.12, 0.16)], abs(x))


def _braided_beard(mb, head, m):
    """
    A great beard from the sideburns down over the chest in heavy locks, a mustache drooping into it, and
    two braids bound with metal rings.
    """
    hs = head.shape

    def mask(p):
        return np.maximum(p[:, 2] - np.interp(np.abs(p[:, 0]), [0.0, 0.03, 0.06, 0.09, 0.11], [0.074, 0.062, 0.088, 0.115, 0.135]),
                          p[:, 1] + 0.005)
    layer = sc.intersect(hs.offset(0.014), sc.Shape(mask), k=0.014)
    mass = sc.blend(sc.ellipsoid((0, -0.112, -0.03), (0.09, 0.066, 0.085)),
                    (sc.cone((0, -0.13, -0.06), (0, -0.172, -0.17), 0.066, 0.04), 0.04), k=0.03)
    mustache = sc.union(*[sc.chain([(k * 0.008, -0.136, 0.076), (k * 0.04, -0.128, 0.052), (k * 0.062, -0.122, 0.0)],
                                   [0.014, 0.016, 0.012]) for k in (1, -1)])
    beard_shape = sc.blend(layer, (mass, 0.03), (mustache, 0.012))
    beard_shape = sc.carve(beard_shape, (sc.ellipsoid((0, -0.13, 0.04), (0.02, 0.03, 0.007)), 0.004))
    center = (0, -0.07, 0.02)
    if lp.DETAIL > 0.5:
        # Grooves down the beard between its locks, cut along the surface.
        star = sc.Star(beard_shape, center)
        grooves = []
        for k in (1, -1):
            for targets in ([(0.066, 0.05), (0.064, -0.03), (0.044, -0.12)], [(0.036, 0.02), (0.034, -0.07), (0.018, -0.16)]):
                pts = star.points([Vector((k * x, -0.3, z)) - star.center for x, z in targets])
                grooves.append(sc.chain(pts, [0.005, 0.006, 0.004]))
        grooves.append(sc.chain(star.points([Vector((0, -0.3, z)) - star.center for z in (-0.01, -0.1, -0.18)]), [0.004, 0.006, 0.004]))
        beard_shape = sc.carve(beard_shape, (sc.union(*grooves), 0.004))
    star = sc.Star(beard_shape, center)
    rows = star.grid(lp.res(20, 7), lp.res(28, 10), focus=(math.radians(110), 0.4))
    sc.grid_mesh(mb, head.rows_at(rows), m["Beard"], "Head", closed=True, inside=lambda p: head.at(star.center))
    # Two braids from the beard's tip: lobes on alternate sides, two rings each.
    for k in (1, -1):
        path = [Vector((k * 0.03, -0.172, -0.14)), Vector((k * 0.036, -0.19, -0.22)), Vector((k * 0.04, -0.196, -0.3)),
                Vector((k * 0.042, -0.19, -0.37))]
        lobes = []
        steps = 9
        for i in range(steps):
            u = i / (steps - 1)
            seg = min(int(u * 3), 2)
            f = u * 3 - seg
            c = path[seg] + (path[seg + 1] - path[seg]) * f
            side = 0.007 if i % 2 else -0.007
            lobes.append(sc.ellipsoid(c + Vector((side * 1.2, 0, 0)), (0.027 - 0.005 * u, 0.024 - 0.004 * u, 0.027)))
        braid = sc.union(*lobes, k=0.006)
        rows = sc.sweep(braid, path, lp.res(8, 5))
        axis = [head.at(q) for q in path]
        sc.grid_mesh(mb, head.rows_at(rows), m["Beard"], "Head", closed=True,
                     inside=lambda c, axis=axis: min(axis, key=lambda q: (q - c).length))
        for seg, f in ((1, 0.4), (2, 0.55)):
            c = path[seg] + (path[seg + 1] - path[seg]) * f
            mb.tube([(head.at(c + Vector((0, 0, 0.014))), 0.028 * head.w), (head.at(c - Vector((0, 0, 0.014))), 0.028 * head.w)],
                    m["BeardMetal"], "Head", sides=lp.res(10, 6))


def hair(name, r):
    """The race's hair on the scalp as its own mesh (None if bald)."""
    if r["hair"] == "none" or r.get("head_style") == "fish":
        return None
    mats = materials(r)
    mb = lp.MeshBuilder(name)
    head = Head(r)
    if r["hair"] == "swept":
        _swept_hair(mb, head, mats)
    elif r["hair"] == "topknot":
        _topknot_hair(mb, head, mats)
    return mb.finish()


def beard(name, r):
    """The race's beard as its own mesh (None if beardless)."""
    if not r.get("beard"):
        return None
    mats = materials(r)
    mb = lp.MeshBuilder(name)
    head = Head(r)
    if r["beard"] == "short":
        _short_beard(mb, head, mats)
    elif r["beard"] == "braided":
        _braided_beard(mb, head, mats)
    return mb.finish()


_POINTS = {}


def head_points(r, hair_=False, nose=False):
    """Points on the head (skull, face, ears, beard; with the hair and the nose) for fitting gear around it."""
    key = (id(r), hair_, nose)
    if key in _POINTS:
        return _POINTS[key]
    detail, min_part = lp.DETAIL, lp.MIN_PART
    lp.set_lod(1)
    head = Head(r)
    rows = head.star.grid(12, 18)
    points = [head.at(p) for row in rows for p in row]
    for side in (1, -1):
        ear, center = ear_sdf(r["head_shape"], side)
        points += [head.at(p) for row in sc.Star(ear, center).grid(4, 6) for p in row]
    if nose:
        shape, center = nose_sdf(r["head_shape"])
        points += [head.at(p) for row in sc.Star(shape, center).grid(4, 6) for p in row]
    for make, use in ((beard, True), (hair, hair_)):
        if not use:
            continue
        obj = make("_FitPoints", r)
        if obj is not None:
            points += [Vector(v.co) for v in obj.data.vertices]
            import bpy
            mesh = obj.data
            bpy.data.objects.remove(obj)
            bpy.data.meshes.remove(mesh)
    lp.DETAIL, lp.MIN_PART = detail, min_part
    _POINTS[key] = points
    return points


# ---------------------------------------------------------------- limbs


def fist_sdf(k):
    """A fist closed around a handle along Y (the weapon grip), the left hand from the wrist outward (+X)."""
    palm = sc.box((0.045 * k, 0, 0.018 * k), (0.045 * k, 0.041 * k, 0.017 * k), radius=0.012 * k)
    wrist = sc.cone((-0.012 * k, 0, 0.012 * k), (0.02 * k, 0, 0.015 * k), 0.03 * k, 0.032 * k)
    fill = sc.ellipsoid((0.078 * k, 0, -0.006 * k), (0.024 * k, 0.04 * k, 0.022 * k))
    fingers = []
    for y, rad in ((-0.031, 0.0118), (-0.0105, 0.012), (0.0105, 0.0115), (0.031, 0.0105)):
        pts = [(0.08 * k, y * k, 0.021 * k), (0.104 * k, y * k, 0.006 * k), (0.102 * k, y * k, -0.022 * k), (0.078 * k, y * k, -0.032 * k)]
        fingers.append(sc.chain(pts, [rad * k, rad * k * 0.97, rad * k * 0.93, rad * k * 0.88]))
    thumb = sc.chain([(0.018 * k, -0.03 * k, 0.004 * k), (0.046 * k, -0.052 * k, -0.012 * k), (0.074 * k, -0.046 * k, -0.026 * k)],
                     [0.014 * k, 0.012 * k, 0.0105 * k])
    return sc.blend(palm, (wrist, 0.012 * k), (fill, 0.01 * k), *[(f, 0.004 * k) for f in fingers], (thumb, 0.008 * k))


def _fist(mb, r, m, side, s):
    k = r["hand"] / 0.13
    z, wx = r["shoulder_z"], r["wrist_x"]
    shape = fist_sdf(k)
    star = sc.Star(shape, (0.06 * k, 0, 0.002 * k))
    rows = star.grid(lp.res(10, 5), lp.res(14, 6), focus=(math.radians(90), 0.3))
    rows = [[Vector((wx * s + p.x * s, p.y, z + p.z)) for p in row] for row in rows]
    center = Vector((s * (wx + 0.06 * k), 0, z))
    sc.grid_mesh(mb, rows, m["Hands"], side + "Hand", closed=True, inside=lambda p: center)


def _stations(entries):
    """Stations along a limb: (position, level) pairs; level 0 always, 1 from LOD1 up, 2 only at LOD0."""
    keep = 2 if lp.DETAIL >= 0.99 else 1 if lp.DETAIL >= 0.5 else 0
    return [p for p, level in entries if level <= keep]


def arm_sdf(r, s):
    """One arm from the deltoid to the wrist (s = 1 left, -1 right): sleeve and cuff, forearm, bracer."""
    z, sx, ex, wx = r["shoulder_z"], r["shoulder_x"], r["elbow_x"], r["wrist_x"]
    ua, fa, dl = [v * 0.88 for v in r["upper_arm"]], r["forearm"], r["deltoid"]
    L, F = ex - sx, wx - ex

    def P(x, y=0.0, dz=0.0):
        return (s * x, y, z + dz)
    deltoid = sc.ellipsoid(P(sx + dl * 0.2, 0, 0.006), (dl * 1.2, dl * 1.0, dl * 0.92))
    upper = sc.chain([P(sx + 0.01), P(sx + L * 0.5, 0, 0.004), P(ex - 0.005)], [ua[0], ua[1] * 0.92, ua[2]])
    biceps = sc.ellipsoid(P(sx + L * 0.52, -0.008, 0.01), (L * 0.28, ua[1] * 0.95, ua[1] * 1.04))
    triceps = sc.ellipsoid(P(sx + L * 0.45, 0.012, -0.004), (L * 0.3, ua[1] * 0.9, ua[1] * 0.92))
    elbow = sc.sphere(P(ex), ua[2] * 0.96)
    sleeve = sc.blend(deltoid, (upper, 0.012), (biceps, 0.02), (triceps, 0.02), (elbow, 0.02))
    if r.get("fins"):
        forearm = sc.chain([P(ex - 0.02), P(ex + F * 0.3), P(wx + 0.005)], [fa[0], fa[1], fa[2]])
        return sc.blend(sleeve, (forearm, 0.025))
    forearm = sc.blend(sc.chain([P(ex - 0.02), P(ex + F * 0.3, 0, 0.003), P(wx - 0.07)], [fa[0], fa[1], fa[2]]),
                       (sc.ellipsoid(P(ex + F * 0.26, 0.002, 0.004), (F * 0.28, fa[1] * 1.0, fa[1] * 0.97)), 0.02))
    cuff = sc.capped(P(ex - 0.052), P(ex + 0.002), ua[2] * 1.2, ua[2] * 1.16, rounding=0.008)
    bracer = sc.capped(P(wx - 0.096), P(wx - 0.004), fa[2] * 1.08, fa[2] * 1.34, rounding=0.006)
    trim = sc.capped(P(wx - 0.025), P(wx + 0.004), fa[2] * 1.42, fa[2] * 1.42, rounding=0.006)
    return sc.union(sc.blend(sleeve, (forearm, 0.025)), cuff, bracer, trim)


def _arm(mb, r, m, side, s):
    """The arm as one swept surface (bending at the elbow), materials by length: Chest, ChestTrim, Arms, ArmsTrim; the fist."""
    z, sx, ex, wx, dl = r["shoulder_z"], r["shoulder_x"], r["elbow_x"], r["wrist_x"], r["deltoid"]
    L, F = ex - sx, wx - ex
    fins = r.get("fins")
    xs = [(sx - dl * 0.85, 0), (sx - dl * 0.45, 1), (sx, 0), (sx + dl * 0.5, 2), (sx + dl * 0.95, 1), (sx + L * 0.4, 0),
          (sx + L * 0.55, 2), (sx + L * 0.7, 1)]
    if fins:
        xs += [(ex - 0.03, 0), (ex, 1), (ex + 0.03, 2), (ex + F * 0.3, 0), (ex + F * 0.55, 1), (ex + F * 0.8, 2), (wx + 0.004, 0)]
    else:
        xs += [(ex - 0.058, 0), (ex - 0.048, 0), (ex - 0.025, 2), (ex - 0.002, 0), (ex + 0.007, 0), (ex + F * 0.22, 2),
               (ex + F * 0.4, 1), (ex + F * 0.55, 2), (wx - 0.101, 0), (wx - 0.091, 0), (wx - 0.06, 2), (wx - 0.029, 1),
               (wx - 0.021, 0), (wx + 0.002, 0)]
    path = [Vector((s * x, 0, z)) for x in _stations(xs)]
    shape = arm_sdf(r, s)
    rows = sc.sweep(shape, path, lp.res(12, 6))
    upper, lower = side + "UpperArm", side + "LowerArm"

    def weigh(p):
        t = smooth(ex - 0.035, ex + 0.035, abs(p.x))
        return {upper: 1.0 - t, lower: t}

    def material_at(c):
        x = abs(c.x)
        if fins:
            return m["Arms"] if x > ex else None
        if x < ex - 0.053:
            return None
        if x < ex + 0.0025:
            return m["ChestTrim"]
        return m["ArmsTrim"] if x > wx - 0.025 else m["Arms"]
    sc.grid_mesh(mb, rows, m["Chest"], weigh, closed=True, material_at=material_at,
                 inside=lambda c: Vector((c.x, 0, z)))
    if not fins and lp.DETAIL > 0.5:
        # Laces across the top of the bracer.
        fa = r["forearm"]
        for f in (0.3, 0.62):
            x = wx - 0.092 + 0.07 * f
            top = z + fa[2] * (1.09 + 0.24 * f) + 0.002
            mb.tube([((s * (x - 0.008), -0.012, top - 0.002), 0.0035), ((s * (x + 0.008), 0.012, top - 0.002), 0.0035)],
                    m["ArmsTrim"], lower, sides=4)
    _fist(mb, r, m, side, s)


def boot_sdf(r, x):
    """A boot's foot (left or right at x): a rounded heel, the instep, a broad toe cap, a flat sole and a heel block."""
    fw, fl, fh = r["foot"]
    hw = fw / 2
    ball = -fl * 0.55
    heel = sc.ellipsoid((x, 0.03, 0.055), (hw * 0.82, 0.06, 0.055))
    instep = sc.cone((x, 0.0, fh * 0.75), (x, ball, fh * 0.55), hw * 0.85, hw * 0.92)
    toe = sc.ellipsoid((x, -fl * 0.74, fh * 0.42), (hw * 1.0, fl * 0.2, fh * 0.42))
    sole = sc.box((x, (0.085 - fl * 0.92) / 2, 0.016), (hw * 1.06, (0.085 + fl * 0.92) / 2, 0.016), radius=0.012)
    heel_block = sc.box((x, 0.045, 0.02), (hw * 0.88, 0.045, 0.02), radius=0.008)
    shape = sc.blend(heel, (instep, 0.03), (toe, 0.03), (sole, 0.012), (heel_block, 0.006))
    return sc.intersect(shape, sc.Shape(lambda p: -p[:, 2]))


def _boot_foot(mb, r, m, side, x):
    fw, fl, fh = r["foot"]
    ball = -fl * 0.55
    shape = boot_sdf(r, x)
    path = [Vector((x, 0.06, fh * 0.6)), Vector((x, 0.0, fh * 0.55)), Vector((x, ball * 0.6, fh * 0.45)),
            Vector((x, ball, fh * 0.4)), Vector((x, -fl * 0.75, fh * 0.32))]
    rows = sc.sweep(shape, path, lp.res(14, 6))
    foot, toes = side + "Foot", side + "Toes"

    def weigh(p):
        t = smooth(ball + 0.025, ball - 0.025, p.y)
        return {foot: 1.0 - t, toes: t}

    def material_at(c):
        return m["FeetTrim"] if c.z < 0.03 else None

    pts = path

    def inside(c):
        return min(pts, key=lambda q: (q - c).length)
    sc.grid_mesh(mb, rows, m["Feet"], weigh, closed=True, inside=inside, material_at=material_at)
    # A strap with a buckle over the instep.
    y = -fl * 0.05
    strap = [Vector((x + math.cos(a) * fw * 0.52, y, fh * 0.5 + math.sin(a) * fh * 0.62)) for a in
             (math.radians(d) for d in (-10, 30, 70, 110, 150, 190))]
    mb.tube([(p, (0.012, 0.006)) for p in strap], m["FeetTrim"], foot, sides=4, smooth=True)
    mb.box(strap[1] + Vector((math.copysign(0.006, x), -0.002, 0)), (0.006, 0.022, 0.02), m["BuckleMetal"], foot)


def leg_sdf(r, x):
    """One leg from the hip down to the ankle: thigh, knee and calf; the boot's shaft and folded cuff."""
    hip, knee, ankle, top = r["hip"], r["knee"], r["ankle"], r["boot_top"]
    th, sh, bt = r["thigh"], r["shin"], r["boot"]
    L, S = hip - knee, knee - top
    thigh = sc.blend(sc.chain([(x, 0, hip + 0.03), (x, -0.004, hip - L * 0.4), (x, 0, knee + 0.04)], [th[0], th[1] * 1.03, th[2] * 1.02]),
                     (sc.ellipsoid((x, -0.008, hip - L * 0.38), (th[1] * 0.98, th[1] * 1.06, L * 0.32)), 0.03))
    knee_cap = sc.ellipsoid((x, -th[2] * 0.6, knee), (th[2] * 0.72, th[2] * 0.5, th[2] * 0.8))
    if r.get("barefoot"):
        shin = sc.chain([(x, 0, knee + 0.01), (x, 0.012, knee - (knee - ankle) * 0.3), (x, 0.01, ankle - 0.02)],
                        [sh[0], sh[1] * 1.04, sh[2] * 0.75])
        return sc.blend(thigh, (knee_cap, 0.02), (shin, 0.03))
    shin = sc.blend(sc.chain([(x, 0, knee + 0.01), (x, 0.01, knee - S * 0.3), (x, 0, top)], [sh[0], sh[1] * 1.02, sh[2]]),
                    (sc.ellipsoid((x, 0.022, knee - S * 0.32), (sh[1] * 0.9, sh[1] * 0.95, S * 0.3)), 0.02))
    leg = sc.blend(thigh, (knee_cap, 0.02), (shin, 0.03))
    shaft = sc.capped((x, 0.004, top + 0.012), (x, 0.01, ankle - 0.03), bt[0], bt[0] * 0.94, rounding=0.006)
    cuff = sc.capped((x, 0.003, top + 0.062), (x, 0.0, top - 0.014), bt[1] * 1.04, bt[1] * 0.98, rounding=0.01)
    return sc.union(leg, shaft, cuff)


def _leg(mb, r, m, side, s):
    """
    The leg as one swept surface (bending at the knee): thigh (Legs), knee and shin (LegsLower: tinted with
    the legs, but stays when a robe hides the thighs), the boot's shaft (Feet) and cuff (FeetTrim); the foot.
    """
    x = s * r["leg_x"]
    hip, knee, ankle, top = r["hip"], r["knee"], r["ankle"], r["boot_top"]
    L, S = hip - knee, knee - top
    barefoot = r.get("barefoot")
    zs = [(hip + 0.05, 0), (hip, 1), (hip - L * 0.2, 2), (hip - L * 0.4, 0), (hip - L * 0.6, 2), (hip - L * 0.8, 1),
          (knee + 0.04, 0), (knee, 1), (knee - 0.04, 2)]
    if barefoot:
        zs += [(knee - (knee - ankle) * 0.3, 0), (knee - (knee - ankle) * 0.65, 1), (ankle, 0)]
    else:
        zs += [(knee - S * 0.3, 0), (knee - S * 0.65, 2), (top + 0.08, 1), (top + 0.068, 0), (top + 0.056, 0), (top + 0.025, 2),
               (top - 0.008, 1), (top - 0.02, 0), (top - 0.034, 0), ((top + ankle) / 2, 1), (ankle + 0.02, 2), (ankle - 0.02, 0)]
    path = [Vector((x, 0, z)) for z in _stations(zs)]
    rows = sc.sweep(leg_sdf(r, x), path, lp.res(12, 6), up=(0, -1, 0))
    upper, lower = side + "UpperLeg", side + "LowerLeg"

    def weigh(p):
        t = smooth(knee + 0.035, knee - 0.035, p.z)
        return {upper: 1.0 - t, lower: t}

    def material_at(c):
        if c.z > knee + 0.05:
            return None
        if barefoot or c.z > top + 0.062:
            return m["LegsLower"]
        return m["FeetTrim"] if c.z > top - 0.027 else m["Feet"]
    sc.grid_mesh(mb, rows, m["Legs"], weigh, closed=True, material_at=material_at, inside=lambda c: Vector((x, 0, c.z)))
    if barefoot:
        _webbed_foot(mb, r, m, side, x)
        return
    _boot_foot(mb, r, m, side, x)
def _webbed_foot(mb, r, m, side, x):
    """A flat webbed foot with three clawed toes (Feet; claws FeetTrim)."""
    fw, fl, fh = r["foot"]
    hw, ball = fw / 2, -fl * 0.5
    heel = [(0.035, 0.06, 0.0), (0.035, 0.05, fh), (0.045, -0.02, fh * 1.2), (hw * 0.6, ball, 0.0), (hw * 0.55, ball, fh * 0.8)]
    mb.hull([(x + px, py, pz) for px, py, pz in lp.mirrored(heel)], m["Feet"], side + "Foot")
    web = [(hw * 0.6, ball, 0.0), (hw * 0.55, ball, fh * 0.8), (hw, -fl * 0.82, 0.0), (hw * 0.9, -fl * 0.8, 0.02),
           (0, -fl * 0.92, 0.0), (0, -fl * 0.9, 0.025)]
    mb.hull([(x + px, py, pz) for px, py, pz in lp.mirrored(web)], m["Feet"], side + "Toes")
    for tx in (-hw * 0.85, 0.0, hw * 0.85):
        base = (x + tx, -fl * (0.86 if tx else 0.9), 0.012)
        mb.tube([(base, 0.014), ((base[0] + tx * 0.15, base[1] - 0.045, 0.004), 0.003)], m["FeetTrim"], side + "Toes", sides=4)


# ---------------------------------------------------------------- models


def materials(r):
    return {k: lp.material(k, v, metallic=0.6 if "Metal" in k else 0.0, roughness=0.5 if "Metal" in k else 0.85,
                           emission=2.0 if "Glow" in k else None)
            for k, v in r["colors"].items()}


def body(name, r):
    """The body mesh in T-pose (skin, face, the clothes under any gear); hair and beard are meshes of their own."""
    mats = materials(r)
    mb = lp.MeshBuilder(name)
    _torso(mb, r, mats)
    _head(mb, r, mats)
    for side, s in SIDES:
        _arm(mb, r, mats, side, s)
        _leg(mb, r, mats, side, s)
    if r.get("fins"):
        _body_fins(mb, r, mats)
    return mb.finish()


def build_race(race, lods=(0, 1, 2)):
    """
    Armature, body, hair, beard and the gear pieces (gear.py) for one race (no animations), at each level
    of detail: meshes named <name>_LOD<n>, which Unity groups into a LOD group.
    """
    import gear  # gear.py builds on this module's tables

    r = RACES[race]
    arm = lp.armature("Armature", skeleton(r))
    for level in lods:
        lp.set_lod(level)
        suffix = "_LOD%d" % level
        parts = [body(race + "Body" + suffix, r), hair(race + "Hair" + suffix, r), beard(race + "Beard" + suffix, r)]
        for part in parts:
            if part is not None:
                lp.bind(part, arm)
        for piece in gear.build_all(r, r.get("gear")):
            piece.name += suffix
            piece.data.name = piece.name
            lp.bind(piece, arm)
    lp.set_lod(0)
    return arm


# ---------------------------------------------------------------- animations

def side_pose(pose, bone, left_rotations):
    """Sets Left<bone> and the mirrored Right<bone>."""
    pose["Left" + bone] = list(left_rotations)
    pose["Right" + bone] = lp.mirror(left_rotations)


def grip(pose, degrees):
    """Bends the main hand's wrist so a held weapon points down: 0 leaves it level with the forearm's bend."""
    pose["RightHand"] = lp.mirror([("Z", degrees)])


def stance(arm_down=74.0, elbow=14.0):
    """Relaxed standing: arms down, elbows a little bent, feet a little apart, a weapon pointing forward and down."""
    pose = {}
    side_pose(pose, "UpperArm", [("Y", arm_down)])
    side_pose(pose, "LowerArm", [("Z", -elbow)])
    side_pose(pose, "UpperLeg", [("Y", -3)])
    side_pose(pose, "Foot", [("Y", 3)])
    grip(pose, 45)
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


def gait(t, swing, knee_swing, knee_stance, arm_swing, elbow, lean, bob, bounce_height, wrist=58):
    """A two-step cycle; at t = 0 the left foot lands in front. `wrist`: see grip."""
    pose = {}
    grip(pose, wrist)
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
    return gait(t, swing=40, knee_swing=85, knee_stance=18, arm_swing=38, elbow=75, lean=11, bob=0.09, bounce_height=0.07, wrist=64)


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
