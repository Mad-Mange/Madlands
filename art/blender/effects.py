"""
Spell and interface effects: crystals, flecks, stars, flames, leaves, rings and waves that the client
(Effects.cs, SpellEffects.cs) shows in a spell school's color, named `Fx...` (FxShard, FxRing). Faceted
and without textures like the rest of the art. Each face of a solid takes one of three materials by the way it faces a light from the
front, left and above: `EffectLight`, `Effect` and `EffectDark`. The client gives the three a light, the
full and a dark tone of the color, so a crystal reads as cut stone even with the unlit glow. Flat
shapes (rings, waves, bursts) are all `Effect`.

Solids are centered on the origin and about one meter long (the client scales them); long ones
(shards, flames) point up +Z, which becomes Unity's +Y. Rings and waves lie on the ground (z = 0)
with a radius of one meter.
"""

import math
import random

from mathutils import Vector

import lowpoly as lp

LIGHT = Vector((-0.35, -0.45, 0.82)).normalized()


def _mats():
    return {
        "Light": lp.material("EffectLight", "#e6dcff", emission=1.0),
        "Mid": lp.material("Effect", "#a77cf0", emission=1.0),
        "Dark": lp.material("EffectDark", "#5a3a9a", emission=1.0),
    }


def _toned(obj, light=LIGHT, bright=0.42):
    """Gives each face its tone by how it faces the light."""
    m = _mats()
    mesh = obj.data
    for mat in m.values():
        if mesh.materials.find(mat.name) < 0:
            mesh.materials.append(mat)
    index = {k: mesh.materials.find(mat.name) for k, mat in m.items()}
    for poly in mesh.polygons:
        d = poly.normal.dot(light)
        poly.material_index = index["Light"] if d > bright else index["Dark"] if d < -0.12 else index["Mid"]
    return obj


def _jitter(rng, p, amount):
    return (p[0] + rng.uniform(-amount, amount), p[1] + rng.uniform(-amount, amount), p[2] + rng.uniform(-amount, amount))


def shard():
    """A cut crystal: a six-sided prism, a long point on top and a short one below, a little crooked."""
    rng = random.Random(5)
    mb = lp.MeshBuilder("FxShard")
    pts = []
    for z, r, twist in ((-0.3, 0.14, 0.0), (-0.1, 0.17, 0.12), (0.22, 0.155, 0.22)):
        for i in range(6):
            a = 2 * math.pi * i / 6 + twist
            pts.append(_jitter(rng, (math.cos(a) * r, math.sin(a) * r * 0.86, z), 0.012))
    pts += [(0.02, 0.01, 0.5), (-0.01, 0.0, -0.5)]
    mb.hull(pts, _mats()["Mid"])
    return _toned(mb.finish())


def spikes():
    """Crystals breaking out of the ground (Shard Nova): one tall, two leaning out, origin at their base."""
    rng = random.Random(9)
    mb = lp.MeshBuilder("FxSpikes")
    for x, y, h, r, lean in ((0.0, 0.0, 1.0, 0.16, (0.04, 0.02)), (0.2, 0.1, 0.62, 0.11, (0.3, 0.12)),
                             (-0.16, -0.12, 0.5, 0.1, (-0.26, -0.18)), (0.05, -0.2, 0.36, 0.08, (0.06, -0.3))):
        pts = []
        for z, s in ((-0.08, 1.0), (h * 0.72, 0.85)):
            for i in range(6):
                a = 2 * math.pi * i / 6 + rng.uniform(-0.15, 0.15)
                pts.append((x + lean[0] * z + math.cos(a) * r * s, y + lean[1] * z + math.sin(a) * r * s, z))
        pts.append((x + lean[0] * h, y + lean[1] * h, h))
        mb.hull(pts, _mats()["Mid"])
    return _toned(mb.finish())


def fleck():
    """A small crooked octahedron: sparks, ice, embers and shards of every burst and trail."""
    rng = random.Random(3)
    mb = lp.MeshBuilder("FxFleck")
    pts = [(0.5, 0, 0), (-0.45, 0, 0), (0, 0.4, 0), (0, -0.42, 0), (0, 0, 0.55), (0, 0, -0.5)]
    mb.hull([_jitter(rng, p, 0.06) for p in pts], _mats()["Mid"])
    return _toned(mb.finish())


