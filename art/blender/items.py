"""
Held items. The origin is the grip (the middle of the fist), the weapon points along +Z (Unity +Y)
and its edge or face lies along X, so the client can put any of them in either hand.
Shields: the face looks along +X (outward on the left arm).
"""

import math
import random

from mathutils import Vector

import lowpoly as lp

METAL = "#c9cdd5"


def blade(mb, mat, base, length, width, thickness=0.012, curve=0.0, tip=0.25, fuller=None):
    """
    A blade from z = base to a point: a diamond cross-section (a ridge down the middle), tapering into the
    tip; `curve` bends it toward +X, `fuller` (a material) cuts a dark groove down both faces.
    """
    rows = []
    for t, w in ((0.0, width), (0.35, width * 0.98), (1 - tip, width * 1.04), (1 - tip * 0.45, width * 0.6), (1.0, 0.0)):
        z = base + length * t
        bend = curve * t * t
        rows.append([(bend + w / 2, 0, z), (bend, thickness / 2, z), (bend - w / 2, 0, z), (bend, -thickness / 2, z)])
    rows[-1] = [rows[-1][0]]
    for a, b in zip(rows, rows[1:]):
        pts = a + b
        mb.hull(pts, mat)
    if fuller is not None:
        for side in (1, -1):
            pts = []
            for t in (0.04, 0.62):
                z = base + length * t
                bend = curve * t * t
                pts += [(bend + dx, side * thickness * 0.45, z) for dx in (-width * 0.08, width * 0.08)]
                pts += [(bend + dx, side * thickness * 0.52, z) for dx in (-width * 0.05, width * 0.05)]
            mb.hull(pts, fuller)


def grip(mb, z0, z1, radius, wrap, band=None, wraps=5):
    """A grip wrapped in leather: alternating fat and thin rings between z0 and z1, bands of metal at both ends."""
    for k in range(wraps):
        a = z0 + (z1 - z0) * k / wraps
        b = z0 + (z1 - z0) * (k + 1) / wraps
        mb.tube([((0, 0, a), radius * 0.92), ((0, 0, (a + b) / 2), radius * 1.08), ((0, 0, b), radius * 0.92)], wrap, sides=7)
    if band is not None:
        for z in (z0, z1):
            mb.frustum((0, 0, z - 0.008), (0, 0, z + 0.008), radius * 1.25, radius * 1.25, band, sides=7)


def crossguard(mb, z, half, mat, curl=0.0, thick=0.022):
    """A crossguard along X, swelling at the middle, its ends curling toward the blade by `curl`."""
    path = [(-half, 0, z + curl), (-half * 0.6, 0, z + curl * 0.15), (0, 0, z), (half * 0.6, 0, z + curl * 0.15), (half, 0, z + curl)]
    radii = [(thick * 0.6, thick * 0.6), (thick * 0.75, thick * 0.8), (thick * 1.1, thick * 1.2), (thick * 0.75, thick * 0.8), (thick * 0.6, thick * 0.6)]
    mb.tube(list(zip(path, radii)), mat, sides=6)
    for x in (-half, half):
        mb.sphere((x, 0, z + curl), thick * 0.8, mat, segments=6, rings=4)


def shortsword():
    """A soldier's sword: a ridged blade with a fuller, a crossguard with curled ends, a wrapped grip, a disc pommel."""
    mb = lp.MeshBuilder("Shortsword")
    leather = lp.material("Leather", "#5a3a22")
    metal = lp.material("Metal", METAL)
    dark = lp.material("DarkMetal", "#5b5f68")
    grip(mb, -0.08, 0.075, 0.019, leather, dark)
    mb.frustum((0, -0.014, -0.105), (0, 0.014, -0.105), 0.036, 0.036, dark, sides=8)
    mb.sphere((0, 0, -0.105), 0.016, metal, segments=6, rings=4)
    crossguard(mb, 0.095, 0.11, dark, curl=0.025)
    mb.hull([(x, y, z) for x in (-0.035, 0.035) for y in (-0.012, 0.012) for z in (0.088, 0.12)], dark)
    blade(mb, metal, 0.11, 0.62, 0.058, thickness=0.014, fuller=dark)
    return mb.finish()


