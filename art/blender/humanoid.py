"""
Humanoid races and the shared humanoid animation set.

Every race uses the same bone names (Unity's humanoid names, so Unity maps the avatar by itself) in a
T-pose. Animations are made once on the Brightvale Human skeleton and exported on their own
(HumanoidAnimations.fbx); Unity retargets them to every race.

Material names are the gear regions the client tints: Skin, Hair (the hair on the scalp, hidden under
helmets and hoods), Beard (beard and brows, tinted with the hair), Eyes, Chest (torso and sleeves), Arms
(forearms), Hands, Waist, Legs (hips and thighs: hidden under robes) with LegsLower (knees and shins) and
Feet. A region's "Trim" material (ChestTrim, FeetTrim ...) is its darker second tone: collars, cuffs, soles.
BuckleMetal keeps its color.

The style is stylized and heroic, after classic fantasy MMOs: broad shoulders from big deltoids, a
tapering torso, chunky fists and boots, a strong brow and a big nose.
"""

import math

from mathutils import Vector

import lowpoly as lp

# Joint heights and widths in meters. Z up, the model faces -Y, +X is its left.
# Body cross-sections are (z, width, depth[, front, back]), bottom to top (see lp.ring).
RACES = {
    "BrightvaleHuman": dict(
        ankle=0.09, knee=0.5, hip=0.93, spine=1.06, chest=1.24, neck=1.47, head=1.57, top=1.84,
        leg_x=0.105, shoulder_z=1.43, clavicle_x=0.05, shoulder_x=0.215, elbow_x=0.48, wrist_x=0.72, hand=0.14,
        foot=(0.13, 0.27, 0.1),
        pelvis=[(0.83, 0.22, 0.17), (0.91, 0.31, 0.215), (0.99, 0.31, 0.215), (1.05, 0.29, 0.205)],
        belly=[(0.99, 0.29, 0.205), (1.11, 0.30, 0.215), (1.22, 0.31, 0.225)],
        torso=[(1.16, 0.31, 0.22), (1.26, 0.33, 0.235, 1.15), (1.36, 0.335, 0.235, 1.1), (1.445, 0.31, 0.205), (1.505, 0.19, 0.15)],
        collar=[(1.47, 0.205, 0.17), (1.52, 0.16, 0.135)],
        tunic=[(0.9, 0.4, 0.27), (1.0, 0.315, 0.225)],
        belt=[(0.995, 0.325, 0.235), (1.065, 0.32, 0.23)],
        buckle=(0.06, 0.05),
        neck_r=0.07, deltoid=0.074, upper_arm=(0.07, 0.076, 0.058), forearm=(0.057, 0.064, 0.05),
        thigh=(0.108, 0.1, 0.075), shin=(0.072, 0.08, 0.06), boot=(0.077, 0.093), boot_top=0.31,
        head_shape=dict(w=1.0, d=1.0, h=1.0, nose=1.0, brow=1.0, ear=1.0, jaw=1.0),
        hair="swept", beard=False,
        colors=dict(Skin="#e3b08a", Eyes="#1d1b22", EyeWhite="#e6dccb", Chest="#8c6e4b", ChestTrim="#5e4832", Arms="#6e5238",
                    ArmsTrim="#4a3624", Hands="#e3b08a", Waist="#4b3424", BuckleMetal="#c9a24a", Legs="#4e5868", LegsLower="#4e5868",
                    Feet="#3e2c20", FeetTrim="#2a1e16", Beard="#5b3920", Hair="#5b3920"),
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
        tunic=[(0.6, 0.5, 0.37), (0.73, 0.455, 0.33)],
        belt=[(0.725, 0.465, 0.345), (0.815, 0.455, 0.335)],
        buckle=(0.09, 0.075),
        neck_r=0.085, deltoid=0.094, upper_arm=(0.088, 0.094, 0.075), forearm=(0.076, 0.084, 0.066),
        thigh=(0.12, 0.112, 0.085), shin=(0.085, 0.09, 0.072), boot=(0.092, 0.11), boot_top=0.24,
        head_shape=dict(w=1.16, d=1.08, h=1.04, nose=1.55, brow=1.5, ear=1.25, jaw=1.12),
        hair="topknot", beard=True,
        colors=dict(Skin="#d49a78", Eyes="#1d1b22", EyeWhite="#e6dccb", Chest="#6f7a5a", ChestTrim="#4a5240", Arms="#5a4632",
                    ArmsTrim="#3e3022", Hands="#d49a78", Waist="#4b3424", BuckleMetal="#c9a24a", BeardMetal="#d0a848", Legs="#5e4a3a",
                    LegsLower="#5e4a3a",
                    Feet="#3e2c20", FeetTrim="#2a1e16", Beard="#a4512a", Hair="#a4512a"),
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
        head_shape=dict(w=1.3, d=1.25, h=1.15, nose=1.0, brow=1.0, ear=1.0, jaw=1.0),
        hair="none", beard=False, head_style="fish", barefoot=True, fins=True, gear=("RobeSkirt", "RobeCuffs"),
        colors=dict(Skin="#4f8a7a", Eyes="#141814", EyeGlow="#f0e070", Belly="#b8c99a", Chest="#4f8a7a", ChestTrim="#3a6a5c",
                    Arms="#4f8a7a", ArmsTrim="#3a6a5c", Hands="#4f8a7a", Waist="#5a6a30", BuckleMetal="#c9c2a0",
                    Legs="#6a9a80", LegsLower="#4f8a7a", Feet="#3f6a5a", FeetTrim="#2f5048", Hair="#2f5a4a"),
    ),
}

