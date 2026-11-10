"""
Nature props: trees, bushes, rocks, a fallen log, a stump, reeds and mushrooms. Origin at the base center, meters,
+Z up; the map builder turns and scales them (HollowmereBuilder).

The broad ones are sculpted like clay and then cut into facets: a signed distance shape (sculpt.py) of any form,
roots and branches included, becomes a surface by `sculpt.surface_net`, and Blender's decimation reduces it to a
set number of triangles. That leaves the irregular, broken facets of low-poly stone and foliage, and puts the
triangles where the shape bends. Each facet is then colored by where it faces: sunlit tops of a crown lighter,
its underside darker, moss on the upward faces of stone and roots.
"""

import math
import random

import bpy
import numpy as np
from mathutils import Vector

import lowpoly as lp
import sculpt as sc
from props import mats

NATURE = {"BarkGrayDark": "#5f5850", "DeadWood": "#a89f8e"}


def _decimated(verts, faces, triangles):
    """The surface reduced to about `triangles` triangles: (vertices, [(face, center, normal)])."""
    mesh = bpy.data.meshes.new("Decimating")
    mesh.from_pydata(verts, [], faces)
    obj = bpy.data.objects.new("Decimating", mesh)
    bpy.context.scene.collection.objects.link(obj)
    modifier = obj.modifiers.new("Decimate", "DECIMATE")
    modifier.ratio = min(1.0, triangles / (2.0 * len(faces)))
    modifier.use_collapse_triangulate = True
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    result = evaluated.to_mesh()
    out_verts = [tuple(v.co) for v in result.vertices]
    out_faces = [(tuple(p.vertices), Vector(p.center), Vector(p.normal)) for p in result.polygons]
    evaluated.to_mesh_clear()
    bpy.data.objects.remove(obj)
    bpy.data.meshes.remove(mesh)
    return out_verts, out_faces


def carved(mb, shape, lo, hi, cell, triangles, paint):
    """
    A shape as faceted low-poly surface of about `triangles` triangles: sampled every `cell` meters over the box
    lo..hi (which must hold it), each facet in the material `paint(center, normal)` gives.
    """
    verts, faces = sc.surface_net(shape, lo, hi, cell)
    verts, faces = _decimated(verts, faces, triangles)
    groups = {}
    for face, center, normal in faces:
        groups.setdefault(paint(center, normal), []).append(face)
    mb.add_faces(verts, groups)


def _keep_below(height):
    """Everything under a surface z = height(p) (numpy points -> heights)."""
    return sc.Shape(lambda p: p[:, 2] - height(p))


def _above(shape, z):
    """The part of a shape above height z: what is buried is never drawn."""
    return sc.intersect(shape, sc.Shape(lambda p: z - p[:, 2]))


def _field(seed, wavelength):
    """A smooth noise field over space, about -1..1, for a single point."""
    waves = sc.waves(seed, wavelength)
    return lambda c: float(waves(np.array([tuple(c)]))[0])


def _along(path, z):
    """The point of a rising path (list of Vectors) at height z."""
    for a, b in zip(path, path[1:]):
        if a.z <= z <= b.z:
            return a.lerp(b, (z - a.z) / max(b.z - a.z, 1e-6))
    return path[-1] if z > path[-1].z else path[0]


def _roots(rng, count, reach, height=0.7, radius=0.18, base=0.18):
    """Roots flaring out of the foot of a trunk and down into the ground, as round cones to blend onto it."""
    shapes = []
    for i in range(count):
        a = 2 * math.pi * i / count + rng.uniform(-0.35, 0.35)
        out = reach * rng.uniform(0.85, 1.15)
        c, s = math.cos(a), math.sin(a)
        shapes.append(sc.chain([(c * base, s * base, height), (c * out * 0.55, s * out * 0.55, 0.1), (c * out, s * out, -0.22)],
                               [radius, radius * 0.65, radius * 0.3]))
    return shapes


def _leaves(m, middle, depth):
    """Crown facets: lighter where they face the sky and sit high, darker underneath and low in it."""
    def paint(c, n):
        light = n.z + 0.4 * (c.z - middle) / depth
        if light > 0.7:
            return m["LeavesLight"]
        if light < -0.3:
            return m["LeavesDark"]
        return m["Leaves"]
    return paint


# ---------------------------------------------------------------- trees