def star():
    """A four-pointed star, thick in the middle (holy sparks): long points up, down and to the sides."""
    mb = lp.MeshBuilder("FxStar")
    outline = []
    for i in range(8):
        a = math.pi / 2 + i * math.pi / 4
        r = 0.5 if i % 2 == 0 else 0.13
        outline.append((math.cos(a) * r, 0.0, math.sin(a) * r))
    front, back = (0.0, -0.16, 0.0), (0.0, 0.16, 0.0)
    verts = outline + [front, back]
    f, b = len(outline), len(outline) + 1
    faces = []
    for i in range(len(outline)):
        j = (i + 1) % len(outline)
        faces += [(f, j, i), (b, i, j)]
    mb.add(verts, faces, _mats()["Mid"], closed=True)
    return _toned(mb.finish())


def flame():
    """A fireball: a lumpy core and tongues of flame twisting back (down -Z) into a tail."""
    rng = random.Random(13)
    mb = lp.MeshBuilder("FxFlame")
    m = _mats()
    pts = []
    for i in range(18):
        z = 1 - 2 * (i + 0.5) / 18
        r = math.sqrt(1 - z * z)
        a = i * 2.39996
        k = rng.uniform(0.26, 0.31)
        pts.append((math.cos(a) * r * k, math.sin(a) * r * k, z * k + 0.18))
    mb.hull(pts, m["Mid"])
    # Each tongue starts inside the core, bulges out of its back half and swirls in toward the tail.
    for i in range(7):
        a = 2 * math.pi * i / 7 + rng.uniform(-0.15, 0.15)
        length = rng.uniform(0.62, 0.82)

        def at(t, r):
            turn = a + 1.1 * t
            return Vector((math.cos(turn) * r, math.sin(turn) * r, 0.12 - length * t))

        mb.tube([(at(0.0, 0.05), 0.15), (at(0.22, 0.16), 0.1), (at(0.5, 0.15), 0.07), (at(0.78, 0.08), 0.035), (at(1.0, 0.02), 0.004)],
                m["Mid"], sides=5)
    return _toned(mb.finish())


def leaf():
    """A leaf folded along its midrib, its tip up +Z (nature's healing)."""
    mb = lp.MeshBuilder("FxLeaf")
    rows = []
    for z, w in ((-0.5, 0.0), (-0.42, 0.1), (-0.2, 0.24), (0.05, 0.27), (0.28, 0.2), (0.45, 0.07), (0.5, 0.0)):
        fold = 0.09 * w / 0.27
        rows.append([(-w, fold, z), (0.0, 0.0, z), (w, fold, z)])
    verts, faces = [], []
    for k, row in enumerate(rows):
        verts += row
    for k in range(len(rows) - 1):
        a, b = k * 3, (k + 1) * 3
        faces += [(a, a + 1, b + 1, b), (a + 1, a + 2, b + 2, b + 1)]
    mb.add(verts, faces, _mats()["Mid"], double_sided=True)
    mb.frustum((0, 0.01, -0.5), (0, 0.0, -0.66), 0.018, 0.012, _mats()["Mid"], sides=4)
    # The face full, the underside dark: two tones, not stripes along the fold.
    return _toned(mb.finish(), light=Vector((0, -1, 0)), bright=2.0)


def orb():
    """A cut sphere (lights, shadow bolts)."""
    mb = lp.MeshBuilder("FxOrb")
    pts = []
    n = 26
    for i in range(n):
        z = 1 - 2 * (i + 0.5) / n
        r = math.sqrt(1 - z * z)
        a = i * 2.39996
        pts.append((math.cos(a) * r * 0.5, math.sin(a) * r * 0.5, z * 0.5))
    mb.hull(pts, _mats()["Mid"])
    return _toned(mb.finish())