def cutlass():
    """A curved cutlass with a single-edged widening blade, a brass knuckle guard and a wooden grip."""
    mb = lp.MeshBuilder("Cutlass")
    wood = lp.material("DarkWood", "#3e2a18")
    gold = lp.material("GoldMetal", "#d6a740")
    metal = lp.material("Metal", METAL)
    dark = lp.material("DarkMetal", "#5b5f68")
    grip(mb, -0.08, 0.075, 0.019, wood, gold, wraps=4)
    mb.sphere((0, 0, -0.1), 0.026, gold, segments=7, rings=4)
    crossguard(mb, 0.095, 0.075, gold, curl=-0.02)
    # The knuckle guard: a bar from the guard's back end down around the fist to the pommel.
    mb.tube([((-0.075, 0, 0.075), 0.009), ((-0.1, 0, 0.0), 0.009), ((-0.085, 0, -0.08), 0.009), ((-0.02, 0, -0.1), 0.009)], gold, sides=5)
    # The blade: one edge (+X) curving back, widening toward a clipped point; a dark back edge.
    rows = []
    for t, w in ((0.0, 0.05), (0.4, 0.06), (0.75, 0.072), (0.92, 0.05), (1.0, 0.0)):
        z = 0.11 + 0.56 * t
        bend = 0.1 * t * t
        rows.append([(bend + w * 0.6, 0, z), (bend - w * 0.4, 0.006, z), (bend - w * 0.4, -0.006, z)])
    rows[-1] = [rows[-1][0]]
    for a, b in zip(rows, rows[1:]):
        mb.hull(a + b, metal)
    mb.tube([((0.1 * t * t - [0.05, 0.06, 0.072, 0.05][k] * 0.4, 0, 0.11 + 0.56 * t), 0.004) for k, t in enumerate((0.0, 0.4, 0.75, 0.92))], dark, sides=4)
    return mb.finish()


def staff():
    """A gnarled wooden staff: a knotty shaft bound with leather where it is held, its head split into twisting prongs around a stone."""
    mb = lp.MeshBuilder("Staff")
    wood = lp.material("Wood", "#7a5531")
    dark = lp.material("DarkWood", "#4e341e")
    leather = lp.material("Leather", "#5a3a22")
    stone = lp.material("Stone", "#8a9a9a")
    path = [(0, 0, -0.6), (0.006, 0.004, -0.2), (-0.004, 0, 0.3), (0.008, -0.004, 0.75), (0, 0, 1.1)]
    mb.tube([(q, r) for q, r in zip(path, (0.022, 0.025, 0.026, 0.028, 0.032))], wood, sides=7)
    for z, dx in ((-0.35, 0.02), (0.55, -0.02), (0.85, 0.022)):
        mb.sphere((dx, 0, z), 0.022, dark, segments=5, rings=3)
    grip(mb, -0.12, 0.12, 0.029, leather, wraps=6)
    for k in range(4):
        a = math.pi / 2 * k
        mb.tube([((0, 0, 1.08), 0.022), ((math.cos(a) * 0.06, math.sin(a) * 0.06, 1.2), 0.016),
                 ((math.cos(a + 0.8) * 0.05, math.sin(a + 0.8) * 0.05, 1.32), 0.011), ((math.cos(a + 1.5) * 0.015, math.sin(a + 1.5) * 0.015, 1.38), 0.004)],
                wood if k % 2 else dark, sides=5)
    mb.sphere((0, 0, 1.25), 0.045, stone, segments=7, rings=5)
    return mb.finish()


