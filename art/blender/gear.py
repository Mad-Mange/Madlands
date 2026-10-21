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
keeps its own color: BuckleMetal (rivets, buckles, clasps), Bone, LampGlow, Seaweed.
"""

import math

import bmesh
from mathutils import Matrix, Vector

import humanoid as hu
import lowpoly as lp

TAU = 2 * math.pi
UP = Vector((0, 0, 1))


def _materials():
    return dict(
        Gear=lp.material("Gear", "#8a7458"),
        GearTrim=lp.material("GearTrim", "#564837"),
        GearDark=lp.material("GearDark", "#342c21"),
        GearMetal=lp.material("GearMetal", "#9aa3ae", metallic=0.6, roughness=0.5),
        GearMetalTrim=lp.material("GearMetalTrim", "#5f656c", metallic=0.6, roughness=0.5),
        BuckleMetal=lp.material("BuckleMetal", "#c9a24a", metallic=0.6, roughness=0.5),
        Bone=lp.material("Bone", "#ddd2b4"),
        LampGlow=lp.material("LampGlow", "#ffd27a", emission=3.0),
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
    """The head's hull: skull, jaw, ears and beard; with the hair (hats sit on it) and the nose (face plates)."""
    at, s = hu.head_at(r), r["head_shape"]
    points = [at(*p) for p in lp.mirrored(hu.SKULL)]
    points += [at(x * s["jaw"], y, z) for x, y, z in lp.mirrored(hu.JAW)]
    points += hu.ear(r, 1) + hu.ear(r, -1)
    if r["beard"]:
        points += [at(*p) for p in lp.mirrored(hu.BEARD + hu.MUSTACHE)]
    if nose:
        root = Vector((0, -0.12, 0.15))
        points += [at(*(root + (Vector(p) - root) * s["nose"])) for p in lp.mirrored(hu.NOSE)]
    if hair:
        points += [at(*p) for p in lp.mirrored(hu.HAIR_CAP + hu.HAIRLINE[r["hair"]])]
        if r["hair"] == "swept":
            points += [at(*tip) for _, tip, _ in hu.SWEPT_LOCKS]
        else:
            points += [at(x, 0.03 + y, 0.29 + z) for x, y, z in lp.mirrored([(0.05, 0, 0), (0, 0.05, 0), (0, -0.05, 0), (0, 0, 0.045)])]
    return Fit(points)


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

    ts = [TAU * i / cols for i in range(cols)]
    ends = [rim_theta(t) for t in ts]
    out = [[point(0.0, 0.0)]]
    for k in range(1, rows + 1):
        out.append([point(e * k / rows, t) for t, e in zip(ts, ends)])
    return out


def ring_at(fit, axis, z, clearance, cols=16, extra=None):
    """A closed horizontal ring around `fit` at height z: rays from the point `axis` (x, y) at that height."""
    origin = Vector((axis[0], axis[1], z))
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
    mb.tube(list(zip(path, radii)), mat, bone, sides=sides)


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
    mb = lp.MeshBuilder("GearHelm")
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
    front, back = 0, len(rows[1]) // 2
    path = [rows[k][front] for k in range(5, 0, -1)] + [rows[0][0]] + [rows[k][back] for k in range(1, 5)]
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
    mb = lp.MeshBuilder("GearHornedHelm")
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
        rows.append(arc(zz, half, -front + grow, back + grow, open_t, TAU - open_t, 15, square=square))
    # A slightly scalloped hem.
    rows[-1] = [p + Vector((0, 0, -0.012 if i % 2 else 0.0)) for i, p in enumerate(rows[-1])]
    mb.shell(rows, 0.01, outer, inner, lambda p: Vector((0, 0.0, p.z - 0.1)), "Chest", rim_mat=m["GearTrim"])
    loop_tube(mb, rows[-1], 0.01, m["GearTrim"], "Chest", closed=False, sides=5)


def _hood(r, m, name, outer, inner, edge):
    at = _head_frame(r)
    mb = lp.MeshBuilder(name)
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
    mb = lp.MeshBuilder("GearStrawHat")
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
    mb = lp.MeshBuilder("GearWizardHat")
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
    mb = lp.MeshBuilder("GearMinerHelm")
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
    mb.frustum(housing + Vector((0, 0.03, 0)), housing + Vector((0, -0.02, 0)), 0.026, 0.032, m["BuckleMetal"], "Head", sides=8)
    mb.frustum(housing + Vector((0, -0.02, 0)), housing + Vector((0, -0.026, 0)), 0.026, 0.026, m["LampGlow"], "Head", sides=8)
    return mb.finish()


