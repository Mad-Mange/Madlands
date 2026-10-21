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
    # Brightvale farmhouses: whitewash, dark oak, thatch and field stone.
    "Plaster": "#e2d6b8", "Oak": "#4e3522", "OakLight": "#6b4a2f", "Thatch": "#b99a54", "ThatchLight": "#c9ab62",
    "ThatchDark": "#9a7f42", "Stone": "#8d8a83", "StoneDark": "#6c6a64", "Shutter": "#5a7a5a", "Firewood": "#8a6238",
    # The inn's red shingles and its sign.
    "RoofRed": "#7e3328", "RoofRedLight": "#8f3d30", "RoofRedDark": "#6a2b22", "SignBoard": "#a57d4c", "Leather": "#4a2e1c",
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


# ---------------------------------------------------------------- walls seen from outside


class Wall:
    """
    One face of a house: u runs along it (left to right seen from outside), `out` away from the house,
    z up. `yaw` turns the front face (yaw 0: at y = -depth/2, facing -Y) to the others.
    """

    def __init__(self, origin, yaw):
        self.yaw = yaw
        self.matrix = Matrix.Translation(Vector(origin)) @ Matrix.Rotation(math.radians(yaw), 4, "Z")

    def at(self, u, out, z):
        return self.matrix @ Vector((u, -out, z))

    def box(self, mb, u, out, z, size, mat, tilt=0.0):
        """A box on the wall: size is (along, out, up); `tilt` turns it in the wall's plane (degrees)."""
        mb.box(self.at(u, out, z), size, mat, rotation=(0, tilt, self.yaw))


def faces_of(width, depth):
    """The four walls of a box house centered on the origin: front, right, back, left (and their lengths)."""
    hx, hy = width / 2, depth / 2
    return [(Wall((0, -hy, 0), 0), width), (Wall((hx, 0, 0), 90), depth), (Wall((0, hy, 0), 180), width),
            (Wall((-hx, 0, 0), -90), depth)]


def framed_wall(mb, wall, length, z0, z1, mid, posts, openings, m):
    """
    Half-timbering on a plastered wall: posts at the u positions `posts`, a sill, a top plate, a rail at
    `mid`, and braces in the panels without an opening. `openings` are (u, width, bottom, top).
    """
    oak = m["Oak"]
    for u in posts:
        wall.box(mb, u, 0.05, (z0 + z1) / 2, (0.2, 0.12, z1 - z0), oak)
    wall.box(mb, 0, 0.06, z0 + 0.08, (length + 0.1, 0.14, 0.16), oak)
    wall.box(mb, 0, 0.06, z1 - 0.08, (length + 0.1, 0.14, 0.18), oak)
    for k, (a, b) in enumerate(zip(posts, posts[1:])):
        inside = [o for o in openings if a < o[0] < b]
        door = any(o[2] < mid for o in inside)
        if not door:
            wall.box(mb, (a + b) / 2, 0.05, mid, (b - a, 0.12, 0.12), oak)
        for lo, hi in ((z0 + 0.16, mid - 0.06), (mid + 0.06, z1 - 0.17)):
            if any(o[2] < hi and o[3] > lo for o in inside):
                continue
            span, rise = b - a - 0.2, hi - lo
            angle = math.degrees(math.atan2(rise, span)) * (1 if k % 2 else -1)
            wall.box(mb, (a + b) / 2, 0.04, (lo + hi) / 2, (math.hypot(span, rise), 0.1, 0.12), oak, tilt=angle)


def shuttered_window(mb, wall, u, z, m, width=0.8, height=0.9, flowers=None):
    """A glowing window in an oak frame with a mullion cross, green shutters folded open and a flower box."""
    oak = m["Oak"]
    wall.box(mb, u, 0.07, z, (width + 0.18, 0.1, height + 0.18), oak)
    wall.box(mb, u, 0.1, z, (width, 0.04, height), m["WindowGlow"])
    wall.box(mb, u, 0.14, z, (0.06, 0.05, height), oak)
    wall.box(mb, u, 0.14, z, (width, 0.05, 0.06), oak)
    for side in (-1, 1):
        cu = u + side * (width / 2 + width / 4 + 0.1)
        wall.box(mb, cu, 0.09, z, (width / 2, 0.05, height + 0.05), m["Shutter"])
        for dz in (-height * 0.3, height * 0.3):
            wall.box(mb, cu, 0.12, z + dz, (width / 2 - 0.04, 0.03, 0.06), oak)
    sill = z - height / 2 - 0.14
    wall.box(mb, u, 0.17, sill, (width + 0.3, 0.26, 0.18), m["OakLight"])
    if flowers is not None:
        for i in range(5):
            fu = u - width / 2 + width * i / 4
            mb.sphere(wall.at(fu, 0.18, sill + 0.15), 0.11, m["Leaves"] if i % 2 else m["LeavesDark"], segments=6, rings=4)
            if i % 2 == 0:
                mb.sphere(wall.at(fu + flowers.uniform(-0.05, 0.05), 0.26, sill + 0.24), 0.05, m["Blossom"], segments=5, rings=3)