def crystal_rod():
    """A rod of dark metal with silver bands, a cradle of four claws holding a glowing crystal, a spike at the foot."""
    mb = lp.MeshBuilder("CrystalRod")
    dark = lp.material("DarkMetal", "#454a55")
    metal = lp.material("Metal", METAL)
    leather = lp.material("Leather", "#3a3048")
    crystal = lp.material("CrystalGlow", "#30c8ec", emission=2.0)
    mb.tube([((0, 0, -0.5), 0.02), ((0, 0, 0.4), 0.024), ((0, 0, 1.12), 0.026)], dark, sides=7)
    mb.hull([(0, 0, -0.62)] + [(math.cos(t) * 0.02, math.sin(t) * 0.02, -0.5) for t in (2 * math.pi * k / 6 for k in range(6))], metal)
    grip(mb, -0.12, 0.12, 0.026, leather, metal, wraps=6)
    for z in (0.45, 0.8):
        mb.frustum((0, 0, z), (0, 0, z + 0.03), 0.032, 0.032, metal, sides=7)
    mb.tube([((0, 0, 1.1), 0.03), ((0, 0, 1.16), 0.055), ((0, 0, 1.2), 0.04)], metal, sides=8)
    for k in range(4):
        a = math.pi / 2 * k + math.pi / 4
        mb.tube([((math.cos(a) * 0.035, math.sin(a) * 0.035, 1.19), 0.012), ((math.cos(a) * 0.07, math.sin(a) * 0.07, 1.3), 0.01),
                 ((math.cos(a) * 0.045, math.sin(a) * 0.045, 1.42), 0.004)], metal, sides=4)
    mb.hull([(0.05, 0, 1.34), (-0.05, 0, 1.34), (0, 0.05, 1.34), (0, -0.05, 1.34), (0.035, 0.035, 1.3), (-0.035, -0.035, 1.3),
             (0, 0, 1.22), (0, 0, 1.58)], crystal)
    return mb.finish()


def cudgel():
    """A knobbly club: a root of hardwood thickening into a gnarled head, studded with iron nails, a thong at the grip."""
    mb = lp.MeshBuilder("Cudgel")
    wood = lp.material("Wood", "#6e4b2a")
    dark = lp.material("DarkWood", "#4e341e")
    iron = lp.material("DarkMetal", "#5b5f68")
    leather = lp.material("Leather", "#5a3a22")
    mb.tube([((0, 0, -0.11), 0.022), ((0, 0, 0.12), 0.026), ((0.004, 0, 0.3), 0.04), ((0.008, 0, 0.45), 0.058), ((0.004, 0, 0.56), 0.05)], wood, sides=7)
    rng = random.Random(10)
    for _ in range(5):
        a = rng.uniform(0, 2 * math.pi)
        z = rng.uniform(0.3, 0.52)
        mb.sphere((math.cos(a) * 0.045, math.sin(a) * 0.045, z), 0.022, dark, segments=5, rings=3)
    for k in range(8):
        a = 2 * math.pi * k / 8 + (0.4 if k % 2 else 0)
        z = 0.38 + 0.1 * (k % 2)
        base = (math.cos(a) * 0.05, math.sin(a) * 0.05, z)
        mb.tube([(base, 0.008), ((base[0] * 1.6, base[1] * 1.6, z + 0.01), 0.002)], iron, sides=4)
    grip(mb, -0.09, 0.08, 0.023, leather, wraps=4)
    mb.tube([((0, 0, -0.1), 0.006), ((0.03, 0, -0.16), 0.006), ((0, 0, -0.2), 0.006), ((-0.03, 0, -0.16), 0.006), ((0, 0, -0.1), 0.006)], leather, sides=4)
    return mb.finish()


def ornate_mace():
    """A temple mace: a gilded haft with a wrapped grip, a round head ringed by flanges, a spike on top."""
    mb = lp.MeshBuilder("OrnateMace")
    gold = lp.material("GoldMetal", "#d6a740")
    metal = lp.material("Metal", METAL)
    leather = lp.material("Leather", "#6a2a2a")
    mb.tube([((0, 0, -0.12), 0.02), ((0, 0, 0.42), 0.022)], gold, sides=7)
    grip(mb, -0.09, 0.08, 0.022, leather, gold, wraps=4)
    mb.sphere((0, 0, -0.13), 0.032, gold, segments=7, rings=4)
    for z in (0.25, 0.4):
        mb.frustum((0, 0, z), (0, 0, z + 0.02), 0.03, 0.03, gold, sides=7)
    mb.sphere((0, 0, 0.5), 0.062, metal, segments=8, rings=6)
    for k in range(6):
        a = 2 * math.pi * k / 6
        d = (math.cos(a), math.sin(a))
        mb.hull([(d[0] * r + d[1] * dy, d[1] * r - d[0] * dy, z) for r, z in ((0.04, 0.42), (0.04, 0.58), (0.1, 0.47), (0.1, 0.53)) for dy in (-0.006, 0.006)],
                metal)
    mb.frustum((0, 0, 0.42), (0, 0, 0.45), 0.045, 0.05, gold, sides=8)
    mb.hull([(math.cos(t) * 0.022, math.sin(t) * 0.022, 0.56) for t in (2 * math.pi * k / 6 for k in range(6))] + [(0, 0, 0.68)], gold)
    return mb.finish()