def tree_oak(seed):
    """
    A broad oak: a trunk flaring into roots that grip the ground, forking into heavy limbs that reach up into
    a crown of swelling, billowing lobes, sunlit on top and shaded underneath.
    """
    def build():
        rng = random.Random(seed)
        m = mats()
        mb = lp.MeshBuilder("TreeOak%d" % seed)
        h = rng.uniform(3.0, 3.5)
        lean = (rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3))
        trunk = [Vector((lean[0] * t * t, lean[1] * t * t, -0.35 + (h + 0.35) * t)) for t in (0.0, 0.35, 0.7, 1.0)]
        parts = [sc.chain(trunk, [0.44, 0.34, 0.3, 0.25])] + _roots(rng, 5, 1.05)
        top = trunk[-1]
        tips = []
        for i in range(5):
            a = 2 * math.pi * i / 5 + rng.uniform(-0.3, 0.3)
            start = _along(trunk, h * rng.uniform(0.65, 0.9))
            reach = rng.uniform(1.7, 2.2)
            tip = top + Vector((math.cos(a) * reach, math.sin(a) * reach, rng.uniform(0.5, 0.9)))
            parts.append(sc.chain([start, (start + tip) / 2 - Vector((0, 0, 0.15)), tip], [0.19, 0.14, 0.1]))
            tips.append(tip)
        wood = _above(sc.lumpy(sc.union(*parts, k=0.2), 0.022, 0.4, seed), -0.3)
        moss = _field(seed + 3, 0.8)
        carved(mb, wood, (-3.2, -3.2, -0.5), (3.2, 3.2, h + 1.6), 0.055, 300,
               lambda c, n: m["Moss"] if n.z > 0.5 and c.z < 0.5 and moss(c) > -0.2 else m["Bark"])

        # The crown: a lobe over each limb's tip, a ring of lobes higher up between them and one on top, blended
        # only a little so that each still swells out on its own.
        lobes = [sc.ellipsoid((top.x, top.y, h + 1.4), (1.8, 1.8, 1.2))]
        lobes += [sc.ellipsoid(tip + Vector((0, 0, 0.3)), (r, r, r * 0.85)) for tip, r in ((t, rng.uniform(1.05, 1.3)) for t in tips)]
        for i in range(3):
            a = 2 * math.pi * i / 3 + rng.uniform(0, 1.0)
            d = rng.uniform(0.9, 1.2)
            lobes.append(sc.sphere((top.x + math.cos(a) * d, top.y + math.sin(a) * d, h + rng.uniform(2.1, 2.4)), rng.uniform(0.95, 1.15)))
        lobes.append(sc.sphere((top.x + rng.uniform(-0.3, 0.3), top.y + rng.uniform(-0.3, 0.3), h + 2.85), 0.85))
        crown = sc.lumpy(sc.union(*lobes, k=0.3), 0.12, 0.9, seed + 1)
        carved(mb, crown, (top.x - 4.4, top.y - 4.4, h - 1.2), (top.x + 4.4, top.y + 4.4, h + 4.2), 0.1, 720,
               _leaves(m, h + 1.6, 1.6))
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
    """
    A dead tree: a gnarled grey trunk with roots gripping the ground, crooked limbs ending in bare twigs and a
    top broken off in splinters, pale where the wood shows.
    """
    rng = random.Random(7)
    m = mats()
    for name, color in NATURE.items():
        m[name] = lp.material(name, color)
    mb = lp.MeshBuilder("TreeDead")
    trunk = [Vector(p) for p in ((0, 0, -0.35), (0.05, 0.02, 1.2), (0.15, -0.05, 3.0), (0.1, 0.05, 4.4), (0.22, 0.1, 5.6))]
    parts = [sc.chain(trunk, [0.44, 0.3, 0.23, 0.16, 0.11])] + _roots(rng, 4, 1.0, height=0.6, radius=0.17, base=0.15)
    twigs = []
    for i in range(5):
        a = 2 * math.pi * i / 5 + rng.uniform(-0.4, 0.4)
        z = 2.0 + 2.5 * i / 4 + rng.uniform(-0.2, 0.2)
        reach = rng.uniform(1.2, 1.9) * (1.0 - 0.25 * i / 4)
        start = _along(trunk, z)
        bend = start + Vector((math.cos(a) * reach * 0.5, math.sin(a) * reach * 0.5, rng.uniform(0.25, 0.6)))
        b = a + rng.uniform(-0.6, 0.6)
        tip = start + Vector((math.cos(b) * reach, math.sin(b) * reach, rng.uniform(0.7, 1.4)))
        parts.append(sc.chain([start, bend, tip], [0.13, 0.085, 0.055]))
        twigs.append((bend, tip, a))
    snapped = sc.waves(8, 0.3)
    wood = sc.intersect(sc.lumpy(sc.union(*parts, k=0.15), 0.028, 0.5, 7), _keep_below(lambda p: 5.15 + 0.3 * snapped(p)))
    wood = _above(wood, -0.3)

    def paint(c, n):
        if c.z > 4.9 and n.z > 0.35:
            return m["DeadWood"]
        if c.z < 0.25 or n.z < -0.6:
            return m["BarkGrayDark"]
        return m["BarkGray"]
    carved(mb, wood, (-2.6, -2.6, -0.5), (2.6, 2.6, 6.6), 0.05, 440, paint)
    # Bare twigs, too thin for the grid: forking off the limbs' bends and tips.
    for bend, tip, a in twigs:
        out = Vector((math.cos(a + 0.7), math.sin(a + 0.7), 0.0))
        mb.tube([(bend, 0.045), (bend + out * 0.45 + Vector((0, 0, 0.75)), 0.012)], m["BarkGray"], sides=4)
        up = (tip - bend).normalized()
        mb.tube([(tip - up * 0.05, 0.03), (tip + up * 0.35 + Vector((0, 0, 0.25)), 0.008)], m["BarkGray"], sides=4)
    return mb.finish()