def oilskin_hat(r, m):
    """A fisherman's oilskin hat: a round crown, a short brim in front and a long one over the neck."""
    at = _head_frame(r)
    mb = lp.MeshBuilder("GearOilskinHat")
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
    mb = lp.MeshBuilder("GearLeatherPads")
    for s in (1, -1):
        sh = _Shoulder(r, s)
        mus = [-180 + 360 * i / 14 for i in range(14)]
        end = lambda mu: _edge(mu, 62, 66, 58)
        cap = sh.rows([0] + [lambda mu, f=f: end(mu) * f for f in (0.3, 0.6, 0.85, 1.0)], mus, lambda th, mu: 0.024)
        mb.shell(cap, 0.012, m["Gear"], m["GearDark"], lambda p: sh.center, sh.bone, closed=True, rim_mat=m["GearTrim"])
        loop_tube(mb, cap[-1], 0.009, m["GearTrim"], sh.bone, sides=4)
        side_mus = [-95 + 190 * i / 8 for i in range(9)]
        for k, (a, b) in enumerate(((-6, 12), (6, 24), (18, 36))):
            lame = sh.rows([lambda mu, a=a: end(mu) + a, lambda mu, a=a, b=b: end(mu) + (a + b) / 2, lambda mu, b=b: end(mu) + b],
                           side_mus, lambda th, mu, k=k: 0.02 - 0.003 * k)
            mb.shell(lame, 0.01, m["Gear"], m["GearDark"], lambda p: sh.center, sh.bone, rim_mat=m["GearTrim"])
            for i in (2, 4, 6):
                p = lame[-1][i]
                stud(mb, p + (p - sh.center).normalized() * 0.004 + (lame[0][i] - p) * 0.25, p - sh.center, 0.007, m["BuckleMetal"], sh.bone)
    return mb.finish()


def _pauldron(r, m, mb, s, spikes):
    sh = _Shoulder(r, s, elevation=58.0)
    mus = [-180 + 360 * i / 16 for i in range(16)]
    end = lambda mu: _edge(mu, 72, 72, 60)
    clear = lambda th, mu: 0.03 + 0.012 * math.sin(math.radians(th) * 2)
    dome_rows = sh.rows([0] + [lambda mu, f=f: end(mu) * f for f in (0.25, 0.5, 0.75, 1.0)], mus, clear)
    mb.shell(dome_rows, 0.014, m["GearMetal"], m["GearMetalTrim"], lambda p: sh.center, sh.bone, closed=True)
    loop_tube(mb, dome_rows[-1], 0.013, m["GearMetalTrim"], sh.bone, sides=5)
    for i in range(0, 16, 2):
        p = dome_rows[-2][i]
        stud(mb, p, p - sh.center, 0.008, m["BuckleMetal"], sh.bone)
    side_mus = [-95 + 190 * i / 8 for i in range(9)]
    lame = sh.rows([lambda mu: end(mu) - 4, lambda mu: end(mu) + 10, lambda mu: end(mu) + 22], side_mus,
                   lambda th, mu: 0.024)
    mb.shell(lame, 0.012, m["GearMetal"], m["GearMetalTrim"], lambda p: sh.center, sh.bone)
    loop_tube(mb, lame[-1], 0.009, m["GearMetalTrim"], sh.bone, sides=4, closed=False)
    if spikes:
        for theta, length in ((8, 0.12), (30, 0.095), (52, 0.07)):
            d = sh.dir(theta, 0)
            base = sh.at(theta, 0, 0.03)
            spike(mb, base - d * 0.02, base + (d + sh.pole * 0.4).normalized() * length, 0.024, m["GearMetalTrim"], sh.bone, sides=6)


def pauldrons(r, m):
    """Round plate pauldrons with a rolled rim, rivets and a lame below."""
    mb = lp.MeshBuilder("GearPauldrons")
    for s in (1, -1):
        _pauldron(r, m, mb, s, spikes=False)
    return mb.finish()


