"""
Worn gear for the humanoid races: helmets, hoods and hats, shoulder pads and mantles, cloaks, robes,
breastplates and aprons.

Every piece is a mesh of its own in the race's model, named "Gear" + the piece ("GearHood"). The client
hides them all and shows the ones a unit wears (GearVisuals.cs), tinted with the item's color. Each is
built from the race's proportions (around the head's hull, the trunk's sections, the joints) and skinned to
the race's skeleton, so it fits every race and moves with the body: hats with the head, pads with the
shoulders, a robe's skirt with the hips and thighs, a cloak's hem with the Cloak bone (the client swings
it back while the unit runs).

Materials: "Gear" takes the item's color, "GearTrim" a darker and "GearDark" a much darker tone of it
(linings, the inside of a hood); "GearMetal" and "GearMetalTrim" the same, metallic. Everything else
keeps its own color: BuckleMetal (rivets, buckles, clasps, gilded rims), Embroidery (gold thread on cloth),
Bone, LampGlow, CrystalGlow, Fur, Seaweed.

Pieces are shaded smoothly with hard edges where faces meet at a sharp angle (rims, plate edges), and are
built at every level of detail (lp.res for their rows and sides; rivets drop out of the coarser levels).
"""

import math

import bmesh
from mathutils import Matrix, Vector

import humanoid as hu
import lowpoly as lp
import sculpt as sc

TAU = 2 * math.pi
UP = Vector((0, 0, 1))
# Faces meeting at more than this many degrees get a hard edge.
SHARP = 40.0


class _Builder(lp.MeshBuilder):
    """A MeshBuilder for gear: everything shaded smoothly, hard edges by angle when finished."""

    def add_faces(self, verts, groups, bone=None, matrix=None, closed=False, double_sided=False, smooth=True,
                  inside=None, facing_in=False):
        return super().add_faces(verts, groups, bone, matrix=matrix, closed=closed, double_sided=double_sided, smooth=True,
                                 inside=inside, facing_in=facing_in)

    def finish(self, collection=None, sharp_angle=SHARP):
        return super().finish(collection, sharp_angle)


def _materials():
    return dict(
        Gear=lp.material("Gear", "#8a7458"),
        GearTrim=lp.material("GearTrim", "#564837"),
        GearDark=lp.material("GearDark", "#342c21"),
        GearMetal=lp.material("GearMetal", "#9aa3ae", metallic=0.6, roughness=0.5),
        GearMetalTrim=lp.material("GearMetalTrim", "#5f656c", metallic=0.6, roughness=0.5),
        BuckleMetal=lp.material("BuckleMetal", "#c9a24a", metallic=0.6, roughness=0.5),
        Embroidery=lp.material("Embroidery", "#c9a54e"),
        Bone=lp.material("Bone", "#ddd2b4"),
        LampGlow=lp.material("LampGlow", "#ffd27a", emission=3.0),
        CrystalGlow=lp.material("CrystalGlow", "#b48cff", emission=3.0),
        Fur=lp.material("Fur", "#cdb894"),
        FurDark=lp.material("FurDark", "#9c8664"),
    )


# ---------------------------------------------------------------- shapes and fitting


def smooth(a, b, x):
    """0 at a, 1 at b, smooth in between (a may be greater than b)."""
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def interp(table, x):
    """Piecewise linear through (x, y) pairs sorted by x."""
    if x <= table[0][0]:
        return table[0][1]
    for (x0, y0), (x1, y1) in zip(table, table[1:]):
        if x <= x1:
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0)
    return table[-1][1]


def wrap(t):
    """An angle in -pi..pi."""
    return (t + math.pi) % TAU - math.pi


def around(t):
    """Horizontal direction at angle t: 0 is the front (-Y), pi/2 the model's left (+X)."""
    return Vector((math.sin(t), -math.cos(t), 0))


def ray(theta, t):
    """Direction at polar angle theta from straight up, angle t around (see around)."""
    return Vector((math.sin(theta) * math.sin(t), -math.sin(theta) * math.cos(t), math.cos(theta)))


class Fit:
    """The convex hull of some points (a head, a head with its hair) that gear is wrapped around."""

    def __init__(self, points):
        bm = bmesh.new()
        for p in points:
            bm.verts.new(Vector(p))
        bmesh.ops.convex_hull(bm, input=bm.verts)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        self.planes = [(f.normal.copy(), f.verts[0].co.copy()) for f in bm.faces]
        self.top = max(v.co.z for v in bm.verts if v.link_faces)
        bm.free()

    def reach(self, origin, direction):
        """How far the hull's surface is from `origin` (inside it) along `direction`."""
        origin, direction = Vector(origin), Vector(direction).normalized()
        best = 10.0
        for n, v in self.planes:
            nd = n.dot(direction)
            if nd > 1e-6:
                best = min(best, n.dot(v - origin) / nd)
        return max(best, 0.0)


def head_fit(r, hair=False, nose=False):
    """The head's hull: skull, face, ears and beard; with the hair (hats sit on it) and the nose (face plates)."""
    return Fit(hu.head_points(r, hair, nose))


def dome(fit, center, clearance, rim, rows=5, cols=16, bulge=None):
    """
    Rows over a head from the crown (a pole) down to a rim, `clearance` outside `fit`: rays from
    `center`. `rim(t)` is the rim's height (model space) at angle t; `bulge(direction)` adds clearance
    (a hood's peak, loose cloth). Each row goes around closed, starting at the front.
    """
    def point(theta, t):
        d = ray(theta, t)
        return center + d * (fit.reach(center, d) + clearance + (bulge(d) if bulge else 0.0))

    def rim_theta(t):
        lo, hi = 0.0, math.radians(175)
        for _ in range(24):
            mid = (lo + hi) / 2
            if point(mid, t).z > rim(t):
                lo = mid
            else:
                hi = mid
        return (lo + hi) / 2

    rows, cols = lp.res(rows, 1), lp.res(cols, 8)
    ts = [TAU * i / cols for i in range(cols)]
    ends = [rim_theta(t) for t in ts]
    out = [[point(0.0, 0.0)]]
    for k in range(1, rows + 1):
        out.append([point(e * k / rows, t) for t, e in zip(ts, ends)])
    return out


def ring_at(fit, axis, z, clearance, cols=16, extra=None):
    """A closed horizontal ring around `fit` at height z: rays from the point `axis` (x, y) at that height."""
    origin = Vector((axis[0], axis[1], z))
    cols = lp.res(cols, 8)
    out = []
    for i in range(cols):
        d = around(TAU * i / cols)
        out.append(origin + d * (fit.reach(origin, d) + clearance + (extra(TAU * i / cols) if extra else 0.0)))
    return out


def arc(z, half, front, back, t0, t1, n, y=0.0, square=0.0):
    """
    Points on a horizontal (super)ellipse from angle t0 to t1 (see around): `half` wide to each side,
    reaching `front` (a positive distance) before the center and `back` behind it.
    """
    e = 2.0 / (2.0 + 3.0 * square)
    out = []
    for i in range(n):
        t = t0 + (t1 - t0) * i / (n - 1)
        s, c = math.sin(t), -math.cos(t)
        px = math.copysign(abs(s) ** e, s) * half
        py = math.copysign(abs(c) ** e, c) * (back if c > 0 else front)
        out.append(Vector((px, y + py, z)))
    return out


def outward(points, center, amount):
    """Points pushed away from a vertical axis through `center` (horizontally)."""
    out = []
    for p in points:
        d = Vector((p.x - center.x, p.y - center.y, 0))
        out.append(p + d.normalized() * amount if d.length > 1e-6 else p.copy())
    return out


def loop_tube(mb, points, radius, mat, bone, sides=5, closed=True, squash=(1.0, 1.0)):
    """A tube along a path (a closed loop by default): rolled hems, rims and hat bands."""
    pts = [Vector(p) for p in points]
    n = len(pts)
    sides = lp.res(sides, 3)
    rings = []
    for i in range(n):
        a = pts[(i - 1) % n] if closed or i > 0 else pts[i]
        b = pts[(i + 1) % n] if closed or i < n - 1 else pts[i]
        axis = (b - a).normalized()
        ref = UP if abs(axis.z) < 0.9 else Vector((0, -1, 0))
        u = axis.cross(ref).normalized()
        v = axis.cross(u).normalized()
        rings.append([pts[i] + u * (math.cos(t) * radius * squash[0]) + v * (math.sin(t) * radius * squash[1])
                      for t in (TAU * (j + 0.5) / sides for j in range(sides))])
    if closed:
        rings.append(rings[0])
    mb.loft(rings, mat, bone, cap_a=not closed, cap_b=not closed)


def stud(mb, p, normal, size, mat, bone):
    """A round rivet head on a surface."""
    mb.sphere(Vector(p) + Vector(normal).normalized() * size * 0.2, size, mat, bone, segments=5, rings=3)


def spike(mb, base, tip, radius, mat, bone, sides=5, bend=None):
    """A tapering horn or spike from `base` to `tip`, curved toward `bend` (an offset at its middle)."""
    base, tip = Vector(base), Vector(tip)
    mid = (base + tip) / 2 + (Vector(bend) if bend else Vector())
    path = [base, (base + mid) / 2, mid, (mid + tip) / 2, tip]
    radii = [radius, radius * 0.8, radius * 0.58, radius * 0.34, radius * 0.06]
    mb.tube(list(zip(path, radii)), mat, bone, sides=lp.res(sides, 4))


def by_height(low, high, z0, z1):
    """Bone weights: `high` above z1, `low` below z0, blended in between."""
    def weigh(p):
        s = smooth(z0, z1, p.z)
        return {high: s, low: 1.0 - s}
    return weigh


