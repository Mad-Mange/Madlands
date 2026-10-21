"""
Static props for building zones: nature, village buildings and camp clutter. Origin at the base
center, meters, +Z up, the front faces -Y. Colliders are chosen by the map builder (ZoneProps.cs).
"""

import math
import random

import lowpoly as lp

PALETTE = {
    "Bark": "#6b4a2f", "BarkGray": "#7d7468", "Leaves": "#46752f", "LeavesDark": "#335a26", "LeavesLight": "#6a9a3c",
    "Pine": "#2f5a36", "PineDark": "#244a2c", "Stone": "#8d8a83", "StoneDark": "#6c6a64", "Moss": "#5b7a3c",
    "Wood": "#8a6238", "WoodDark": "#5e4128", "Plank": "#a57d4c", "Thatch": "#b99a54", "Plaster": "#ddd2b6",
    "Canvas": "#cbb68b", "CanvasRed": "#9a3b2e", "Banner": "#2f4f9a", "Metal": "#8f959d", "Reed": "#8a9a4a",
    "ReedTop": "#6b5232", "MushroomCap": "#b5442e", "MushroomStem": "#e6dcc0", "Water": "#3d6a7a",
}


def mats():
    """Every palette material plus the glowing ones, created on first use."""
    m = {name: lp.material(name, color) for name, color in PALETTE.items()}
    m["FireGlow"] = lp.material("FireGlow", "#ffa03a", emission=3.0)
    m["LanternGlow"] = lp.material("LanternGlow", "#ffd27a", emission=2.5)
    m["CrystalGlow"] = lp.material("CrystalGlow", "#7ff2ff", emission=2.0)
    return m


def blob(mb, center, radius, mat, rng, points=14, squash=1.0):
    """A lumpy convex blob (tree crowns, bushes, rocks)."""
    pts = []
    for _ in range(points):
        theta = rng.uniform(0, 2 * math.pi)
        z = rng.uniform(-1, 1)
        r = math.sqrt(1 - z * z)
        k = rng.uniform(0.8, 1.1)
        pts.append((center[0] + radius * r * math.cos(theta) * k, center[1] + radius * r * math.sin(theta) * k, center[2] + radius * z * k * squash))
    mb.hull(pts, mat)


# ---------------------------------------------------------------- nature

def tree_oak(seed):
    def build():
        rng = random.Random(seed)
        m = mats()
        mb = lp.MeshBuilder("TreeOak%d" % seed)
        h = rng.uniform(3.2, 4.0)
        mb.frustum((0, 0, -0.3), (0, 0, h), 0.42, 0.26, m["Bark"], sides=6)
        for angle in (rng.uniform(0, 360), rng.uniform(0, 360)):
            a = math.radians(angle)
            mb.frustum((0, 0, h * 0.7), (math.cos(a) * 1.4, math.sin(a) * 1.4, h + 1.0), 0.16, 0.08, m["Bark"], sides=5)
        leaves = [m["Leaves"], m["LeavesDark"], m["LeavesLight"]]
        mb.hull([(0, 0, h + 3.0)] + [(math.cos(i) * 2.6, math.sin(i) * 2.6, h + 1.6) for i in range(6)] + [(0, 0, h + 0.4)], leaves[0])
        for i in range(4):
            a = rng.uniform(0, 2 * math.pi)
            d = rng.uniform(1.2, 2.0)
            blob(mb, (math.cos(a) * d, math.sin(a) * d, h + rng.uniform(1.2, 2.6)), rng.uniform(1.3, 1.8), leaves[i % 3], rng)
        return mb.finish()
    return build


