"""
Held items. The origin is the grip (the middle of the fist), the weapon points along +Z (Unity +Y)
and its edge or face lies along X, so the client can put any of them in either hand.
Shields: the face looks along +X (outward on the left arm).
"""

import lowpoly as lp

METAL = "#c9cdd5"


def blade(mb, mat, base, length, width, thickness=0.012, curve=0.0, tip=0.25):
    """A flat blade from z = base, tapering to a point; `curve` bends it toward +X."""
    pts = []
    for z, w in ((base, width), (base + length * (1 - tip), width * 1.05), (base + length, 0.0)):
        bend = curve * ((z - base) / length) ** 2
        for y in (thickness / 2, -thickness / 2):
            pts += [(bend + w / 2, y, z), (bend - w / 2, y, z)]
    mb.hull(pts, mat)


def shortsword():
    mb = lp.MeshBuilder("Shortsword")
    leather = lp.material("Leather", "#5a3a22")
    metal = lp.material("Metal", METAL)
    dark = lp.material("DarkMetal", "#5b5f68")
    mb.frustum((0, 0, -0.08), (0, 0, 0.08), 0.02, 0.02, leather, sides=6)
    mb.sphere((0, 0, -0.1), 0.028, dark, segments=6, rings=4)
    mb.box((0, 0, 0.095), (0.2, 0.035, 0.03), dark)
    blade(mb, metal, 0.11, 0.6, 0.055)
    return mb.finish()


def cutlass():
    mb = lp.MeshBuilder("Cutlass")
    wood = lp.material("DarkWood", "#3e2a18")
    gold = lp.material("GoldMetal", "#d6a740")
    metal = lp.material("Metal", METAL)
    mb.frustum((0, 0, -0.08), (0, 0, 0.08), 0.02, 0.02, wood, sides=6)
    mb.box((0, 0, 0.095), (0.18, 0.04, 0.035), gold)
    mb.frustum((-0.06, 0, 0.09), (-0.07, 0, -0.07), 0.01, 0.01, gold, sides=4)
    blade(mb, metal, 0.11, 0.56, 0.07, curve=0.09, tip=0.3)
    return mb.finish()


def staff():
    mb = lp.MeshBuilder("Staff")
    wood = lp.material("Wood", "#7a5531")
    dark = lp.material("DarkWood", "#4e341e")
    mb.frustum((0, 0, -0.6), (0, 0, 1.2), 0.024, 0.03, wood, sides=6)
    for z in (0.15, 0.95):
        mb.frustum((0, 0, z), (0, 0, z + 0.05), 0.035, 0.035, dark, sides=6)
    mb.sphere((0, 0, 1.26), 0.065, dark, segments=6, rings=4)
    return mb.finish()


def crystal_rod():
    mb = lp.MeshBuilder("CrystalRod")
    dark = lp.material("DarkMetal", "#454a55")
    metal = lp.material("Metal", METAL)
    crystal = lp.material("CrystalGlow", "#30c8ec", emission=2.0)
    mb.frustum((0, 0, -0.5), (0, 0, 1.15), 0.022, 0.026, dark, sides=6)
    mb.frustum((0, 0, 1.14), (0, 0, 1.2), 0.055, 0.035, metal, sides=6)
    for x, y in ((0.045, 0), (-0.045, 0), (0, 0.045), (0, -0.045)):
        mb.box((x, y, 1.24), (0.015, 0.015, 0.1), metal)
    mb.hull([(0.05, 0, 1.36), (-0.05, 0, 1.36), (0, 0.05, 1.36), (0, -0.05, 1.36), (0, 0, 1.22), (0, 0, 1.56)], crystal)
    return mb.finish()


def cudgel():
    mb = lp.MeshBuilder("Cudgel")
    wood = lp.material("Wood", "#6e4b2a")
    dark = lp.material("DarkWood", "#4e341e")
    mb.frustum((0, 0, -0.1), (0, 0, 0.3), 0.022, 0.035, wood, sides=6)
    mb.frustum((0, 0, 0.28), (0, 0, 0.55), 0.05, 0.06, dark, sides=6)
    for x, y, z in ((0.05, 0.02, 0.38), (-0.04, 0.04, 0.47), (0.02, -0.05, 0.5)):
        mb.box((x, y, z), (0.03, 0.03, 0.03), wood)
    return mb.finish()


def ornate_mace():
    mb = lp.MeshBuilder("OrnateMace")
    gold = lp.material("GoldMetal", "#d6a740")
    metal = lp.material("Metal", METAL)
    mb.frustum((0, 0, -0.12), (0, 0, 0.42), 0.02, 0.022, gold, sides=6)
    mb.sphere((0, 0, -0.13), 0.03, gold, segments=6, rings=4)
    mb.sphere((0, 0, 0.5), 0.075, metal, segments=8, rings=5)
    for x, y in ((0.07, 0), (-0.07, 0), (0, 0.07), (0, -0.07)):
        mb.box((x, y, 0.5), (0.04 if x else 0.02, 0.04 if y else 0.02, 0.14), metal)
    mb.hull([(0, 0, 0.57), (0.02, 0, 0.57), (0, 0.02, 0.57), (0, 0, 0.66)], gold)
    return mb.finish()


