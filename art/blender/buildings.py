"""
Detailed buildings (props with more parts than props.py's village set). Origin at the base center,
meters, +Z up, the front faces -Y. Still low-poly and flat shaded: the detail comes from shapes
(shingle courses, planks, beams), not from textures.
"""

import math
import random

from mathutils import Matrix, Vector

import lowpoly as lp

PALETTE = {
    # Dark red-brown timber, slate-green shingles, cream stone and warm lamplight.
    "Timber": "#3b2420", "TimberLight": "#4a2e28", "Planks": "#5a3a31", "PlanksDark": "#4e322b",
    "Slate": "#4f6a65", "SlateLight": "#5d7a74", "SlateDark": "#425a56", "PorchPlanks": "#5c4033",
    "Door": "#6a2f29", "DoorLattice": "#8a4a3a", "Plinth": "#4b4440", "StepStone": "#d6c9a0",
    "StepRim": "#8f8064", "Pot": "#7a5a44", "Leaves": "#46752f", "LeavesDark": "#335a26", "Blossom": "#e58aa6",
    "Iron": "#2d2a28",
}


def mats():
    m = {name: lp.material(name, color) for name, color in PALETTE.items()}
    m["WindowGlow"] = lp.material("WindowGlow", "#ffb54a", emission=2.0)
    m["LanternGlow"] = lp.material("LanternGlow", "#ffd27a", emission=2.5)
    return m


# ---------------------------------------------------------------- building blocks

def slab(mb, upper, lower, normals, thickness, mat, matrix=None):
    """
    A thin solid between two polylines (its upper and lower edge), thickened along per-point normals:
    one course of shingles or a row of boards.
    """
    n = len(upper)
    a = [Vector(p) for p in upper]
    b = [Vector(p) for p in lower]
    c = [p + Vector(nv) * thickness for p, nv in zip(a, normals)]
    d = [p + Vector(nv) * thickness for p, nv in zip(b, normals)]
    verts = a + b + c + d
    A, B, C, D = 0, n, 2 * n, 3 * n
    faces = []
    for i in range(n - 1):
        faces.append((A + i, A + i + 1, B + i + 1, B + i))
        faces.append((C + i, D + i, D + i + 1, C + i + 1))
        faces.append((A + i, C + i, C + i + 1, A + i + 1))
        faces.append((B + i, B + i + 1, D + i + 1, D + i))
    faces.append((A, B, D, C))
    faces.append((A + n - 1, C + n - 1, D + n - 1, B + n - 1))
    mb.add(verts, faces, mat, matrix=matrix, closed=True)


def shingled_roof(mb, u0, u1, half_span, ridge, eave, courses, materials, rng, matrix=None, segments=10, thickness=0.1):
    """
    A two-sided roof with its ridge along local X (u0..u1), made of overlapping shingle courses that
    follow the curves ridge(u) and eave(u) (heights). `matrix` places it (turn it for a cross gable).
    """
    us = [u0 + (u1 - u0) * i / segments for i in range(segments + 1)]
    for side in (-1, 1):
        jitter = [[rng.uniform(-0.03, 0.03) for _ in us] for _ in range(courses + 1)]
        for k in range(courses):
            t0 = k / courses
            t1 = min(1.0, (k + 1) / courses + 0.08)
            upper, lower, normals = [], [], []
            for i, u in enumerate(us):
                r, e = ridge(u), eave(u)
                drop = r - e
                normal = Vector((0, side * drop, half_span)).normalized()
                upper.append((u, side * half_span * t0, r - drop * t0 + jitter[k][i]))
                lower.append((u, side * half_span * t1, r - drop * t1 + jitter[k + 1][i] - 0.04))
                normals.append(normal)
            slab(mb, upper, lower, normals, thickness, materials[k % len(materials)], matrix)


def beam(mb, points, radius, mat, taper=0.75, sides=6):
    """A bent beam through `points`, thinning toward the end (roof horns, bargeboards)."""
    count = len(points) - 1
    for i in range(count):
        ra = radius * (1 - (1 - taper) * i / count)
        rb = radius * (1 - (1 - taper) * (i + 1) / count)
        mb.frustum(points[i], points[i + 1], ra, rb, mat, sides=sides)


def lantern(mb, hook, m, drop=0.45):
    """A lantern hanging from `hook` on a short chain."""
    x, y, z = hook
    mb.frustum((x, y, z), (x, y, z - drop), 0.015, 0.015, m["Iron"], sides=4)
    top = z - drop
    mb.frustum((x, y, top), (x, y, top - 0.1), 0.05, 0.13, m["Iron"], sides=6)
    mb.box((x, y, top - 0.27), (0.18, 0.18, 0.26), m["LanternGlow"])
    for dx in (-0.09, 0.09):
        for dy in (-0.09, 0.09):
            mb.box((x + dx, y + dy, top - 0.27), (0.03, 0.03, 0.28), m["Iron"])
    mb.frustum((x, y, top - 0.4), (x, y, top - 0.47), 0.12, 0.04, m["Iron"], sides=6)