def tree_pine(seed):
    def build():
        rng = random.Random(seed)
        m = mats()
        mb = lp.MeshBuilder("TreePine%d" % seed)
        h = rng.uniform(8.5, 11.0)
        mb.frustum((0, 0, -0.3), (0, 0, h * 0.5), 0.32, 0.22, m["Bark"], sides=6)
        tiers = 4
        for i in range(tiers):
            base = 1.6 + i * (h - 2.4) / tiers
            radius = 2.4 * (1 - i / (tiers + 0.6))
            mb.frustum((0, 0, base), (0, 0, base + 3.2 - i * 0.35), radius, 0.05, m["Pine"] if i % 2 == 0 else m["PineDark"], sides=7, twist=rng.uniform(0, 1))
        return mb.finish()
    return build


def tree_dead():
    rng = random.Random(7)
    m = mats()
    mb = lp.MeshBuilder("TreeDead")
    mb.frustum((0, 0, -0.3), (0, 0, 5.5), 0.36, 0.12, m["BarkGray"], sides=6)
    for i in range(4):
        a = rng.uniform(0, 2 * math.pi)
        z = rng.uniform(2.2, 4.8)
        mb.frustum((0, 0, z), (math.cos(a) * 1.8, math.sin(a) * 1.8, z + rng.uniform(0.8, 1.6)), 0.1, 0.03, m["BarkGray"], sides=4)
    return mb.finish()


def bush(seed):
    def build():
        rng = random.Random(seed)
        m = mats()
        mb = lp.MeshBuilder("Bush%d" % seed)
        for i in range(3):
            blob(mb, (rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5), 0.5 + rng.uniform(0, 0.3)), rng.uniform(0.6, 0.9),
                 [m["Leaves"], m["LeavesDark"], m["LeavesLight"]][i], rng, squash=0.8)
        return mb.finish()
    return build


def rock(seed, size):
    def build():
        rng = random.Random(seed)
        m = mats()
        mb = lp.MeshBuilder("Rock%d" % seed)
        pts = []
        for _ in range(16):
            a = rng.uniform(0, 2 * math.pi)
            r = rng.uniform(0.6, 1.0) * size
            pts.append((math.cos(a) * r, math.sin(a) * r * rng.uniform(0.7, 1.0), rng.uniform(-0.3, 0.75) * size))
        mb.hull(pts, m["Stone"])
        # A mossy cap on top.
        top = [(x * 0.7, y * 0.7, z + 0.04 * size) for x, y, z in pts if z > 0.45 * size]
        if len(top) >= 4:
            mb.hull(top + [(0, 0, max(p[2] for p in top) + 0.05 * size)], m["Moss"])
        return mb.finish()
    return build


def reeds():
    rng = random.Random(3)
    m = mats()
    mb = lp.MeshBuilder("Reeds")
    for _ in range(14):
        x, y = rng.uniform(-0.8, 0.8), rng.uniform(-0.8, 0.8)
        h = rng.uniform(1.2, 2.0)
        lean = (rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2))
        mb.frustum((x, y, -0.1), (x + lean[0], y + lean[1], h), 0.025, 0.01, m["Reed"], sides=4)
        if rng.random() < 0.5:
            mb.frustum((x + lean[0] * 0.8, y + lean[1] * 0.8, h * 0.8), (x + lean[0] * 0.95, y + lean[1] * 0.95, h * 0.95), 0.045, 0.045, m["ReedTop"], sides=5)
    return mb.finish()


def mushrooms():
    rng = random.Random(5)
    m = mats()
    mb = lp.MeshBuilder("Mushrooms")
    for _ in range(5):
        x, y = rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4)
        h = rng.uniform(0.15, 0.4)
        mb.frustum((x, y, 0), (x, y, h), 0.04, 0.035, m["MushroomStem"], sides=5)
        mb.frustum((x, y, h - 0.02), (x, y, h + 0.1), h * 0.45, 0.02, m["MushroomCap"], sides=6)
    return mb.finish()


def log():
    m = mats()
    mb = lp.MeshBuilder("Log")
    mb.frustum((-2.0, 0, 0.32), (2.0, 0, 0.3), 0.34, 0.3, m["Bark"], sides=7)
    mb.frustum((-2.02, 0, 0.32), (-1.98, 0, 0.32), 0.3, 0.3, m["Plank"], sides=7)
    mb.frustum((0.6, 0, 0.5), (1.0, 0.4, 1.0), 0.08, 0.04, m["Bark"], sides=4)
    return mb.finish()


