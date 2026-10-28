"""
Organic shapes for the characters, modelled like clay: signed distance functions (SDF) built from
ellipsoids, round cones and rounded boxes, blended into each other (smooth unions) or carved away
(smooth subtractions), all on numpy arrays of points.

A shape becomes a mesh by casting rays from a point inside it (`Star`): every ray stops where it
leaves the shape, and the rays form a regular grid of rows over a sphere of directions. That works
for star-shaped things (a head, a nose, a fist, a beard), gives clean quads for smooth shading, and
makes the resolution a parameter: a model's levels of detail are the same shapes sampled coarser.

Directions follow gear.py: theta is the angle from straight up, t the angle around (0 is the front,
-Y; pi/2 the model's left, +X).
"""

import math

import numpy as np
from mathutils import Matrix, Vector

TAU = 2 * math.pi
# material_at may return this to leave a face out (a hole for the mouth in a beard).
SKIP = "skip"


def _arr(v):
    return np.asarray(v, dtype=np.float64)


class Shape:
    """A signed distance function: points (N, 3) -> distances (N,), negative inside."""

    def __init__(self, f):
        self.f = f

    def __call__(self, p):
        return self.f(np.atleast_2d(p))

    def __or__(self, other):
        return union(self, other)

    def __sub__(self, other):
        return subtract(self, other)

    def moved(self, offset):
        o = _arr(offset)
        return Shape(lambda p: self.f(p - o))

    def scaled(self, s):
        """Scaled per axis (a non-uniform scale bends the distances a little: fine for marching)."""
        s = _arr(s if isinstance(s, (tuple, list)) else (s, s, s))
        k = float(min(s))
        return Shape(lambda p: self.f(p / s) * k)

    def mirrored(self):
        """The shape and its mirror image in X (for symmetric features)."""
        return Shape(lambda p: self.f(p * _arr((-1, 1, 1))))

    def symmetric(self):
        """Evaluated on |x|: whatever is modelled on the left side appears on both."""
        return Shape(lambda p: self.f(np.abs(p) * _arr((1, 0, 0)) + p * _arr((0, 1, 1))))

    def offset(self, amount):
        """Grown (positive) or shrunk (negative) by `amount`."""
        return Shape(lambda p: self.f(p) - amount)


def ellipsoid(center, radii):
    c, r = _arr(center), _arr(radii)

    def f(p):
        q = p - c
        k0 = np.linalg.norm(q / r, axis=1)
        k1 = np.linalg.norm(q / (r * r), axis=1)
        return np.where(k1 > 1e-9, k0 * (k0 - 1.0) / np.maximum(k1, 1e-9), -float(r.min()))
    return Shape(f)


def sphere(center, radius):
    c = _arr(center)
    return Shape(lambda p: np.linalg.norm(p - c, axis=1) - radius)


def cone(a, b, ra, rb=None):
    """A round cone (a capsule when rb is None) from a (radius ra) to b (radius rb)."""
    a, b = _arr(a), _arr(b)
    rb = ra if rb is None else rb
    ba = b - a
    l2 = float(ba.dot(ba))
    rr = ra - rb
    a2 = l2 - rr * rr
    il2 = 1.0 / l2

    def f(p):
        pa = p - a
        y = pa @ ba
        z = y - l2
        w = pa * l2 - np.outer(y, ba)
        x2 = np.einsum("ij,ij->i", w, w)
        y2 = y * y * l2
        z2 = z * z * l2
        k = math.copysign(1.0, rr) * rr * rr * x2
        d_b = np.sqrt(x2 + z2) * il2 - rb
        d_a = np.sqrt(x2 + y2) * il2 - ra
        d_s = (np.sqrt(np.maximum(x2 * a2 * il2, 0.0)) + y * rr) * il2 - ra
        return np.where(np.sign(z) * a2 * z2 > k, d_b, np.where(np.sign(y) * a2 * y2 < k, d_a, d_s))
    return Shape(f)


def capped(a, b, ra, rb=None, rounding=0.004):
    """A cylinder or cone with flat ends from a (radius ra) to b (radius rb), its rims rounded: cuffs, bands."""
    a, b = _arr(a), _arr(b)
    rb = ra if rb is None else rb
    axis = (b - a) / np.linalg.norm(b - a)
    a, b = a + axis * rounding, b - axis * rounding
    ra, rb = ra - rounding, rb - rounding
    ba = b - a
    baba = float(ba.dot(ba))
    rba = rb - ra
    k = rba * rba + baba

    def f(p):
        pa = p - a
        papa = np.einsum("ij,ij->i", pa, pa)
        paba = (pa @ ba) / baba
        x = np.sqrt(np.maximum(papa - paba * paba * baba, 0.0))
        cax = np.maximum(0.0, x - np.where(paba < 0.5, ra, rb))
        cay = np.abs(paba - 0.5) - 0.5
        ff = np.clip((rba * (x - ra) + paba * baba) / k, 0.0, 1.0)
        cbx = x - ra - ff * rba
        cby = paba - ff
        sign = np.where((cbx < 0.0) & (cay < 0.0), -1.0, 1.0)
        return sign * np.sqrt(np.minimum(cax * cax + cay * cay * baba, cbx * cbx + cby * cby * baba)) - rounding
    return Shape(f)


