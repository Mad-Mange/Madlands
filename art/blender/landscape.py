"""
Landscape pieces. Ground cover (Unity folder Ground) is scattered by the terrain itself and drawn GPU
instanced near the camera (HollowmereBuilder.PlantGroundCover): tufts of grass, flowers, ferns and pebbles
of a few dozen triangles each. A terrain detail may only have one material, so each piece's colors are
baked into its vertex colors under a single material (the Madlands/GroundCover shader draws both sides, lights
them like the flat ground they grow from and sways them in the wind). Props: rock formations that break the
skyline (cliffs, outcrops and a leaning spire of stacked slabs in bands of stone, grass on their ledges) and
water lilies for the shallows.
"""

import math
import random

import lowpoly as lp

# ---------------------------------------------------------------- ground cover


def _one_material(obj):
    """
    The colors of the piece's materials moved into vertex colors (sRGB, each face the color of its material),
    and a single material over all of it.
    """
    mesh = obj.data
    colors = [tuple(lp.linear_to_srgb(c) for c in m.diffuse_color[:3]) + (1.0,) for m in mesh.materials]
    layer = mesh.color_attributes.new("Color", "BYTE_COLOR", "CORNER")
    for poly in mesh.polygons:
        for loop in poly.loop_indices:
            layer.data[loop].color_srgb = colors[poly.material_index]
        poly.material_index = 0
    mesh.materials.clear()
    mesh.materials.append(lp.material("GroundCover", "#ffffff"))
    return obj


def _blade(mb, x, y, angle, height, width, lean, root, tip):
    """A blade of grass: a strip from the ground bending out along `angle`, in two tones, drooping at the tip."""
    dx, dy = math.cos(angle), math.sin(angle)
    sx, sy = -dy * width, dx * width
    mid = (x + dx * lean * height * 0.3, y + dy * lean * height * 0.3, height * 0.5)
    end = (x + dx * lean * height, y + dy * lean * height, height * (1.0 - 0.3 * lean * lean))
    mb.add([(x + sx, y + sy, -0.03), (x - sx, y - sy, -0.03), (mid[0] - sx * 0.7, mid[1] - sy * 0.7, mid[2]),
            (mid[0] + sx * 0.7, mid[1] + sy * 0.7, mid[2])], [(0, 1, 2, 3)], root)
    mb.add([(mid[0] + sx * 0.7, mid[1] + sy * 0.7, mid[2]), (mid[0] - sx * 0.7, mid[1] - sy * 0.7, mid[2]), end], [(0, 1, 2)], tip)


def _tuft(mb, rng, blades, height, root, tip, spread=0.12, width=0.03, lean=(0.15, 0.55)):
    """Blades fanning out of one spot, each leaning out by a share of its height in the `lean` range."""
    for k in range(blades):
        a = 2 * math.pi * k / blades + rng.uniform(-0.4, 0.4)
        r = rng.uniform(0.0, spread)
        _blade(mb, math.cos(a) * r, math.sin(a) * r, a, height * rng.uniform(0.65, 1.1), width * rng.uniform(0.8, 1.2), rng.uniform(*lean), root, tip)


def grass_tuft(name, seed, root, tip, blades=13, height=0.48):
    """A tuft of grass: blades dark at the root (the color of the ground under it), light at the tips."""
    def build():
        rng = random.Random(seed)
        mb = lp.MeshBuilder(name)
        _tuft(mb, rng, blades, height, lp.material(name + "Root", root), lp.material(name + "Tip", tip))
        return _one_material(mb.finish())
    return build


def grass_tall():
    """Tall grass going to seed: long arching blades and a few stems with pale seed heads."""
    rng = random.Random(23)
    mb = lp.MeshBuilder("GrassTall")
    root, tip = lp.material("GrassTallRoot", "#4f6e2e"), lp.material("GrassTallTip", "#a3ad5a")
    seed = lp.material("GrassSeed", "#c0a465")
    _tuft(mb, rng, 9, 0.85, root, tip, spread=0.16, width=0.032, lean=(0.05, 0.3))
    for k in range(4):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.02, 0.12)
        x, y = math.cos(a) * r, math.sin(a) * r
        h = rng.uniform(0.95, 1.2)
        lean = (math.cos(a) * 0.12, math.sin(a) * 0.12)
        top = (x + lean[0], y + lean[1], h)
        _blade(mb, x, y, a, h * 0.8, 0.008, 0.15, root, tip)
        # The seed head: a slim spindle along the top of the stem.
        sx, sy = -math.sin(a) * 0.022, math.cos(a) * 0.022
        lo = (x + lean[0] * 0.75, y + lean[1] * 0.75, h * 0.78)
        mb.add([lo, (top[0] + sx, top[1] + sy, h * 0.9), (top[0] + lean[0] * 0.2, top[1] + lean[1] * 0.2, h * 1.02),
                (top[0] - sx, top[1] - sy, h * 0.9)], [(0, 1, 2, 3)], seed)
    return _one_material(mb.finish())