SIDES = (("Left", 1.0), ("Right", -1.0))

# The head, relative to the head joint (x >= 0 half, mirrored): skull, jaw (widened by `jaw`) and nose.
SKULL = [(0, 0.0, 0.255), (0.075, -0.03, 0.25), (0.075, 0.07, 0.245), (0, 0.11, 0.23), (0, 0.135, 0.15),
         (0.09, 0.1, 0.16), (0, 0.095, 0.025), (0.072, 0.075, 0.035), (0.112, 0.01, 0.19), (0.112, 0.04, 0.12),
         (0.07, -0.105, 0.22), (0, -0.115, 0.21), (0.09, -0.112, 0.165), (0, -0.122, 0.165), (0.1, -0.075, 0.115)]
JAW = [(0.086, -0.06, 0.05), (0.092, 0.02, 0.05), (0.035, -0.1, 0.005), (0, -0.106, 0.01), (0.03, -0.075, -0.012),
       (0.05, 0.0, 0.015)]
NOSE = [(0, -0.118, 0.155), (0.012, -0.118, 0.145), (0, -0.158, 0.095), (0.022, -0.128, 0.09), (0, -0.13, 0.085),
        (0.016, -0.142, 0.105)]
# Hair: a cap over the skull (the front hairline per style), and locks as (root, tip, width).
HAIR_CAP = [(0, 0.0, 0.268), (0.08, -0.04, 0.262), (0.082, 0.07, 0.256), (0, 0.12, 0.243), (0, 0.146, 0.15),
            (0.097, 0.108, 0.16), (0, 0.108, 0.03), (0.078, 0.086, 0.04), (0.119, 0.01, 0.19), (0.118, 0.045, 0.12)]
HAIRLINE = {
    "swept": [(0.116, -0.05, 0.175), (0.085, -0.11, 0.21), (0, -0.12, 0.22)],
    "topknot": [(0.112, -0.03, 0.2), (0.07, -0.08, 0.245), (0, -0.09, 0.255)],
}
SWEPT_LOCKS = [((0.0, -0.112, 0.232), (0.012, 0.0, 0.292), 0.09), ((0.058, -0.1, 0.228), (0.078, 0.02, 0.278), 0.075),
               ((-0.055, -0.102, 0.23), (-0.075, 0.015, 0.28), 0.075), ((0.025, -0.04, 0.262), (0.03, 0.11, 0.272), 0.085),
               ((-0.04, -0.035, 0.262), (-0.05, 0.115, 0.268), 0.085), ((0.075, 0.04, 0.245), (0.092, 0.155, 0.18), 0.07),
               ((-0.07, 0.045, 0.246), (-0.088, 0.16, 0.176), 0.07), ((0.0, 0.08, 0.25), (0.0, 0.178, 0.15), 0.085),
               ((0.105, -0.02, 0.21), (0.126, 0.08, 0.16), 0.055), ((-0.105, -0.02, 0.21), (-0.126, 0.085, 0.158), 0.055)]