def gem():
    """A cut gem: a flat table on top, a ring of crown facets, the girdle and a deep point below (loot)."""
    mb = lp.MeshBuilder("FxGem")
    pts = []
    for i in range(8):
        a = 2 * math.pi * i / 8
        pts.append((math.cos(a) * 0.28, math.sin(a) * 0.28, 0.2))
        b = a + math.pi / 8
        pts.append((math.cos(b) * 0.5, math.sin(b) * 0.5, 0.04))
        pts.append((math.cos(a) * 0.48, math.sin(a) * 0.48, -0.02))
    pts.append((0.0, 0.0, -0.55))
    mb.hull(pts, _mats()["Mid"])
    return _toned(mb.finish())


def _band(mb, inner, outer, segments, start=0.0, end=2 * math.pi, z=0.0):
    """A flat band between two radii from angle `start` to `end`, facing up."""
    n = max(2, segments)
    verts, faces = [], []
    for i in range(n + 1):
        a = start + (end - start) * i / n
        verts += [(math.cos(a) * inner, math.sin(a) * inner, z), (math.cos(a) * outer, math.sin(a) * outer, z)]
    for i in range(n):
        k = i * 2
        faces.append((k, k + 1, k + 3, k + 2))
    mb.add(verts, faces, _mats()["Mid"])


def ring():
    """A thin flat ring on the ground (halos, healing circles)."""
    mb = lp.MeshBuilder("FxRing")
    _band(mb, 0.86, 1.0, 64)
    return mb.finish()


def target_ring():
    """
    The circle under the target: four arcs with gaps between them, a small arrowhead in each gap
    pointing in at the target.
    """
    mb = lp.MeshBuilder("FxTargetRing")
    gap = math.radians(14)
    for q in range(4):
        start = q * math.pi / 2 + math.pi / 4 + gap / 2
        _band(mb, 0.9, 1.0, 14, start, start + math.pi / 2 - gap)
        a = q * math.pi / 2 + math.pi / 4
        tip = (math.cos(a) * 0.86, math.sin(a) * 0.86, 0.0)
        side = Vector((-math.sin(a), math.cos(a), 0)) * 0.07
        base = Vector((math.cos(a), math.sin(a), 0)) * 1.03
        mb.add([tip, tuple(base - side), tuple(base + side)], [(0, 1, 2)], _mats()["Mid"])
    return mb.finish()


def wave():
    """A shock wave: a low crest running around a ring of one meter, steeper inside (novas)."""
    rng = random.Random(21)
    mb = lp.MeshBuilder("FxWave")
    n = 48
    profile = ((0.78, 0.0), (0.9, 0.16), (0.97, 0.12), (1.0, 0.0))
    rows = []
    for r, z in profile:
        row = []
        for i in range(n):
            a = 2 * math.pi * i / n
            k = 1.0 + (rng.uniform(-0.025, 0.025) if 0 < z else 0.0)
            row.append((math.cos(a) * r * k, math.sin(a) * r * k, z * (1.0 + rng.uniform(-0.3, 0.3))))
        rows.append(row)
    mb.grid(rows, _mats()["Mid"], closed=True, inside=lambda p: (0.0, 0.0, -1.0))
    return mb.finish()


def burst():
    """A flash of light: thin rays of two lengths around a small middle, flat, facing -Y (the camera)."""
    mb = lp.MeshBuilder("FxBurst")
    rays = 10
    verts = [(0.0, 0.0, 0.0)]
    for i in range(rays * 2):
        a = math.pi * i / rays
        r = (0.5 if i % 4 == 0 else 0.34) if i % 2 == 0 else 0.07
        verts.append((math.cos(a) * r, 0.0, math.sin(a) * r))
    faces = [(0, 1 + (i + 1) % (rays * 2), 1 + i) for i in range(rays * 2)]
    mb.add(verts, faces, _mats()["Mid"])
    return mb.finish()


EFFECTS = {
    "FxShard": shard, "FxSpikes": spikes, "FxFleck": fleck, "FxStar": star, "FxFlame": flame, "FxLeaf": leaf, "FxOrb": orb,
    "FxGem": gem, "FxRing": ring, "FxTargetRing": target_ring, "FxWave": wave, "FxBurst": burst,
}