def stump():
    m = mats()
    mb = lp.MeshBuilder("Stump")
    mb.frustum((0, 0, -0.2), (0, 0, 0.6), 0.55, 0.45, m["Bark"], sides=7)
    mb.frustum((0, 0, 0.59), (0, 0, 0.62), 0.42, 0.42, m["Plank"], sides=7)
    return mb.finish()


def slag_pool():
    """A pool of molten slag in a crust of black rock: walk around it (or through it, it is only a look)."""
    rng = random.Random(31)
    m = mats()
    mb = lp.MeshBuilder("SlagPool")
    glow = lp.material("LavaGlow", "#b8300a", emission=3.0)
    crust = lp.material("SlagCrust", "#2e2a2b")
    rim = []
    for i in range(14):
        a = 2 * math.pi * i / 14
        r = rng.uniform(2.2, 2.7)
        rim.append((math.cos(a) * r, math.sin(a) * r))
    # The molten middle, a little sunk, and the crust ring around it in lumps.
    mb.add([(x * 0.8, y * 0.8, 0.04) for x, y in rim], [tuple(range(len(rim)))], glow)
    for i, (x, y) in enumerate(rim):
        nx, ny = rim[(i + 1) % len(rim)]
        mb.hull([(x * 0.75, y * 0.75, 0.0), (nx * 0.75, ny * 0.75, 0.0), (x * 1.05, y * 1.05, 0.0), (nx * 1.05, ny * 1.05, 0.0),
                 ((x + nx) * 0.45, (y + ny) * 0.45, rng.uniform(0.15, 0.35))], crust)
    # Floating crust plates with glowing cracks between them.
    for _ in range(4):
        cx, cy = rng.uniform(-1.0, 1.0), rng.uniform(-1.0, 1.0)
        mb.hull([(cx + rng.uniform(-0.45, 0.45), cy + rng.uniform(-0.45, 0.45), rng.uniform(0.03, 0.09)) for _ in range(7)], crust)
    return mb.finish()


def slag_rock():
    """A heap of black slag split by glowing veins."""
    rng = random.Random(37)
    m = mats()
    mb = lp.MeshBuilder("SlagRock")
    glow = lp.material("LavaGlow", "#b8300a", emission=3.0)
    crust = lp.material("SlagCrust", "#2e2a2b")
    for cx, cy, r, h in ((0, 0, 1.1, 1.6), (0.9, 0.4, 0.7, 0.9), (-0.8, -0.3, 0.6, 0.7)):
        mb.hull([(cx + math.cos(a) * r * rng.uniform(0.7, 1.0), cy + math.sin(a) * r * rng.uniform(0.7, 1.0), rng.uniform(-0.2, 0.2))
                 for a in [i * 2 * math.pi / 7 for i in range(7)]]
                + [(cx + rng.uniform(-0.3, 0.3) * r, cy + rng.uniform(-0.3, 0.3) * r, h * rng.uniform(0.8, 1.0)) for _ in range(3)], crust)
    for a, z0, z1 in ((0.3, 0.2, 1.3), (2.2, 0.1, 1.0), (4.1, 0.3, 1.2)):
        x, y = math.cos(a) * 0.82, math.sin(a) * 0.82
        mb.frustum((x, y, z0), (x * 0.55, y * 0.55, z1), 0.06, 0.03, glow, sides=4)
    return mb.finish()