def hatchet():
    """A woodsman's hatchet: a curved haft with a swollen end, a bearded head wedged on, a wrapped grip."""
    mb = lp.MeshBuilder("Hatchet")
    wood = lp.material("Wood", "#7a5531")
    metal = lp.material("Metal", METAL)
    dark = lp.material("DarkMetal", "#5b5f68")
    leather = lp.material("Leather", "#5a3a22")
    mb.tube([((0, 0, -0.13), 0.026), ((0.004, 0, -0.08), 0.02), ((0.01, 0, 0.2), 0.022), ((0.004, 0, 0.48), 0.022)], wood, sides=7)
    grip(mb, -0.08, 0.08, 0.022, leather, wraps=4)
    # The head: an eye around the haft, a cheek widening into a bearded edge.
    mb.hull([(x, y, z) for x in (-0.03, 0.03) for y in (-0.022, 0.022) for z in (0.37, 0.47)], dark)
    mb.hull([(0.03, 0.016, 0.38), (0.03, -0.016, 0.38), (0.03, 0.016, 0.46), (0.03, -0.016, 0.46),
             (0.15, 0.004, 0.3), (0.15, -0.004, 0.3), (0.18, 0.003, 0.36), (0.18, -0.003, 0.36),
             (0.17, 0.003, 0.5), (0.17, -0.003, 0.5)], metal)
    mb.hull([(0.15, 0.005, 0.3), (0.15, -0.005, 0.3), (0.185, 0.0, 0.36), (0.175, 0.0, 0.5), (0.16, 0.005, 0.5), (0.16, -0.005, 0.5)], dark)
    mb.box((0.0, 0, 0.49), (0.02, 0.012, 0.03), dark)
    return mb.finish()


def great_axe():
    """A two-handed axe: a long haft bound in three places, a crescent blade with a fluted cheek, a back spike and a top spike."""
    mb = lp.MeshBuilder("GreatAxe")
    wood = lp.material("Wood", "#6e4b2a")
    metal = lp.material("Metal", METAL)
    dark = lp.material("DarkMetal", "#5b5f68")
    leather = lp.material("Leather", "#5a3a22")
    mb.tube([((0, 0, -0.45), 0.026), ((0, 0, 0.4), 0.029), ((0, 0, 1.05), 0.031)], wood, sides=7)
    grip(mb, -0.14, 0.32, 0.03, leather, dark, wraps=8)
    mb.sphere((0, 0, -0.47), 0.035, dark, segments=6, rings=4)
    # The blade: a crescent, its edge a curve from the beard below to the horn above.
    edge = [(0.26 + 0.07 * math.sin(math.pi * t), 0.6 + 0.52 * t) for t in [k / 6 for k in range(7)]]
    rows = [[(0.03, y, z) for y in (0.02, -0.02)] for z in (0.74, 0.96)]
    pts = rows[0] + rows[1]
    for x, z in edge:
        pts += [(x, 0.004, z), (x, -0.004, z)]
    mb.hull(pts, metal)
    mb.hull([(x, y, z) for x in (-0.035, 0.035) for y in (-0.028, 0.028) for z in (0.72, 0.98)], dark)
    for side in (1, -1):
        mb.hull([(0.06, side * 0.016, 0.78), (0.06, side * 0.016, 0.92), (0.2, side * 0.008, 0.8), (0.2, side * 0.008, 0.9),
                 (0.06, side * 0.02, 0.85)], dark)
    mb.hull([(0.0, 0.016, 0.79), (0.0, -0.016, 0.79), (0.0, 0.016, 0.91), (0.0, -0.016, 0.91), (-0.17, 0, 0.83)], dark)
    mb.hull([(0.022, 0.022, 1.02), (-0.022, 0.022, 1.02), (0.022, -0.022, 1.02), (-0.022, -0.022, 1.02), (0, 0, 1.2)], dark)
    return mb.finish()