def tilted(points, pivot, degrees):
    """Points turned about the X axis through `pivot` (negative degrees raise the front)."""
    m = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(degrees), 4, "X") @ Matrix.Translation(-Vector(pivot))
    return [[m @ Vector(p) for p in row] for row in points]


# ---------------------------------------------------------------- head


def _head_frame(r):
    at = hu.head_at(r)
    return lambda x, y, z: Vector(at(x, y, z))


def _rim(r, table):
    """A rim height function from (degrees off the front, head-relative z) pairs."""
    h, head = r["head_shape"]["h"], r["head"]
    return lambda t: head + interp(table, abs(math.degrees(wrap(t)))) * h


def helm(r, m):
    """A Korrin-forged nasal helm: a riveted dome over a rim band, cheek guards, a nose guard and a crest."""
    at = _head_frame(r)
    s = r["head_shape"]
    mb = _Builder("GearHelm")
    fit = head_fit(r)
    center = at(0, 0.01, 0.12)
    rows = dome(fit, center, 0.018, _rim(r, [(0, 0.2), (45, 0.19), (90, 0.165), (180, 0.12)]), rows=5, cols=18)
    mb.grid(rows, m["GearMetal"], "Head", inside=lambda p: center)
    rim = outward(rows[-1], center, 0.006)
    loop_tube(mb, [p + Vector((0, 0, 0.012)) for p in rim], 0.016, m["GearMetalTrim"], "Head", sides=6, squash=(0.55, 1.0))
    for i, p in enumerate(rim):
        if i % 3 == 1:
            stud(mb, p + Vector((0, 0, 0.012)) + (p - center).normalized() * 0.006, p - center, 0.008, m["BuckleMetal"], "Head")
    # The crest, over the top from the brow to the nape.
    front, back, last = 0, len(rows[1]) // 2, len(rows) - 1
    path = [rows[k][front] for k in range(last, 0, -1)] + [rows[0][0]] + [rows[k][back] for k in range(1, last)]
    mb.tube([(p + (p - center).normalized() * 0.008, (0.007, 0.016)) for p in path], m["GearMetalTrim"], "Head", sides=4)
    # The nose guard follows the nose's ridge from the rim down.
    root = Vector((0, -0.12, 0.15))
    top = root + (Vector((0, -0.118, 0.155)) - root) * s["nose"]
    tip = root + (Vector((0, -0.158, 0.095)) - root) * s["nose"]
    low = top + (tip - top) * 0.45
    upper, lower = at(0, top.y - 0.016, 0.2), at(0, low.y - 0.014, low.z)
    mb.hull([upper + Vector((x, y, 0)) for x in (-0.017, 0.017) for y in (0, 0.012)]
            + [lower + Vector((x, y, 0)) for x in (-0.012, 0.012) for y in (0, 0.01)], m["GearMetal"], "Head")
    # Cheek guards over the ears, curving in under the cheekbones.
    for side in (1, -1):
        ts = [side * math.radians(a) for a in (62, 82, 102, 118)]
        guard = []
        for z, extra in ((0.18, 0.024), (0.12, 0.026), (0.07, 0.02), (0.035, 0.012)):
            origin = at(0, 0.0, z)
            guard.append([origin + around(t) * (fit.reach(origin, around(t)) + extra) for t in ts])
        mb.shell(guard, 0.008, m["GearMetal"], m["GearMetalTrim"], lambda p: at(0, 0.0, 0.1), "Head")
    # The neck guard flares over the nape.
    ts = [math.radians(a) for a in (140, 160, 180, 200, 220)]
    guard = []
    for z, extra in ((0.14, 0.02), (0.08, 0.03), (0.03, 0.045)):
        origin = at(0, 0.01, z)
        guard.append([origin + around(t) * (fit.reach(origin, around(t)) + extra) for t in ts])
    mb.shell(guard, 0.008, m["GearMetal"], m["GearMetalTrim"], lambda p: at(0, 0.0, 0.12), "Head")
    return mb.finish()


def horned_helm(r, m):
    """A closed war helm: a dome down over the cheeks and nape, a face plate with an eye slit, horns."""
    at = _head_frame(r)
    mb = _Builder("GearHornedHelm")
    fit = head_fit(r, nose=True)
    center = at(0, 0.01, 0.12)
    rows = dome(fit, center, 0.02, _rim(r, [(0, 0.2), (40, 0.19), (70, 0.04), (180, 0.03)]), rows=6, cols=18)
    mb.grid(rows, m["GearMetal"], "Head", inside=lambda p: center)
    loop_tube(mb, outward(rows[-1], center, 0.004), 0.012, m["GearMetalTrim"], "Head", sides=5, squash=(0.6, 1.0))
    # The face plate: a brow band, the eye slit, then a mask down to the chin with a ridge.
    ts = [math.radians(a) for a in (-62, -40, -20, 0, 20, 40, 62)]
    for zs in ((0.2, 0.175, 0.153), (0.118, 0.08, 0.04, 0.0)):
        plate = []
        for z in zs:
            origin = at(0, 0.0, z)
            plate.append([origin + around(t) * (fit.reach(origin, around(t)) + 0.022) for t in ts])
        mb.shell(plate, 0.01, m["GearMetal"], m["GearDark"], lambda p: at(0, 0.02, 0.1), "Head", rim_mat=m["GearMetalTrim"])
        ridge = [row[3] for row in plate]
        mb.tube([(p + Vector((0, -0.006, 0)), (0.008, 0.008)) for p in ridge], m["GearMetalTrim"], "Head", sides=4)
    # Horns from the temples, out, up and forward.
    for side in (1, -1):
        base = at(side * 0.118, 0.0, 0.215)
        tip = at(side * 0.2, -0.07, 0.43)
        spike(mb, base, tip, 0.032, m["Bone"], "Head", sides=7, bend=(side * 0.07, 0.02, -0.05))
    return mb.finish()


def _hood_rows(r, fit, center, cols=18):
    """A hood's cloth: over the crown with a soft peak at the back, open around the face, down to the nape."""
    peak = Vector((0, 0.45, 0.9)).normalized()

    def bulge(d):
        return 0.1 * max(0.0, d.dot(peak)) ** 4 + 0.035 * max(0.0, -d.z)

    rim = _rim(r, [(0, 0.24), (22, 0.225), (45, 0.14), (65, 0.03), (90, -0.06), (180, -0.11)])
    return dome(fit, center, 0.026, rim, rows=6, cols=cols, bulge=bulge)


def _capelet(r, mb, m, outer, inner):
    """The short cape under a hood, from the neck over the shoulders, open at the throat."""
    z, sx, dl, neck = r["shoulder_z"], r["shoulder_x"], r["deltoid"], r["neck"]
    open_t = math.radians(26)
    rows = []
    for zz, half, grow, square in ((neck + 0.03, r["neck_r"] + 0.07, 0.07, 0.0), (z + 0.08, sx + 0.02, 0.06, 0.25),
                                   (z + 0.0, sx + dl * 1.3, 0.075, 0.35), (z - 0.09, sx + dl * 1.5, 0.09, 0.35)):
        _, front, back = hu.extent(r, min(zz, z + 0.04))
        rows.append(arc(zz, half, -front + grow, back + grow, open_t, TAU - open_t, lp.res(15, 7), square=square))
    # A slightly scalloped hem.
    rows[-1] = [p + Vector((0, 0, -0.012 if i % 2 else 0.0)) for i, p in enumerate(rows[-1])]
    mb.shell(rows, 0.01, outer, inner, lambda p: Vector((0, 0.0, p.z - 0.1)), "Chest", rim_mat=m["GearTrim"])
    loop_tube(mb, rows[-1], 0.01, m["GearTrim"], "Chest", closed=False, sides=5)


def _hood(r, m, name, outer, inner, edge):
    at = _head_frame(r)
    mb = _Builder(name)
    fit = head_fit(r)
    center = at(0, 0.02, 0.12)
    rows = _hood_rows(r, fit, center)
    weigh = by_height("Chest", "Head", r["neck"] + 0.01, r["head"] + 0.03)
    mb.shell(rows, 0.012, outer, inner, lambda p: center, weigh, closed=True, rim_mat=edge)
    # A rolled edge around the face.
    face = [p for p, t in zip(rows[-1], (TAU * i / len(rows[-1]) for i in range(len(rows[-1]))))
            if abs(math.degrees(wrap(t))) <= 80]
    order = sorted(face, key=lambda p: math.atan2(p.x, -p.y))
    loop_tube(mb, order, 0.011, edge, weigh, closed=False, sides=5)
    _capelet(r, mb, m, outer, inner)
    return mb, rows, center


def hood(r, m):
    """A cloth hood with a soft peak and a short cape over the shoulders."""
    mb, _, _ = _hood(r, m, "GearHood", m["Gear"], m["GearDark"], m["GearTrim"])
    return mb.finish()