def beast_skull():
    """The bleached skull of something enormous, half sunk in the ground: horns, eye sockets, a row of teeth."""
    m = mats()
    mb = lp.MeshBuilder("BeastSkull")
    bone = lp.material("Bone", "#ddd7c3")
    shadow = lp.material("BoneShadow", "#aaa290")
    dark = lp.material("SocketDark", "#2c2829")
    mb.hull([(sx * 1.2, -0.2, 0.0) for sx in (1, -1)] + [(sx * 1.0, 1.6, 0.0) for sx in (1, -1)]
            + [(sx * 0.9, 0.1, 1.5) for sx in (1, -1)] + [(sx * 0.6, 1.4, 1.2) for sx in (1, -1)] + [(0, -0.4, 1.2)], bone)
    mb.hull([(sx * 0.8, -0.2, 0.0) for sx in (1, -1)] + [(sx * 0.5, -2.2, 0.0) for sx in (1, -1)]
            + [(sx * 0.7, -0.2, 0.9) for sx in (1, -1)] + [(sx * 0.4, -2.1, 0.5) for sx in (1, -1)], bone)
    for sx in (1, -1):
        mb.box((sx * 0.92, -0.05, 0.95), (0.3, 0.55, 0.42), dark, rotation=(0, sx * -15, 0))
        mb.frustum((sx * 0.7, 0.8, 1.2), (sx * 2.2, 1.6, 2.8), 0.32, 0.03, shadow, sides=6)
        for i in range(5):
            y = -2.0 + i * 0.36
            mb.frustum((sx * (0.45 - i * 0.02), y, 0.1), (sx * (0.45 - i * 0.02), y, -0.35), 0.09, 0.01, bone, sides=4)
    mb.box((0, -1.2, 0.62), (0.28, 0.5, 0.18), dark)
    return mb.finish()


def crystal_cluster():
    rng = random.Random(11)
    m = mats()
    mb = lp.MeshBuilder("CrystalCluster")
    mb.hull([(rng.uniform(-0.9, 0.9), rng.uniform(-0.9, 0.9), rng.uniform(-0.2, 0.3)) for _ in range(10)], m["StoneDark"])
    for _ in range(6):
        x, y = rng.uniform(-0.6, 0.6), rng.uniform(-0.6, 0.6)
        h = rng.uniform(0.8, 2.0)
        tilt = (rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4))
        r = rng.uniform(0.12, 0.22)
        pts = [(x + r * math.cos(i * math.pi / 3), y + r * math.sin(i * math.pi / 3), 0.1) for i in range(6)]
        pts += [(x + tilt[0] * h + r * 0.9 * math.cos(i * math.pi / 3), y + tilt[1] * h + r * 0.9 * math.sin(i * math.pi / 3), h * 0.8) for i in range(6)]
        pts.append((x + tilt[0] * h * 1.15, y + tilt[1] * h * 1.15, h))
        mb.hull(pts, m["CrystalGlow"])
    return mb.finish()


# ---------------------------------------------------------------- village

def palisade():
    m = mats()
    mb = lp.MeshBuilder("Palisade")
    rng = random.Random(2)
    n = 9
    for i in range(n):
        x = -2.0 + i * 4.0 / (n - 1)
        h = 3.2 + rng.uniform(-0.25, 0.25)
        mb.frustum((x, 0, -0.5), (x, 0, h), 0.24, 0.22, m["Wood"], sides=6)
        mb.frustum((x, 0, h), (x, 0, h + 0.45), 0.22, 0.01, m["Wood"], sides=6)
    mb.box((0, 0.22, 2.2), (4.3, 0.12, 0.25), m["WoodDark"])
    mb.box((0, 0.22, 0.8), (4.3, 0.12, 0.25), m["WoodDark"])
    return mb.finish()


def gate():
    m = mats()
    mb = lp.MeshBuilder("Gate")
    for x in (-2.2, 2.2):
        mb.frustum((x, 0, -0.5), (x, 0, 4.6), 0.32, 0.3, m["Wood"], sides=6)
        mb.frustum((x, 0, 4.6), (x, 0, 5.1), 0.3, 0.02, m["Wood"], sides=6)
    mb.box((0, 0, 4.1), (4.9, 0.45, 0.4), m["WoodDark"])
    mb.box((0, -0.25, 4.1), (1.6, 0.06, 0.9), m["Banner"])
    return mb.finish()