def cleaver():
    """
    A two-handed cleaver of slag iron: a long haft wrapped in leather between iron bands, a heavy pommel
    ring, and a broad blade with a thick spine, a bellied, chipped edge glowing with heat, a hanging hole
    near its top corner, rivets down the spine and cracks of fire across its faces.
    """
    mb = lp.MeshBuilder("Cleaver")
    wrap = lp.material("Leather", "#3a3230")
    iron = lp.material("DarkMetal", "#4a4146")
    rust = lp.material("Rust", "#6e3a2e")
    glow = lp.material("EmberGlow", "#ffb030", emission=3.0)
    hole = lp.material("Hole", "#141012")
    mb.tube([((0, 0, -0.44), 0.024), ((0, 0, 0.3), 0.026), ((0, 0, 1.0), 0.028)], wrap, sides=7)
    grip(mb, -0.36, 0.3, 0.027, wrap, iron, wraps=9)
    for z in (0.38, 0.55):
        mb.frustum((0, 0, z), (0, 0, z + 0.035), 0.034, 0.034, iron, sides=7)
    mb.tube([((0, 0.0, -0.44), 0.03), ((0, 0, -0.5), 0.045), ((0, 0, -0.56), 0.03)], iron, sides=7)
    mb.tube([(Vector((0, math.cos(a) * 0.05, -0.6 + math.sin(a) * 0.05)), 0.012) for a in [i * math.pi / 5 for i in range(11)]], iron, sides=5)

    # The blade: cross-sections up its height, a thick spine at the haft thinning to a wedge at the edge.
    def edge_x(z):
        """How far out the edge reaches: a belly in the middle, chipped in a few places."""
        t = (z - 0.44) / (1.1 - 0.44)
        x = 0.3 + 0.08 * math.sin(math.pi * min(1.0, t * 1.1)) - 0.05 * max(0.0, t - 0.85) / 0.15
        for chip, depth in ((0.3, 0.025), (0.62, 0.018)):
            x -= depth * max(0.0, 1 - abs(t - chip) / 0.04)
        return x

    zs = [0.44 + (1.1 - 0.44) * k / 14 for k in range(15)]
    body, edge = [], []
    for z in zs:
        x1 = edge_x(z)
        bevel = x1 - 0.045
        body.append([(-0.035, 0.024, z), (bevel * 0.6, 0.02, z), (bevel, 0.012, z), (bevel, -0.012, z), (bevel * 0.6, -0.02, z), (-0.035, -0.024, z)])
        edge.append([(bevel, 0.012, z), (x1, 0.0, z), (bevel, -0.012, z)])
    mb.loft(body, iron)
    mb.loft(edge, glow)
    # A rusty spine along the top and back, rivets down the haft, the hanging hole.
    mb.tube([((-0.045, 0, 0.42), 0.03), ((-0.045, 0, 1.12), 0.03)], rust, sides=6)
    mb.tube([((-0.04, 0, 1.11), 0.024), ((edge_x(1.1) * 0.5, 0, 1.12), 0.022), ((edge_x(1.1) - 0.02, 0, 1.1), 0.015)], rust, sides=6)
    for z in (0.5, 0.66, 0.82, 0.98):
        for y in (0.027, -0.027):
            mb.sphere((0.0, y, z), 0.012, iron, segments=5, rings=3)
    for y in (0.0205, -0.0205):
        mb.sphere((0.22, y, 1.0), (0.032, 0.004, 0.032), hole, segments=8, rings=3)
    # Cracks of fire across both faces.
    rng = random.Random(9)
    for y in (0.0215, -0.0215):
        for start in ((0.05, 0.6), (0.12, 0.86)):
            pts = [Vector((start[0], y, start[1]))]
            for _ in range(4):
                pts.append(pts[-1] + Vector((rng.uniform(0.03, 0.05), 0, rng.uniform(-0.04, 0.04))))
            mb.tube([(p, 0.004 if i else 0.006) for i, p in enumerate(pts)], glow, sides=4)
    return mb.finish()