def plank_door(mb, wall, u, z0, m, width=1.1, height=2.0):
    """A door of boards with iron hinges and a ring, in an oak frame, under a little pent roof."""
    oak = m["Oak"]
    wall.box(mb, u, 0.06, z0 + height / 2 + 0.05, (width + 0.24, 0.12, height + 0.1), oak)
    boards = 5
    for i in range(boards):
        bu = u - width / 2 + width * (i + 0.5) / boards
        wall.box(mb, bu, 0.1, z0 + height / 2, (width / boards - 0.02, 0.05, height - 0.04), m["Firewood"] if i % 2 else m["OakLight"])
    for dz in (0.35, height - 0.35):
        wall.box(mb, u - width * 0.1, 0.14, z0 + dz, (width * 0.75, 0.03, 0.07), m["Iron"])
    mb.sphere(wall.at(u + width * 0.3, 0.16, z0 + 1.0), 0.05, m["Iron"], segments=6, rings=4)
    wall.box(mb, u, 0.35, z0 - 0.06, (width + 0.6, 0.7, 0.14), m["StoneDark"])
    # The pent roof on two brackets.
    for side in (-1, 1):
        wall.box(mb, u + side * (width / 2 + 0.15), 0.3, z0 + height + 0.25, (0.1, 0.6, 0.1), oak, tilt=0)
    roof_z = z0 + height + 0.45
    upper = [wall.at(u + du, 0.05, roof_z) for du in (-width / 2 - 0.45, 0, width / 2 + 0.45)]
    lower = [wall.at(u + du, 0.85, roof_z - 0.4) for du in (-width / 2 - 0.45, 0, width / 2 + 0.45)]
    out = wall.at(0, 1, 0) - wall.at(0, 0, 0)
    normal = (out.normalized() * 0.4 + Vector((0, 0, 0.8))).normalized()
    slab(mb, upper, lower, [normal] * 3, 0.12, m["ThatchDark"])


def stone_stack(mb, x, y, z0, z1, w, d, m, rng):
    """A stack of rough stone courses (a chimney), with a slab on top."""
    z = z0
    k = 0
    while z < z1:
        h = rng.uniform(0.28, 0.4)
        mat = m["Stone"] if k % 2 == 0 else m["StoneDark"]
        mb.box((x + rng.uniform(-0.03, 0.03), y + rng.uniform(-0.03, 0.03), z + h / 2), (w + rng.uniform(0, 0.06), d + rng.uniform(0, 0.06), h), mat)
        z += h
        k += 1
    mb.box((x, y, z + 0.06), (w + 0.18, d + 0.18, 0.12), m["StoneDark"])


# ---------------------------------------------------------------- the cottage