def tent():
    """
    A canvas ridge tent: the cloth sags between the poles and flares at the hem, the front flaps are tied
    back from a dark opening, a seam runs down each slope, guy ropes run out to stakes.
    """
    m = mats()
    mb = lp.MeshBuilder("Tent")
    w, d, h = 3.6, 2.8, 2.4
    hw, hd = w / 2, d / 2
    inside = lambda p: (p[0] * 0.5, 0.0, 0.4)
    seam = lp.material("CanvasSeam", "#a8936a")
    shade = lp.material("CanvasShade", "#7f6e4f")
    # Each slope: rows along X (front end to back end), from the ridge down to the hem; the ridge sags a little.
    for side in (-1, 1):
        rows = []
        for k, t in enumerate((0.0, 0.35, 0.7, 1.0)):
            row = []
            for i in range(7):
                u = -hw + w * i / 6
                sag = 0.12 * (1 - (2 * i / 6 - 1) ** 2)
                z = (h - sag) * (1 - t) + 0.02 * t
                y = side * (hd * t + 0.08 * t * t)
                bulge = 0.06 * math.sin(math.pi * t) * (1 - (2 * i / 6 - 1) ** 2)
                row.append((u, y + side * bulge, z - bulge * 0.5))
            rows.append(row)
        mb.grid(rows, m["Canvas"], None, inside)
        mb.grid(rows, shade, None, inside, facing_in=True)
        for i in (2, 4):
            mb.tube([((rows[k][i][0], rows[k][i][1] * 1.01, rows[k][i][2] + 0.01), 0.018) for k in range(4)], seam, None, sides=4)
    # The back end closed, the front open with its flaps tied back.
    mb.add([(hw, -hd, 0.02), (hw, hd, 0.02), (hw, 0, h - 0.02)], [(0, 1, 2)], m["Canvas"], double_sided=True)
    mb.add([(-hw + 0.3, -hd * 0.6, 0.02), (-hw + 0.3, hd * 0.6, 0.02), (-hw + 0.3, 0, h * 0.8)], [(0, 1, 2)], m["WoodDark"], double_sided=True)
    for side in (-1, 1):
        mb.add([(-hw, side * hd, 0.02), (-hw, side * hd * 0.25, h * 0.35), (-hw, 0, h - 0.05), (-hw - 0.05, side * hd * 0.55, h * 0.45)],
               [(0, 3, 2, 1)], m["Canvas"], double_sided=True)
        mb.tube([((-hw - 0.06, side * hd * 0.5, h * 0.42), 0.02), ((-hw - 0.06, side * hd * 0.42, h * 0.48), 0.02)], m["Wood"], None, sides=4)
    # Poles, the ridge pole, guy ropes and stakes.
    for x in (-hw, hw):
        mb.frustum((x, 0, 0), (x, 0, h + 0.35), 0.06, 0.05, m["Wood"], sides=6)
        for side in (-1, 1):
            stake = (x + (-0.9 if x < 0 else 0.9), side * 0.5, 0.0)
            mb.tube([((x, 0, h + 0.25), 0.012), ((stake[0], stake[1], 0.12), 0.012)], m["Plank"], None, sides=4)
            mb.frustum((stake[0], stake[1], -0.1), (stake[0], stake[1], 0.18), 0.03, 0.04, m["WoodDark"], sides=4)
    mb.frustum((-hw - 0.12, 0, h + 0.1), (hw + 0.12, 0, h + 0.1), 0.05, 0.05, m["Wood"], sides=6)
    for side in (-1, 1):
        for i in range(5):
            u = -hw + w * (i + 0.5) / 5
            mb.frustum((u, side * (hd + 0.45), -0.1), (u, side * (hd + 0.45), 0.15), 0.025, 0.035, m["WoodDark"], sides=4)
            mb.tube([((u, side * (hd + 0.05), 0.05), 0.01), ((u, side * (hd + 0.45), 0.12), 0.01)], m["Plank"], None, sides=4)
    return mb.finish()