def buckler():
    """A round buckler of planks in an iron rim, riveted, with a domed boss and a painted band."""
    mb = lp.MeshBuilder("Buckler")
    wood = lp.material("Wood", "#7a5531")
    dark = lp.material("DarkWood", "#5a3d22")
    metal = lp.material("Metal", METAL)
    paint = lp.material("Paint", "#2f4f9a")
    cx, cz, r = 0.05, 0.1, 0.27
    for k in range(5):
        y0 = -r + 2 * r * k / 5
        y1 = -r + 2 * r * (k + 1) / 5
        pts = []
        for y in (y0 + 0.004, y1 - 0.004):
            h = math.sqrt(max(0.0, r * r - y * y))
            pts += [(x, y, cz + dz) for x in (cx - 0.02, cx + 0.02) for dz in (-h, h)]
        mb.hull(pts, wood if k % 2 else dark)
    ring_pts = [(cx + 0.025, math.cos(t) * r * 0.62, cz + math.sin(t) * r * 0.62) for t in (2 * math.pi * k / 16 for k in range(17))]
    mb.tube([(q, 0.022) for q in ring_pts], paint, sides=4)
    mb.tube([((cx, math.cos(t) * r, cz + math.sin(t) * r), (0.03, 0.016)) for t in (2 * math.pi * k / 20 for k in range(21))], metal, sides=5)
    for k in range(10):
        t = 2 * math.pi * k / 10
        mb.sphere((cx + 0.024, math.cos(t) * r * 0.85, cz + math.sin(t) * r * 0.85), 0.011, metal, segments=5, rings=3)
    mb.sphere((cx + 0.03, 0, cz), (0.05, 0.075, 0.075), metal, segments=9, rings=5)
    mb.frustum((cx + 0.02, 0, cz), (cx + 0.03, 0, cz), 0.09, 0.09, metal, sides=10)
    return mb.finish()


def _ring(mb, mat, cx, cz, r, radius, n=32, x_at=None):
    """A closed ring of tube around a shield in the YZ plane (a rim): radius (across the face, out of it)."""
    ru, rv = radius if isinstance(radius, tuple) else (radius, radius)
    rings = []
    for k in range(n):
        t = 2 * math.pi * k / n
        c = Vector((x_at(r) if x_at else cx, math.cos(t) * r, cz + math.sin(t) * r))
        out = Vector((0, math.cos(t), math.sin(t)))
        rings.append([c + out * (math.cos(a) * ru) + Vector((1, 0, 0)) * (math.sin(a) * rv) for a in (2 * math.pi * j / 6 for j in range(6))])
    rings.append(rings[0])
    mb.loft(rings, mat, cap_a=False, cap_b=False, smooth=True)


def _round_face(mb, mat, cx, cz, r, dome, rings, segments, inside_x):
    """A shield's round face in the YZ plane at x = cx, bulging out (+X) by `dome` at the middle; returns the height function."""
    def height(rho):
        return dome * (1 - (rho / r) ** 2)
    rows = [[(cx + dome, 0.0, cz)]]
    for k in range(1, rings + 1):
        rho = r * k / rings
        rows.append([(cx + height(rho), math.cos(t) * rho, cz + math.sin(t) * rho)
                     for t in (2 * math.pi * i / segments for i in range(segments))])
    mb.grid(rows, mat, inside=lambda p: (inside_x, p[1] * 0.5, cz + (p[2] - cz) * 0.5), closed=True, smooth=True)
    return height


def _star_on(mb, mat, cx, cz, height, long_ray, short_ray, inner, lift=0.007, depth=0.012):
    """An eight-pointed star lying on a domed face (its height function), raised by `lift`."""
    ring = []
    for i in range(16):
        a = 2 * math.pi * i / 16
        radius = (long_ray if i % 4 == 0 else short_ray) if i % 2 == 0 else inner
        ring.append((cx + height(radius) + lift, math.sin(a) * radius, cz + math.cos(a) * radius))
    back = [(x - depth, y, z) for x, y, z in ring]
    tip = (cx + height(0) + lift + 0.01, 0, cz)
    n = len(ring)
    faces = [(i, (i + 1) % n, 2 * n) for i in range(n)] + [((i + 1) % n, i, n + i, n + (i + 1) % n) for i in range(n)]
    mb.add(ring + back + [tip], faces, mat, closed=True)