BEARD = [(0.112, 0.0, 0.15), (0.112, 0.035, 0.1), (0.104, -0.07, 0.1), (0.1, -0.03, 0.03), (0.098, -0.095, 0.035),
         (0.05, -0.13, 0.06), (0, -0.15, 0.0), (0.065, -0.145, -0.02), (0.085, -0.135, -0.1), (0, -0.18, -0.12),
         (0.05, -0.18, -0.22), (0, -0.19, -0.26), (0.06, -0.02, -0.03), (0.0, -0.07, -0.12), (0.03, -0.12, -0.22)]
MUSTACHE = [(0.075, -0.13, 0.075), (0.02, -0.148, 0.092), (0, -0.152, 0.09), (0.095, -0.13, 0.03), (0.03, -0.142, 0.066)]
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
# A loose fist around a handle along Y (the weapon grip), the left hand, from the wrist outward (+X).
FIST = [(0, 0.032, 0.024), (0, -0.032, 0.024), (0, 0.032, -0.024), (0, -0.032, -0.024), (0.06, -0.044, 0.03),
        (0.06, 0.04, 0.028), (0.095, -0.042, 0.022), (0.095, 0.038, 0.02), (0.105, -0.04, -0.01), (0.105, 0.036, -0.01),
        (0.09, -0.04, -0.042), (0.09, 0.034, -0.04), (0.05, -0.038, -0.045), (0.05, 0.032, -0.043),
        (0.025, -0.035, -0.035), (0.025, 0.035, -0.035)]
THUMB = [(0.02, -0.034, 0.004), (0.05, -0.058, 0.014), (0.076, -0.056, 0.0), (0.07, -0.05, -0.02), (0.03, -0.044, -0.018)]


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
    skull_top = r["head"] + SKULL[0][2] * r["head_shape"]["h"]
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


def _torso(mb, r, m):
    """Pelvis (Legs), belly and chest (Chest), the collar, the tunic's hem, the belt and its buckle."""
    mb.loft(_rings(r["pelvis"]), m["Legs"], "Hips")
    mb.loft(_rings(r["belly"]), m["Chest"], "Spine")
    mb.loft(_rings(r["torso"], sides=10), m["Chest"], "Chest")
    mb.loft(_rings(r["collar"], sides=10), m["ChestTrim"], "Chest")
    mb.loft(_rings(r["tunic"], sides=10, square=0.15), m["Chest"], "Hips")
    belt = _rings(r["belt"], sides=10, square=0.35)
    mb.loft(belt, m["Waist"], "Hips")
    bw, bh = r["buckle"]
    z = (r["belt"][0][0] + r["belt"][1][0]) / 2
    front = min(p[1] for p in belt[0])
    mb.box((0, front - 0.008, z), (bw, 0.02, bh), m["BuckleMetal"], "Hips", top=(0.85, 1.0))
    # A pouch on the belt, at the back of the left hip.
    pw = r["belt"][0][1] / 2
    mb.box((pw * 0.72, r["belt"][0][2] * 0.32, z - bh * 0.3), (bw * 0.9, bw * 0.7, bh * 1.3), m["Waist"], "Hips",
           top=(0.9, 0.8), rotation=(0, 0, 30))