def signpost():
    m = mats()
    mb = lp.MeshBuilder("Signpost")
    mb.frustum((0, 0, -0.3), (0, 0, 2.6), 0.08, 0.07, m["Wood"], sides=5)
    mb.hull([(0.05, -0.04, 2.15), (0.05, 0.04, 2.15), (0.05, -0.04, 2.4), (0.05, 0.04, 2.4), (1.1, 0, 2.275), (0.9, -0.04, 2.15), (0.9, 0.04, 2.4)], m["Plank"])
    mb.hull([(-0.05, -0.04, 1.75), (-0.05, 0.04, 1.75), (-0.05, -0.04, 2.0), (-0.05, 0.04, 2.0), (-1.0, 0, 1.875), (-0.8, 0.04, 1.75), (-0.8, -0.04, 2.0)], m["Plank"])
    return mb.finish()


def fence():
    m = mats()
    mb = lp.MeshBuilder("Fence")
    for x in (-1.5, 1.5):
        mb.box((x, 0, 0.5), (0.14, 0.14, 1.3), m["Wood"])
    for z in (0.45, 0.9):
        mb.box((0, 0, z), (3.1, 0.07, 0.12), m["Plank"])
    return mb.finish()


def crate():
    m = mats()
    mb = lp.MeshBuilder("Crate")
    mb.box((0, 0, 0.45), (0.9, 0.9, 0.9), m["Plank"])
    for y in (-0.46, 0.46):
        mb.box((0, y, 0.45), (0.95, 0.04, 0.12), m["WoodDark"], rotation=(0, 45, 0))
    for x in (-0.46, 0.46):
        mb.box((x, 0, 0.45), (0.04, 0.95, 0.12), m["WoodDark"], rotation=(45, 0, 0))
    return mb.finish()


def barrel():
    m = mats()
    mb = lp.MeshBuilder("Barrel")
    mb.frustum((0, 0, 0), (0, 0, 0.5), 0.36, 0.43, m["Wood"], sides=8)
    mb.frustum((0, 0, 0.5), (0, 0, 1.0), 0.43, 0.36, m["Wood"], sides=8)
    for z in (0.15, 0.85):
        mb.frustum((0, 0, z - 0.04), (0, 0, z + 0.04), 0.42, 0.42, m["Metal"], sides=8, cap_a=False, cap_b=False)
    return mb.finish()


def sacks():
    m = mats()
    mb = lp.MeshBuilder("Sacks")
    rng = random.Random(4)
    for x, y, z in ((-0.35, 0, 0.3), (0.35, 0.05, 0.3), (0, 0, 0.75)):
        blob(mb, (x, y, z), 0.38, m["Canvas"], rng, squash=0.75)
    return mb.finish()