def bone_hood(r, m):
    """The hood with a crown of bone spikes around the brow."""
    mb, rows, center = _hood(r, m, "GearBoneHood", m["Gear"], m["GearDark"], m["GearTrim"])
    fit = head_fit(r)
    band = dome(fit, center, 0.04, _rim(r, [(0, 0.255), (90, 0.215), (180, 0.2)]), rows=1, cols=18)[-1]
    loop_tube(mb, band, 0.012, m["Bone"], "Head", sides=5, squash=(0.8, 1.2))
    for i, p in enumerate(band):
        t = wrap(TAU * i / len(band))
        if abs(math.degrees(t)) > 100 or i % 2:
            continue
        tall = 0.06 + 0.06 * math.cos(t) ** 2
        d = (p - center).normalized()
        spike(mb, p, p + (d * 0.4 + UP).normalized() * tall, 0.018, m["Bone"], "Head", sides=5)
    # A small skull on the brow.
    brow = band[0] + Vector((0, -0.012, 0.0))
    skull = [(0.028, 0, 0.025), (0, 0, 0.042), (0.03, 0.025, 0.0), (0.022, 0, -0.022), (0.012, -0.004, -0.04), (0, 0.02, 0.03)]
    mb.hull([brow + Vector((x, y - 0.012, z)) for x, y, z in lp.mirrored(skull)], m["Bone"], "Head")
    for side in (1, -1):
        mb.box(brow + Vector((side * 0.012, -0.026, 0.002)), (0.012, 0.006, 0.012), m["GearDark"], "Head")
    return mb.finish()


def _hat_base(r, fit, z, clearance):
    """The ring a hat's crown stands on, at head-relative height z, around the head and its hair."""
    at = _head_frame(r)
    base = at(0, 0.015, z)
    return ring_at(fit, (base.x, base.y), base.z, clearance, cols=14), base


def _brim(mb, m, ring, base, widths, droop, inner_drop=0.0, bone="Head"):
    """A hat's brim from the crown's ring out to `widths(t)`, drooping at the edge; underside darker."""
    rows = [[p + Vector((0, 0, -inner_drop)) for p in ring]]
    mid, edge = [], []
    for i, p in enumerate(ring):
        t = TAU * i / len(ring)
        d = around(t)
        w = widths(t)
        mid.append(base + d * w * 0.62 + Vector((0, 0, -droop * 0.25 - inner_drop)))
        edge.append(base + d * w + Vector((0, 0, -droop)))
    rows += [mid, edge]
    mb.shell(rows, 0.012, m["Gear"], m["GearTrim"], lambda p: p + Vector((0, 0, -1)), bone, closed=True)
    loop_tube(mb, [p + Vector((0, 0, -0.006)) for p in edge], 0.009, m["GearTrim"], bone, sides=4)


def straw_hat(r, m):
    """A wide straw hat with a dented crown and a band, worn over the hair."""
    mb = _Builder("GearStrawHat")
    fit = head_fit(r, hair=True)
    ring, base = _hat_base(r, fit, 0.215, 0.012)
    top = fit.top + 0.045
    crown = [ring]
    for scale, f in ((0.93, 0.5), (0.78, 0.88), (0.6, 1.0)):
        crown.append([base + (p - base) * scale + Vector((0, 0, (top - base.z) * f)) for p in ring])
    crown.append([base + Vector((0, 0, top - base.z - 0.02))])
    mb.grid(crown, m["Gear"], "Head", inside=lambda p: base + Vector((0, 0, 0.04)), closed=True)
    loop_tube(mb, [base + (p - base) * 0.985 + Vector((0, 0, 0.022)) for p in ring], 0.02, m["GearTrim"], "Head",
              sides=4, squash=(0.45, 1.0))
    _brim(mb, m, ring, base, lambda t: 0.29 + 0.012 * math.cos(3 * t), 0.035)
    return mb.finish()


def wizard_hat(r, m):
    """A tall pointed hat with a wide brim, its tip bent back; a buckled band."""
    mb = _Builder("GearWizardHat")
    fit = head_fit(r, hair=True)
    ring, base = _hat_base(r, fit, 0.205, 0.014)
    rows = [ring]
    steps = [(0.06, 0.88, 0.0), (0.14, 0.72, 0.006), (0.22, 0.56, 0.02), (0.29, 0.41, 0.045), (0.35, 0.27, 0.085), (0.39, 0.15, 0.135)]
    for height, scale, back in steps:
        center = base + Vector((0, back, height))
        rows.append([center + (p - base) * scale for p in ring])
    rows.append([base + Vector((0, 0.2, 0.395))])
    mb.grid(rows, m["Gear"], "Head", inside=lambda p: Vector((0, base.y + (p.z - base.z) * 0.3, p.z)), closed=True)
    band = [base + (p - base) * 0.99 + Vector((0, 0, 0.024)) for p in ring]
    loop_tube(mb, band, 0.022, m["GearTrim"], "Head", sides=4, squash=(0.45, 1.0))
    front = band[0] + Vector((0, -0.01, 0))
    mb.box(front, (0.05, 0.012, 0.042), m["BuckleMetal"], "Head")
    mb.box(front + Vector((0, -0.004, 0)), (0.026, 0.012, 0.02), m["GearTrim"], "Head")
    _brim(mb, m, ring, base, lambda t: 0.31 + 0.015 * math.sin(2 * t + 0.5), 0.05)
    return mb.finish()


def miner_helm(r, m):
    """A rounded miner's cap with a short brim and a lamp."""
    at = _head_frame(r)
    mb = _Builder("GearMinerHelm")
    fit = head_fit(r)
    center = at(0, 0.01, 0.12)
    rows = dome(fit, center, 0.022, _rim(r, [(0, 0.2), (90, 0.17), (180, 0.13)]), rows=5, cols=16)
    mb.grid(rows, m["GearMetal"], "Head", inside=lambda p: center)
    rim = rows[-1]
    edge = [p + around(TAU * i / len(rim)) * (0.05 if abs(wrap(TAU * i / len(rim))) < 1.0 else 0.03) + Vector((0, 0, -0.006))
            for i, p in enumerate(rim)]
    mb.shell([rim, edge], 0.01, m["GearMetal"], m["GearMetalTrim"], lambda p: p + Vector((0, 0, -1)), "Head", closed=True)
    loop_tube(mb, rim, 0.012, m["GearMetalTrim"], "Head", sides=5)
    # The lamp: a brass housing with a glowing lens, over the brow.
    origin = at(0, 0.0, 0.25)
    front = origin + around(0) * fit.reach(origin, around(0))
    housing = front + Vector((0, -0.035, 0.0))
    mb.frustum(housing + Vector((0, 0.03, 0)), housing + Vector((0, -0.02, 0)), 0.026, 0.032, m["BuckleMetal"], "Head", sides=lp.res(8, 5))
    mb.frustum(housing + Vector((0, -0.02, 0)), housing + Vector((0, -0.026, 0)), 0.026, 0.026, m["LampGlow"], "Head", sides=lp.res(8, 5))
    return mb.finish()


def oilskin_hat(r, m):
    """A fisherman's oilskin hat: a round crown, a short brim in front and a long one over the neck."""
    at = _head_frame(r)
    mb = _Builder("GearOilskinHat")
    fit = head_fit(r)
    center = at(0, 0.01, 0.12)
    rows = dome(fit, center, 0.028, _rim(r, [(0, 0.205), (90, 0.17), (180, 0.14)]), rows=5, cols=16)
    mb.grid(rows, m["Gear"], "Head", inside=lambda p: center)
    rim = rows[-1]
    edge = []
    for i, p in enumerate(rim):
        t = TAU * i / len(rim)
        back = smooth(1.2, 2.8, abs(wrap(t)))
        edge.append(p + around(t) * (0.045 + 0.08 * back) + Vector((0, 0, -0.01 - 0.07 * back)))
    mb.shell([rim, edge], 0.01, m["Gear"], m["GearTrim"], lambda p: p + Vector((0, 0, -1)), "Head", closed=True)
    loop_tube(mb, [p + Vector((0, 0, 0.006)) for p in rim], 0.012, m["GearTrim"], "Head", sides=4)
    return mb.finish()


# ---------------------------------------------------------------- shoulders


class _Shoulder:
    """
    Directions over one shoulder for pads: the pole points up and out; mu turns around it (0 toward the
    outside of the arm, 90 degrees to the front, 180 toward the neck). Pads are wrapped around the
    shoulder as it is when the arms hang down (the deltoid, the top of the arm, the torso's top and the
    base of the neck) and sit on the Shoulder bone, so they stay put while the arm swings under them.
    """

    def __init__(self, r, s, elevation=60.0):
        a = math.radians(elevation)
        self.s = s
        z, sx, dl = r["shoulder_z"], r["shoulder_x"], r["deltoid"]
        self.joint = Vector((s * sx, 0, z))
        self.center = self.joint + Vector((-s * 0.035, 0, -0.005))
        self.pole = Vector((s * math.cos(a), 0, math.sin(a)))
        self.out = Vector((s * math.sin(a), 0, -math.cos(a)))
        self.bone = ("Left" if s > 0 else "Right") + "Shoulder"
        points = []
        for lat in (-60, -25, 15, 50, 90):
            for i in range(8):
                t = TAU * i / 8
                c = math.cos(math.radians(lat))
                points.append(self.joint + Vector((c * math.cos(t), c * math.sin(t), math.sin(math.radians(lat)))) * dl * 1.08)
        hang = math.radians(16)
        arm = self.joint + Vector((s * 0.12 * math.sin(hang), 0, -0.12 * math.cos(hang)))
        points += [arm + Vector((math.cos(t) * r["upper_arm"][1], math.sin(t) * r["upper_arm"][1], 0)) for t in (TAU * i / 8 for i in range(8))]
        for zz in (z + 0.015, z - 0.06):
            half, front, back = hu.extent(r, zz)
            points += [p for p in arc(zz, half, -front, back, 0, TAU, 17) if p.x * s > 0.02]
        points += [Vector((s * r["neck_r"] * 1.15, y, r["neck"])) for y in (-0.05, 0.0, 0.05)]
        self.fit = Fit(points)

    def dir(self, theta, mu):
        theta, mu = math.radians(theta), math.radians(mu)
        side = self.out * math.cos(mu) + Vector((0, -1, 0)) * math.sin(mu)
        return self.pole * math.cos(theta) + side * math.sin(theta)

    def at(self, theta, mu, clearance):
        d = self.dir(theta, mu)
        return self.center + d * (self.fit.reach(self.center, d) + clearance)

    def rows(self, thetas, mus, clearance):
        """
        Rows at the polar angles in `thetas` (functions of mu, or numbers; 0 is the pole) over the angles
        `mus`, `clearance(theta, mu)` outside the shoulder.
        """
        out = []
        for theta in thetas:
            if theta == 0:
                out.append([self.at(0, 0, clearance(0, 0))])
                continue
            row = []
            for mu in mus:
                th = theta(mu) if callable(theta) else theta
                row.append(self.at(th, mu, clearance(th, mu)))
            out.append(row)
        return out