def cottage():
    """
    A Brightvale farmhouse: whitewashed walls in a frame of dark oak with braces on a field-stone
    plinth, a thick thatched roof with a rolled ridge and oak bargeboards, a stone chimney, shuttered
    windows with flower boxes, a plank door under a little pent roof, and a stack of firewood.
    """
    m = mats()
    rng = random.Random(7)
    mb = lp.MeshBuilder("Cottage")
    W, D, base, top = 6.0, 4.6, 0.25, 2.8
    hx, hy = W / 2, D / 2
    mb.box((0, 0, 0.0), (W + 0.4, D + 0.4, 0.5), m["Stone"])
    for i in range(14):
        a = 2 * math.pi * i / 14
        x, y = math.cos(a) * (hx + 0.2), math.sin(a) * (hy + 0.2)
        mb.box((max(-hx - 0.18, min(hx + 0.18, x)), max(-hy - 0.18, min(hy + 0.18, y)), 0.05), (0.5, 0.5, 0.35), m["StoneDark"],
               rotation=(0, 0, rng.uniform(0, 90)))
    mb.box((0, 0, (base + top) / 2), (W, D, top - base), m["Plaster"])
    walls = [wall for wall, _ in faces_of(W, D)]
    mid = 1.3
    door_u = 0.7
    layouts = [
        (walls[0], [-hx, -1.6, door_u - 0.75, door_u + 0.75, hx], [(-2.25, 0.8, 1.45, 2.35), (door_u, 1.1, base, 2.3)]),
        (walls[1], [-hy, 0, hy], [(-1.1, 0.7, 1.45, 2.3)]),
        (walls[2], [-hx, -1.0, 1.0, hx], [(-2.0, 0.8, 1.45, 2.35), (2.0, 0.8, 1.45, 2.35)]),
        (walls[3], [-hy, 0, hy], [(1.1, 0.7, 1.45, 2.3)]),
    ]
    for wall, posts, openings in layouts:
        length = posts[-1] - posts[0]
        framed_wall(mb, wall, length, base, top, mid, posts, openings, m)
        for u, width, bottom, top_ in openings:
            if bottom > mid:
                shuttered_window(mb, wall, u, (bottom + top_) / 2, m, width=width, height=top_ - bottom - 0.05, flowers=rng)
            else:
                plank_door(mb, wall, u, base, m, width=width, height=top_ - base - 0.1)

    # Thatch: thick courses, a rolled ridge, gables of plaster and oak.
    L, span = hx + 0.55, hy + 0.65
    ridge = lambda u: top + 2.05 - 0.08 * (u / L) ** 2
    eave = lambda u: top - 0.2
    shingled_roof(mb, -L, L, span, ridge, eave, 4, [m["Thatch"], m["ThatchLight"], m["ThatchDark"], m["Thatch"]], rng,
                  segments=8, thickness=0.2)
    beam(mb, [(u, 0, ridge(u) + 0.14) for u in [-L - 0.05 + (2 * L + 0.1) * i / 8 for i in range(9)]], 0.2, m["ThatchDark"], taper=1.0, sides=7)
    for x in (-hx, hx):
        apex = ridge(hx) - 0.2
        mb.add([(x, -hy, top), (x, hy, top), (x, 0, apex)], [(0, 1, 2)], m["Plaster"], double_sided=True)
        f = 1 if x > 0 else -1
        mb.box((x + f * 0.05, 0, (top + apex) / 2), (0.12, 0.2, apex - top), m["Oak"])
        mb.box((x + f * 0.05, 0, top + (apex - top) * 0.45), (0.12, D * 0.55, 0.14), m["Oak"])
        for side in (-1, 1):
            beam(mb, [(x + f * 0.58, side * span, eave(L) + 0.1), (x + f * 0.58, 0, ridge(L) + 0.2)], 0.09, m["Oak"], taper=1.0)

    stone_stack(mb, -hx + 0.95, hy - 0.45, top - 0.4, ridge(0) + 0.9, 0.75, 0.7, m, rng)
    # Firewood against the right gable wall.
    for row in range(3):
        for i in range(5 - row):
            y = -1.2 + 0.24 * i + 0.12 * row
            z = 0.12 + 0.2 * row
            mb.frustum((hx + 0.1, y, z), (hx + 0.75, y, z), 0.11, 0.11, m["Firewood"] if (i + row) % 2 else m["OakLight"], sides=6)
    return mb.finish()


# ---------------------------------------------------------------- the inn