def campfire():
    """A ring of rounded stones around crossed logs, embers, and flames in layers: a bright core in tongues of fire."""
    m = mats()
    rng = random.Random(9)
    mb = lp.MeshBuilder("Campfire")
    for i in range(10):
        a = i * 2 * math.pi / 10 + rng.uniform(-0.1, 0.1)
        blob(mb, (math.cos(a) * 0.75, math.sin(a) * 0.75, 0.1), 0.17, m["Stone"] if i % 2 else m["StoneDark"], rng, points=9, squash=0.75)
    for i in range(5):
        a = i * 2 * math.pi / 5 + 0.3
        mb.tube([((math.cos(a) * 0.62, math.sin(a) * 0.62, 0.06), 0.075), ((math.cos(a) * -0.05, math.sin(a) * -0.05, 0.42), 0.06)],
                m["WoodDark"] if i % 2 else m["Wood"], None, sides=6)
        mb.sphere((math.cos(a) * 0.12, math.sin(a) * 0.12, 0.3), 0.065, m["FireGlow"], segments=5, rings=3)
    ember = lp.material("EmberGlow", "#c8461e", emission=2.0)
    mb.hull([(math.cos(a) * 0.42, math.sin(a) * 0.42, 0.02) for a in (i * math.pi / 4 for i in range(8))] + [(0, 0, 0.12)], ember)
    core = lp.material("FlameCoreGlow", "#ffe39a", emission=4.0)
    for i in range(5):
        a = i * 2 * math.pi / 5
        r = 0.16
        base = (math.cos(a) * r, math.sin(a) * r, 0.15)
        tip = (math.cos(a) * r * 0.4 + rng.uniform(-0.05, 0.05), math.sin(a) * r * 0.4, 0.75 + rng.uniform(0, 0.3))
        mb.hull([(base[0] + dx, base[1] + dy, base[2]) for dx, dy in ((0.12, 0), (-0.12, 0), (0, 0.12), (0, -0.12))]
                + [((base[0] + tip[0]) / 2, (base[1] + tip[1]) / 2, (base[2] + tip[2]) / 2 + 0.05), tip], m["FireGlow"])
    mb.hull([(0.1, 0, 0.15), (-0.1, 0, 0.15), (0, 0.1, 0.15), (0, -0.1, 0.15), (0.03, 0.02, 1.15), (0, 0, 0.55)], core)
    return mb.finish()

def banner_pole():
    m = mats()
    mb = lp.MeshBuilder("BannerPole")
    mb.frustum((0, 0, -0.3), (0, 0, 5.0), 0.09, 0.07, m["Wood"], sides=6)
    mb.frustum((0, 0, 4.7), (0.0, -1.2, 4.7), 0.04, 0.04, m["WoodDark"], sides=5)
    mb.add([(0, -0.1, 4.65), (0, -1.15, 4.65), (0, -1.15, 3.0), (0, -0.62, 3.3), (0, -0.1, 3.0)],
           [(0, 1, 2, 3, 4)], m["Banner"], double_sided=True)
    mb.sphere((0, 0, 5.05), 0.1, m["Metal"], segments=6, rings=4)
    return mb.finish()


def cauldron():
    m = mats()
    mb = lp.MeshBuilder("Cauldron")
    mb.frustum((0, 0, 0.15), (0, 0, 0.45), 0.35, 0.55, m["Metal"], sides=8)
    mb.frustum((0, 0, 0.45), (0, 0, 0.8), 0.55, 0.45, m["Metal"], sides=8)
    mb.frustum((0, 0, 0.74), (0, 0, 0.76), 0.42, 0.42, m["Leaves"], sides=8)
    for i in range(3):
        a = i * 2 * math.pi / 3
        mb.frustum((math.cos(a) * 0.35, math.sin(a) * 0.35, 0.3), (math.cos(a) * 0.45, math.sin(a) * 0.45, 0.0), 0.05, 0.04, m["Metal"], sides=4)
    return mb.finish()


def bridge():
    m = mats()
    mb = lp.MeshBuilder("Bridge")
    length, width = 10.0, 3.0
    n = 16
    for i in range(n):
        y = -length / 2 + (i + 0.5) * length / n
        z = 0.6 + 0.5 * math.cos(math.pi * y / length)
        mb.box((0, y, z), (width, length / n - 0.04, 0.12), m["Plank"])
    for x in (-width / 2, width / 2):
        for i in range(5):
            y = -length / 2 + i * length / 4
            z = 0.6 + 0.5 * math.cos(math.pi * y / length)
            mb.frustum((x, y, -1.0), (x, y, z + 1.0), 0.1, 0.09, m["WoodDark"], sides=5)
        for i in range(4):
            y0, y1 = -length / 2 + i * length / 4, -length / 2 + (i + 1) * length / 4
            z0, z1 = 0.6 + 0.5 * math.cos(math.pi * y0 / length), 0.6 + 0.5 * math.cos(math.pi * y1 / length)
            mb.frustum((x, y0, z0 + 0.9), (x, y1, z1 + 0.9), 0.05, 0.05, m["Wood"], sides=5)
    return mb.finish()