def _star(mb, center, radius, points, inner, mat, cup=0.0, twist=0.0):
    """A flower head lying flat: a star of petals around its middle, cupped up by `cup` at the petal tips."""
    cx, cy, cz = center
    verts = [(cx, cy, cz - cup * 0.5)]
    for k in range(points * 2):
        a = twist + math.pi * k / points
        r = radius if k % 2 == 0 else radius * inner
        verts.append((cx + math.cos(a) * r, cy + math.sin(a) * r, cz + (cup if k % 2 == 0 else cup * 0.5)))
    n = points * 2
    mb.add(verts, [(0, 1 + k, 1 + (k + 1) % n) for k in range(n)], mat)


def flowers(name, seed, petal, middle, kind):
    """
    A clump of wild flowers over a little leafy tuft: white daisies (rayed stars with a yellow eye), yellow
    buttercups (cupped five-petaled stars) or blue bells hanging in spikes.
    """
    def build():
        rng = random.Random(seed)
        mb = lp.MeshBuilder(name)
        leaf_root, leaf_tip = lp.material(name + "Leaf", "#45682b"), lp.material(name + "LeafTip", "#6f9a42")
        petal_mat, middle_mat = lp.material(name + "Petal", petal), lp.material(name + "Middle", middle)
        _tuft(mb, rng, 6, 0.22, leaf_root, leaf_tip, spread=0.1, width=0.028)
        stems = 5 if kind != "bells" else 4
        for k in range(stems):
            a = 2 * math.pi * k / stems + rng.uniform(-0.5, 0.5)
            r = rng.uniform(0.04, 0.16)
            x, y = math.cos(a) * r, math.sin(a) * r
            h = rng.uniform(0.24, 0.38) if kind != "bells" else rng.uniform(0.38, 0.55)
            lean = 0.18
            top = (x + math.cos(a) * lean * h, y + math.sin(a) * lean * h, h)
            _blade(mb, x, y, a, h, 0.009, lean, leaf_root, leaf_tip)
            if kind == "daisy":
                _star(mb, (top[0], top[1], h + 0.005), 0.075, 8, 0.45, petal_mat, cup=0.01, twist=rng.uniform(0, 1))
                _star(mb, (top[0], top[1], h + 0.014), 0.026, 5, 0.8, middle_mat)
            elif kind == "cup":
                _star(mb, (top[0], top[1], h), 0.06, 5, 0.62, petal_mat, cup=0.025, twist=rng.uniform(0, 1))
                _star(mb, (top[0], top[1], h + 0.01), 0.018, 5, 0.8, middle_mat)
            else:
                # Bells up the top of the stem, smaller toward the tip, a bud at the end.
                for i in range(4):
                    f = i / 4
                    bz = h * (0.62 + 0.36 * f)
                    bx, by = x + (top[0] - x) * (0.62 + 0.36 * f), y + (top[1] - y) * (0.62 + 0.36 * f)
                    br = 0.042 * (1 - 0.45 * f)
                    _star(mb, (bx, by, bz), br, 5, 0.7, petal_mat if i % 2 == 0 else middle_mat, cup=-br * 0.8, twist=i * 0.6)
        return _one_material(mb.finish())
    return build


def fern():
    """A fern: fronds arching out of one crown, each a stem with leaflets in pairs, shrinking toward its tip."""
    rng = random.Random(31)
    mb = lp.MeshBuilder("Fern")
    dark, light = lp.material("FernDark", "#2f5526"), lp.material("FernLight", "#5c8c3a")
    for k in range(7):
        a = 2 * math.pi * k / 7 + rng.uniform(-0.3, 0.3)
        length = rng.uniform(0.6, 0.85)
        rise = rng.uniform(0.35, 0.5)
        dx, dy = math.cos(a), math.sin(a)
        sx, sy = -dy, dx
        steps = 6
        spine = []
        for i in range(steps + 1):
            f = i / steps
            out = length * f
            spine.append((dx * out, dy * out, rise * math.sin(f * math.pi * 0.85) * (1.0 - 0.3 * f) + 0.02))
        for i in range(steps):
            p, q = spine[i], spine[i + 1]
            w = 0.13 * math.sin((i + 0.6) / steps * math.pi) + 0.02
            mat = dark if i < steps // 2 else light
            for side in (1, -1):
                tip = ((p[0] + q[0]) / 2 + sx * w * side + dx * 0.04, (p[1] + q[1]) / 2 + sy * w * side + dy * 0.04, (p[2] + q[2]) / 2 - 0.03)
                mb.add([p, q, tip] if side > 0 else [q, p, tip], [(0, 1, 2)], mat)
    return _one_material(mb.finish())