def arch_outline(width, straight, segments=10):
    """A door or window outline in X/Z: a rectangle `straight` high with a half circle on top."""
    r = width / 2
    pts = [(-r, 0.0), (r, 0.0), (r, straight)]
    for i in range(1, segments):
        a = math.pi * i / segments
        pts.append((r * math.cos(a), straight + r * math.sin(a)))
    pts.append((-r, straight))
    return pts


def prism(mb, outline, y0, y1, mat, offset=(0, 0, 0)):
    """Extrudes an X/Z outline from y0 to y1 (a door, a gable wall, a step)."""
    ox, oy, oz = offset
    n = len(outline)
    verts = [(ox + x, oy + y0, oz + z) for x, z in outline] + [(ox + x, oy + y1, oz + z) for x, z in outline]
    faces = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
    mb.add(verts, faces, mat, closed=True)


def half_disc(radius, segments=12):
    """A half circle in X/Y toward -Y (the front), for round steps."""
    return [(radius * math.cos(math.pi + math.pi * i / segments), radius * math.sin(math.pi + math.pi * i / segments))
            for i in range(segments + 1)]


def plank_wall(mb, a0, a1, at, z0, z1, m, rng, board=0.32, facing=-1, along="x"):
    """
    Vertical boards on a wall from a0 to a1: along X at y = `at` (facing -Y or +Y), or along Y at
    x = `at` (facing -X or +X).
    """
    count = max(1, int(round((a1 - a0) / board)))
    width = (a1 - a0) / count
    for i in range(count):
        c = a0 + width * (i + 0.5)
        depth = 0.05 + rng.uniform(0, 0.03)
        mat = m["Planks"] if i % 2 == 0 else m["PlanksDark"]
        out = at + facing * depth / 2
        if along == "x":
            mb.box((c, out, (z0 + z1) / 2), (width - 0.02, depth, z1 - z0), mat)
        else:
            mb.box((out, c, (z0 + z1) / 2), (depth, width - 0.02, z1 - z0), mat)


def window(mb, x, y, z, m, width=0.7, height=0.9, facing=-1):
    """A glowing window in a dark frame with a cross of mullions, on a wall facing -Y (or +Y)."""
    f = facing
    mb.box((x, y + f * 0.06, z), (width + 0.2, 0.08, height + 0.2), m["Timber"])
    mb.box((x, y + f * 0.1, z), (width, 0.04, height), m["WindowGlow"])
    mb.box((x, y + f * 0.13, z), (0.06, 0.04, height), m["Timber"])
    mb.box((x, y + f * 0.13, z), (width, 0.04, 0.06), m["Timber"])
    mb.box((x, y + f * 0.16, z - height / 2 - 0.12), (width + 0.35, 0.18, 0.08), m["TimberLight"])


def planter(mb, x, y, m, rng):
    """A clay pot with a round bush and a few blossoms."""
    mb.frustum((x, y, 0), (x, y, 0.45), 0.28, 0.36, m["Pot"], sides=8)
    mb.sphere((x, y, 0.85), 0.42, m["Leaves"], segments=8, rings=6)
    for _ in range(4):
        a = rng.uniform(0, 2 * math.pi)
        mb.sphere((x + math.cos(a) * 0.36, y + math.sin(a) * 0.36, 0.8 + rng.uniform(0, 0.3)), 0.07, m["Blossom"], segments=5, rings=3)


def lattice(mb, x0, x1, z0, z1, y, mat, bars=4, lean=32.0):
    """A criss-cross trellis panel on a wall facing -Y, between two rails."""
    h = z1 - z0
    length = h / math.cos(math.radians(lean))
    reach = h * math.tan(math.radians(lean)) / 2
    for sign in (-1, 1):
        for i in range(bars):
            cx = x0 + reach + (x1 - x0 - 2 * reach) * i / (bars - 1)
            mb.box((cx, y, (z0 + z1) / 2), (0.05, 0.04, length), mat, rotation=(0, sign * lean, 0))
    mb.box(((x0 + x1) / 2, y - 0.02, z0), (x1 - x0, 0.06, 0.07), mat)
    mb.box(((x0 + x1) / 2, y - 0.02, z1), (x1 - x0, 0.06, 0.07), mat)


# ---------------------------------------------------------------- the grove house

