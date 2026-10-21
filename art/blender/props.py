"""
Static props for building zones: nature, village buildings and camp clutter. Origin at the base
center, meters, +Z up, the front faces -Y. Colliders are chosen by the map builder (ZoneProps.cs).
"""

import math
import random

import lowpoly as lp

PALETTE = {
    "Bark": "#6b4a2f", "BarkGray": "#7d7468", "Leaves": "#46752f", "LeavesDark": "#335a26", "LeavesLight": "#6a9a3c",
    "Pine": "#2f5a36", "PineDark": "#244a2c", "PineLight": "#3d6e3e", "Blossom": "#e8e0c0", "Stone": "#8d8a83", "StoneDark": "#6c6a64", "Moss": "#5b7a3c",
    "Wood": "#8a6238", "WoodDark": "#5e4128", "Plank": "#a57d4c", "Thatch": "#b99a54", "Plaster": "#ddd2b6",
    "Canvas": "#cbb68b", "CanvasRed": "#9a3b2e", "Banner": "#2f4f9a", "Metal": "#8f959d", "Reed": "#8a9a4a",
    "ReedTop": "#6b5232", "MushroomCap": "#b5442e", "MushroomStem": "#e6dcc0", "Water": "#3d6a7a",
}


def mats():
    """Every palette material plus the glowing ones, created on first use."""
    m = {name: lp.material(name, color) for name, color in PALETTE.items()}
    m["FireGlow"] = lp.material("FireGlow", "#ffa03a", emission=3.0)
    m["LanternGlow"] = lp.material("LanternGlow", "#ffd27a", emission=2.5)
    m["CrystalGlow"] = lp.material("CrystalGlow", "#30c8ec", emission=2.0)
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
    """
    A broad oak: a bending trunk flaring into roots, a few heavy branches, and a crown of leafy clumps
    built in layers: darker underneath, lighter where the sun falls on top.
    """
    def build():
        rng = random.Random(seed)
        m = mats()
        mb = lp.MeshBuilder("TreeOak%d" % seed)
        h = rng.uniform(3.2, 4.0)
        lean = (rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25))
        path = [(lean[0] * t * t, lean[1] * t * t, -0.3 + (h + 0.3) * t) for t in (0.0, 0.3, 0.6, 1.0)]
        mb.tube([(path[0], 0.5), (path[1], 0.36), (path[2], 0.31), (path[3], 0.26)], m["Bark"], sides=8)
        for i in range(5):
            a = 2 * math.pi * i / 5 + rng.uniform(-0.3, 0.3)
            out = rng.uniform(0.75, 1.0)
            mb.tube([((math.cos(a) * 0.2, math.sin(a) * 0.2, 0.7), 0.17), ((math.cos(a) * 0.5, math.sin(a) * 0.5, 0.15), 0.14),
                     ((math.cos(a) * out, math.sin(a) * out, -0.15), 0.04)], m["Bark"], sides=5)
        top = path[-1]
        tips = []
        for i in range(4):
            a = 2 * math.pi * i / 4 + rng.uniform(0, 1.2)
            start = (top[0] * 0.7, top[1] * 0.7, h * rng.uniform(0.55, 0.8))
            reach = rng.uniform(1.9, 2.5)
            tip = (math.cos(a) * reach + top[0], math.sin(a) * reach + top[1], h + rng.uniform(0.4, 0.9))
            mid = ((start[0] + tip[0]) / 2, (start[1] + tip[1]) / 2, (start[2] + tip[2]) / 2 - 0.15)
            mb.tube([(start, 0.17), (mid, 0.12), (tip, 0.06)], m["Bark"], sides=5)
            tips.append(tip)
        cx, cy = top[0], top[1]
        # Clumps: a dark under-layer, the body, and light sunlit tops.
        blob(mb, (cx, cy, h + 1.3), 2.5, m["LeavesDark"], rng, points=24, squash=0.55)
        for tip in tips:
            blob(mb, (tip[0], tip[1], tip[2] + 0.45), rng.uniform(1.4, 1.7), m["Leaves"], rng, points=20, squash=0.7)
        for i in range(3):
            a = 2 * math.pi * i / 3 + rng.uniform(0, 1)
            d = rng.uniform(0.8, 1.4)
            blob(mb, (cx + math.cos(a) * d, cy + math.sin(a) * d, h + rng.uniform(2.0, 2.4)), rng.uniform(1.2, 1.5), m["LeavesLight"], rng,
                 points=20, squash=0.65)
        blob(mb, (cx, cy, h + 2.7), 1.1, m["LeavesLight"], rng, points=16, squash=0.65)
        return mb.finish()
    return build


def tree_pine(seed):
    """
    A pine: a straight trunk under tiers of drooping boughs, each a skirt with a jagged, sagging rim,
    lighter on top and darker underneath, narrowing to a spire.
    """
    def build():
        rng = random.Random(seed)
        m = mats()
        mb = lp.MeshBuilder("TreePine%d" % seed)
        h = rng.uniform(8.5, 11.0)
        mb.tube([((0, 0, -0.3), 0.36), ((0, 0, 0.4), 0.27), ((0, 0, h * 0.85), 0.1)], m["Bark"], sides=7)
        tiers = 6
        n = 14
        for i in range(tiers):
            f = i / (tiers - 1)
            base = 1.5 + f * (h - 3.6)
            radius = 2.5 * (1 - f * 0.78) * rng.uniform(0.92, 1.05)
            height = 2.4 - f * 0.9
            twist = rng.uniform(0, 1)
            top = [(0, 0, base + height)]
            mid = [(math.cos(2 * math.pi * k / n + twist) * radius * 0.55, math.sin(2 * math.pi * k / n + twist) * radius * 0.55,
                    base + height * 0.42) for k in range(n)]
            rim = []
            for k in range(n):
                a = 2 * math.pi * k / n + twist
                r = radius * (1.0 if k % 2 == 0 else 0.78) * rng.uniform(0.95, 1.05)
                rim.append((math.cos(a) * r, math.sin(a) * r, base - (0.28 if k % 2 == 0 else 0.0) * (1 - f * 0.5)))
            inside = lambda q, base=base: (0, 0, base + 0.3)
            mb.grid([top, mid, rim], m["Pine"] if i % 2 else m["PineLight"], None, inside, closed=True)
            mb.grid([rim, [(0, 0, base + 0.25)]], m["PineDark"], None, lambda q, base=base: (0, 0, base + 1.0), closed=True)
        mb.tube([((0, 0, h - 0.4), 0.18), ((0, 0, h + 0.9), 0.01)], m["PineLight"], sides=6)
        return mb.finish()
    return build