def _head(mb, r, m):
    """Skull and jaw, nose, brow, eyes, mouth and ears; the neck below."""
    s = r["head_shape"]
    w, d, h = s["w"], s["d"], s["h"]

    def at(x, y, z):
        return (x * w, y * d, r["head"] + z * h)

    mb.tube([((0, 0.006, r["neck"] - 0.04), r["neck_r"]), ((0, 0.012, r["head"] + 0.06), r["neck_r"] * 0.9)],
            m["Skin"], "Neck", sides=7)
    if r.get("head_style") == "fish":
        _fish_head(mb, r, m, at)
        return
    points = [at(*p) for p in lp.mirrored(SKULL)] + [at(x * s["jaw"], y, z) for x, y, z in lp.mirrored(JAW)]
    mb.hull(points, m["Skin"], "Head")
    nose, root = s["nose"], (0, -0.12, 0.15)
    mb.hull([at(root[0] + (x - root[0]) * nose * s.get("nose_w", 1.0), root[1] + (y - root[1]) * nose, root[2] + (z - root[2]) * nose)
             for x, y, z in lp.mirrored(NOSE)], m["Skin"], "Head")
    brow = s["brow"]
    mb.box(at(0, -0.121, 0.165), (0.19 * w, 0.026 * d, 0.024 * brow), m["Skin"], "Head", top=(0.9, 0.7))
    for side in (1, -1):
        mb.box(at(side * 0.046, -0.131, 0.177), (0.056 * w, 0.016 * brow, 0.014 * brow), m["Beard"], "Head",
               rotation=(0, -side * 14, 0))
        mb.box(at(side * 0.044, -0.112, 0.136), (0.036 * w, 0.014, 0.02), m["EyeWhite"], "Head", top=(0.8, 1.0))
        mb.box(at(side * 0.041, -0.118, 0.135), (0.015 * w, 0.006, 0.018), m["Eyes"], "Head")
        mb.hull(ear(r, side), m["Skin"], "Head")
    mb.box(at(0, -0.1, 0.045), (0.036 * w, 0.008, 0.006), m["Eyes"], "Head")
    if r["beard"]:
        mb.hull([at(*p) for p in lp.mirrored(BEARD)], m["Beard"], "Head")
        mb.hull([at(*p) for p in lp.mirrored(MUSTACHE)], m["Beard"], "Head")
        # Two braids from the beard's tip, bound with metal rings.
        for side in (1, -1):
            top, bottom = at(side * 0.035, -0.17, -0.2), at(side * 0.04, -0.17, -0.36)
            mb.tube([(top, 0.026), (bottom, 0.018)], m["Beard"], "Head", sides=5)
            ring_z = bottom[2] + 0.03
            mb.tube([((bottom[0], bottom[1], ring_z + 0.018), 0.024), ((bottom[0], bottom[1], ring_z - 0.018), 0.024)],
                    m["BeardMetal"], "Head", sides=6)


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
        mb.sphere(eye, 0.038 * w, m["EyeGlow"], "Head", segments=8, rings=5)
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


def head_at(r):
    """Head-relative coordinates (the tables above) to model space for a race."""
    s = r["head_shape"]
    return lambda x, y, z: (x * s["w"], y * s["d"], r["head"] + z * s["h"])


def ear(r, side):
    """The points of one ear (side 1 = left), in model space."""
    at, e = head_at(r), r["head_shape"]["ear"]
    return [at(side * x, y, z) for x, y, z in ((0.104, 0.0, 0.145), (0.104 + 0.022 * e, 0.03, 0.165),
                                               (0.104 + 0.018 * e, 0.045, 0.1), (0.102, 0.03, 0.09), (0.1, 0.012, 0.12))]


def extent(r, z):
    """The trunk at height z over all its sections: (half width, front, back); the front is negative Y."""
    half, front, back = 0.0, 0.0, 0.0
    for key in ("pelvis", "belly", "torso", "collar", "tunic", "belt"):
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


def _hair(mb, r, m):
    """The hair on the scalp (material Hair: the client hides it under helmets and hoods)."""
    s = r["head_shape"]
    w, d, h = s["w"], s["d"], s["h"]

    def at(x, y, z):
        return (x * w, y * d, r["head"] + z * h)

    if r["hair"] == "none":
        return
    mb.hull([at(*p) for p in lp.mirrored(HAIR_CAP + HAIRLINE[r["hair"]])], m["Hair"], "Head")
    if r["hair"] == "swept":
        for root, tip, width in SWEPT_LOCKS:
            rx, ry, rz = root
            mb.hull([at(rx - width / 2, ry, rz - 0.01), at(rx + width / 2, ry, rz - 0.01), at(rx, ry - width * 0.35, rz - 0.012),
                     at(rx, ry + width * 0.4, rz - 0.015), at(rx, ry, rz + 0.012), at(*tip)], m["Hair"], "Head")
        for side in (1, -1):
            mb.box(at(side * 0.111, -0.03, 0.135), (0.016, 0.032 * d, 0.065 * h), m["Hair"], "Head")
    elif r["hair"] == "topknot":
        mb.sphere(at(0, 0.03, 0.29), (0.05 * w, 0.05 * d, 0.045 * h), m["Hair"], "Head", segments=7, rings=4)
        mb.tube([(at(0, 0.03, 0.262), 0.035 * w), (at(0, 0.03, 0.28), 0.033 * w)], m["Hair"], "Head", sides=6)