def _edge(mu, outer, side, inner):
    """How far down a pad reaches at angle mu: `outer` over the arm, `side` front and back, `inner` at the neck."""
    a = abs(((mu + 180) % 360) - 180)
    return interp([(0, outer), (90, side), (180, inner)], a)


def leather_pads(r, m):
    """Layered leather shoulder pads: a cap over the shoulder and three riveted lames down the arm."""
    mb = _Builder("GearLeatherPads")
    for s in (1, -1):
        sh = _Shoulder(r, s)
        n = lp.res(14, 8)
        mus = [-180 + 360 * i / n for i in range(n)]
        end = lambda mu: _edge(mu, 62, 66, 58)
        cap = sh.rows([0] + [lambda mu, f=f: end(mu) * f for f in (0.3, 0.6, 0.85, 1.0)], mus, lambda th, mu: 0.024)
        mb.shell(cap, 0.012, m["Gear"], m["GearDark"], lambda p: sh.center, sh.bone, closed=True, rim_mat=m["GearTrim"])
        loop_tube(mb, cap[-1], 0.009, m["GearTrim"], sh.bone, sides=4)
        n = lp.res(8, 4)
        side_mus = [-95 + 190 * i / n for i in range(n + 1)]
        for k, (a, b) in enumerate(((-6, 12), (6, 24), (18, 36))):
            lame = sh.rows([lambda mu, a=a: end(mu) + a, lambda mu, a=a, b=b: end(mu) + (a + b) / 2, lambda mu, b=b: end(mu) + b],
                           side_mus, lambda th, mu, k=k: 0.02 - 0.003 * k)
            mb.shell(lame, 0.01, m["Gear"], m["GearDark"], lambda p: sh.center, sh.bone, rim_mat=m["GearTrim"])
            for i in range(2, len(side_mus) - 1, 2):
                p = lame[-1][i]
                stud(mb, p + (p - sh.center).normalized() * 0.004 + (lame[0][i] - p) * 0.25, p - sh.center, 0.007, m["BuckleMetal"], sh.bone)
    return mb.finish()


def _pauldron(r, m, mb, s, spikes):
    sh = _Shoulder(r, s, elevation=58.0)
    n = lp.res(16, 8)
    mus = [-180 + 360 * i / n for i in range(n)]
    end = lambda mu: _edge(mu, 72, 72, 60)
    clear = lambda th, mu: 0.03 + 0.012 * math.sin(math.radians(th) * 2)
    dome_rows = sh.rows([0] + [lambda mu, f=f: end(mu) * f for f in (0.25, 0.5, 0.75, 1.0)], mus, clear)
    rim = m["GearMetalTrim"] if spikes else m["BuckleMetal"]
    mb.shell(dome_rows, 0.014, m["GearMetal"], m["GearMetalTrim"], lambda p: sh.center, sh.bone, closed=True)
    loop_tube(mb, dome_rows[-1], 0.013, rim, sh.bone, sides=5)
    for i in range(0, n, 2):
        p = dome_rows[-2][i]
        stud(mb, p, p - sh.center, 0.008, m["BuckleMetal"], sh.bone)
    k = lp.res(8, 4)
    side_mus = [-95 + 190 * i / k for i in range(k + 1)]
    lame = sh.rows([lambda mu: end(mu) - 4, lambda mu: end(mu) + 10, lambda mu: end(mu) + 22], side_mus,
                   lambda th, mu: 0.024)
    mb.shell(lame, 0.012, m["GearMetal"], m["GearMetalTrim"], lambda p: sh.center, sh.bone)
    loop_tube(mb, lame[-1], 0.009, rim, sh.bone, sides=4, closed=False)
    if not spikes:
        lame = sh.rows([lambda mu: end(mu) + 18, lambda mu: end(mu) + 30, lambda mu: end(mu) + 40], side_mus[1:-1],
                       lambda th, mu: 0.02)
        mb.shell(lame, 0.01, m["GearMetal"], m["GearMetalTrim"], lambda p: sh.center, sh.bone)
        loop_tube(mb, lame[-1], 0.008, rim, sh.bone, sides=4, closed=False)
    if spikes:
        for theta, length in ((8, 0.12), (30, 0.095), (52, 0.07)):
            d = sh.dir(theta, 0)
            base = sh.at(theta, 0, 0.03)
            spike(mb, base - d * 0.02, base + (d + sh.pole * 0.4).normalized() * length, 0.024, m["GearMetalTrim"], sh.bone, sides=6)


def pauldrons(r, m):
    """Round plate pauldrons with a rolled rim, rivets and a lame below."""
    mb = _Builder("GearPauldrons")
    for s in (1, -1):
        _pauldron(r, m, mb, s, spikes=False)
    return mb.finish()


def spiked_pauldrons(r, m):
    """The plate pauldrons with a row of spikes."""
    mb = _Builder("GearSpikedPauldrons")
    for s in (1, -1):
        _pauldron(r, m, mb, s, spikes=True)
    return mb.finish()


def fur_mantle(r, m):
    """A thick, lumpy fur mantle rolled around the neck and over both shoulders, open in front, tufts along its hem, with clasps."""
    mb = _Builder("GearFurMantle")
    z, sx, dl, neck, nr = r["shoulder_z"], r["shoulder_x"], r["deltoid"], r["neck"], r["neck_r"]
    open_t = math.radians(28)
    cols = lp.res(23, 9)
    specs = ((neck + 0.07, nr + 0.05, 0.05), (neck + 0.045, nr + 0.085, 0.085), (z + 0.09, sx + 0.035, 0.095),
             (z + 0.03, sx + dl * 1.42, 0.115), (z - 0.05, sx + dl * 1.62, 0.125), (z - 0.1, sx + dl * 1.68, 0.12))
    rows = []
    for k, (zz, half, grow) in enumerate(specs):
        _, front, back = hu.extent(r, min(zz, z + 0.04))
        row = arc(zz, half, -front + grow, back + grow, open_t, TAU - open_t, cols, square=0.3)
        center = Vector((0, 0, zz))
        row = [q + (q - center).normalized() * (0.012 * math.sin(3.1 * i + 1.7 * k) + (0.01 if (i + k) % 2 else 0.0))
               for i, q in enumerate(row)]
        rows.append(row)
    mb.shell(rows, 0.032, m["Gear"], m["GearDark"], lambda q: Vector((0, 0.0, q.z - 0.1)), "Chest", rim_mat=m["GearTrim"])
    # Soft tufts along the hem.
    for i, q in enumerate(rows[-1]):
        if i in (0, len(rows[-1]) - 1):
            continue
        out = (q - Vector((0, 0, q.z))).normalized()
        length = 0.035 + 0.02 * ((i * 7) % 3) / 2
        tip = q + out * 0.012 + Vector((0, 0, -length))
        mb.tube([(q + Vector((0, 0, 0.03)), 0.03), ((q + tip) / 2, 0.026), (tip, 0.008)], m["Gear"], "Chest", sides=lp.res(7, 4))
    for side in (1, -1):
        q = rows[2][0 if side < 0 else -1]
        mb.sphere(q + Vector((0, -0.025, -0.02)), (0.022, 0.012, 0.022), m["BuckleMetal"], "Chest", segments=lp.res(8, 5), rings=4)
    return mb.finish(sharp_angle=80)


# ---------------------------------------------------------------- back and body


def _cloak_rows(r, cols, ragged=False):
    """A cloak's rows from the neck over the shoulders down to the calves, in folds; a torn hem when `ragged`."""
    z, sx, dl = r["shoulder_z"], r["shoulder_x"], r["deltoid"]
    hem = r["knee"] * 0.62
    rows = []
    _, _, back = hu.extent(r, z - 0.06)
    rows.append(arc(z + 0.075, r["neck_r"] + 0.06, r["neck_r"] + 0.01, r["neck_r"] + 0.07, math.radians(70), math.radians(290), cols))
    rows.append(arc(z - 0.0, sx + dl * 0.9, 0.04, back + 0.055, math.radians(88), math.radians(272), cols, square=0.4))
    drops = [z - 0.16, z - 0.36, (z + hem) / 2 - 0.08, hem + (z - hem) * 0.2, hem]
    for k, zz in enumerate(drops):
        f = (z - zz) / (z - hem)
        _, _, body_back = hu.extent(r, zz)
        reach = max(body_back, back * 0.9) + 0.06 + 0.1 * f
        half = sx + dl * 1.0 + 0.07 * f
        row = []
        for i in range(cols):
            u = 1 - 2 * i / (cols - 1)
            fold = 0.018 * f * (1 if i % 2 else -1) if 0 < i < cols - 1 else 0.0
            drop = 0.0
            if ragged and k == len(drops) - 1 and 0 < i < cols - 1:
                drop = (0.07, -0.02, 0.05, 0.0, 0.08, -0.015, 0.04)[i % 7]
            row.append(Vector((u * half, reach - 0.11 * u * u * (1 - 0.3 * f) + fold, zz + drop)))
        rows.append(row)

    def weigh(p):
        s = smooth(z - 0.02, z - 0.2, p.z)
        return {"Chest": 1.0 - s, "Cloak": s}
    return rows, weigh