def bush(seed):
    """A round bush of swelling leafy lobes, lighter on top and dark underneath, dotted with blossoms or berries."""
    def build():
        rng = random.Random(seed)
        m = mats()
        mb = lp.MeshBuilder("Bush%d" % seed)
        lobes = [sc.ellipsoid((0, 0, 0.4), (0.85, 0.85, 0.6))]
        for i in range(4):
            a = 2 * math.pi * i / 4 + rng.uniform(0, 0.8)
            d = rng.uniform(0.4, 0.55)
            lobes.append(sc.sphere((math.cos(a) * d, math.sin(a) * d, 0.5 + rng.uniform(0, 0.15)), rng.uniform(0.48, 0.6)))
        lobes.append(sc.sphere((rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), 0.88), 0.45))
        shape = _above(sc.lumpy(sc.union(*lobes, k=0.22), 0.06, 0.5, seed), -0.08)
        carved(mb, shape, (-1.5, -1.5, -0.2), (1.5, 1.5, 1.6), 0.04, 260, _leaves(m, 0.55, 0.5))
        dots = m["MushroomCap"] if seed % 2 else m["Blossom"]
        star = sc.Star(shape, (0, 0, 0.5), limit=2.5)
        dirs = [sc.direction(rng.uniform(0.25, 1.45), rng.uniform(0, 2 * math.pi)) for _ in range(9)]
        for p in star.points(dirs, -0.015):
            mb.sphere(tuple(p), 0.06, dots, segments=4, rings=2)
        return mb.finish()
    return build


# ---------------------------------------------------------------- stone

def _stone(rng, seed, center, size):
    """
    A weathered stone: two rounded blocks merged, swelling a little, split along a few cleavage planes (broad
    faces) and worn down on top; a faint ripple over all of it breaks the flat faces into facets too.
    """
    c = Vector(center)
    lean = (rng.uniform(-12, 12), rng.uniform(-12, 12), rng.uniform(0, 180))
    second = c + Vector((rng.uniform(-0.4, 0.4), rng.uniform(-0.3, 0.3), size * 0.15)) * size
    shape = sc.union(sc.box(c, (size * 0.95, size * 0.72, size * 0.55), radius=size * 0.32, rotation=lean),
                     sc.box(second, (size * 0.6, size * 0.55, size * 0.45), radius=size * 0.28,
                            rotation=(rng.uniform(-15, 15), rng.uniform(-15, 15), rng.uniform(0, 180))), k=size * 0.25)
    shape = sc.lumpy(shape, size * 0.07, size * 0.9, seed)
    for _ in range(3):
        a = rng.uniform(0, 2 * math.pi)
        e = rng.uniform(0.6, 1.3)
        n = np.array((math.cos(a) * math.sin(e), math.sin(a) * math.sin(e), math.cos(e)))
        at = np.array(tuple(c)) + n * size * rng.uniform(0.62, 0.8)
        shape = sc.intersect(shape, sc.Shape(lambda p, n=n, at=at: (p - at) @ n))
    flat = c.z + size * rng.uniform(0.5, 0.6)
    tilt = (rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2))
    shape = sc.intersect(shape, _keep_below(lambda p: flat + tilt[0] * p[:, 0] + tilt[1] * p[:, 1]))
    return sc.lumpy(shape, size * 0.02, size * 0.45, seed + 7)