def hatchet():
    mb = lp.MeshBuilder("Hatchet")
    wood = lp.material("Wood", "#7a5531")
    metal = lp.material("Metal", METAL)
    mb.frustum((0, 0, -0.12), (0, 0, 0.46), 0.02, 0.022, wood, sides=6)
    mb.hull([(0.0, 0.016, 0.36), (0.0, -0.016, 0.36), (0.0, 0.016, 0.47), (0.0, -0.016, 0.47),
             (0.16, 0.004, 0.32), (0.16, -0.004, 0.32), (0.17, 0.004, 0.5), (0.17, -0.004, 0.5)], metal)
    return mb.finish()


def great_axe():
    mb = lp.MeshBuilder("GreatAxe")
    wood = lp.material("Wood", "#6e4b2a")
    metal = lp.material("Metal", METAL)
    dark = lp.material("DarkMetal", "#5b5f68")
    mb.frustum((0, 0, -0.45), (0, 0, 1.05), 0.026, 0.03, wood, sides=6)
    for z in (-0.1, 0.25):
        mb.frustum((0, 0, z), (0, 0, z + 0.06), 0.034, 0.034, dark, sides=6)
    mb.hull([(0.0, 0.02, 0.72), (0.0, -0.02, 0.72), (0.0, 0.02, 0.98), (0.0, -0.02, 0.98),
             (0.3, 0.005, 0.62), (0.3, -0.005, 0.62), (0.32, 0.005, 1.08), (0.32, -0.005, 1.08)], metal)
    mb.hull([(0.0, 0.015, 0.8), (0.0, -0.015, 0.8), (0.0, 0.015, 0.9), (0.0, -0.015, 0.9), (-0.14, 0, 0.85)], dark)
    mb.hull([(0.02, 0.02, 1.03), (-0.02, 0.02, 1.03), (0.02, -0.02, 1.03), (-0.02, -0.02, 1.03), (0, 0, 1.18)], dark)
    return mb.finish()


def cleaver():
    """A two-handed cleaver of slag iron: a long wrapped haft and a broad slab blade with a glowing toothed edge."""
    mb = lp.MeshBuilder("Cleaver")
    wrap = lp.material("Leather", "#3a3230")
    iron = lp.material("DarkMetal", "#4a4146")
    rust = lp.material("Rust", "#6e3a2e")
    glow = lp.material("EmberGlow", "#ffb030", emission=3.0)
    mb.frustum((0, 0, -0.42), (0, 0, 0.95), 0.024, 0.026, wrap, sides=6)
    for z in (-0.2, 0.05, 0.3):
        mb.frustum((0, 0, z), (0, 0, z + 0.05), 0.032, 0.032, iron, sides=6)
    mb.frustum((0, 0.03, -0.47), (0, -0.03, -0.47), 0.05, 0.05, iron, sides=6, cap_a=False, cap_b=False)
    # The blade: a slab from the haft toward +X, wider at the tip, its edge along the far side.
    mb.hull([(x, y, z) for y in (0.018, -0.018) for x, z in ((-0.03, 0.5), (-0.03, 1.02), (0.3, 1.08), (0.34, 0.46))], iron)
    mb.hull([(x, y, z) for y in (0.022, -0.022) for x, z in ((-0.05, 0.98), (-0.05, 1.04), (0.3, 1.1), (0.3, 1.04))], rust)
    mb.box((0.12, 0, 0.92), (0.06, 0.05, 0.06), iron)
    for i in range(6):
        z = 0.5 + i * 0.105
        x = 0.335 - 0.04 * (i / 5)
        mb.hull([(x - 0.03, 0.012, z), (x - 0.03, -0.012, z), (x - 0.03, 0.012, z + 0.09), (x - 0.03, -0.012, z + 0.09),
                 (x + 0.06, 0.0, z + 0.04)], glow)
    return mb.finish()


def buckler():
    mb = lp.MeshBuilder("Buckler")
    wood = lp.material("Wood", "#7a5531")
    metal = lp.material("Metal", METAL)
    mb.frustum((0.03, 0, 0.1), (0.07, 0, 0.1), 0.27, 0.25, wood, sides=10)
    mb.frustum((0.025, 0, 0.1), (0.075, 0, 0.1), 0.285, 0.285, metal, sides=10, cap_a=False, cap_b=False)
    mb.sphere((0.08, 0, 0.1), (0.04, 0.07, 0.07), metal, segments=8, rings=4)
    return mb.finish()


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
}