def tree_dead():
    """A dead tree: a gnarled grey trunk splitting into crooked, broken limbs, roots gripping the ground."""
    rng = random.Random(7)
    m = mats()
    mb = lp.MeshBuilder("TreeDead")
    mb.tube([((0, 0, -0.3), 0.42), ((0.05, 0.02, 1.2), 0.3), ((0.15, -0.05, 3.0), 0.22), ((0.1, 0.05, 4.6), 0.14), ((0.25, 0.1, 5.6), 0.04)],
            m["BarkGray"], sides=7)
    for i in range(4):
        a = 2 * math.pi * i / 4 + rng.uniform(-0.3, 0.3)
        mb.tube([((math.cos(a) * 0.15, math.sin(a) * 0.15, 0.6), 0.16), ((math.cos(a) * 0.5, math.sin(a) * 0.5, 0.1), 0.11),
                 ((math.cos(a) * 0.95, math.sin(a) * 0.95, -0.15), 0.03)], m["BarkGray"], sides=5)
    for i in range(5):
        a = rng.uniform(0, 2 * math.pi)
        z = rng.uniform(2.0, 4.6)
        reach = rng.uniform(1.2, 2.0)
        start = (0.1, 0, z)
        bend = (math.cos(a) * reach * 0.5, math.sin(a) * reach * 0.5, z + rng.uniform(0.2, 0.7))
        tip = (math.cos(a + rng.uniform(-0.5, 0.5)) * reach, math.sin(a + rng.uniform(-0.5, 0.5)) * reach, z + rng.uniform(0.6, 1.6))
        mb.tube([(start, 0.11), (bend, 0.07), (tip, 0.015)], m["BarkGray"], sides=5)
        twig = (bend[0] * 1.3, bend[1] * 1.3, bend[2] + 0.9)
        mb.tube([(bend, 0.04), (twig, 0.01)], m["BarkGray"], sides=4)
    return mb.finish()


def bush(seed):
    """A round bush of leafy clumps: dark at the bottom, lighter tops, a few blossoms or berries."""
    def build():
        rng = random.Random(seed)
        m = mats()
        mb = lp.MeshBuilder("Bush%d" % seed)
        blob(mb, (0, 0, 0.45), 0.95, m["LeavesDark"], rng, points=20, squash=0.65)
        for i in range(4):
            a = 2 * math.pi * i / 4 + rng.uniform(0, 0.8)
            d = rng.uniform(0.35, 0.55)
            blob(mb, (math.cos(a) * d, math.sin(a) * d, 0.55 + rng.uniform(0, 0.15)), rng.uniform(0.5, 0.65), m["Leaves"], rng, points=16, squash=0.8)
        blob(mb, (rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), 0.95), 0.5, m["LeavesLight"], rng, points=14, squash=0.75)
        dots = m["MushroomCap"] if seed % 2 else m["Blossom"]
        for _ in range(6):
            a = rng.uniform(0, 2 * math.pi)
            z = rng.uniform(0.55, 1.05)
            r = 0.95 * math.sqrt(max(0.0, 1 - ((z - 0.55) / 0.65) ** 2)) + 0.02
            mb.sphere((math.cos(a) * r * 0.85, math.sin(a) * r * 0.85, z), 0.055, dots, segments=5, rings=3)
        return mb.finish()
    return build


def rock(seed, size):
    """
    A weathered rock: a main block with flat-ish facets, a second chunk leaning on it, a few pebbles at
    its foot and moss on the top faces.
    """
    def build():
        rng = random.Random(seed)
        m = mats()
        mb = lp.MeshBuilder("Rock%d" % seed)

        def chunk(cx, cy, cz, rx, ry, rz, mat):
            pts = []
            for _ in range(13):
                a = rng.uniform(0, 2 * math.pi)
                e = rng.uniform(-0.9, 0.9)
                k = rng.uniform(0.85, 1.05)
                pts.append((cx + math.cos(a) * math.cos(e) * rx * k, cy + math.sin(a) * math.cos(e) * ry * k, cz + math.sin(e) * rz * k))
            # Flatten: clamp to a few planes so the facets read as broken stone, not a ball.
            flat = []
            for x, y, z in pts:
                z = min(z, cz + rz * 0.8)
                flat.append((x, y, z))
            mb.hull(flat, mat)
            return flat

        main = chunk(0, 0, size * 0.3, size * 0.95, size * 0.75, size * 0.55, m["Stone"])
        chunk(size * 0.7, size * 0.35, size * 0.15, size * 0.45, size * 0.4, size * 0.38, m["StoneDark"])
        for _ in range(3):
            a = rng.uniform(0, 2 * math.pi)
            d = size * rng.uniform(1.0, 1.3)
            r = size * rng.uniform(0.1, 0.18)
            blob(mb, (math.cos(a) * d, math.sin(a) * d, r * 0.3), r, m["StoneDark"] if rng.random() < 0.5 else m["Stone"], rng, points=8, squash=0.6)
        top = sorted(main, key=lambda q: -q[2])[:6]
        ztop = max(q[2] for q in top)
        mb.hull([(x * 0.85, y * 0.85, z + 0.03 * size) for x, y, z in top] + [(0, 0, ztop + 0.04 * size)], m["Moss"])
        return mb.finish()
    return build