def _arm(mb, r, m, side, s):
    """Deltoid and sleeve (Chest, a ChestTrim cuff), forearm with a flared bracer (Arms), the fist (Hands)."""
    z, sx, ex, wx = r["shoulder_z"], r["shoulder_x"], r["elbow_x"], r["wrist_x"]
    ua, fa = r["upper_arm"], r["forearm"]
    upper, lower, hand = side + "UpperArm", side + "LowerArm", side + "Hand"
    dl = r["deltoid"]
    mb.sphere((s * (sx + dl * 0.15), 0, z + 0.004), (dl * 1.15, dl * 0.95, dl * 0.85), m["Chest"], upper, segments=8, rings=6)
    mb.tube([((s * (sx + 0.01), 0, z), ua[0]), ((s * (sx + (ex - sx) * 0.45), 0, z), (ua[1] * 0.95, ua[1])),
             ((s * (ex - 0.005), 0, z), ua[2])], m["Chest"], upper, sides=7)
    if r.get("fins"):
        # Bare scaled arms: no cuffs or bracers.
        mb.tube([((s * (ex - 0.02), 0, z), fa[0]), ((s * (ex + (wx - ex) * 0.3), 0, z), (fa[1], fa[1] * 0.95)),
                 ((s * (wx + 0.005), 0, z), fa[2])], m["Arms"], lower, sides=7)
    else:
        mb.tube([((s * (ex - 0.045), 0, z), ua[2] * 1.12), ((s * (ex + 0.002), 0, z), ua[2] * 1.12)], m["ChestTrim"], upper, sides=7)
        mb.tube([((s * (ex - 0.02), 0, z), fa[0]), ((s * (ex + (wx - ex) * 0.3), 0, z), (fa[1], fa[1] * 0.95)),
                 ((s * (wx - 0.07), 0, z), fa[2])], m["Arms"], lower, sides=7)
        mb.tube([((s * (wx - 0.09), 0, z), fa[2] * 1.06), ((s * (wx - 0.005), 0, z), fa[2] * 1.32)], m["Arms"], lower, sides=7)
        mb.tube([((s * (wx - 0.022), 0, z), fa[2] * 1.38), ((s * (wx + 0.004), 0, z), fa[2] * 1.38)], m["ArmsTrim"], lower, sides=7)
    k = r["hand"] / 0.13
    for points in (FIST, THUMB):
        mb.hull([(s * (wx + x * k), y * k, z + zz * k) for x, y, zz in points], m["Hands"], hand)


def _leg(mb, r, m, side, s):
    """
    Thigh (Legs), knee and shin (LegsLower: tinted with the legs, but stays when a robe hides the thighs),
    the boot with a folded cuff (FeetTrim), foot and toe cap on soles.
    """
    x = s * r["leg_x"]
    hip, knee, ankle, top = r["hip"], r["knee"], r["ankle"], r["boot_top"]
    th, sh, bt = r["thigh"], r["shin"], r["boot"]
    mb.tube([((x, 0, hip + 0.03), (th[0], th[0] * 1.05)), ((x, 0, hip - (hip - knee) * 0.35), (th[1], th[1] * 1.06)),
             ((x, 0, knee + 0.04), (th[2], th[2] * 1.05)), ((x, 0, knee - 0.02), th[2] * 0.95)], m["Legs"], side + "UpperLeg", sides=7)
    mb.sphere((x, -th[2] * 0.6, knee), (th[2] * 0.72, th[2] * 0.5, th[2] * 0.8), m["LegsLower"], side + "LowerLeg", segments=6, rings=4)
    if r.get("barefoot"):
        _webbed_foot(mb, r, m, side, x)
        return
    mb.tube([((x, 0, knee + 0.01), sh[0]), ((x, 0.012, knee - (knee - ankle) * 0.3), (sh[1], sh[1] * 1.1)),
             ((x, 0, top), sh[2])], m["LegsLower"], side + "LowerLeg", sides=7)
    mb.tube([((x, 0, top + 0.01), bt[0]), ((x, 0.004, ankle + 0.06), bt[0] * 0.94), ((x, 0, ankle - 0.03), bt[0] * 0.96)],
            m["Feet"], side + "LowerLeg", sides=7)
    mb.tube([((x, 0, top + 0.055), bt[1]), ((x, 0, top - 0.012), bt[1] * 0.92)], m["FeetTrim"], side + "LowerLeg", sides=7)

    fw, fl, fh = r["foot"]
    ball = -fl * 0.55
    hw = fw / 2
    sole = 0.022
    foot = [(hw * 0.84, fl * 0.18, sole), (hw * 0.84, fl * 0.18, fh * 0.9), (hw * 0.96, -fl * 0.1, fh * 1.05),
            (hw, ball, sole), (hw * 0.92, ball, fh * 0.7)]
    toe = [(hw, ball, sole), (hw * 0.92, ball, fh * 0.7), (hw * 0.82, -fl * 0.84, sole), (hw * 0.66, -fl * 0.86, fh * 0.55),
           (0, -fl * 0.96, fh * 0.28), (0, -fl * 0.95, sole + 0.01)]
    for points, bone in ((foot, side + "Foot"), (toe, side + "Toes")):
        mb.hull([(x + px, py, pz) for px, py, pz in lp.mirrored(points)], m["Feet"], bone)
        outline = [(px * 1.04, py * (1.02 if py < 0 else 1.06), 0.0) for px, py, pz in points if pz <= sole + 0.011]
        mb.hull([(x + px, py, pz) for px, py, _ in lp.mirrored(outline) for pz in (0.0, sole + 0.004)], m["FeetTrim"], bone)