def chain(points, radii):
    """Round cones through a path of points with a radius at each: horns, braids, fingers."""
    shapes = [cone(points[i], points[i + 1], radii[i], radii[i + 1]) for i in range(len(points) - 1)]
    return union(*shapes)


def box(center, half, radius=0.0, rotation=None):
    """A box with rounded edges; `rotation` (degrees about X, Y, Z) turns it about its center."""
    c, h = _arr(center), _arr(half)
    inv = None
    if rotation is not None:
        from lowpoly import euler_matrix
        inv = np.array(euler_matrix(rotation).to_3x3().inverted())

    def f(p):
        q = p - c
        if inv is not None:
            q = q @ inv.T
        q = np.abs(q) - h + radius
        outside = np.linalg.norm(np.maximum(q, 0.0), axis=1)
        inside = np.minimum(q.max(axis=1), 0.0)
        return outside + inside - radius
    return Shape(f)


def _smin(a, b, k):
    if k <= 0:
        return np.minimum(a, b)
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b * (1 - h) + a * h - k * h * (1 - h)


def union(*shapes, k=0.0):
    """Shapes merged; with `k` (meters) the seams are blended like clay."""
    def f(p):
        d = shapes[0].f(p)
        for s in shapes[1:]:
            d = _smin(d, s.f(p), k)
        return d
    return Shape(f)


def subtract(a, b, k=0.0):
    """`a` with `b` carved out of it (blended over `k`)."""
    return Shape(lambda p: -_smin(-a.f(p), b.f(p), k))


def intersect(a, b, k=0.0):
    return Shape(lambda p: -_smin(-a.f(p), -b.f(p), k))


def blend(base, *parts, k=0.02):
    """A base with parts blended on (each part a shape, or (shape, k) for its own softness)."""
    out = base
    for part in parts:
        shape, kk = part if isinstance(part, tuple) else (part, k)
        out = union(out, shape, k=kk)
    return out


def carve(base, *parts, k=0.01):
    out = base
    for part in parts:
        shape, kk = part if isinstance(part, tuple) else (part, k)
        out = subtract(out, shape, k=kk)
    return out


def direction(theta, t):
    """Unit vector at polar angle theta (from up) and angle t around (0 = front, -Y)."""
    return Vector((math.sin(theta) * math.sin(t), -math.sin(theta) * math.cos(t), math.cos(theta)))