def knight_shield():
    """A round steel shield of Mirewatch's knights: domed, a gilded rim, an eight-pointed gilded star with a boss, rivets."""
    mb = lp.MeshBuilder("KnightShield")
    steel = lp.material("Metal", METAL)
    dark = lp.material("DarkMetal", "#6c7480")
    gold = lp.material("GoldMetal", "#d2a845")
    leather = lp.material("Leather", "#4a3222")
    cx, cz, r = 0.04, 0.1, 0.3
    height = _round_face(mb, steel, cx, cz, r, 0.05, 6, 28, cx - 0.2)
    # The back: a flat dish of darker steel, straps where the arm goes.
    back = [[(cx - 0.012, 0.0, cz)]] + [[(cx - 0.012, math.cos(t) * r, cz + math.sin(t) * r) for t in (2 * math.pi * i / 28 for i in range(28))]]
    mb.grid(back, dark, inside=lambda p: (cx + 0.2, 0, cz), closed=True)
    for z in (cz - 0.09, cz + 0.07):
        mb.box((cx - 0.03, 0, z), (0.02, 0.2, 0.04), leather)
    _ring(mb, gold, cx + 0.002, cz, r, (0.014, 0.02))
    _ring(mb, dark, cx, cz, r * 0.72, 0.007, n=28, x_at=lambda rho: cx + height(rho) + 0.003)
    _star_on(mb, gold, cx, cz, height, 0.2, 0.13, 0.045)
    mb.sphere((cx + height(0) + 0.02, 0, cz), (0.03, 0.045, 0.045), gold, segments=12, rings=7, smooth=True)
    for k in range(12):
        t = 2 * math.pi * (k + 0.5) / 12
        rho = r * 0.86
        mb.sphere((cx + height(rho) + 0.004, math.cos(t) * rho, cz + math.sin(t) * rho), 0.01, gold, segments=6, rings=4, smooth=True)
    return mb.finish(sharp_angle=40)


def warden_shield():
    """A warden's round shield: planks painted the Greywood's teal, the great tree in gold, an iron rim with rivets."""
    mb = lp.MeshBuilder("WardenShield")
    wood = lp.material("Wood", "#7a5531")
    dark = lp.material("DarkWood", "#5a3d22")
    paint = lp.material("Paint", "#2f5e5a")
    iron = lp.material("Metal", "#8f969e")
    gold = lp.material("GoldMetal", "#d2a845")
    cx, cz, r = 0.05, 0.1, 0.31
    for k in range(6):
        y0 = -r + 2 * r * k / 6
        y1 = -r + 2 * r * (k + 1) / 6
        pts = []
        for y in (y0 + 0.003, y1 - 0.003):
            h = math.sqrt(max(0.0, r * r - y * y))
            pts += [(x, y, cz + dz) for x in (cx - 0.022, cx + 0.018) for dz in (-h, h)]
        mb.hull(pts, wood if k % 2 else dark)
    height = _round_face(mb, paint, cx + 0.019, cz, r * 0.86, 0.012, 5, 28, cx - 0.2)
    # The tree: a trunk, branches and roots of gold.
    def at(y, z):
        rho = math.hypot(y, z)
        return (cx + 0.019 + height(min(rho, r * 0.86)) + 0.004, y, cz + z)
    trunk = [(0, -0.15), (0.01, -0.05), (0, 0.04), (0, 0.13)]
    mb.tube([(at(*p), 0.016) for p in trunk], gold, sides=6, smooth=True)
    for branch in ([(0, 0.0), (-0.08, 0.07), (-0.15, 0.1)], [(0, 0.0), (0.08, 0.07), (0.15, 0.1)], [(0, 0.05), (-0.05, 0.12), (-0.07, 0.18)],
                   [(0, 0.05), (0.05, 0.12), (0.07, 0.18)], [(0, -0.12), (-0.09, -0.17), (-0.15, -0.18)], [(0, -0.12), (0.09, -0.17), (0.15, -0.18)]):
        mb.tube([(at(*p), 0.011 - 0.003 * i) for i, p in enumerate(branch)], gold, sides=5, smooth=True)
    _ring(mb, iron, cx, cz, r, (0.018, 0.032))
    for k in range(12):
        t = 2 * math.pi * k / 12
        mb.sphere((cx + 0.026, math.cos(t) * r * 0.93, cz + math.sin(t) * r * 0.93), 0.011, iron, segments=6, rings=4, smooth=True)
    return mb.finish(sharp_angle=40)


WEAPONS = {
    "Shortsword": shortsword,
    "Cutlass": cutlass,
    "Staff": staff,
    "CrystalRod": crystal_rod,
    "Cudgel": cudgel,
    "OrnateMace": ornate_mace,
    "Hatchet": hatchet,
    "GreatAxe": great_axe,
    "Cleaver": cleaver,
    "Buckler": buckler,
    "KnightShield": knight_shield,
    "WardenShield": warden_shield,
}