def pebbles():
    """A few rounded stones half sunk in the ground."""
    rng = random.Random(41)
    mb = lp.MeshBuilder("Pebbles")
    tones = [lp.material(n, c) for n, c in (("Pebble", "#8f897d"), ("PebbleLight", "#aaa497"), ("PebbleDark", "#6c675f"))]
    for _ in range(5):
        a, d = rng.uniform(0, 2 * math.pi), rng.uniform(0.0, 0.3)
        cx, cy = math.cos(a) * d, math.sin(a) * d
        r = rng.uniform(0.05, 0.13)
        pts = []
        for _ in range(9):
            t, e = rng.uniform(0, 2 * math.pi), rng.uniform(-0.6, 1.0)
            pts.append((cx + math.cos(t) * math.cos(e) * r, cy + math.sin(t) * math.cos(e) * r * 0.8, max(-0.03, math.sin(e) * r * 0.55)))
        mb.hull(pts, rng.choice(tones))
    return _one_material(mb.finish())


GROUND = {
    "GrassTuft": grass_tuft("GrassTuft", 1, "#45682b", "#7ea64a"),
    "GrassTuftMeadow": grass_tuft("GrassTuftMeadow", 2, "#6c8b3b", "#b6c067", blades=10, height=0.38),
    "GrassTall": grass_tall,
    "FlowersWhite": flowers("FlowersWhite", 3, "#f2efe2", "#e8b830", "daisy"),
    "FlowersYellow": flowers("FlowersYellow", 4, "#f0c82a", "#c8901a", "cup"),
    "FlowersBlue": flowers("FlowersBlue", 5, "#6f74d8", "#5258b8", "bells"),
    "Fern": fern,
    "Pebbles": pebbles,
}

def lily_pads():
    """Water lilies floating in a loose cluster: round leaves notched to the middle, two flowers of pointed petals."""
    rng = random.Random(61)
    mb = lp.MeshBuilder("LilyPads")
    leaves = [lp.material("LilyLeaf", "#4c7a32"), lp.material("LilyLeafLight", "#6a9a42")]
    petal, middle = lp.material("LilyPetal", "#f3e9ef"), lp.material("LilyMiddle", "#e8b830")
    pads = []
    for _ in range(30):
        if len(pads) == 7:
            break
        a, d = rng.uniform(0, 2 * math.pi), rng.uniform(0.0, 0.9)
        x, y, r = math.cos(a) * d, math.sin(a) * d, rng.uniform(0.16, 0.32)
        if any(math.hypot(x - px, y - py) < (r + pr) * 0.85 for px, py, pr in pads):
            continue
        pads.append((x, y, r))
        notch = rng.uniform(0, 2 * math.pi)
        z = rng.uniform(0.0, 0.012)
        sides = 12
        rim = [(x + math.cos(notch + 0.35 + (2 * math.pi - 0.7) * k / sides) * r,
                y + math.sin(notch + 0.35 + (2 * math.pi - 0.7) * k / sides) * r, z) for k in range(sides + 1)]
        mb.add([(x, y, z)] + rim, [(0, 1 + k, 2 + k) for k in range(sides)], rng.choice(leaves))
    for x, y, r in pads[:2]:
        for layer, (size, lift) in enumerate(((0.11, 0.02), (0.075, 0.045))):
            ring = []
            for k in range(16):
                a = math.pi * k / 8 + layer * 0.2
                rr = size if k % 2 == 0 else size * 0.35
                ring.append((x + math.cos(a) * rr, y + math.sin(a) * rr, lift + (0.04 if k % 2 == 0 else 0.0)))
            mb.add([(x, y, lift)] + ring, [(0, 1 + k, 1 + (k + 1) % 16) for k in range(16)], petal, double_sided=True)
        mb.sphere((x, y, 0.06), 0.025, middle, segments=6, rings=3)
    return mb.finish()


# ---------------------------------------------------------------- rock formations

STONE = (("Stone", "#8f8b83"), ("StoneDark", "#74716a"), ("StoneWarm", "#9a8f80"))