def _stone_paint(m, seed, size, ground=0.0):
    """Moss in patches on the faces looking up from its upper half, the foot and the undersides darker."""
    moss = _field(seed, size * 1.3)

    def paint(c, n):
        if n.z > 0.75 and c.z > ground + size * 0.45 and moss(c) > -0.1:
            return m["Moss"]
        if n.z < -0.25 or c.z < ground + size * 0.08:
            return m["StoneDark"]
        return m["Stone"]
    return paint


def _pebbles(mb, m, rng, size, count):
    """A few stones broken off, lying at the foot."""
    for _ in range(count):
        a = rng.uniform(0, 2 * math.pi)
        d = size * rng.uniform(1.0, 1.35)
        r = size * rng.uniform(0.1, 0.17)
        x, y = math.cos(a) * d, math.sin(a) * d
        pebble = _above(sc.lumpy(sc.ellipsoid((x, y, r * 0.2), (r, r * 0.8, r * 0.6)), r * 0.12, r * 1.5, rng.randrange(1000)), -r * 0.2)
        carved(mb, pebble, (x - r * 1.5, y - r * 1.5, -r * 0.5), (x + r * 1.5, y + r * 1.5, r * 1.2), r * 0.18, 18,
               lambda c, n: m["StoneDark"] if rng.random() < 0.5 else m["Stone"])