def reeds():
    """A clump of reeds: leaning stems, a few long blade-like leaves, and cattail heads on some of them."""
    rng = random.Random(3)
    m = mats()
    mb = lp.MeshBuilder("Reeds")
    for _ in range(16):
        x, y = rng.uniform(-0.7, 0.7), rng.uniform(-0.7, 0.7)
        h = rng.uniform(1.2, 2.0)
        lean = (rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25))
        mid = (x + lean[0] * 0.35, y + lean[1] * 0.35, h * 0.5)
        top = (x + lean[0], y + lean[1], h)
        mb.tube([((x, y, -0.1), 0.022), (mid, 0.018), (top, 0.006)], m["Reed"], sides=4)
        if rng.random() < 0.5:
            c0 = tuple(a + (b - a) * 0.78 for a, b in zip((x, y, 0), top))
            c1 = tuple(a + (b - a) * 0.93 for a, b in zip((x, y, 0), top))
            mb.tube([(c0, 0.035), (c1, 0.035)], m["ReedTop"], sides=6)
    for _ in range(7):
        x, y = rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5)
        a = rng.uniform(0, 2 * math.pi)
        d = (math.cos(a), math.sin(a))
        h = rng.uniform(0.6, 1.1)
        tip = (x + d[0] * h * 0.6, y + d[1] * h * 0.6, h)
        mid = (x + d[0] * h * 0.15, y + d[1] * h * 0.15, h * 0.65)
        w = 0.035
        mb.add([(x - d[1] * w, y + d[0] * w, 0.0), (x + d[1] * w, y - d[0] * w, 0.0), (mid[0] + d[1] * w * 0.8, mid[1] - d[0] * w * 0.8, mid[2]),
                tip, (mid[0] - d[1] * w * 0.8, mid[1] + d[0] * w * 0.8, mid[2])], [(0, 1, 2, 3, 4)], m["Leaves"], double_sided=True)
    return mb.finish()


def mushrooms():
    """A cluster of red-capped mushrooms: domed caps with pale spots and gills underneath, curved stems with a ring."""
    rng = random.Random(5)
    m = mats()
    mb = lp.MeshBuilder("Mushrooms")
    for _ in range(6):
        x, y = rng.uniform(-0.35, 0.35), rng.uniform(-0.35, 0.35)
        h = rng.uniform(0.12, 0.38)
        r = h * rng.uniform(0.4, 0.55)
        lean = (rng.uniform(-0.04, 0.04), rng.uniform(-0.04, 0.04))
        top = (x + lean[0], y + lean[1], h)
        mb.tube([((x, y, 0), r * 0.25), ((x + lean[0] * 0.5, y + lean[1] * 0.5, h * 0.5), r * 0.2), (top, r * 0.18)], m["MushroomStem"], sides=6)
        mb.frustum((top[0], top[1], h * 0.62), (top[0], top[1], h * 0.66), r * 0.3, r * 0.3, m["MushroomStem"], sides=6)
        cap = [[(top[0] + math.cos(t) * r * f, top[1] + math.sin(t) * r * f, h + dz) for t in (2 * math.pi * k / 9 for k in range(9))]
               for f, dz in ((1.0, -0.01), (0.85, r * 0.35), (0.5, r * 0.6))]
        cap.append([(top[0], top[1], h + r * 0.7)])
        mb.grid(cap, m["MushroomCap"], None, lambda q, t=top: (t[0], t[1], h - 0.05), closed=True)
        mb.grid([cap[0], [(top[0], top[1], h - r * 0.15)]], m["MushroomStem"], None, lambda q, t=top: (t[0], t[1], h + 0.2), closed=True)
        for k in range(3):
            a = rng.uniform(0, 2 * math.pi)
            mb.sphere((top[0] + math.cos(a) * r * 0.55, top[1] + math.sin(a) * r * 0.55, h + r * 0.42), r * 0.12, m["MushroomStem"], segments=4, rings=3)
    return mb.finish()


def log():
    """A fallen log: bark, cut ends with rings, a broken branch and moss along the top."""
    m = mats()
    mb = lp.MeshBuilder("Log")
    mb.tube([((-2.0, 0, 0.33), 0.34), ((-0.6, 0.03, 0.32), 0.32), ((0.8, -0.02, 0.31), 0.31), ((2.0, 0, 0.3), 0.29)], m["Bark"], sides=8)
    for x, r, f in ((-2.01, 0.31, -1), (2.01, 0.27, 1)):
        for i, rr in enumerate((r, r * 0.66, r * 0.33)):
            mb.frustum((x, 0, 0.33 - (0.03 if f > 0 else 0)), (x + f * (0.012 + 0.004 * i), 0, 0.33 - (0.03 if f > 0 else 0)), rr, rr,
                       m["Plank"] if i % 2 == 0 else m["Wood"], sides=8)
    mb.tube([((0.6, 0.05, 0.55), 0.09), ((0.85, 0.3, 0.95), 0.05), ((0.95, 0.4, 1.15), 0.02)], m["Bark"], sides=5)
    mb.tube([((-1.6, 0, 0.62), (0.02, 0.12)), ((-0.2, 0.02, 0.62), (0.02, 0.16)), ((1.2, -0.02, 0.6), (0.02, 0.1))], m["Moss"], sides=4)
    return mb.finish()