def spiked_pauldrons(r, m):
    """The plate pauldrons with a row of spikes."""
    mb = lp.MeshBuilder("GearSpikedPauldrons")
    for s in (1, -1):
        _pauldron(r, m, mb, s, spikes=True)
    return mb.finish()


def fur_mantle(r, m):
    """A shaggy fur mantle over both shoulders and around the neck, open in front, with clasps."""
    mb = lp.MeshBuilder("GearFurMantle")
    z, sx, dl, neck = r["shoulder_z"], r["shoulder_x"], r["deltoid"], r["neck"]
    open_t = math.radians(30)
    cols = 21
    rows = []
    for k, (zz, half, grow) in enumerate(((neck + 0.05, r["neck_r"] + 0.075, 0.075), (z + 0.09, sx + 0.03, 0.08),
                                          (z + 0.0, sx + dl * 1.45, 0.1), (z - 0.1, sx + dl * 1.6, 0.11))):
        _, front, back = hu.extent(r, min(zz, z + 0.04))
        row = arc(zz, half, -front + grow, back + grow, open_t, TAU - open_t, cols, square=0.3)
        # Shaggy: every other point pushed out, the hem in long tufts.
        center = Vector((0, 0, zz))
        row = [p + (p - center).normalized() * (0.014 if (i + k) % 2 else 0.0) for i, p in enumerate(row)]
        if k == 3:
            row = [p + Vector((0, 0, -0.055 if i % 2 else 0.0)) for i, p in enumerate(row)]
        rows.append(row)
    mb.shell(rows, 0.03, m["Gear"], m["GearDark"], lambda p: Vector((0, 0.0, p.z - 0.1)), "Chest", rim_mat=m["GearTrim"])
    for s in (1, -1):
        p = rows[1][0 if s < 0 else -1]
        mb.sphere(p + Vector((0, -0.02, -0.02)), (0.022, 0.012, 0.022), m["BuckleMetal"], "Chest", segments=6, rings=4)
    return mb.finish()


# ---------------------------------------------------------------- back and body


def cloak(r, m):
    """A cloak from the shoulders to the calves, in folds, with clasps; the hem follows the Cloak bone."""
    mb = lp.MeshBuilder("GearCloak")
    z, sx, dl, neck = r["shoulder_z"], r["shoulder_x"], r["deltoid"], r["neck"]
    hem = r["knee"] * 0.62
    cols = 13
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
            row.append(Vector((u * half, reach - 0.11 * u * u * (1 - 0.3 * f) + fold, zz)))
        rows.append(row)

    def weigh(p):
        s = smooth(z - 0.02, z - 0.2, p.z)
        return {"Chest": 1.0 - s, "Cloak": s}

    mb.shell(rows, 0, m["Gear"], m["GearTrim"], lambda p: Vector((0, 0.0, p.z)), weigh)
    border = [row[0] for row in rows] + rows[-1][1:] + [row[-1] for row in reversed(rows[:-1])] + list(reversed(rows[0][1:-1]))
    loop_tube(mb, border, 0.01, m["GearTrim"], weigh, sides=5)
    for end in (0, -1):
        p = rows[0][end]
        mb.sphere(p + Vector((0, -0.012, -0.01)), (0.026, 0.012, 0.026), m["BuckleMetal"], "Chest", segments=7, rings=4)
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
    mb = lp.MeshBuilder("GearRobeSkirt")
    rows = _skirt_rows(r, 18)
    weigh = skirt_weights(r)
    mb.shell(rows, 0, m["Gear"], m["GearDark"], lambda p: Vector((0, 0.0, p.z + 0.2)), weigh, closed=True)
    loop_tube(mb, rows[-1], 0.012, m["GearTrim"], weigh, sides=5)
    # The band down the front.
    band = [row[0] + Vector((0, -0.006, 0)) for row in rows]
    mb.tube([(p, (0.03, 0.005)) for p in band], m["GearTrim"], weigh, sides=4)
    return mb.finish()