def grove_house():
    """
    A small house of dark timber under a swaybacked roof of slate shingles: two cross gables at the
    front whose bargeboards cross above the peak and curl outward like horns, with lanterns hanging
    from them; an arched door under a plank porch roof, round stone steps, a dormer and planters.
    """
    m = mats()
    rng = random.Random(11)
    mb = lp.MeshBuilder("GroveHouse")
    W, D, base, top = 6.6, 4.6, 0.3, 3.3
    hx, hy = W / 2, D / 2

    # Plinth, core walls and the timber frame.
    mb.box((0, 0, base / 2), (W + 0.6, D + 0.6, base), m["Plinth"])
    mb.box((0, 0, (base + top) / 2), (W, D, top - base), m["Planks"])
    plank_wall(mb, -hx, hx, hy, base, top, m, rng, facing=1)
    plank_wall(mb, -0.8, 0.8, -hy, base, top, m, rng)
    for x, f in ((-hx, -1), (hx, 1)):
        plank_wall(mb, -hy, hy, x, base, top, m, rng, facing=f, along="y")
    for x in (-hx, hx):
        for y in (-hy, hy):
            mb.box((x, y, (base + top) / 2 + 0.05), (0.34, 0.34, top - base + 0.1), m["Timber"])
    for y in (-hy, hy):
        mb.box((0, y, top - 0.08), (W + 0.2, 0.3, 0.24), m["Timber"])
    for x in (-hx, hx):
        mb.box((x, 0, top - 0.08), (0.3, D + 0.2, 0.24), m["Timber"])
    for x in (-1.7, 1.7):
        window(mb, x, hy, 1.9, m, facing=1)
    # Side windows (on the gable walls, facing out along X).
    for x, f in ((-hx, -1), (hx, 1)):
        mb.box((x + f * 0.06, 0.4, 1.9), (0.08, 0.9, 1.1), m["Timber"])
        mb.box((x + f * 0.1, 0.4, 1.9), (0.04, 0.7, 0.9), m["WindowGlow"])
        mb.box((x + f * 0.13, 0.4, 1.9), (0.04, 0.06, 0.9), m["Timber"])

    # Main roof: ridge along X, swaybacked (higher at the ends), eaves that flick up at the corners.
    L, span = hx + 0.6, hy + 0.75
    ridge = lambda u: 6.0 + 0.75 * (u / L) ** 2
    eave = lambda u: 3.1 + 0.35 * (u / L) ** 4
    shingled_roof(mb, -L, L, span, ridge, eave, 6, [m["Slate"], m["SlateLight"], m["SlateDark"]], rng)
    beam(mb, [(u, 0, ridge(u) + 0.12) for u in [-L - 0.1 + (2 * L + 0.2) * i / 12 for i in range(13)]], 0.13, m["Timber"], taper=1.0)
    # Gable walls under the main roof ends, with bargeboards that turn up at the peak.
    for x in (-hx, hx):
        apex = ridge(hx) - 0.35
        mb.add([(x, -hy, top), (x, hy, top), (x, 0, apex)], [(0, 1, 2)], m["PlanksDark"], double_sided=True)
        ex = x + (0.62 if x > 0 else -0.62)
        for side in (-1, 1):
            beam(mb, [(ex, side * span, eave(L) + 0.05), (ex, side * span * 0.5, (eave(L) + ridge(L)) / 2 + 0.1),
                      (ex, 0, ridge(L) + 0.2), (ex, -side * 0.35, ridge(L) + 0.75), (ex, -side * 0.55, ridge(L) + 1.0)],
                 0.12, m["Timber"], taper=0.5)

    # Two cross gables at the front, each with a window, a little gable window and horned bargeboards.
    for cx in (-2.05, 2.05):
        ww, front = 1.25, -hy - 0.85
        mb.box((cx, (front - hy) / 2, (base + top) / 2), (2 * ww, -hy - front + 0.1, top - base), m["Planks"])
        plank_wall(mb, cx - ww, cx + ww, front, base, top, m, rng)
        for x in (cx - ww, cx + ww):
            mb.box((x, front, (base + top) / 2 + 0.05), (0.32, 0.32, top - base + 0.1), m["Timber"])
        mb.box((cx, front, top - 0.08), (2 * ww + 0.2, 0.3, 0.24), m["Timber"])
        window(mb, cx, front, 1.85, m)
        lattice(mb, cx - ww + 0.25, cx + ww - 0.25, base + 0.1, 1.1, front - 0.08, m["DoorLattice"])
        g_ridge_front, g_span, g_front = 5.4, ww + 0.55, front - 0.55
        g_ridge = lambda u, f=g_front: g_ridge_front - 0.25 * ((u - f) / 3.0)
        g_eave = lambda u: 3.1
        turn = Matrix.Translation((cx, 0, 0)) @ Matrix.Rotation(math.radians(90), 4, "Z")
        # Local X runs along world Y after the turn: the gable ridge goes from the front edge back into the main roof.
        shingled_roof(mb, g_front, -0.3, g_span, g_ridge, g_eave, 5, [m["SlateLight"], m["Slate"], m["SlateDark"]], rng,
                      matrix=turn, segments=6)
        apex = g_ridge_front - 0.3
        mb.add([(cx - ww, front, top), (cx, front, apex), (cx + ww, front, top)], [(0, 1, 2)], m["PlanksDark"], double_sided=True)
        window(mb, cx, front, 4.1, m, width=0.45, height=0.55)
        # Bargeboards: from each eave corner up to the peak, crossing it and curling outward into horns.
        y = g_front + 0.02
        for side in (-1, 1):
            beam(mb, [(cx + side * (g_span + 0.05), y, 3.0), (cx + side * g_span * 0.5, y, (3.0 + g_ridge_front) / 2 + 0.12),
                      (cx, y, g_ridge_front + 0.15), (cx - side * 0.45, y, g_ridge_front + 0.85),
                      (cx - side * 0.75, y, g_ridge_front + 1.45), (cx - side * 0.62, y, g_ridge_front + 1.8)],
                 0.13, m["Timber"], taper=0.45)
        lantern(mb, (cx - 0.7 if cx < 0 else cx + 0.7, y - 0.02, g_ridge_front + 1.6), m)

    # The middle: an arched door under a plank porch roof, round steps, planters.
    door_y = -hy
    prism(mb, arch_outline(1.55, 1.75), door_y - 0.02, door_y - 0.12, m["Timber"], offset=(0, 0, base))
    prism(mb, arch_outline(1.25, 1.65), door_y - 0.1, door_y - 0.18, m["Door"], offset=(0, 0, base))
    for sign in (-1, 1):
        mb.box((sign * 0.22, door_y - 0.2, base + 1.05), (0.06, 0.04, 2.0), m["DoorLattice"], rotation=(0, sign * 18, 0))
    mb.box((0, door_y - 0.2, base + 1.1), (1.1, 0.04, 0.06), m["DoorLattice"])
    mb.sphere((0.4, door_y - 0.24, base + 1.0), 0.05, m["Iron"], segments=6, rings=4)
    for k, (y0, y1, z0, z1) in enumerate(((door_y, door_y - 0.6, top - 0.05, 2.95), (door_y - 0.5, door_y - 1.15, 3.0, 2.7))):
        upper = [(x, y0, z0) for x in (-1.05, -0.35, 0.35, 1.05)]
        lower = [(x, y1, z1) for x in (-1.05, -0.35, 0.35, 1.05)]
        normal = Vector((0, -(z0 - z1), abs(y1 - y0))).normalized()
        slab(mb, upper, lower, [normal] * 4, 0.1, m["PorchPlanks"])
    for x in (-0.95, 0.95):
        mb.box((x, door_y - 1.05, (base + 2.75) / 2), (0.14, 0.14, 2.75 - base), m["Timber"])
    for radius, z0, z1, mat in ((2.0, 0.0, 0.1, m["StepRim"]), (1.75, 0.1, 0.2, m["StepStone"]), (1.4, 0.2, 0.3, m["StepStone"])):
        outline = half_disc(radius)
        verts = [(x, door_y + y, z0) for x, y in outline] + [(x, door_y + y, z1) for x, y in outline]
        n = len(outline)
        faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
        for i in range(n):
            j = (i + 1) % n
            faces.append((i, j, n + j, n + i))
        mb.add(verts, faces, mat, closed=True)
    for x in (-1.25, 1.25):
        planter(mb, x, door_y - 1.6, m, rng)

    # A dormer in the middle of the main roof, above the porch.
    dz = 4.55
    mb.box((0, -1.55, dz), (1.1, 1.0, 1.0), m["Planks"])
    window(mb, 0, -2.05, dz - 0.05, m, width=0.5, height=0.55)
    d_ridge = lambda u: dz + 1.0
    turn = Matrix.Translation((0, 0, 0)) @ Matrix.Rotation(math.radians(90), 4, "Z")
    shingled_roof(mb, -2.35, -1.0, 0.8, d_ridge, lambda u: dz + 0.35, 2, [m["SlateDark"], m["Slate"]], rng, matrix=turn, segments=3)
    return mb.finish()


BUILDINGS = {
    "GroveHouse": grove_house,
}