def stump():
    """A cut stump with roots, bark and growth rings on top, a few mushrooms at its foot."""
    m = mats()
    mb = lp.MeshBuilder("Stump")
    mb.tube([((0, 0, -0.2), 0.58), ((0, 0, 0.25), 0.48), ((0, 0, 0.6), 0.45)], m["Bark"], sides=9)
    for i, r in enumerate((0.42, 0.3, 0.18, 0.07)):
        mb.frustum((0, 0, 0.6), (0, 0, 0.615 + 0.004 * i), r, r, m["Plank"] if i % 2 == 0 else m["Wood"], sides=9)
    for i in range(5):
        a = 2 * math.pi * i / 5 + 0.3
        mb.tube([((math.cos(a) * 0.35, math.sin(a) * 0.35, 0.3), 0.16), ((math.cos(a) * 0.7, math.sin(a) * 0.7, 0.05), 0.1),
                 ((math.cos(a) * 0.95, math.sin(a) * 0.95, -0.12), 0.03)], m["Bark"], sides=5)
    for x, y, h in ((0.5, -0.35, 0.18), (0.6, -0.15, 0.12)):
        mb.frustum((x, y, 0), (x, y, h), 0.025, 0.02, m["MushroomStem"], sides=5)
        mb.frustum((x, y, h - 0.02), (x, y, h + 0.06), 0.08, 0.01, m["MushroomCap"], sides=6)
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
    """
    A stretch of palisade: stakes of barked logs of uneven height with pale sharpened tips, lashed with rope
    to two rails behind them, braced by props leaning against the inside.
    """
    m = mats()
    mb = lp.MeshBuilder("Palisade")
    rng = random.Random(2)
    rope = lp.material("Rope", "#a88a5a")
    n = 9
    for i in range(n):
        x = -2.0 + i * 4.0 / (n - 1) + rng.uniform(-0.03, 0.03)
        h = 3.2 + rng.uniform(-0.3, 0.3)
        r = 0.23 + rng.uniform(-0.02, 0.02)
        lean = rng.uniform(-0.04, 0.04)
        mb.tube([((x, 0, -0.5), r), ((x + lean * 0.5, 0, h * 0.5), r * 0.98), ((x + lean, 0, h), r * 0.95)], m["Wood"] if i % 3 else m["WoodDark"], sides=7)
        mb.frustum((x + lean, 0, h), (x + lean * 1.1, 0, h + 0.55), r * 0.95, 0.01, m["Plank"], sides=7)
        for z in (0.8, 2.2):
            mb.frustum((x, 0, z - 0.06), (x, 0, z + 0.06), r + 0.015, r + 0.015, rope, sides=7)
    for z in (0.8, 2.2):
        mb.tube([((-2.2, 0.3, z), 0.1), ((2.2, 0.3, z), 0.1)], m["WoodDark"], sides=6)
    for x in (-1.0, 1.0):
        mb.tube([((x, 1.4, -0.1), 0.08), ((x, 0.36, 2.2), 0.07)], m["WoodDark"], sides=5)
    return mb.finish()


def gate():
    """
    Mirewatch's gate: two towers of three bound logs, a double lintel under a little shingled roof whose
    bargeboards curl into horns like the grove houses', the banner of the village, and two lanterns.
    """
    m = mats()
    mb = lp.MeshBuilder("Gate")
    rope = lp.material("Rope", "#a88a5a")
    slate = lp.material("Slate", "#4f6a65")
    slate_light = lp.material("SlateLight", "#5d7a74")
    iron = lp.material("Iron", "#2d2a28")
    for x in (-2.2, 2.2):
        for dx, dy, h in ((0.0, 0.0, 4.45), (0.3 if x > 0 else -0.3, -0.18, 4.0), (0.3 if x > 0 else -0.3, 0.18, 4.15)):
            r = 0.3 if dx == 0 else 0.2
            mb.tube([((x + dx, dy, -0.5), r), ((x + dx, dy, h), r * 0.93)], m["Wood"] if dx == 0 else m["WoodDark"], sides=7)
            mb.frustum((x + dx, dy, h), (x + dx, dy, h + 0.5), r * 0.93, 0.01, m["Plank"], sides=7)
        for z in (0.6, 2.4, 3.6):
            mb.tube([((x + (0.15 if x > 0 else -0.15), 0, z), (0.36, 0.38)), ((x + (0.15 if x > 0 else -0.15), 0, z + 0.12), (0.36, 0.38))], rope, sides=8)
    for z, r in ((3.85, 0.2), (4.3, 0.18)):
        mb.tube([((-2.75, 0, z), r), ((2.75, 0, z), r)], m["WoodDark"], sides=7)
    for side in (-1, 1):
        mb.tube([((side * 2.0, -0.05, 3.0), 0.09), ((side * 1.2, -0.05, 3.8), 0.08)], m["WoodDark"], sides=5)
    # The roof over the lintel, its bargeboards curling up at both ends.
    for side in (-1, 1):
        for k in range(3):
            t0, t1 = k / 3, min(1.0, (k + 1) / 3 + 0.08)
            z0, z1 = 5.15 - 0.6 * t0, 5.15 - 0.6 * t1
            y0, y1 = side * 0.75 * t0, side * 0.75 * t1
            mb.box((0, (y0 + y1) / 2, (z0 + z1) / 2 + 0.02), (6.0 - 0.15 * k, abs(y1 - y0) + 0.04, 0.09), slate if k % 2 == 0 else slate_light,
                   rotation=(side * -math.degrees(math.atan2(0.6, 0.75)), 0, 0))
    mb.tube([((-3.05, 0, 5.2), 0.1), ((3.05, 0, 5.2), 0.1)], m["WoodDark"], sides=6)
    for x in (-3.05, 3.05):
        f = 1 if x > 0 else -1
        for side in (-1, 1):
            mb.tube([((x, side * 0.8, 4.5), 0.1), ((x, 0, 5.25), 0.09), ((x + f * 0.25, -side * 0.3, 5.8), 0.06), ((x + f * 0.45, -side * 0.35, 6.1), 0.02)],
                    m["WoodDark"], sides=5)
    # The banner hanging from the lintel (above the path: the navmesh needs 2 m of headroom under it, scaled and
    # sunk as HollowmereBuilder places the gate), and the lanterns.
    mb.add([(-0.8, -0.24, 4.1), (0.8, -0.24, 4.1), (0.8, -0.24, 3.35), (0.0, -0.24, 3.1), (-0.8, -0.24, 3.35)], [(0, 1, 2, 3, 4)], m["Banner"], double_sided=True)
    mb.tube([((-0.85, -0.25, 4.12), 0.035), ((0.85, -0.25, 4.12), 0.035)], m["WoodDark"], sides=5)
    mb.hull([(x, -0.26, z) for x, z in ((0, 3.9), (0.2, 3.7), (0, 3.45), (-0.2, 3.7))] + [(0, -0.29, 3.7)], m["Thatch"])
    for x in (-2.2, 2.2):
        top = (x, -0.45, 3.2)
        mb.tube([((x, -0.25, 3.3), 0.025), (top, 0.025)], iron, sides=4)
        mb.frustum(top, (top[0], top[1], top[2] - 0.1), 0.04, 0.12, iron, sides=6)
        mb.box((top[0], top[1], top[2] - 0.26), (0.17, 0.17, 0.24), m["LanternGlow"])
        mb.frustum((top[0], top[1], top[2] - 0.38), (top[0], top[1], top[2] - 0.44), 0.11, 0.04, iron, sides=6)
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
    """A signpost: a post under a little peaked cap, three pointed boards nailed on at different heights."""
    m = mats()
    mb = lp.MeshBuilder("Signpost")
    mb.tube([((0, 0, -0.3), 0.09), ((0, 0, 2.6), 0.075)], m["WoodDark"], sides=6)
    mb.hull([(x, y, 2.62) for x in (-0.14, 0.14) for y in (-0.14, 0.14)] + [(0, 0, 2.85)], m["Thatch"])
    for z, length, yaw in ((2.3, 1.1, 0), (1.92, 1.0, 180), (1.58, 0.85, 35)):
        a = math.radians(yaw)
        d = (math.cos(a), math.sin(a))
        def at(u, v, w, d=d):
            return (d[0] * u - d[1] * v, d[1] * u + d[0] * v, w)
        board = [at(0.06, y, z + dz) for y in (-0.03, 0.03) for dz in (-0.12, 0.12)]
        board += [at(length - 0.2, y, z + dz) for y in (-0.03, 0.03) for dz in (-0.12, 0.12)] + [at(length, y, z) for y in (-0.03, 0.03)]
        mb.hull(board, m["Plank"])
        mb.hull([at(0.12, y, z + dz) for y in (-0.035, 0.035) for dz in (-0.07, 0.07)]
                + [at(length - 0.3, y, z + dz) for y in (-0.035, 0.035) for dz in (-0.07, 0.07)], m["Wood"])
        for u in (0.12, length - 0.3):
            mb.box(at(u, 0.04, z), (0.03, 0.02, 0.03), m["Metal"], rotation=(0, 0, yaw))
    return mb.finish()