def rock(seed, size, triangles, leaning=False):
    """
    A weathered rock: a block split along cleavage planes, moss on its upward faces, a few pebbles at its foot;
    `leaning` puts a second stone against it.
    """
    def build():
        rng = random.Random(seed)
        m = mats()
        mb = lp.MeshBuilder("Rock%d" % seed)
        shape = _stone(rng, seed, (0, 0, size * 0.25), size)
        shape = _above(shape, -size * 0.2)
        span = size * 1.6
        carved(mb, shape, (-span, -span, -size * 0.35), (span, span, size * 1.2), size * 0.045, triangles,
               _stone_paint(m, seed, size))
        if leaning:
            small = size * 0.5
            at = (size * 0.85, size * 0.4, small * 0.2)
            other = _above(_stone(rng, seed + 1, at, small), -small * 0.2)
            carved(mb, other, (at[0] - small * 1.6, at[1] - small * 1.6, -small * 0.4), (at[0] + small * 1.6, at[1] + small * 1.6, small * 1.4),
                   small * 0.05, triangles // 3, _stone_paint(m, seed + 1, small))
        _pebbles(mb, m, rng, size, 3)
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


# ---------------------------------------------------------------- dead wood

def _end_rings(mb, m, center, normal, radius, sides=10):
    """Growth rings on a sawn face: alternating light and dark discs, each a hair above the last."""
    n = Vector(normal).normalized()
    u = n.cross(Vector((0, 0, 1)) if abs(n.z) < 0.9 else Vector((1, 0, 0))).normalized()
    v = n.cross(u)
    for i, (f, mat) in enumerate(((0.72, m["Wood"]), (0.5, m["Plank"]), (0.26, m["Wood"]))):
        middle = Vector(center) + n * (0.003 * (i + 1))
        ring = [middle + (u * math.cos(2 * math.pi * k / sides) + v * math.sin(2 * math.pi * k / sides)) * radius * f for k in range(sides)]
        mb.add(ring, [tuple(range(sides))], mat)


def log():
    """A fallen log: knobbly bark, sawn flat at one end with growth rings, splintered at the other, a broken
    branch stub, and moss in patches along its top."""
    m = mats()
    mb = lp.MeshBuilder("Log")
    path = [(-2.0, 0, 0.33), (-0.7, 0.04, 0.32), (0.7, -0.03, 0.31), (2.0, 0.02, 0.3)]
    body = sc.union(sc.chain(path, [0.34, 0.32, 0.31, 0.29]),
                    sc.chain([(0.55, 0.05, 0.45), (0.8, 0.28, 0.85), (0.9, 0.38, 1.05)], [0.12, 0.08, 0.065]),
                    sc.sphere((-0.9, -0.3, 0.4), 0.09), sc.sphere((0.2, 0.28, 0.5), 0.08), k=0.1)
    splinters = sc.waves(9, 0.22)
    shape = sc.lumpy(body, 0.012, 0.45, 9)
    shape = sc.intersect(shape, sc.Shape(lambda p: -1.95 - p[:, 0]))
    shape = sc.intersect(shape, sc.Shape(lambda p: p[:, 0] - 1.85 - 0.16 * splinters(p)))
    moss = _field(10, 0.9)

    def paint(c, n):
        if c.x < -1.9 and n.x < -0.8:
            return m["Plank"]
        if c.x > 1.6 and n.x > 0.35:
            return m["Plank"]
        if n.z > 0.45 and -1.75 < c.x < 1.5 and moss(c) > 0.0:
            return m["Moss"]
        return m["Bark"]
    carved(mb, shape, (-2.2, -0.6, -0.15), (2.3, 0.7, 1.25), 0.035, 440, paint)
    _end_rings(mb, m, (-1.95, 0.0, 0.33), (-1, 0, 0), 0.3)
    return mb.finish()


def stump():
    """A sawn stump: roots gripping the ground, the cut a little aslant with growth rings, splinters left
    standing where it broke off at the back, moss on the roots and two mushrooms at its foot."""
    m = mats()
    mb = lp.MeshBuilder("Stump")
    rng = random.Random(5)
    trunk = sc.capped((0, 0, -0.35), (0, 0, 0.9), 0.52, 0.45, rounding=0.03)
    body = sc.lumpy(sc.union(trunk, *_roots(rng, 5, 0.85, height=0.4, radius=0.2, base=0.3), k=0.15), 0.02, 0.3, 5)

    def cut(x):
        return 0.6 + 0.06 * x
    body = sc.intersect(body, _keep_below(lambda p: cut(p[:, 0])))
    splinters = sc.union(sc.cone((-0.12, 0.36, 0.5), (-0.05, 0.4, 0.95), 0.11, 0.015),
                         sc.cone((0.14, 0.35, 0.5), (0.2, 0.37, 0.8), 0.08, 0.012),
                         sc.cone((-0.3, 0.25, 0.5), (-0.33, 0.27, 0.72), 0.06, 0.01))
    shape = _above(sc.union(body, splinters, k=0.03), -0.28)
    moss = _field(6, 0.6)

    def paint(c, n):
        if c.z > cut(c.x) - 0.03 and n.z > 0.8:
            return m["Plank"]
        if c.z > 0.62 and n.y < -0.2:
            return m["Plank"]
        if n.z > 0.5 and c.z < 0.4 and moss(c) > -0.3:
            return m["Moss"]
        return m["Bark"]
    carved(mb, shape, (-1.3, -1.3, -0.4), (1.3, 1.3, 1.1), 0.03, 340, paint)
    _end_rings(mb, m, (0.0, -0.02, cut(0.0)), (-0.06, 0, 1), 0.36)
    for x, y, h in ((0.55, -0.45, 0.18), (0.68, -0.25, 0.12)):
        mb.frustum((x, y, 0), (x, y, h), 0.025, 0.02, m["MushroomStem"], sides=5)
        mb.frustum((x, y, h - 0.02), (x, y, h + 0.06), 0.08, 0.01, m["MushroomCap"], sides=6)
    return mb.finish()


PROPS = {
    "TreeOak1": tree_oak(1), "TreeOak2": tree_oak(2), "TreePine1": tree_pine(1), "TreePine2": tree_pine(2),
    "TreeDead": tree_dead, "Bush1": bush(1), "Bush2": bush(2),
    "Rock1": rock(1, 1.0, 220), "Rock2": rock(2, 1.4, 260, leaning=True), "Boulder": rock(3, 3.2, 460),
    "Reeds": reeds, "Mushrooms": mushrooms, "Log": log, "Stump": stump,
}