def inn():
    """
    The Muddy Boot, the inn on Mirewatch's square: a long hall of dark timber under a swaybacked roof of
    red shingles, horned bargeboards with lanterns at both gables, two dormers, a stone chimney, glowing
    windows, a porch over the double door and a hanging sign with a muddy boot on it.
    """
    m = mats()
    rng = random.Random(23)
    mb = lp.MeshBuilder("Longhouse")
    W, D, base, top = 11.0, 6.5, 0.3, 3.4
    hx, hy = W / 2, D / 2
    mb.box((0, 0, base / 2 - 0.15), (W + 0.6, D + 0.6, base + 0.3), m["Plinth"])
    mb.box((0, 0, (base + top) / 2), (W, D, top - base), m["Planks"])
    plank_wall(mb, -hx, hx, hy, base, top, m, rng, facing=1)
    for a0, a1 in ((-hx, -1.3), (1.3, hx)):
        plank_wall(mb, a0, a1, -hy, base, top, m, rng)
    for x, f in ((-hx, -1), (hx, 1)):
        plank_wall(mb, -hy, hy, x, base, top, m, rng, facing=f, along="y")
    for x in [-hx + i * W / 4 for i in range(5)]:
        for y in (-hy, hy):
            mb.box((x, y, (base + top) / 2 + 0.05), (0.36, 0.36, top - base + 0.1), m["Timber"])
    for y in (-hy, hy):
        mb.box((0, y, top - 0.1), (W + 0.25, 0.32, 0.28), m["Timber"])
        mb.box((0, y, base + 0.12), (W + 0.25, 0.3, 0.2), m["Timber"])
    for x in (-hx, hx):
        mb.box((x, 0, top - 0.1), (0.32, D + 0.25, 0.28), m["Timber"])
    for x in (-4.1, -2.1, 2.1, 4.1):
        window(mb, x, -hy, 1.95, m)
        window(mb, x, hy, 1.95, m, facing=1)
    for x, f in ((-hx, -1), (hx, 1)):
        for y in (-1.4, 1.4):
            mb.box((x + f * 0.06, y, 1.95), (0.08, 0.9, 1.1), m["Timber"])
            mb.box((x + f * 0.1, y, 1.95), (0.04, 0.7, 0.9), m["WindowGlow"])
            mb.box((x + f * 0.13, y, 1.95), (0.04, 0.06, 0.9), m["Timber"])

    # Roof: red shingles on a swaybacked ridge, eaves flicking up at the corners.
    L, span = hx + 0.7, hy + 0.8
    ridge = lambda u: top + 3.0 + 0.6 * (u / L) ** 2
    eave = lambda u: top - 0.2 + 0.35 * (u / L) ** 4
    shingled_roof(mb, -L, L, span, ridge, eave, 7, [m["RoofRed"], m["RoofRedLight"], m["RoofRedDark"]], rng, segments=14)
    beam(mb, [(u, 0, ridge(u) + 0.13) for u in [-L - 0.1 + (2 * L + 0.2) * i / 16 for i in range(17)]], 0.14, m["Timber"], taper=1.0)
    for x in (-hx, hx):
        apex = ridge(hx) - 0.4
        mb.add([(x, -hy, top), (x, hy, top), (x, 0, apex)], [(0, 1, 2)], m["PlanksDark"], double_sided=True)
        f = 1 if x > 0 else -1
        mb.box((x + f * 0.06, 0, top + 1.0), (0.08, 0.8, 0.9), m["Timber"])
        mb.box((x + f * 0.1, 0, top + 1.0), (0.04, 0.6, 0.7), m["WindowGlow"])
        ex = x + f * 0.72
        for side in (-1, 1):
            beam(mb, [(ex, side * span, eave(L) + 0.05), (ex, side * span * 0.5, (eave(L) + ridge(L)) / 2 + 0.1),
                      (ex, 0, ridge(L) + 0.2), (ex, -side * 0.4, ridge(L) + 0.85), (ex, -side * 0.6, ridge(L) + 1.15)],
                 0.14, m["Timber"], taper=0.5)
        lantern(mb, (ex, -0.62, ridge(L) + 0.95), m)

    # Two dormers on the front of the roof.
    for dx in (-3.0, 3.0):
        dz = top + 1.35
        mb.box((dx, -hy + 1.25, dz), (1.3, 1.4, 1.1), m["Planks"])
        window(mb, dx, -hy + 0.55, dz, m, width=0.55, height=0.6)
        turn = Matrix.Translation((dx, 0, 0)) @ Matrix.Rotation(math.radians(90), 4, "Z")
        shingled_roof(mb, -hy + 0.35, -hy + 2.2, 0.9, lambda u, dz=dz: dz + 1.05, lambda u, dz=dz: dz + 0.4, 2,
                      [m["RoofRedDark"], m["RoofRed"]], rng, matrix=turn, segments=3)
    stone_stack(mb, hx - 1.6, hy - 1.2, top + 0.5, ridge(hx - 1.6) + 0.9, 0.9, 0.8, m, rng)

    # The double door in an arch, a porch on posts with a little gabled roof, steps.
    door_y = -hy
    prism(mb, arch_outline(2.0, 1.9), door_y - 0.02, door_y - 0.12, m["Timber"], offset=(0, 0, base))
    for side in (-1, 1):
        prism(mb, [(0, 0), (0.78, 0), (0.78, 1.85), (0, 2.6)] if side > 0 else [(-0.78, 0), (0, 0), (0, 2.6), (-0.78, 1.85)],
              door_y - 0.1, door_y - 0.18, m["Door"], offset=(side * 0.02, 0, base))
        mb.sphere((side * 0.18, door_y - 0.22, base + 1.1), 0.06, m["Iron"], segments=6, rings=4)
    porch = door_y - 1.6
    for x in (-1.25, 1.25):
        mb.box((x, porch, (base + 3.05) / 2), (0.2, 0.2, 3.05 - base), m["Timber"])
        mb.box((x, (porch + door_y) / 2, 3.0), (0.18, -door_y + porch, 0.18), m["Timber"])
    mb.box((0, porch, 3.0), (2.8, 0.2, 0.2), m["Timber"])
    turn = Matrix.Rotation(math.radians(90), 4, "Z")
    shingled_roof(mb, porch - 0.4, door_y + 0.2, 1.65, lambda u: 4.1, lambda u: 3.05, 3,
                  [m["RoofRedLight"], m["RoofRed"], m["RoofRedDark"]], rng, matrix=turn, segments=3)
    for k, (h, depth) in enumerate(((0.12, 2.2), (0.24, 1.6))):
        mb.box((0, door_y - depth / 2, h / 2 + 0.05 * k), (3.0 - 0.5 * k, depth, h), m["StepStone"] if k else m["StepRim"])
    # The sign: hanging from an iron bracket on the porch's front beam, a boot on a board.
    sx = 1.25 + 0.15
    mb.box((sx + 0.55, porch - 0.05, 2.85), (1.2, 0.06, 0.06), m["Iron"])
    for dx in (0.15, 0.95):
        mb.box((sx + dx, porch - 0.05, 2.62), (0.03, 0.03, 0.42), m["Iron"])
    board = (sx + 0.55, porch - 0.05, 2.05)
    mb.box(board, (1.0, 0.08, 0.75), m["SignBoard"])
    mb.box((board[0], board[1], board[2] + 0.4), (1.06, 0.1, 0.06), m["Timber"])
    mb.box((board[0], board[1], board[2] - 0.4), (1.06, 0.1, 0.06), m["Timber"])
    bx, by, bz = board
    boot = [(-0.12, 0, 0.26), (0.08, 0, 0.26), (0.08, 0, -0.08), (0.32, 0, -0.14), (0.34, 0, -0.26), (-0.14, 0, -0.26)]
    for face in (-1, 1):
        mb.hull([(bx + x, by + face * 0.05 + face * dy, bz + z) for x, _, z in boot for dy in (0.0, 0.03)], m["Leather"])
    # Lanterns on the porch posts, benches and barrels by the walls.
    for x in (-1.25, 1.25):
        lantern(mb, (x, porch - 0.22, 2.7), m, drop=0.2)
    for x in (-4.0, 3.2):
        mb.box((x, door_y - 0.45, 0.45), (1.6, 0.4, 0.08), m["PorchPlanks"])
        for dx in (-0.65, 0.65):
            mb.box((x + dx, door_y - 0.45, 0.22), (0.1, 0.35, 0.44), m["Timber"])
    for x, y in ((-hx - 0.6, -1.8), (-hx - 0.6, -0.9), (hx + 0.6, 1.6)):
        mb.frustum((x, y, 0), (x, y, 0.95), 0.36, 0.36, m["PorchPlanks"], sides=10)
        for z in (0.15, 0.8):
            mb.frustum((x, y, z), (x, y, z + 0.06), 0.375, 0.375, m["Iron"], sides=10)
    for x in (-1.9, 1.9):
        planter(mb, x, door_y - 0.9, m, rng)
    return mb.finish()


BUILDINGS = {
    "GroveHouse": grove_house,
    "Cottage": cottage,
    "Longhouse": inn,
}