def fence():
    """A split-rail fence: two leaning posts, rough rails resting in notches, one rail sagging, a tuft of grass at a post."""
    m = mats()
    rng = random.Random(16)
    mb = lp.MeshBuilder("Fence")
    for x in (-1.5, 1.5):
        lean = rng.uniform(-0.04, 0.04)
        mb.tube([((x, 0, -0.2), 0.08), ((x + lean, 0, 1.15), 0.07)], m["WoodDark"], sides=6)
        mb.frustum((x + lean, 0, 1.15), (x + lean, 0, 1.22), 0.07, 0.04, m["WoodDark"], sides=6)
        for k in range(4):
            a = 2 * math.pi * k / 4 + 0.3
            mb.tube([((x + math.cos(a) * 0.08, math.sin(a) * 0.08, 0.0), 0.012), ((x + math.cos(a) * 0.15, math.sin(a) * 0.15, 0.25), 0.003)],
                    m["Leaves"], sides=3)
    for z, sag in ((0.45, 0.0), (0.92, 0.06)):
        path = [(-1.65, 0.07, z), (0.0, 0.07 + rng.uniform(-0.01, 0.01), z - sag), (1.65, 0.07, z)]
        mb.tube([(q, (0.05, 0.04)) for q in path], m["Plank"] if z < 0.5 else m["Wood"], sides=5)
    return mb.finish()


def crate():
    """A crate of boards in a frame of darker battens, braced across each side, with iron corners."""
    m = mats()
    mb = lp.MeshBuilder("Crate")
    w = 0.9
    h = w / 2
    mb.box((0, 0, h), (w - 0.04, w - 0.04, w - 0.04), m["WoodDark"])
    for k in range(4):
        turn = k * 90
        for i in range(4):
            u = -h + 0.06 + (w - 0.12) * (i + 0.5) / 4
            x, y = (u, -h) if k % 2 == 0 else (h, u)
            x, y = (x, y) if k < 2 else (-x, -y)
            mb.box((x, y, h), (0.19 if k % 2 == 0 else 0.03, 0.03 if k % 2 == 0 else 0.19, w - 0.16), m["Plank"] if i % 2 else m["Wood"])
    for z in (0.06, w - 0.06):
        for k in range(4):
            a = math.radians(90 * k)
            mb.box((math.sin(a) * (h + 0.01), -math.cos(a) * (h + 0.01), z), (w + 0.02, 0.05, 0.1), m["WoodDark"], rotation=(0, 0, 90 * k))
    for k in range(4):
        a = math.radians(90 * k)
        mb.box((math.sin(a) * (h + 0.025), -math.cos(a) * (h + 0.025), h), (w * 1.18, 0.04, 0.09), m["WoodDark"], rotation=(0, 45 if k % 2 else -45, 90 * k))
        for x in (-1, 1):
            for z in (0.06, w - 0.06):
                c = (math.sin(a) * (h + 0.03) + math.cos(a) * x * (h - 0.03), -math.cos(a) * (h + 0.03) + math.sin(a) * x * (h - 0.03), z)
                mb.box(c, (0.09, 0.03, 0.12), m["Metal"], rotation=(0, 0, 90 * k))
    mb.box((0, 0, w + 0.005), (w - 0.02, w - 0.02, 0.03), m["Plank"])
    return mb.finish()