def dock():
    m = mats()
    mb = lp.MeshBuilder("Dock")
    length, width = 8.0, 2.4
    for i in range(12):
        y = (i + 0.5) * length / 12
        mb.box((0, -y, 0.4), (width, length / 12 - 0.05, 0.1), m["Plank"])
    for x in (-width / 2 + 0.1, width / 2 - 0.1):
        for y in (0.3, length / 2, length - 0.3):
            mb.frustum((x, -y, -1.5), (x, -y, 0.65), 0.12, 0.11, m["WoodDark"], sides=5)
    return mb.finish()


def lantern_post():
    m = mats()
    mb = lp.MeshBuilder("LanternPost")
    mb.frustum((0, 0, -0.3), (0, 0, 3.0), 0.09, 0.07, m["WoodDark"], sides=6)
    mb.box((0, -0.35, 2.9), (0.06, 0.8, 0.06), m["WoodDark"])
    mb.box((0, -0.7, 2.55), (0.22, 0.22, 0.32), m["LanternGlow"])
    mb.hull([(-0.15, -0.85, 2.72), (0.15, -0.85, 2.72), (0.15, -0.55, 2.72), (-0.15, -0.55, 2.72), (0, -0.7, 2.9)], m["Metal"])
    return mb.finish()


def ruin_pillar():
    m = mats()
    mb = lp.MeshBuilder("RuinPillar")
    mb.box((0, 0, 0.2), (1.3, 1.3, 0.4), m["StoneDark"])
    mb.frustum((0, 0, 0.4), (0, 0, 2.6), 0.42, 0.4, m["Stone"], sides=8)
    mb.hull([(0.42 * math.cos(i * math.pi / 4), 0.42 * math.sin(i * math.pi / 4), 2.6) for i in range(8)]
            + [(0.3, 0.1, 3.2), (-0.2, 0.3, 2.9), (0.1, -0.35, 3.05)], m["Stone"])
    mb.box((0.9, 0.4, 0.2), (0.8, 0.5, 0.4), m["Stone"], rotation=(0, 0, 25))
    return mb.finish()


def ruin_wall():
    m = mats()
    mb = lp.MeshBuilder("RuinWall")
    rng = random.Random(9)
    for i in range(6):
        h = rng.uniform(0.8, 2.6)
        mb.box((-2.5 + i, 0, h / 2), (0.98, 0.7, h), m["Stone"] if i % 2 else m["StoneDark"])
    blob(mb, (1.5, -0.8, 0.2), 0.45, m["Stone"], rng, squash=0.6)
    return mb.finish()


PROPS = {
    "TreeOak1": tree_oak(1), "TreeOak2": tree_oak(2), "TreePine1": tree_pine(1), "TreePine2": tree_pine(2),
    "TreeDead": tree_dead, "Bush1": bush(1), "Bush2": bush(2),
    "Rock1": rock(1, 1.0), "Rock2": rock(2, 1.4), "Boulder": rock(3, 3.2),
    "Reeds": reeds, "Mushrooms": mushrooms, "Log": log, "Stump": stump, "CrystalCluster": crystal_cluster,
    "Palisade": palisade, "Gate": gate,
    "Tent": tent, "Signpost": signpost, "Fence": fence,
    "Crate": crate, "Barrel": barrel, "Sacks": sacks, "Campfire": campfire, "BannerPole": banner_pole,
    "Cauldron": cauldron, "Bridge": bridge, "Dock": dock, "LanternPost": lantern_post,
    "RuinPillar": ruin_pillar, "RuinWall": ruin_wall,
    "SlagPool": slag_pool, "SlagRock": slag_rock, "BeastSkull": beast_skull,
}