def rags(r, m):
    """Rotten clothes: a skirt torn into ragged strips, tatters over the shoulders, strands of weed (Seaweed)."""
    mb = lp.MeshBuilder("GearRags")
    cols = 20
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
        mus = [-110 + 220 * i / 7 for i in range(8)]
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
    mb = lp.MeshBuilder("GearGauntlets")
    z, ex, wx, k = r["shoulder_z"], r["elbow_x"], r["wrist_x"], r["hand"] / 0.13
    fa = r["forearm"]
    for side, s in hu.SIDES:
        lower, hand = side + "LowerArm", side + "Hand"
        rows = []
        for x, radius in ((ex + (wx - ex) * 0.45, fa[1] * 1.15), (ex + (wx - ex) * 0.75, fa[2] * 1.3), (wx - 0.005, fa[2] * 1.75)):
            rows.append([Vector((s * x, math.cos(t) * radius, z + math.sin(t) * radius)) for t in (TAU * i / 10 for i in range(10))])
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
    mb = lp.MeshBuilder("GearGreaves")
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
    mb = lp.MeshBuilder("GearSash")
    z0, z1 = r["belt"][0][0] - 0.005, r["belt"][1][0] + 0.005
    rows = []
    for zz in (z0, z1):
        half, front, back = hu.extent(r, zz)
        rows.append(arc(zz, half + 0.016, -front + 0.016, back + 0.016, 0, TAU * 15 / 16, 16, square=0.35))
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
    mb = lp.MeshBuilder("GearRobeCuffs")
    z, ex, wx = r["shoulder_z"], r["elbow_x"], r["wrist_x"]
    fa = r["forearm"]
    for side, s in hu.SIDES:
        bone = side + "LowerArm"
        rows = []
        for x, radius in ((ex + (wx - ex) * 0.3, fa[1] * 1.1), (ex + (wx - ex) * 0.7, fa[2] * 1.5), (wx - 0.01, fa[2] * 2.2)):
            rows.append([Vector((s * x, math.cos(t) * radius, z - 0.012 + math.sin(t) * radius * 1.08 - (radius - fa[1]) * 0.6))
                         for t in (TAU * i / 12 for i in range(12))])
        mb.shell(rows, 0.008, m["Gear"], m["GearDark"], lambda p: Vector((p.x, 0, z)), bone, closed=True, rim_mat=m["GearTrim"])
        loop_tube(mb, rows[-1], 0.01, m["GearTrim"], bone, sides=4)
    return mb.finish()


def breastplate(r, m):
    """A plate cuirass over the chest and belly with a keel down the front and rolled edges."""
    mb = lp.MeshBuilder("GearBreastplate")
    cols = 18
    rows = []
    for zz in (r["spine"] + 0.03, r["chest"], (r["chest"] + r["shoulder_z"]) / 2, r["shoulder_z"] - 0.02, r["shoulder_z"] + 0.02):
        half, front, back = hu.extent(r, zz)
        row = arc(zz, half + 0.012, -front + 0.032, back + 0.022, 0, TAU * (cols - 1) / cols, cols, square=0.3)
        row[0] = row[0] + Vector((0, -0.016, 0))
        rows.append(row)
    weigh = by_height("Spine", "Chest", r["chest"] - 0.05, r["chest"] + 0.05)
    mb.shell(rows, 0.012, m["GearMetal"], m["GearMetalTrim"], lambda p: Vector((0, 0.0, p.z)), weigh, closed=True)
    for row in (rows[0], rows[-1]):
        loop_tube(mb, row, 0.012, m["GearMetalTrim"], weigh, sides=5)
    return mb.finish()


def apron(r, m):
    """A heavy apron from the chest to the knees, with a pocket, neck straps and waist ties."""
    mb = lp.MeshBuilder("GearApron")
    z, chest, knee = r["shoulder_z"], r["chest"], r["knee"]
    top = chest + (z - chest) * 0.55
    hip_z = r["tunic"][0][0]
    cols = 9
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
    a, b = rows[4][3], rows[4][5]
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
    tie = arc(belt, half + 0.012, -front + 0.012, back + 0.012, math.radians(60), math.radians(300), 9)
    mb.tube([(p, (0.004, 0.012)) for p in tie], m["GearDark"], "Hips", sides=4)
    return mb.finish()


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
}


def build_all(r, names=None):
    """The gear pieces for a race (all, or those named), each its own mesh object to bind to the race's armature."""
    m = _materials()
    return [build(r, m) for name, build in PIECES.items() if names is None or name in names]