def barrel():
    """A barrel of staves in two tones, bellied, with three iron hoops and a lid of boards."""
    m = mats()
    mb = lp.MeshBuilder("Barrel")
    n = 12
    profile = [(0.0, 0.34), (0.25, 0.41), (0.5, 0.44), (0.75, 0.41), (1.0, 0.34)]
    for k in range(n):
        a0, a1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
        rings = [[(math.cos(a) * r, math.sin(a) * r, z) for a in (a0 + 0.01, a1 - 0.01)] + [(math.cos(a) * (r - 0.04), math.sin(a) * (r - 0.04), z) for a in (a1 - 0.01, a0 + 0.01)]
                 for z, r in profile]
        mb.loft(rings, m["Wood"] if k % 2 else m["Plank"])
    for z in (0.12, 0.5, 0.88):
        r = 0.34 + 0.1 * math.sin(math.pi * z) + 0.012
        mb.frustum((0, 0, z - 0.035), (0, 0, z + 0.035), r, r, m["Metal"], sides=n, cap_a=False, cap_b=False)
    mb.frustum((0, 0, 0.93), (0, 0, 0.945), 0.31, 0.31, m["WoodDark"], sides=n)
    for i in range(4):
        x = -0.27 + 0.18 * i
        mb.box((x, 0, 0.955), (0.16, 0.62 - abs(x) * 0.9, 0.03), m["WoodDark"] if i % 2 else m["Wood"])
    return mb.finish()


def sacks():
    """Three grain sacks slumped against each other, tied at the neck, one spilling a little grain."""
    m = mats()
    mb = lp.MeshBuilder("Sacks")
    rng = random.Random(4)
    grain = lp.material("Grain", "#d8b85a")
    for x, y, z, lean in ((-0.35, 0, 0.0, 12), (0.35, 0.05, 0.0, -10), (0.0, -0.05, 0.42, 4)):
        rings = []
        for h, r in ((0.0, 0.28), (0.08, 0.34), (0.3, 0.37), (0.5, 0.32), (0.62, 0.18), (0.68, 0.08)):
            rings.append([(x + math.cos(t) * r * rng.uniform(0.94, 1.04) + math.radians(lean) * h, y + math.sin(t) * r * 0.85, z + h + 0.02)
                          for t in (2 * math.pi * k / 9 for k in range(9))])
        mb.loft(rings, m["Canvas"])
        top = (x + math.radians(lean) * 0.7, y, z + 0.72)
        mb.tube([((top[0], top[1], top[2] - 0.05), 0.09), (top, 0.09)], m["ReedTop"])
        mb.hull([(top[0] + dx, top[1] + dy, top[2] + dz) for dx, dy, dz in
                 ((0.1, 0, 0.0), (-0.1, 0, 0.0), (0, 0.08, 0.0), (0, -0.08, 0.0), (0.06, 0.03, 0.12), (-0.05, -0.02, 0.1))], m["Canvas"])
    for _ in range(9):
        mb.sphere((rng.uniform(0.6, 0.9), rng.uniform(-0.3, 0.3), 0.02), 0.03, grain, segments=4, rings=3)
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
    """A banner pole: a post with a finial, a crossbar from which a long swallow-tailed banner hangs with a sigil and a fringe."""
    m = mats()
    mb = lp.MeshBuilder("BannerPole")
    gold = lp.material("GoldMetal", "#d6a740")
    mb.tube([((0, 0, -0.3), 0.1), ((0, 0, 5.0), 0.07)], m["WoodDark"], sides=7)
    mb.frustum((0, 0, 5.0), (0, 0, 5.08), 0.09, 0.09, gold, sides=7)
    mb.hull([(math.cos(t) * 0.05, math.sin(t) * 0.05, 5.08) for t in (2 * math.pi * k / 6 for k in range(6))] + [(0, 0, 5.4)], gold)
    mb.tube([((0, 0.02, 4.75), 0.04), ((0, -1.25, 4.75), 0.04)], m["WoodDark"], sides=5)
    for y in (-1.25,):
        mb.sphere((0, y, 4.75), 0.06, gold, segments=6, rings=4)
    # The banner: a cloth with a slight wave, cut into a swallow tail at the bottom.
    rows = []
    for k, z in enumerate((4.7, 4.1, 3.5, 2.95)):
        wave = 0.05 * math.sin(k * 1.3)
        rows.append([(wave + 0.02 * math.sin(y * 4), y, z) for y in (-0.1, -0.45, -0.8, -1.15)])
    rows.append([(0.03, -0.1, 2.5), (0.0, -0.45, 2.85), (0.0, -0.8, 2.85), (0.03, -1.15, 2.5)])
    mb.grid(rows, m["Banner"], None, lambda q: (q[0] - 1, q[1], q[2]))
    mb.grid(rows, m["Banner"], None, lambda q: (q[0] + 1, q[1], q[2]))
    for side in (1, -1):
        mb.hull([(side * 0.03, y, z) for y, z in ((-0.625, 4.35), (-0.4, 3.95), (-0.625, 3.5), (-0.85, 3.95))], gold)
    mb.tube([((0.0, -0.08, 4.7), 0.02), ((0.0, -1.17, 4.7), 0.02)], gold, sides=4)
    return mb.finish()