def _slab(mb, rng, cx, cy, z0, z1, rx, ry, mat, dip, sides=7, bevel=0.18):
    """
    A slab of rock: a blocky prism (corners pushed out toward a box) with chamfered top edges, its top and
    bottom tilted along `dip` (meters per meter in x and y, the same for a whole formation), flat-shaded.
    """
    phase = rng.uniform(0, 2 * math.pi)
    ring = []
    for k in range(sides):
        a = phase + 2 * math.pi * k / sides + rng.uniform(-0.25, 0.25)
        c, s = math.cos(a), math.sin(a)
        f = rng.uniform(0.78, 1.08)
        ring.append((math.copysign(abs(c) ** 0.55, c) * rx * f, math.copysign(abs(s) ** 0.55, s) * ry * f))

    def tilt(x, y):
        return dip[0] * x + dip[1] * y
    pts = [(cx + x, cy + y, z0 + tilt(x, y)) for x, y in ring]
    pts += [(cx + x * 0.98, cy + y * 0.98, z1 - bevel + tilt(x, y)) for x, y in ring]
    inset = 1.0 - bevel / max(0.2, min(rx, ry))
    pts += [(cx + x * inset, cy + y * inset, z1 + tilt(x * inset, y * inset) + rng.uniform(-0.04, 0.04)) for x, y in ring]
    mb.hull(pts, mat)


def _ledges(obj, grass, moss, min_z, steep=0.72):
    """Grass on every face that looks up (the ledges and the top), moss on the low ones."""
    mesh = obj.data
    for mat in (grass, moss):
        if mat.name not in mesh.materials:
            mesh.materials.append(mat)
    names = [m.name for m in mesh.materials]
    for poly in mesh.polygons:
        if poly.normal.z > steep:
            poly.material_index = names.index(grass.name if poly.center.z > min_z else moss.name)
    return obj


def _formation(name, seed, width, depth, height, columns, slabs, taper, step_back, lean=0.0, rubble=4):
    """
    Rock in stacked strata: columns side by side across `width`, each a pile of slabs narrowing and stepping back
    as they rise (to `height`, the front facing -Y), the stone in bands of three tones, grass on every ledge,
    fallen blocks at the foot.
    """
    def build():
        rng = random.Random(seed)
        tones = [lp.material(n, c) for n, c in STONE]
        grass, moss = lp.material("CliffGrass", "#62803a"), lp.material("CliffMoss", "#56703a")
        mb = lp.MeshBuilder(name)
        # The strata dip a little, all the same way.
        a = rng.uniform(0, 2 * math.pi)
        dip = (math.cos(a) * 0.1, math.sin(a) * 0.1)
        x = -width / 2
        spans = [rng.uniform(0.8, 1.25) for _ in range(columns)]
        for i, span in enumerate(spans):
            w = width * span / sum(spans)
            cx = x + w / 2
            x += w
            # Taller columns in the middle, with a ragged skyline.
            top = height * (1.0 - 0.35 * abs(cx) / (width / 2 + 0.01)) * rng.uniform(0.75, 1.05)
            count = max(2, int(round(slabs * top / height)))
            z = -1.2
            band = rng.randrange(3)
            for k in range(count):
                f = k / max(1, count - 1)
                t = (top + 1.2) / count * rng.uniform(0.85, 1.15)
                rx = w * 0.68 * (1.0 - taper * f) * rng.uniform(0.85, 1.08)
                ry = depth * 0.5 * (1.0 - taper * 0.7 * f) * rng.uniform(0.9, 1.05)
                ox = cx + lean * (z + t) + rng.uniform(-0.12, 0.12) * w
                oy = step_back * depth * f + rng.uniform(-0.08, 0.08) * depth
                _slab(mb, rng, ox, oy, z, z + t, rx, ry, tones[(band + k) % 3], dip, sides=rng.choice((6, 7, 8)))
                z += t * 0.97
        for _ in range(rubble):
            a = rng.uniform(math.pi * 1.15, math.pi * 1.85)
            cx, cy = math.cos(a) * width * 0.45, math.sin(a) * depth * 0.75
            size = rng.uniform(0.3, 0.75)
            _slab(mb, rng, cx, cy, -0.3, size * 0.9, size, size * rng.uniform(0.7, 1.0), rng.choice(tones), dip, sides=6, bevel=size * 0.3)
        return _ledges(mb.finish(), grass, moss, height * 0.3)
    return build


PROPS = {
    "Cliff1": _formation("Cliff1", 51, 12.0, 5.0, 7.0, 4, 5, 0.22, 0.3),
    "Cliff2": _formation("Cliff2", 52, 10.0, 5.5, 9.5, 3, 6, 0.25, 0.35),
    "Outcrop1": _formation("Outcrop1", 53, 6.5, 5.0, 2.6, 3, 3, 0.3, 0.2, rubble=3),
    "Outcrop2": _formation("Outcrop2", 54, 4.8, 4.4, 3.8, 2, 3, 0.3, 0.15, rubble=3),
    "RockSpire": _formation("RockSpire", 57, 3.2, 3.0, 9.5, 1, 7, 0.4, 0.1, lean=0.08, rubble=5),
    "LilyPads": lily_pads,
}