class Star:
    """
    The surface of `shape` seen from `center` (inside it): rays out in every direction stop where they
    leave the shape. `reach` gives the distance along a ray (also from other origins inside), so gear
    can be fitted around it like around a hull.
    """

    def __init__(self, shape, center, limit=1.5):
        self.shape = shape
        self.center = Vector(center)
        self.limit = limit

    def reach_many(self, origins, dirs):
        o = _arr(origins)
        d = _arr(dirs)
        d = d / np.linalg.norm(d, axis=1)[:, None]
        n = len(o)
        t = np.zeros(n)
        prev = np.zeros(n)
        active = np.ones(n, dtype=bool)
        for _ in range(400):
            if not active.any():
                break
            idx = np.nonzero(active)[0]
            dist = self.shape.f(o[idx] + d[idx] * t[idx, None])
            out = dist > 0
            done = idx[out]
            active[done] = False
            go = idx[~out]
            prev[go] = t[go]
            t[go] += np.clip(-dist[~out] * 0.8, 0.0006, 0.03)
            over = t > self.limit
            active &= ~over
        lo, hi = prev.copy(), t.copy()
        for _ in range(16):
            mid = (lo + hi) / 2
            dist = self.shape.f(o + d * mid[:, None])
            inside = dist <= 0
            lo = np.where(inside, mid, lo)
            hi = np.where(inside, hi, mid)
        return (lo + hi) / 2

    def reach(self, origin, direction_):
        """How far the surface is from `origin` (inside) along `direction_` (the gear.Fit interface)."""
        return float(self.reach_many([tuple(origin)], [tuple(direction_)])[0])

    @property
    def top(self):
        return self.center.z + self.reach(self.center, (0, 0, 1))

    def points(self, dirs, offset=0.0):
        """Surface points along unit directions from the center, pushed out by `offset`."""
        dirs = [Vector(d).normalized() for d in dirs]
        reach = self.reach_many([tuple(self.center)] * len(dirs), [tuple(d) for d in dirs])
        return [self.center + d * (float(r) + offset) for d, r in zip(dirs, reach)]

    def normal(self, p, eps=0.0008):
        return self.normals([p], eps)[0]

    def normals(self, points, eps=0.0008):
        """Outward surface normals at points on (or near) the surface."""
        p = _arr([tuple(q) for q in points])
        g = np.stack([self.shape.f(p + np.eye(3)[i] * eps) - self.shape.f(p - np.eye(3)[i] * eps) for i in range(3)], axis=1)
        g /= np.maximum(np.linalg.norm(g, axis=1), 1e-9)[:, None]
        return [Vector(n.tolist()) for n in g]

    def patch(self, top, bottom, t0, t1, rings, segments, offset=0.0, along_normal=True):
        """
        Rows over part of the surface: around from angle t0 to t1, each column from polar angle top(t) down
        to bottom(t) (radians), lifted `offset` off the surface (along the normal: a beard, a cap of hair).
        """
        rows_dirs = []
        for k in range(rings + 1):
            f = k / rings
            row = []
            for i in range(segments + 1):
                t = t0 + (t1 - t0) * i / segments
                row.append(direction(top(t) + (bottom(t) - top(t)) * f, t))
            rows_dirs.append(row)
        flat = [d for row in rows_dirs for d in row]
        pts = self.points(flat, 0.0 if along_normal else offset)
        if along_normal and offset:
            pts = [q + n * offset for q, n in zip(pts, self.normals(pts))]
        out, i = [], 0
        for row in rows_dirs:
            out.append(pts[i:i + len(row)])
            i += len(row)
        return out

    def grid(self, rings, segments, theta0=0.0, theta1=math.pi, offset=0.0, t0=0.0, t1=None, focus=None):
        """
        Rows of surface points from polar angle theta0 to theta1 (rings + 1 rows) around t0..t1 (closed
        around without t1). A row at theta 0 or pi is a single pole point. `focus` (theta, strength)
        crowds the rows around that polar angle and the columns toward the front (more detail in a face).
        """
        closed = t1 is None
        rows_dirs = []
        for k in range(rings + 1):
            u = k / rings
            if focus is not None:
                u = _crowd(u, (focus[0] - theta0) / (theta1 - theta0), focus[1])
            theta = theta0 + (theta1 - theta0) * u
            if theta < 1e-6 or theta > math.pi - 1e-6:
                rows_dirs.append([direction(theta, 0.0)])
                continue
            if closed:
                ts = [t0 + TAU * i / segments for i in range(segments)]
                if focus is not None:
                    ts = [math.pi * _crowd(t / math.pi if t <= math.pi + 1e-9 else t / math.pi - 2, 0.0, focus[1]) for t in ts]
            else:
                ts = [t0 + (t1 - t0) * i / segments for i in range(segments + 1)]
            rows_dirs.append([direction(theta, t) for t in ts])
        flat = [d for row in rows_dirs for d in row]
        pts = self.points(flat, offset)
        out, i = [], 0
        for row in rows_dirs:
            out.append(pts[i:i + len(row)])
            i += len(row)
        return out


def _crowd(u, at, strength, width=0.2):
    """
    Remaps u in [0, 1] (or [-1, 1]) so samples crowd around `at`, keeping the ends: the inverse of the
    cumulative density 1 + 4 * strength * exp(-((x - at) / width)^2).
    """
    if strength <= 0:
        return u
    lo, hi = (-1.0, 1.0) if u < 0 or at < 0 else (0.0, 1.0)
    if u < 0:
        lo = -1.0
    xs = np.linspace(lo, hi, 257)
    rho = 1.0 + 4.0 * strength * np.exp(-((xs - at) / width) ** 2)
    cdf = np.concatenate([[0.0], np.cumsum((rho[1:] + rho[:-1]) / 2 * np.diff(xs))])
    cdf /= cdf[-1]
    return float(np.interp((u - lo) / (hi - lo), cdf, xs))


def star_mesh(mb, star, mat, bone, rings, segments, smooth=True, material_at=None, inside=None, focus=None):
    """
    The whole surface of a star-shaped shape as one closed mesh (poles at the top and bottom).
    `material_at(p)` may pick another material per face (by the face's center).
    """
    rows = star.grid(rings, segments, focus=focus)
    return grid_mesh(mb, rows, mat, bone, closed=True, smooth=smooth, material_at=material_at,
                     inside=inside or (lambda p: star.center))