def _webbed_foot(mb, r, m, side, x):
    """A bare shin down to a flat webbed foot with three clawed toes (Feet; claws FeetTrim)."""
    knee, ankle, sh = r["knee"], r["ankle"], r["shin"]
    fw, fl, fh = r["foot"]
    mb.tube([((x, 0, knee + 0.01), sh[0]), ((x, 0.012, knee - (knee - ankle) * 0.3), (sh[1], sh[1] * 1.1)),
             ((x, 0.01, ankle + 0.03), sh[2] * 0.85), ((x, 0.01, ankle - 0.02), sh[2] * 0.75)], m["LegsLower"], side + "LowerLeg", sides=7)
    hw, ball = fw / 2, -fl * 0.5
    heel = [(0.035, 0.06, 0.0), (0.035, 0.05, fh), (0.045, -0.02, fh * 1.2), (hw * 0.6, ball, 0.0), (hw * 0.55, ball, fh * 0.8)]
    mb.hull([(x + px, py, pz) for px, py, pz in lp.mirrored(heel)], m["Feet"], side + "Foot")
    web = [(hw * 0.6, ball, 0.0), (hw * 0.55, ball, fh * 0.8), (hw, -fl * 0.82, 0.0), (hw * 0.9, -fl * 0.8, 0.02),
           (0, -fl * 0.92, 0.0), (0, -fl * 0.9, 0.025)]
    mb.hull([(x + px, py, pz) for px, py, pz in lp.mirrored(web)], m["Feet"], side + "Toes")
    for tx in (-hw * 0.85, 0.0, hw * 0.85):
        base = (x + tx, -fl * (0.86 if tx else 0.9), 0.012)
        mb.tube([(base, 0.014), ((base[0] + tx * 0.15, base[1] - 0.045, 0.004), 0.003)], m["FeetTrim"], side + "Toes", sides=4)


def body(name, r):
    """The body mesh in T-pose, every part bound to one bone."""
    mats = {k: lp.material(k, v, metallic=0.6 if "Metal" in k else 0.0, roughness=0.5 if "Metal" in k else 0.85,
                           emission=2.0 if "Glow" in k else None)
            for k, v in r["colors"].items()}
    mb = lp.MeshBuilder(name)
    _torso(mb, r, mats)
    _head(mb, r, mats)
    for side, s in SIDES:
        _arm(mb, r, mats, side, s)
        _leg(mb, r, mats, side, s)
    _hair(mb, r, mats)
    if r.get("fins"):
        _body_fins(mb, r, mats)
    return mb.finish()


def build_race(race):
    """Armature, body and the gear pieces (gear.py) for one race (no animations)."""
    import gear  # gear.py builds on this module's tables

    r = RACES[race]
    arm = lp.armature("Armature", skeleton(r))
    mesh = body(race + "Body", r)
    lp.bind(mesh, arm)
    for piece in gear.build_all(r, r.get("gear")):
        lp.bind(piece, arm)
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