def cauldron():
    """An iron cauldron on three legs over a few glowing coals, a thick rim, ring handles, a green brew."""
    m = mats()
    mb = lp.MeshBuilder("Cauldron")
    iron = lp.material("Iron", "#3b3a3e")
    brew = lp.material("BrewGlow", "#7ac24a", emission=0.8)
    rings = []
    for z, r in ((0.15, 0.2), (0.22, 0.38), (0.36, 0.52), (0.52, 0.56), (0.68, 0.5), (0.78, 0.44)):
        rings.append([(math.cos(t) * r, math.sin(t) * r, z) for t in (2 * math.pi * k / 12 for k in range(12))])
    mb.loft(rings, iron, cap_b=False)
    mb.tube([((math.cos(t) * 0.46, math.sin(t) * 0.46, 0.8), 0.045) for t in (2 * math.pi * k / 12 for k in range(13))], iron, sides=5)
    mb.frustum((0, 0, 0.7), (0, 0, 0.72), 0.43, 0.43, brew, sides=12)
    for t in (0.0, math.pi):
        mb.tube([((math.cos(t) * 0.54, math.sin(t) * 0.0 + 0.0, 0.66), 0.02), ((math.cos(t) * 0.64, 0, 0.6), 0.02), ((math.cos(t) * 0.6, 0, 0.5), 0.02),
                 ((math.cos(t) * 0.53, 0, 0.5), 0.02)], m["Metal"], sides=4)
    for i in range(3):
        a = i * 2 * math.pi / 3 + 0.5
        mb.tube([((math.cos(a) * 0.3, math.sin(a) * 0.3, 0.25), 0.05), ((math.cos(a) * 0.42, math.sin(a) * 0.42, 0.1), 0.04),
                 ((math.cos(a) * 0.44, math.sin(a) * 0.44, 0.0), 0.05)], iron, sides=5)
    rng = random.Random(3)
    for i in range(7):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.0, 0.25)
        mb.sphere((math.cos(a) * r, math.sin(a) * r, 0.03), rng.uniform(0.05, 0.08), m["FireGlow"] if i % 2 else m["StoneDark"], segments=5, rings=3)
    return mb.finish()


def bridge():
    """
    A humped footbridge: planks of two tones on two log stringers, posts with braces, rope-bound joints
    and a handrail following the curve on each side.
    """
    m = mats()
    mb = lp.MeshBuilder("Bridge")
    rope = lp.material("Rope", "#a88a5a")
    length, width = 10.0, 3.0
    n = 18
    deck = lambda y: 0.6 + 0.5 * math.cos(math.pi * y / length)
    for x in (-width / 2 + 0.3, width / 2 - 0.3):
        mb.tube([((x, y, deck(y) - 0.14), 0.12) for y in [-length / 2 + length * k / 8 for k in range(9)]], m["WoodDark"], sides=6)
    rng = random.Random(14)
    for i in range(n):
        y = -length / 2 + (i + 0.5) * length / n
        mb.box((rng.uniform(-0.04, 0.04), y, deck(y)), (width + rng.uniform(-0.1, 0.1), length / n - 0.05, 0.1), m["Plank"] if i % 2 else m["Wood"],
               rotation=(math.degrees(math.atan(-0.5 * math.pi / length * math.sin(math.pi * y / length))), 0, rng.uniform(-1.5, 1.5)))
    for x in (-width / 2, width / 2):
        for i in range(5):
            y = -length / 2 + i * length / 4
            mb.tube([((x, y, -1.0), 0.11), ((x, y, deck(y) + 1.05), 0.09)], m["WoodDark"], sides=6)
            mb.frustum((x, y, deck(y) + 0.88), (x, y, deck(y) + 0.96), 0.115, 0.115, rope, sides=6)
        rail = [(x, y, deck(y) + 0.95) for y in [-length / 2 + length * k / 12 for k in range(13)]]
        mb.tube([(q, 0.055) for q in rail], m["Wood"], sides=5)
        for i in range(4):
            y0, y1 = -length / 2 + i * length / 4, -length / 2 + (i + 1) * length / 4
            mb.tube([((x, y0, deck(y0) + 0.1), 0.04), ((x, y1, deck(y1) + 0.85), 0.04)], m["WoodDark"], sides=4)
    return mb.finish()


def dock():
    """A dock of planks on log piles with rope-bound tops, a mooring post, a coil of rope and a little boat tied up."""
    m = mats()
    mb = lp.MeshBuilder("Dock")
    rope = lp.material("Rope", "#a88a5a")
    rng = random.Random(15)
    length, width = 8.0, 2.4
    for i in range(14):
        y = (i + 0.5) * length / 14
        mb.box((rng.uniform(-0.05, 0.05), -y, 0.4), (width + rng.uniform(-0.12, 0.08), length / 14 - 0.05, 0.1), m["Plank"] if i % 2 else m["Wood"],
               rotation=(0, 0, rng.uniform(-2, 2)))
    for x in (-width / 2 + 0.1, width / 2 - 0.1):
        mb.box((x, -length / 2, 0.29), (0.14, length, 0.14), m["WoodDark"])
        for y in (0.3, length / 2, length - 0.3):
            mb.tube([((x, -y, -1.5), 0.13), ((x, -y, 0.7), 0.11)], m["WoodDark"], sides=6)
            mb.frustum((x, -y, 0.55), (x, -y, 0.62), 0.125, 0.125, rope, sides=6)
    mb.tube([((width / 2 - 0.1, -length + 0.3, 0.7), 0.11), ((width / 2 - 0.1, -length + 0.3, 1.0), 0.09)], m["WoodDark"], sides=6)
    for k in range(3):
        mb.frustum((0.6, -length + 1.2, 0.46 + 0.035 * k), (0.6, -length + 1.2, 0.49 + 0.035 * k), 0.2 - 0.03 * k, 0.2 - 0.03 * k, rope, sides=8)
    # A small rowing boat tied alongside.
    bx, by = width / 2 + 0.9, -length + 2.2
    rings = []
    for f, half, depth in ((-1.0, 0.05, 0.25), (-0.7, 0.4, 0.38), (0.0, 0.58, 0.42), (0.7, 0.48, 0.38), (1.0, 0.3, 0.3)):
        y = by + f * 1.5
        rings.append([(bx + math.sin(t) * half, y, 0.15 - depth * max(0.0, -math.cos(t)) * 0.9 + (0.08 if abs(f) > 0.9 else 0.0))
                      for t in (math.pi / 2 + math.pi * k / 6 for k in range(7))])
    mb.grid(rings, m["Wood"], None, lambda q: (bx, q[1], 0.6))
    mb.grid(rings, m["WoodDark"], None, lambda q: (bx, q[1], 0.6), facing_in=True)
    for f in (-0.3, 0.4):
        mb.box((bx, by + f * 1.5, 0.05), (0.95, 0.18, 0.05), m["Plank"])
    mb.tube([((width / 2 - 0.1, -length + 0.3, 0.9), 0.015), ((bx, by - 1.4, 0.2), 0.015)], rope, sides=4)
    return mb.finish()