def grid_mesh(mb, rows, mat, bone, closed=False, smooth=True, material_at=None, inside=None, facing_in=False):
    """lp.MeshBuilder.grid with a material per face (`material_at(center)` -> material, None or SKIP)."""
    if material_at is None:
        return mb.grid(rows, mat, bone, inside=inside, closed=closed, facing_in=facing_in, smooth=smooth)
    # Split the faces by material: one grid call per material over the same rows.
    flat, index = [], []
    for row in rows:
        index.append(list(range(len(flat), len(flat) + len(row))))
        flat.extend(Vector(p) for p in row)
    by_mat = {}
    for k in range(len(rows) - 1):
        a, b = index[k], index[k + 1]
        if len(a) == 1 or len(b) == 1:
            pole, ring = (a[0], b) if len(a) == 1 else (b[0], a)
            for i in range(len(ring) - (0 if closed else 1)):
                by_mat.setdefault(None, []).append((pole, ring[i], ring[(i + 1) % len(ring)]))
            continue
        for i in range(len(a) - (0 if closed else 1)):
            j = (i + 1) % len(a)
            by_mat.setdefault(None, []).append((a[i], a[j], b[j], b[i]))
    faces = by_mat[None]
    groups = {}
    for face in faces:
        c = sum((flat[i] for i in face), Vector()) / len(face)
        m = material_at(c) or mat
        if isinstance(m, str) and m == SKIP:
            continue
        groups.setdefault(m, []).append(face)
    return mb.add_faces(flat, groups, bone, inside=inside, facing_in=facing_in, smooth=smooth)


def rotate_points(points, pivot, rotation):
    """Points turned about `pivot` by (degrees about X, Y, Z)."""
    from lowpoly import euler_matrix
    m = Matrix.Translation(Vector(pivot)) @ euler_matrix(rotation) @ Matrix.Translation(-Vector(pivot))
    return [m @ Vector(p) for p in points]


def sweep(shape, path, sides, caps=(True, True), up=(0, 0, 1), limit=0.5):
    """
    Rows of surface points around a path (points inside the shape): at each station, rays out from the
    path in the plane across it; the ends are rounded off with rows tilted along the path and a pole.
    For long things: braids, fingers, boots, locks of hair. Returns rows for grid_mesh(closed=True).
    """
    path = [Vector(p) for p in path]
    star = Star(shape, path[0], limit=limit)
    up = Vector(up)
    origins, dirs, layout = [], [], []

    def frame(i):
        a, b = path[max(i - 1, 0)], path[min(i + 1, len(path) - 1)]
        axis = (b - a).normalized()
        ref = up if abs(axis.dot(up)) < 0.9 else Vector((0, -1, 0))
        u = axis.cross(ref).normalized()
        v = axis.cross(u).normalized()
        return axis, u, v

    def ring(i, tilt, sign):
        axis, u, v = frame(i)
        row = []
        for k in range(sides):
            a = TAU * k / sides
            radial = u * math.cos(a) + v * math.sin(a)
            row.append(radial * math.cos(tilt) + axis * sign * math.sin(tilt))
        return row

    tilts = (math.radians(70), math.radians(38))
    if caps[0]:
        origins.append(path[0])
        dirs.append(-frame(0)[0])
        layout.append(1)
        for tilt in tilts:
            row = ring(0, tilt, -1)
            origins += [path[0]] * len(row)
            dirs += row
            layout.append(len(row))
    for i in range(len(path)):
        row = ring(i, 0.0, 1)
        origins += [path[i]] * len(row)
        dirs += row
        layout.append(len(row))
    if caps[1]:
        last = len(path) - 1
        for tilt in reversed(tilts):
            row = ring(last, tilt, 1)
            origins += [path[last]] * len(row)
            dirs += row
            layout.append(len(row))
        origins.append(path[last])
        dirs.append(frame(last)[0])
        layout.append(1)
    reach = star.reach_many([tuple(o) for o in origins], [tuple(d) for d in dirs])
    pts = [Vector(o) + Vector(d).normalized() * float(r) for o, d, r in zip(origins, dirs, reach)]
    out, i = [], 0
    for n in layout:
        out.append(pts[i:i + n])
        i += n
    return out


def sweep_mesh(mb, shape, path, sides, mat, bone, smooth=True, material_at=None, caps=(True, True), up=(0, 0, 1)):
    """A swept shape (see sweep) as one closed mesh, faces facing away from the path."""
    rows = sweep(shape, path, sides, caps, up)
    pts = [Vector(p) for p in path]

    def inside(c):
        best = min(range(len(pts)), key=lambda i: (pts[i] - c).length)
        return pts[best]
    return grid_mesh(mb, rows, mat, bone, closed=True, smooth=smooth, material_at=material_at, inside=inside)