def _cloak(mb, r, m, rows, weigh):
    mb.shell(rows, 0, m["Gear"], m["GearTrim"], lambda p: Vector((0, 0.0, p.z)), weigh)
    border = [row[0] for row in rows] + rows[-1][1:] + [row[-1] for row in reversed(rows[:-1])] + list(reversed(rows[0][1:-1]))
    loop_tube(mb, border, 0.01, m["GearTrim"], weigh, sides=5)
    for end in (0, -1):
        p = rows[0][end]
        mb.sphere(p + Vector((0, -0.012, -0.01)), (0.026, 0.012, 0.026), m["BuckleMetal"], "Chest", segments=lp.res(8, 5), rings=4)


def cloak(r, m):
    """A cloak from the shoulders to the calves, in folds, with clasps; the hem follows the Cloak bone."""
    mb = _Builder("GearCloak")
    rows, weigh = _cloak_rows(r, lp.res(13, 7))
    _cloak(mb, r, m, rows, weigh)
    return mb.finish()


def _on_rows(rows, u, v):
    """A point on a cloak's surface: u across from its left edge (-1) to its right (1), v down from the shoulders (0) to the hem (1)."""
    k = 1 + v * (len(rows) - 2)
    k0 = min(int(k), len(rows) - 2)
    f = k - k0

    def across(row):
        c = (1 - u) / 2 * (len(row) - 1)
        c0 = min(int(c), len(row) - 2)
        g = c - c0
        return row[c0] * (1 - g) + row[c0 + 1] * g
    return across(rows[k0]) * (1 - f) + across(rows[k0 + 1]) * f


def warden_cloak(r, m):
    """A warden's cloak with a torn hem and the Greywood's tree embroidered in gold on its back."""
    mb = _Builder("GearWardenCloak")
    rows, weigh = _cloak_rows(r, lp.res(13, 7), ragged=True)
    _cloak(mb, r, m, rows, weigh)
    out = Vector((0, 0.007, 0))
    trunk = [(0, 0.66), (0.02, 0.52), (0, 0.38), (0, 0.24)]
    branches = [[(0, 0.44), (-0.18, 0.34), (-0.34, 0.26)], [(0, 0.44), (0.18, 0.34), (0.34, 0.26)],
                [(0, 0.34), (-0.12, 0.22), (-0.2, 0.12)], [(0, 0.34), (0.12, 0.22), (0.2, 0.12)],
                [(0, 0.28), (-0.04, 0.16), (-0.02, 0.08)], [(0, 0.28), (0.05, 0.17), (0.03, 0.09)],
                [(0.02, 0.62), (-0.14, 0.7), (-0.24, 0.72)], [(0.02, 0.62), (0.15, 0.69), (0.25, 0.72)]]
    mb.tube([(_on_rows(rows, u, v) + out, (0.016, 0.006)) for u, v in trunk], m["Embroidery"], weigh, sides=lp.res(5, 4))
    for branch in branches:
        mb.tube([(_on_rows(rows, u, v) + out, 0.0065 - 0.0015 * i) for i, (u, v) in enumerate(branch)], m["Embroidery"], weigh,
                sides=lp.res(5, 4))
    # A ring around the tree.
    ring = [_on_rows(rows, 0.42 * math.sin(a), 0.42 + 0.34 * math.cos(a)) + out for a in (TAU * i / lp.res(24, 10) for i in range(lp.res(24, 10)))]
    loop_tube(mb, ring, 0.006, m["Embroidery"], weigh, sides=4, squash=(0.5, 1.0))
    return mb.finish()


def skirt_weights(r):
    """A robe's skirt: the hips at the waist, toward the thighs below (each side to its own leg)."""
    top, knee, lx = r["hip"] + 0.04, r["knee"], r["leg_x"]

    def weigh(p):
        down = smooth(top, knee + (top - knee) * 0.1, p.z) * 0.92
        left = smooth(-lx * 0.9, lx * 0.9, p.x)
        return {"Hips": 1.0 - down, "LeftUpperLeg": down * left, "RightUpperLeg": down * (1.0 - left)}
    return weigh


def _skirt_rows(r, cols):
    """A flared skirt's rings from under the belt down to the shins."""
    belt_z = r["belt"][0][0] + 0.015
    tunic_z = r["tunic"][0][0]
    hem = (r["knee"] + r["ankle"]) / 2 - 0.02
    half0, front0, back0 = hu.extent(r, tunic_z)
    rows = []
    for zz in (belt_z, tunic_z, r["hip"] - (r["hip"] - r["knee"]) * 0.5, r["knee"], hem):
        if zz >= tunic_z:
            half, front, back = hu.extent(r, zz)
            half, front, back = half + 0.012, -front + 0.014, back + 0.014
        else:
            f = (tunic_z - zz) / (tunic_z - hem)
            half, front, back = half0 * (1.04 + 0.26 * f), -front0 * (1.05 + 0.32 * f), back0 * (1.05 + 0.38 * f)
        rows.append(arc(zz, half, front, back, 0, TAU * (cols - 1) / cols, cols, square=0.2))
    return rows


def robe_skirt(r, m):
    """A robe's flared skirt from under the belt to the shins, a band down the front and a rolled hem."""
    mb = _Builder("GearRobeSkirt")
    rows = _skirt_rows(r, lp.res(18, 8))
    weigh = skirt_weights(r)
    mb.shell(rows, 0, m["Gear"], m["GearDark"], lambda p: Vector((0, 0.0, p.z + 0.2)), weigh, closed=True)
    loop_tube(mb, rows[-1], 0.012, m["GearTrim"], weigh, sides=5)
    # The band down the front.
    band = [row[0] + Vector((0, -0.006, 0)) for row in rows]
    mb.tube([(p, (0.03, 0.005)) for p in band], m["GearTrim"], weigh, sides=4)
    return mb.finish()