def lantern_post():
    """A carved post with an iron bracket and a hanging lantern: a glowing glass in an iron cage under a cap."""
    m = mats()
    mb = lp.MeshBuilder("LanternPost")
    iron = lp.material("Iron", "#2d2a28")
    mb.tube([((0, 0, -0.3), 0.11), ((0, 0, 0.3), 0.1), ((0, 0, 3.0), 0.075)], m["WoodDark"], sides=6)
    mb.frustum((0, 0, 3.0), (0, 0, 3.15), 0.09, 0.02, m["WoodDark"], sides=6)
    for z in (0.35, 2.5):
        mb.frustum((0, 0, z), (0, 0, z + 0.06), 0.12, 0.12, iron, sides=6)
    mb.tube([((0, 0, 2.9), 0.03), ((0, -0.4, 2.9), 0.03), ((0, -0.72, 2.88), 0.03)], iron, sides=4)
    mb.tube([((0, -0.05, 2.6), 0.02), ((0, -0.35, 2.88), 0.02)], iron, sides=4)
    x, y, top = 0, -0.7, 2.8
    mb.frustum((x, y, top), (x, y, top - 0.12), 0.05, 0.15, iron, sides=6)
    mb.box((x, y, top - 0.3), (0.2, 0.2, 0.3), m["LanternGlow"])
    for dx in (-0.1, 0.1):
        for dy in (-0.1, 0.1):
            mb.box((x + dx, y + dy, top - 0.3), (0.03, 0.03, 0.32), iron)
    mb.frustum((x, y, top - 0.46), (x, y, top - 0.52), 0.13, 0.05, iron, sides=6)
    return mb.finish()


def ruin_pillar():
    """A broken pillar of drums on a stepped base, its top snapped off, a fallen drum and moss in the joints."""
    m = mats()
    rng = random.Random(12)
    mb = lp.MeshBuilder("RuinPillar")
    mb.box((0, 0, 0.12), (1.4, 1.4, 0.24), m["StoneDark"])
    mb.box((0, 0, 0.32), (1.1, 1.1, 0.18), m["Stone"])
    z = 0.41
    for i in range(4):
        h = rng.uniform(0.48, 0.58)
        mb.frustum((rng.uniform(-0.02, 0.02), rng.uniform(-0.02, 0.02), z), (0, 0, z + h - 0.03), 0.42 - 0.01 * i, 0.41 - 0.01 * i,
                   m["Stone"] if i % 2 == 0 else m["StoneDark"], sides=10)
        mb.frustum((0, 0, z + h - 0.03), (0, 0, z + h), 0.38, 0.38, m["Moss"] if i in (1, 2) else m["StoneDark"], sides=10)
        z += h
    mb.hull([(0.4 * math.cos(i * math.pi / 5), 0.4 * math.sin(i * math.pi / 5), z) for i in range(10)]
            + [(0.3, 0.1, z + 0.6), (-0.2, 0.3, z + 0.3), (0.1, -0.35, z + 0.45)], m["Stone"])
    mb.frustum((0.9, 0.5, 0.2), (1.25, 1.0, 0.2), 0.36, 0.36, m["Stone"], sides=10)
    blob(mb, (-0.8, -0.5, 0.1), 0.25, m["StoneDark"], rng, squash=0.6)
    blob(mb, (0.35, -0.75, 0.05), 0.4, m["Moss"], rng, squash=0.25)
    return mb.finish()


def ruin_wall():
    """A broken wall of fitted stone blocks in courses, crumbling to a jagged top, with moss and fallen stones."""
    m = mats()
    mb = lp.MeshBuilder("RuinWall")
    rng = random.Random(9)
    tops = [2.5, 2.2, 1.6, 1.2, 1.9, 2.6, 2.3, 1.4, 0.9, 1.3, 0.7, 0.5]
    length, depth = 6.0, 0.7
    course_h = 0.32
    for c in range(int(2.6 / course_h) + 1):
        z0 = c * course_h
        offset = 0.25 if c % 2 else 0.0
        x = -length / 2 - offset
        while x < length / 2:
            w = rng.uniform(0.45, 0.7)
            x1 = min(x + w, length / 2)
            x0 = max(x, -length / 2)
            col = int((x0 + x1) / 2 / length * len(tops) + len(tops) / 2)
            if x1 - x0 > 0.15 and z0 + course_h <= tops[max(0, min(len(tops) - 1, col))]:
                mb.box(((x0 + x1) / 2, rng.uniform(-0.02, 0.02), z0 + course_h / 2), (x1 - x0 - 0.03, depth - rng.uniform(0, 0.06), course_h - 0.03),
                       m["Stone"] if rng.random() < 0.6 else m["StoneDark"], rotation=(0, 0, rng.uniform(-2, 2)))
            x = x1
    mb.box((0, 0, -0.1), (length + 0.2, depth + 0.1, 0.2), m["StoneDark"])
    for x in (-2.4, -0.5, 1.7):
        blob(mb, (x, rng.uniform(-0.3, 0.3), tops[int((x / length + 0.5) * len(tops))] - 0.05), 0.3, m["Moss"], rng, squash=0.35)
    for _ in range(4):
        blob(mb, (rng.uniform(-2.5, 2.5), rng.choice((-1, 1)) * rng.uniform(0.7, 1.3), 0.12), rng.uniform(0.18, 0.3), m["Stone"], rng, points=10, squash=0.7)
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