def rags(r, m):
    """Rotten clothes: a skirt torn into ragged strips, tatters over the shoulders, strands of weed (Seaweed)."""
    mb = _Builder("GearRags")
    cols = lp.res(20, 8)
    rows = _skirt_rows(r, cols)
    # Torn: every column ends at its own length, some strips hang long, some are torn short.
    lengths = [0.5, 1.0, 0.25, 0.85, 0.6, 0.15, 0.95, 0.4, 0.75, 0.3, 1.0, 0.55, 0.2, 0.9, 0.45, 0.7, 0.1, 0.8, 0.35, 0.65]
    knee_row = rows[-2]
    rows[-1] = [k + (h - k) * f for k, h, f in zip(knee_row, rows[-1], lengths)]
    weigh = skirt_weights(r)
    mb.shell(rows, 0, m["Gear"], m["GearDark"], lambda p: Vector((0, 0.0, p.z + 0.2)), weigh, closed=True)
    z, sx = r["shoulder_z"], r["shoulder_x"]
    for s in (1, -1):
        sh = _Shoulder(r, s)
        n = lp.res(7, 4)
        mus = [-110 + 220 * i / n for i in range(n + 1)]
        tatter = sh.rows([lambda mu: 20, lambda mu: 50, lambda mu: 70 + 22 * ((int(mu) // 10) % 3) / 2], mus, lambda th, mu: 0.012)
        mb.shell(tatter, 0, m["GearTrim"], m["GearDark"], lambda p: sh.center, sh.bone)
    weed = lp.material("Seaweed", "#3f5a2a")
    for s, x0 in ((1, 0.05), (-1, 0.09)):
        front = hu.extent(r, r["chest"])[1]
        top = Vector((s * (sx - 0.02), -0.02, z + 0.07))
        path = [top, Vector((s * x0, front - 0.03, z - 0.12)), Vector((s * x0 * 0.6, front - 0.04, r["chest"] - 0.1)),
                Vector((s * x0 * 0.9, front - 0.035, r["spine"] - 0.02))]
        mb.tube([(p, (0.012, 0.022)) for p in path], weed, by_height("Spine", "Chest", r["chest"] - 0.05, r["chest"] + 0.05), sides=4)
    return mb.finish()


def gauntlets(r, m):
    """Plate gauntlets: flared cuffs over the forearms with a rolled rim, and riveted plates over the fists."""
    mb = _Builder("GearGauntlets")
    z, ex, wx, k = r["shoulder_z"], r["elbow_x"], r["wrist_x"], r["hand"] / 0.13
    fa = r["forearm"]
    n = lp.res(10, 6)
    for side, s in hu.SIDES:
        lower, hand = side + "LowerArm", side + "Hand"
        rows = []
        for x, radius in ((ex + (wx - ex) * 0.45, fa[1] * 1.15), (ex + (wx - ex) * 0.75, fa[2] * 1.3), (wx - 0.005, fa[2] * 1.75)):
            rows.append([Vector((s * x, math.cos(t) * radius, z + math.sin(t) * radius)) for t in (TAU * i / n for i in range(n))])
        mb.shell(rows, 0.008, m["GearMetal"], m["GearMetalTrim"], lambda p: Vector((p.x, 0, z)), lower, closed=True)
        loop_tube(mb, rows[-1], 0.009, m["GearMetalTrim"], lower, sides=4)
        loop_tube(mb, rows[0], 0.007, m["GearMetalTrim"], lower, sides=4)
        # Plates over the back of the fist, in two rows, with rivets.
        for x0, x1 in ((0.0, 0.055), (0.05, 0.1)):
            pts = []
            for x in (x0, x1):
                for y in (-0.046, 0.042):
                    pts += [(s * (wx + x * k), y * k, z + 0.03 * k), (s * (wx + x * k), y * k, z + 0.042 * k)]
            pts += [(s * (wx + (x0 + x1) / 2 * k), 0, z + 0.05 * k)]
            mb.hull(pts, m["GearMetal"], hand)
        for y in (-0.03, 0.026):
            stud(mb, (s * (wx + 0.02 * k), y * k, z + 0.044 * k), (0, 0, 1), 0.006, m["BuckleMetal"], hand)
    return mb.finish()


def greaves(r, m):
    """Plate greaves: a cop over each knee with a fan at its side, a plate down the front of the shin, straps."""
    mb = _Builder("GearGreaves")
    knee, top = r["knee"], r["boot_top"]
    th, sh = r["thigh"], r["shin"]
    for side, s in hu.SIDES:
        bone = side + "LowerLeg"
        x = s * r["leg_x"]
        center = Vector((x, 0, knee))
        # The knee cop: a shell over the front of the knee.
        rows = []
        for dz in (0.07, 0.025, -0.02, -0.06):
            rows.append([center + Vector((math.sin(a) * th[2] * 1.2, -math.cos(a) * th[2] * 1.15 - 0.03, dz)) for a in
                         (math.radians(d) for d in (-80, -40, 0, 40, 80))])
        rows[1] = [p + (p - Vector((x, 0, p.z))).normalized() * 0.012 for p in rows[1]]
        rows[2] = [p + (p - Vector((x, 0, p.z))).normalized() * 0.012 for p in rows[2]]
        mb.shell(rows, 0.008, m["GearMetal"], m["GearMetalTrim"], lambda p: Vector((x, 0, p.z)), bone)
        stud(mb, rows[1][2] + Vector((0, -0.006, -0.02)), (0, -1, 0), 0.01, m["BuckleMetal"], bone)
        # The fan on the outer side of the knee.
        fan = Vector((x + s * th[2] * 0.95, -0.01, knee))
        mb.hull([fan + Vector((0, dy, dz)) for dy, dz in ((-0.04, 0.04), (0.04, 0.04), (0.0, -0.05), (-0.03, -0.03))]
                + [fan + Vector((s * 0.015, 0, 0))], m["GearMetal"], bone)
        # The shin plate, curving round the front of the shin down to the boot.
        rows = []
        for z, rad in ((knee - 0.08, sh[0] * 1.15), ((knee + top) / 2, sh[1] * 1.2), (top + 0.04, sh[2] * 1.2)):
            rows.append([Vector((x + math.sin(a) * rad, -math.cos(a) * rad - 0.004, z)) for a in (math.radians(d) for d in (-70, -35, 0, 35, 70))])
        mb.shell(rows, 0.008, m["GearMetal"], m["GearMetalTrim"], lambda p: Vector((x, 0, p.z)), bone)
        mb.tube([(p + Vector((0, -0.008, 0)), (0.006, 0.006)) for p in [row[2] for row in rows]], m["GearMetalTrim"], bone, sides=4)
        for z in ((knee + top) / 2 + 0.06, top + 0.09):
            mb.tube([((x + s * 0.0, 0, z), (sh[1] * 1.22, sh[1] * 1.22)), ((x + s * 0.0, 0, z - 0.022), (sh[1] * 1.22, sh[1] * 1.22))],
                    m["GearDark"], bone, sides=7)
    return mb.finish()


def sash(r, m):
    """A cloth sash over the belt, knotted at the left hip, its two ends hanging down the thigh."""
    mb = _Builder("GearSash")
    z0, z1 = r["belt"][0][0] - 0.005, r["belt"][1][0] + 0.005
    rows = []
    for zz in (z0, z1):
        half, front, back = hu.extent(r, zz)
        n = lp.res(16, 8)
        rows.append(arc(zz, half + 0.016, -front + 0.016, back + 0.016, 0, TAU * (n - 1) / n, n, square=0.35))
    mb.shell(rows, 0, m["Gear"], m["GearDark"], lambda p: Vector((0, 0, p.z)), "Hips", closed=True)
    for row in rows:
        loop_tube(mb, row, 0.006, m["GearTrim"], "Hips", sides=4)
    half, front, _ = hu.extent(r, (z0 + z1) / 2)
    knot = Vector((half * 0.72, front * 0.75 - 0.025, (z0 + z1) / 2))
    mb.sphere(knot, (0.045, 0.03, 0.04), m["Gear"], "Hips", segments=6, rings=4)
    weigh = skirt_weights(r)
    for dx, length, swing in ((-0.02, 0.3, -0.02), (0.035, 0.24, 0.03)):
        top = knot + Vector((dx, -0.005, -0.02))
        bottom = top + Vector((swing, -0.02, -length))
        strip = [[top + Vector((-0.03, 0, 0)), top + Vector((0.03, 0, 0))],
                 [(top + bottom) / 2 + Vector((-0.032, -0.01, 0)), (top + bottom) / 2 + Vector((0.032, -0.01, 0))],
                 [bottom + Vector((-0.036, 0, 0)), bottom + Vector((0.036, 0, 0.015))]]
        mb.shell(strip, 0, m["Gear"], m["GearDark"], lambda p: Vector((p.x, 0.2, p.z)), weigh)
        mb.tube([(bottom + Vector((-0.036, 0, 0)), 0.006), (bottom + Vector((0.036, 0, 0.015)), 0.006)], m["GearTrim"], weigh, sides=4)
    return mb.finish()


def robe_cuffs(r, m):
    """Wide bell sleeves flaring over the forearms of a robe."""
    mb = _Builder("GearRobeCuffs")
    z, ex, wx = r["shoulder_z"], r["elbow_x"], r["wrist_x"]
    fa = r["forearm"]
    n = lp.res(12, 6)
    for side, s in hu.SIDES:
        bone = side + "LowerArm"
        rows = []
        for x, radius in ((ex + (wx - ex) * 0.3, fa[1] * 1.1), (ex + (wx - ex) * 0.7, fa[2] * 1.5), (wx - 0.01, fa[2] * 2.2)):
            rows.append([Vector((s * x, math.cos(t) * radius, z - 0.012 + math.sin(t) * radius * 1.08 - (radius - fa[1]) * 0.6))
                         for t in (TAU * i / n for i in range(n))])
        mb.shell(rows, 0.008, m["Gear"], m["GearDark"], lambda p: Vector((p.x, 0, z)), bone, closed=True, rim_mat=m["GearTrim"])
        loop_tube(mb, rows[-1], 0.01, m["GearTrim"], bone, sides=4)
    return mb.finish()


TRUNK = ("pelvis", "belly", "torso", "collar")


def _cuirass_row(r, z, cols, lift=0.016, lift_front=0.034, keel=0.014):
    """A ring of plate around the trunk at height z, over the pecs, with a keel down the front."""
    half, front, back = hu.extent(r, z, TRUNK)
    row = arc(z, half + lift, -front + lift_front, back + lift, 0, TAU * (cols - 1) / cols, cols, square=0.3)
    center = Vector((0, 0, z))
    out = []
    for i, p in enumerate(row):
        around_deg = abs(math.degrees(wrap(TAU * i / cols)))
        push = sum(amount * math.exp(-((z - bz) / bh) ** 2) * math.exp(-((around_deg - ba) / bw) ** 2)
                   for bz, bh, ba, bw, amount in hu.trunk_bumps(r))
        q = p + outward([p], center, push)[0] - p
        out.append(q)
    out[0] = out[0] + Vector((0, -keel, 0))
    return out


def breastplate(r, m):
    """
    A plate cuirass over the chest and belly with a keel down the front and gilded rims, and tassets of two
    lames over each hip, below the belt.
    """
    mb = _Builder("GearBreastplate")
    cols = lp.res(18, 8)
    sz, ch, sp = r["shoulder_z"], r["chest"], r["spine"]
    zs = [sp + 0.035, (sp + ch) / 2, ch, ch + (sz - ch) * 0.35, ch + (sz - ch) * 0.7, sz - 0.015, sz + 0.03]
    rows = [_cuirass_row(r, zz, cols) for zz in zs]
    rows[-1] = _cuirass_row(r, zs[-1], cols, lift=0.01, lift_front=0.02, keel=0.004)
    weigh = by_height("Spine", "Chest", ch - 0.05, ch + 0.05)
    mb.shell(rows, 0.012, m["GearMetal"], m["GearMetalTrim"], lambda p: Vector((0, 0.0, p.z)), weigh, closed=True)
    for row in (rows[0], rows[-1]):
        loop_tube(mb, row, 0.012, m["BuckleMetal"], weigh, sides=5)
    # The tassets: two overlapping lames on each hip, hanging from the belt.
    hip, knee = r["hip"], r["knee"]
    top = r["belt"][0][0] + 0.012
    skirt = skirt_weights(r)
    for s in (1, -1):
        for k, (z0, z1) in enumerate(((top, hip - (hip - knee) * 0.2), (hip - (hip - knee) * 0.14, hip - (hip - knee) * 0.38))):
            lame = []
            n = lp.res(6, 3)
            for zz, grow in ((z0, 0.0), ((z0 + z1) / 2, 0.012), (z1, 0.024)):
                half, front, back = hu.extent(r, max(zz, r["tunic"][0][0]))
                lift = 0.022 + 0.01 * k + grow
                lame.append(arc(zz, half + lift, -front + lift, back + lift, s * math.radians(24), s * math.radians(104), n + 1, square=0.3))
            mb.shell(lame, 0.008, m["GearMetal"], m["GearMetalTrim"], lambda p: Vector((0, 0.0, p.z)), skirt)
            loop_tube(mb, lame[-1], 0.007, m["BuckleMetal"], skirt, sides=4, closed=False)
            for p in lame[0][1:-1:2]:
                stud(mb, p + Vector((0, 0, -0.012)), p - Vector((0, 0, p.z)), 0.006, m["BuckleMetal"], skirt)
    return mb.finish()


def star_emblem(r, m):
    """A gilded eight-pointed star on the chest of a breastplate (the knights of Mirewatch)."""
    mb = _Builder("GearStarEmblem")
    sz, ch = r["shoulder_z"], r["chest"]
    z = ch + (sz - ch) * 0.46
    cols = 18
    p0 = _cuirass_row(r, z, cols)[0]
    p1 = _cuirass_row(r, z + 0.03, cols)[0]
    normal = (Vector((0, -1, 0)) - (p1 - p0).normalized() * (p1 - p0).normalized().dot(Vector((0, -1, 0)))).normalized()
    up = (p1 - p0).normalized()
    side = up.cross(normal).normalized()
    base = p0 + normal * 0.008
    ring = []
    for i in range(16):
        a = TAU * i / 16
        radius = (0.058 if i % 4 == 0 else 0.042) if i % 2 == 0 else 0.017
        ring.append(base + side * (math.sin(a) * radius) + up * (math.cos(a) * radius))
    back = [p - normal * 0.006 for p in ring]
    tip = base + normal * 0.012
    tail = base - normal * 0.006
    verts = ring + back + [tip, tail]
    n = len(ring)
    faces = [(i, (i + 1) % n, 2 * n) for i in range(n)]
    faces += [((i + 1) % n, i, n + i, n + (i + 1) % n) for i in range(n)]
    faces += [(n + (i + 1) % n, n + i, 2 * n + 1) for i in range(n)]
    mb.add(verts, faces, m["BuckleMetal"], "Chest", closed=True)
    mb.sphere(base + normal * 0.01, 0.011, m["BuckleMetal"], "Chest", segments=lp.res(8, 5), rings=lp.res(5, 3))
    return mb.finish(sharp_angle=30)


def apron(r, m):
    """A heavy apron from the chest to the knees, with a pocket, neck straps and waist ties."""
    mb = _Builder("GearApron")
    z, chest, knee = r["shoulder_z"], r["chest"], r["knee"]
    top = chest + (z - chest) * 0.55
    hip_z = r["tunic"][0][0]
    cols = lp.res(9, 5)
    rows = []
    for zz in (top, chest, r["spine"], r["belt"][0][0], hip_z, (hip_z + knee) / 2, knee + 0.06):
        half, front, back = hu.extent(r, max(zz, hip_z))
        spread = math.radians(48 if zz > chest + 0.01 else 68)
        row = arc(max(zz, hip_z), half + 0.01, -front + 0.018, back, -spread, spread, cols, square=0.3)
        if zz < hip_z:
            row = [Vector((p.x * 1.02, p.y - (hip_z - zz) * 0.08, zz)) for p in row]
        rows.append(row)
    skirt = skirt_weights(r)

    def weigh(p):
        if p.z < hip_z:
            return skirt(p)
        # Below a joint a part must not follow the bone above it, or it swings the wrong way.
        spine = smooth(r["spine"] - 0.04, r["spine"] + 0.04, p.z)
        upper = smooth(chest - 0.03, chest + 0.03, p.z)
        return {"Hips": 1.0 - spine, "Spine": spine * (1.0 - upper), "Chest": spine * upper}

    mb.shell(rows, 0, m["Gear"], m["GearDark"], lambda p: Vector((0, 0.0, p.z)), weigh)
    border = [row[0] for row in rows] + rows[-1][1:] + [row[-1] for row in reversed(rows[:-1])] + list(reversed(rows[0][1:-1]))
    loop_tube(mb, border, 0.008, m["GearTrim"], weigh, sides=4)
    # The pocket.
    a, b = rows[4][cols // 2 - 1], rows[4][cols // 2 + 1]
    mid = (a + b) / 2 + Vector((0, -0.006, -0.06))
    mb.box(mid, ((b - a).length, 0.012, 0.09), m["GearTrim"], skirt)
    # Straps up over the shoulders to the back of the neck, ties around the waist.
    for end in (0, -1):
        p = rows[0][end]
        nape = Vector((math.copysign(r["neck_r"] * 0.9, p.x), r["neck_r"] * 0.8, r["neck"] + 0.01))
        over = Vector((math.copysign(r["neck_r"] + 0.03, p.x), -0.02, r["neck"] + 0.02))
        mb.tube([(p, 0.009), (over, 0.009), (nape, 0.009)], m["GearDark"], "Chest", sides=4)
    belt = r["belt"][0][0] + 0.01
    half, front, back = hu.extent(r, belt)
    tie = arc(belt, half + 0.012, -front + 0.012, back + 0.012, math.radians(60), math.radians(300), lp.res(9, 5))
    mb.tube([(p, (0.004, 0.012)) for p in tie], m["GearDark"], "Hips", sides=4)
    return mb.finish()


def tabard(r, m):
    """A cloth tabard hanging from under the belt to the knees, front and back, edged in gold, pointed at the hem."""
    mb = _Builder("GearTabard")
    top = r["belt"][1][0] - 0.012
    tz = r["tunic"][0][0]
    hem = r["knee"] + (r["hip"] - r["knee"]) * 0.14
    cols = lp.res(8, 4)
    levels = lp.res(6, 3)
    half_w = hu.extent(r, top)[0] * 0.66
    skirt = skirt_weights(r)
    lx = r["leg_x"]

    def weigh(p):
        down = smooth(tz, hem, p.z) * 0.6
        left = smooth(-lx, lx, p.x)
        w = {"Hips": 1.0 - down, "LeftUpperLeg": down * left, "RightUpperLeg": down * (1.0 - left)}
        return w if p.z < tz else skirt(p)
    for face in (-1, 1):
        rows = []
        for k in range(levels + 1):
            f = k / levels
            zc = top + (hem - top) * f
            half, front, back = hu.extent(r, max(zc, tz))
            reach = (-front if face < 0 else back) + 0.012 + 0.03 * smooth(tz, hem, zc)
            w = half_w * (1.0 - 0.15 * f)
            row = []
            for i in range(cols + 1):
                u = -1 + 2 * i / cols
                zz = zc
                if k == levels:
                    zz = hem - (0.075 if face < 0 else 0.05) * (1 - abs(u))
                row.append(Vector((u * w, face * (reach - 0.035 * u * u), zz)))
            rows.append(row)
        mb.shell(rows, 0, m["Gear"], m["GearDark"], lambda p, face=face: Vector((p.x * 0.5, face * -0.2, p.z)), weigh)
        border = [row[0] for row in rows] + rows[-1][1:] + [row[-1] for row in reversed(rows[:-1])]
        loop_tube(mb, border, 0.006, m["Embroidery"], weigh, closed=False, sides=4)
        if face < 0:
            # A gold lozenge above the point.
            c = rows[-1][cols // 2] + Vector((0, -0.006, 0.07))
            mb.hull([c + Vector(d) for d in ((0.024, 0, 0), (-0.024, 0, 0), (0, 0, 0.034), (0, 0, -0.034), (0, -0.006, 0), (0, 0.004, 0))],
                    m["Embroidery"], weigh)
    return mb.finish(sharp_angle=60)


def cowl(r, m):
    """A cowl of cloth wound around the neck and over the shoulders, its hood thrown back onto the upper back."""
    mb = _Builder("GearCowl")
    neck, sz, nr = r["neck"], r["shoulder_z"], r["neck_r"]
    n = lp.res(16, 8)
    _, front, back = hu.extent(r, sz - 0.02, TRUNK)
    for k, (lift, grow, radius) in enumerate(((0.0, 0.0, 0.034), (0.034, -0.008, 0.026))):
        loop = []
        for i in range(n):
            t = TAU * i / n
            frontness = max(0.0, math.cos(t))
            d = around(t)
            reach = nr + 0.05 + grow + 0.028 * abs(math.sin(t)) + 0.012 * frontness
            z = neck - 0.032 + lift - 0.035 * frontness ** 2
            loop.append(Vector((0, 0.008, z)) + d * reach)
        loop_tube(mb, loop, radius, m["Gear"], "Chest", sides=lp.res(8, 5))
    # The hood, lying in folds between the shoulder blades, its opening rimmed darker.
    center = Vector((0, back + 0.035, sz - 0.075))
    hood = _thrown_hood(center)
    star = sc.Star(hood, center)
    sc.grid_mesh(mb, star.grid(lp.res(8, 4), lp.res(12, 6)), m["Gear"], "Chest", closed=True, inside=lambda p: center)
    rim = [center + Vector((math.sin(a) * 0.075, 0.034 + 0.008 * math.cos(a), 0.05 + math.cos(a) * 0.035)) for a in
           (TAU * i / lp.res(12, 6) for i in range(lp.res(12, 6)))]
    loop_tube(mb, rim, 0.01, m["GearTrim"], "Chest", sides=lp.res(5, 4))
    return mb.finish(sharp_angle=70)


def _thrown_hood(center):
    """The thrown-back hood of a cowl: a fold of cloth lying on the upper back."""
    c = Vector(center)
    return sc.blend(sc.ellipsoid(c, (0.115, 0.05, 0.1)), (sc.ellipsoid(c + Vector((0, 0.018, 0.06)), (0.09, 0.04, 0.05)), 0.03),
                    (sc.ellipsoid(c + Vector((0, 0.01, -0.07)), (0.06, 0.035, 0.05)), 0.03))


def coat(r, m):
    """
    A long coat's skirt split into tails that end in points, a darker underskirt showing between them, gold
    edges and lozenges; a high collar with a crescent clasp, and a glowing crystal on a chain at the hip.
    """
    mb = _Builder("GearCoat")
    weigh = skirt_weights(r)
    belt_z = r["belt"][0][0] + 0.015
    tz = r["tunic"][0][0]
    hem = (r["knee"] + r["ankle"]) / 2 - 0.02
    half0, front0, back0 = hu.extent(r, tz)

    def skirt_ring(zz, t0, t1, n, grow=0.0):
        if zz >= tz:
            half, front, back = hu.extent(r, zz)
            half, front, back = half + 0.014 + grow, -front + 0.016 + grow, back + 0.016 + grow
        else:
            f = (tz - zz) / (tz - hem)
            half, front, back = half0 * (1.05 + 0.24 * f) + grow, -front0 * (1.06 + 0.3 * f) + grow, back0 * (1.06 + 0.34 * f) + grow
        return arc(zz, half, front, back, t0, t1, n, square=0.2)
    levels = [belt_z, tz, r["hip"] - (r["hip"] - r["knee"]) * 0.5, r["knee"], hem]
    # The underskirt (GearDark, a little inside), seen at the front between the tails.
    under = [skirt_ring(zz, math.radians(-40), math.radians(40), lp.res(5, 3), grow=-0.006) for zz in levels[:-1] + [hem - 0.03]]
    mb.shell(under, 0, m["GearDark"], m["GearDark"], lambda p: Vector((0, 0.0, p.z)), weigh)
    n = lp.res(9, 4)
    for s in (1, -1):
        # Each side's tail from beside the front opening round to the back seam, its hem pointed at the front.
        t0, t1 = s * math.radians(12), s * math.radians(178)
        rows = [skirt_ring(zz, t0, t1, n + 1) for zz in levels]
        last = rows[-1]
        for i, p in enumerate(last):
            u = i / n
            point = math.exp(-((u - 0.12) / 0.12) ** 2)
            last[i] = p + Vector((0, 0, -0.09 * point + 0.05 * u * u))
        mb.shell(rows, 0, m["Gear"], m["GearDark"], lambda p: Vector((0, 0.0, p.z)), weigh)
        border = [row[0] for row in rows] + rows[-1][1:] + [row[-1] for row in reversed(rows[:-1])]
        loop_tube(mb, border, 0.007, m["Embroidery"], weigh, closed=False, sides=4)
        tip = rows[-1][min(range(len(last)), key=lambda i: last[i].z)]
        c = tip + Vector((0, 0, 0.075)) + (tip - Vector((0, 0, tip.z))).normalized() * 0.004
        mb.hull([c + Vector(d) for d in ((0.018 * s, 0, 0), (-0.018 * s, 0, 0), (0, 0, 0.028), (0, 0, -0.028), (0, -0.006, 0), (0, 0.004, 0))],
                m["Embroidery"], weigh)
    # The high collar, open at the throat, with a gilded rim.
    nr, neck, sz = r["neck_r"], r["neck"], r["shoulder_z"]
    cols = lp.res(14, 7)
    collar = []
    for zz, reach in ((neck - 0.03, nr + 0.03), (neck + 0.02, nr + 0.035), (neck + 0.065, nr + 0.05)):
        collar.append([Vector((0, 0.01, zz)) + around(t) * reach for t in
                       (math.radians(28) + (TAU - math.radians(56)) * i / (cols - 1) for i in range(cols))])
    mb.shell(collar, 0.008, m["Gear"], m["GearDark"], lambda p: Vector((0, 0.01, p.z)), "Chest", rim_mat=m["GearTrim"])
    loop_tube(mb, collar[-1], 0.006, m["Embroidery"], "Chest", closed=False, sides=4)
    # The crescent clasp under the collar.
    _, front, _ = hu.extent(r, sz - 0.05, TRUNK)
    c = Vector((0, front - 0.025, sz - 0.05))
    arc_pts = [c + Vector((math.sin(a) * 0.026, 0, math.cos(a) * 0.026)) for a in
               (math.radians(-150 + 300 * i / 8) for i in range(9))]
    radii = [0.003 + 0.007 * math.sin(math.pi * i / 8) for i in range(9)]
    mb.tube([(p + Vector((0.008 * math.sin(math.pi * i / 8), 0, 0)), (rr, rr * 0.6)) for i, (p, rr) in enumerate(zip(arc_pts, radii))],
            m["BuckleMetal"], "Chest", sides=lp.res(6, 4))
    # The crystal on a chain at the left hip.
    half, front, _ = hu.extent(r, belt_z)
    hook = Vector((half * 0.55, front - 0.01, belt_z - 0.01))
    gem = hook + Vector((0.01, -0.012, -0.12))
    mb.tube([(hook, 0.003), (hook + Vector((0.006, -0.008, -0.05)), 0.003), (gem + Vector((0, 0, 0.045)), 0.003)], m["BuckleMetal"], weigh, sides=4)
    mb.hull([gem + Vector(d) for d in ((0, 0, 0.045), (0, 0, -0.05), (0.018, 0, 0), (-0.018, 0, 0), (0, 0.016, 0.004), (0, -0.016, 0.004),
                                       (0.011, 0.011, 0.012), (-0.011, -0.011, -0.012))], m["CrystalGlow"], weigh)
    return mb.finish(sharp_angle=60)


def harness(r, m):
    """Leather straps crossed over the chest and back from the shoulders to the hips, studded, a brass medallion where they cross."""
    mb = _Builder("GearHarness")
    sz, ch, sp = r["shoulder_z"], r["chest"], r["spine"]
    low = r["belt"][1][0] + 0.005
    steps = lp.res(10, 4)

    def surface(x, z, face):
        half, front, back = hu.extent(r, z, TRUNK)
        e = 2.0 / (2.0 + 3.0 * 0.3)
        p = 2.0 / e
        depth = -front if face < 0 else back
        q = max(0.0, 1.0 - min(1.0, abs(x) / max(half, 1e-6)) ** p) ** (1.0 / p)
        y0 = face * depth * q
        around_deg = abs(math.degrees(math.atan2(x, -y0)))
        bump = sum(amount * math.exp(-((z - bz) / bh) ** 2) * math.exp(-((around_deg - ba) / bw) ** 2) for bz, bh, ba, bw, amount in hu.trunk_bumps(r))
        return Vector((x, y0 + face * (bump + 0.014), z))

    def weigh(p):
        a = smooth(sp - 0.03, sp + 0.03, p.z)
        b = smooth(ch - 0.04, ch + 0.04, p.z)
        return {"Hips": 1.0 - a, "Spine": a * (1.0 - b), "Chest": a * b}
    for face in (-1, 1):
        for s in (1, -1):
            top = Vector((s * r["shoulder_x"] * 0.62, 0, sz + 0.04))
            path = []
            for i in range(steps + 1):
                u = i / steps
                z = sz - 0.02 + (low - (sz - 0.02)) * u
                x = s * r["shoulder_x"] * 0.62 * (1 - 2 * u) * (1 - 0.1 * u)
                path.append(surface(x, z, face))
            path = [top] + path
            mb.tube([(p, (0.024, 0.006)) for p in path], m["Gear"], weigh, sides=lp.res(6, 4))
            if face < 0:
                for p in path[2:-1:2]:
                    stud(mb, p + Vector((0, -0.006, 0)), Vector((0, -1, 0)), 0.006, m["BuckleMetal"], weigh)
    # The medallion where the straps cross on the chest.
    zc = sz - 0.02 + (low - (sz - 0.02)) * 0.5
    c = surface(0, zc, -1) + Vector((0, -0.006, 0))
    mb.frustum(c + Vector((0, 0.004, 0)), c + Vector((0, -0.01, 0)), 0.04, 0.036, m["BuckleMetal"], weigh, sides=lp.res(14, 7))
    mb.frustum(c + Vector((0, -0.01, 0)), c + Vector((0, -0.016, 0)), 0.02, 0.016, m["BuckleMetal"], weigh, sides=lp.res(10, 6))
    loop_tube(mb, [c + Vector((math.sin(a) * 0.03, -0.012, math.cos(a) * 0.03)) for a in (TAU * i / lp.res(14, 7) for i in range(lp.res(14, 7)))],
              0.004, m["GearTrim"], weigh, sides=4)
    return mb.finish(sharp_angle=50)


# ---------------------------------------------------------------- all pieces

PIECES = {
    "Helm": helm,
    "HornedHelm": horned_helm,
    "Hood": hood,
    "BoneHood": bone_hood,
    "StrawHat": straw_hat,
    "WizardHat": wizard_hat,
    "MinerHelm": miner_helm,
    "OilskinHat": oilskin_hat,
    "LeatherPads": leather_pads,
    "Pauldrons": pauldrons,
    "SpikedPauldrons": spiked_pauldrons,
    "FurMantle": fur_mantle,
    "Cloak": cloak,
    "RobeSkirt": robe_skirt,
    "RobeCuffs": robe_cuffs,
    "Rags": rags,
    "Breastplate": breastplate,
    "Gauntlets": gauntlets,
    "Greaves": greaves,
    "Sash": sash,
    "Apron": apron,
    "Tabard": tabard,
    "Cowl": cowl,
    "Coat": coat,
    "Harness": harness,
    "WardenCloak": warden_cloak,
    "StarEmblem": star_emblem,
}


def build_all(r, names=None):
    """The gear pieces for a race (all, or those named), each its own mesh object to bind to the race's armature."""
    m = _materials()
    return [build(r, m) for name, build in PIECES.items() if names is None or name in names]
