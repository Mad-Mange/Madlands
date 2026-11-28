"""
Small symbols for the interface (what an NPC offers, shown over its head and on the maps), modelled
low-poly like everything else and rendered from the front to transparent PNGs.

    blender -b --factory-startup -P art/blender/icons.py -- [icon ...]

Writes client/Assets/Madlands/Art/Icons/<Name>.png (ArtImporter sets them up as interface textures).
"""

import math
import os
import random
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import items  # noqa: E402
import lowpoly as lp  # noqa: E402

SIZE = 128
# A dark rim around every icon, so it reads over bright grass and sky when drawn small.
OUTLINE = (0.07, 0.05, 0.03)
OUTLINE_PX = 4
OUT_DIR = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                                        "client", "Assets", "Madlands", "Art", "Icons"))


def goods():
    """General goods: a full sack tied at the neck."""
    mb = lp.MeshBuilder("IconGoods")
    canvas = lp.material("Canvas", "#d8c08a")
    rope = lp.material("Rope", "#7a5230")
    rng = random.Random(7)
    pts = []
    for _ in range(40):
        theta = rng.uniform(0, 2 * math.pi)
        z = rng.uniform(-1, 1)
        r = math.sqrt(1 - z * z)
        pts.append((0.42 * r * math.cos(theta), 0.42 * r * math.sin(theta), 0.4 + 0.36 * z))
    mb.hull(pts, canvas)
    mb.frustum((0, 0, 0.72), (0, 0, 0.84), 0.14, 0.1, canvas, sides=7)
    mb.frustum((0, 0, 0.8), (0, 0, 0.86), 0.12, 0.12, rope, sides=7)
    # The open top flares out.
    mb.frustum((0, 0, 0.86), (0, 0, 1.0), 0.1, 0.2, canvas, sides=7, cap_a=False)
    # A few coins in front: this is where you buy and sell.
    gold = lp.material("Gold", "#f0c040")
    for x, z in ((-0.3, 0.06), (-0.12, 0.05), (-0.22, 0.15)):
        mb.frustum((x, -0.4, z), (x, -0.43, z), 0.1, 0.1, gold, sides=10)
    return [mb.finish()]


def inn():
    """The inn: a wooden tankard, banded with iron, foaming over."""
    mb = lp.MeshBuilder("IconInn")
    wood = lp.material("Wood", "#9a6a3a")
    band = lp.material("Metal", "#6e737b")
    foam = lp.material("Foam", "#f6f0dc")
    mb.frustum((0, 0, 0), (0, 0, 0.8), 0.32, 0.3, wood, sides=10)
    for z in (0.12, 0.66):
        mb.frustum((0, 0, z), (0, 0, z + 0.07), 0.335, 0.33, band, sides=10)
    # Handle on the right: three bars bent around.
    mb.box((0.38, 0, 0.62), (0.16, 0.08, 0.08), wood)
    mb.box((0.46, 0, 0.4), (0.08, 0.08, 0.5), wood)
    mb.box((0.38, 0, 0.18), (0.16, 0.08, 0.08), wood)
    rng = random.Random(3)
    for x, z, r in ((-0.14, 0.86, 0.17), (0.12, 0.88, 0.16), (0.0, 0.94, 0.18), (0.22, 0.8, 0.12), (-0.26, 0.8, 0.11)):
        mb.sphere((x, rng.uniform(-0.05, 0.05), z), r, foam, segments=7, rings=5)
    return [mb.finish()]


def weapons():
    """The weaponsmith: two broad swords crossed over a round shield."""
    shield = items.buckler()
    shield.rotation_euler = (0, 0, math.radians(-90))  # its face (+X) toward the camera (-Y)
    shield.scale = (1.5, 1.5, 1.5)
    shield.location = (0, 0.25, -0.15)
    objects = [shield]
    for angle in (-42, 42):
        sword = items.shortsword()
        sword.scale = (2.0, 2.0, 1.0)
        rotation = Matrix.Rotation(math.radians(angle), 4, "Y")
        sword.rotation_euler = rotation.to_euler()
        # Cross the blades at their middle, not at the grips.
        sword.location = -(rotation @ Vector((0, 0, 0.36)))
        objects.append(sword)
    return objects


# (builder, camera yaw, camera pitch): yaw 0 looks at the front (-Y side).
ICONS = {
    "IconGoods": (goods, 20.0, 12.0),
    "IconInn": (inn, 18.0, 14.0),
    "IconWeapons": (weapons, 0.0, 0.0),
}


def home_stone():
    """A smooth slate stone with a bright little house rune."""
    mb = lp.MeshBuilder("HomeStone")
    stone = lp.material("HomeStoneSlate", "#697d87")
    rune = lp.material("HomeStoneRune", "#8de3d2")
    mb.sphere((0, 0, 0.48), (0.34, 0.16, 0.43), stone, segments=12, rings=9)
    # Face the rune towards the camera; its roof and doorway remain readable in a small slot.
    mb.box((-0.11, -0.165, 0.59), (0.24, 0.025, 0.045), rune, rotation=(0, -0.72, 0))
    mb.box((0.11, -0.165, 0.59), (0.24, 0.025, 0.045), rune, rotation=(0, 0.72, 0))
    mb.box((-0.14, -0.165, 0.42), (0.045, 0.025, 0.25), rune)
    mb.box((0.14, -0.165, 0.42), (0.045, 0.025, 0.25), rune)
    mb.box((0, -0.165, 0.30), (0.32, 0.025, 0.045), rune)
    mb.box((0, -0.17, 0.39), (0.055, 0.025, 0.16), rune)
    return [mb.finish()]


ICONS["ItemIcon6001"] = (home_stone, 0.0, 0.0)


def held_item(builder, shield=False):
    """Render the same model the character holds, tilted to fill a square inventory slot."""
    obj = builder()
    if shield:
        obj.rotation_euler = (0, 0, math.radians(-90))
    else:
        obj.rotation_euler = (0, math.radians(-35), 0)
    return [obj]


for name, builder, shield in (
    ("Shortsword", items.shortsword, False), ("Staff", items.staff, False),
    ("Cudgel", items.cudgel, False), ("Hatchet", items.hatchet, False),
    ("Cutlass", items.cutlass, False), ("Buckler", items.buckler, True),
    ("CrystalRod", items.crystal_rod, False), ("OrnateMace", items.ornate_mace, False),
    ("GreatAxe", items.great_axe, False), ("Cleaver", items.cleaver, False),
    ("KnightShield", items.knight_shield, True), ("WardenShield", items.warden_shield, True),
):
    ICONS["Item" + name] = (lambda builder=builder, shield=shield: held_item(builder, shield), 0.0, 0.0)


def potion(color, shape):
    """Opaque, faceted glass keeps the liquid readable even in a small inventory slot."""
    mb = lp.MeshBuilder("Potion")
    liquid = lp.material("PotionGlass", color)
    highlight = lp.material("BottleHighlight", "#c4e5df")
    cork = lp.material("Cork", "#ad8050")
    band = lp.material("BottleBand", "#55412d")
    if shape == "round":
        mb.sphere((0, 0, 0.40), (0.29, 0.20, 0.32), liquid, segments=10, rings=7)
        mb.frustum((0, 0, 0.63), (0, 0, 0.78), 0.12, 0.10, liquid, sides=8)
    elif shape == "square":
        mb.box((0, 0, 0.38), (0.48, 0.28, 0.52), liquid, top=(0.82, 0.82))
        mb.frustum((0, 0, 0.62), (0, 0, 0.78), 0.12, 0.10, liquid, sides=8)
    else:
        mb.frustum((0, 0, 0.09), (0, 0, 0.16), 0.17, 0.24, liquid, sides=8, squash=(1, 0.72))
        mb.frustum((0, 0, 0.16), (0, 0, 0.60), 0.24, 0.17, liquid, sides=8, squash=(1, 0.72))
        mb.frustum((0, 0, 0.60), (0, 0, 0.78), 0.10, 0.09, liquid, sides=8)
    mb.frustum((0, 0, 0.76), (0, 0, 0.80), 0.13, 0.13, band, sides=8)
    mb.frustum((0, 0, 0.80), (0, 0, 0.91), 0.085, 0.095, cork, sides=8)
    # A glint reads as glass at small sizes.
    mb.box((-0.12, -0.205, 0.45), (0.04, 0.02, 0.22), highlight, rotation=(0, -0.12, 0))
    return [mb.finish()]


def bread():
    mb = lp.MeshBuilder("Bread")
    crust = lp.material("Crust", "#bd7736")
    cut = lp.material("BreadCuts", "#f2cc82")
    mb.sphere((0, 0, 0.34), (0.38, 0.23, 0.24), crust, segments=10, rings=7)
    for x in (-0.18, 0, 0.18):
        mb.box((x, -0.205, 0.45), (0.055, 0.025, 0.15), cut, rotation=(0, 0.35, 0))
    return [mb.finish()]


def bowl():
    mb = lp.MeshBuilder("Stew")
    wood = lp.material("Bowl", "#855332")
    rim = lp.material("BowlRim", "#bc8a52")
    stew = lp.material("Stew", "#c98236")
    herb = lp.material("Herb", "#82a24a")
    mb.frustum((0, 0, 0.10), (0, 0, 0.52), 0.18, 0.38, wood, sides=10)
    mb.frustum((0, 0, 0.50), (0, 0, 0.55), 0.39, 0.39, rim, sides=10)
    mb.frustum((0, 0, 0.55), (0, 0, 0.56), 0.34, 0.34, stew, sides=10)
    for x, y in ((-0.14, -0.05), (0.08, -0.12), (0.03, 0.12)):
        mb.sphere((x, y, 0.57), 0.055, herb, segments=5, rings=3)
    return [mb.finish()]


def mushroom():
    mb = lp.MeshBuilder("Glowcap")
    stem = lp.material("MushroomStem", "#ded8bd")
    cap = lp.material("MushroomCap", "#85b8ae")
    spots = lp.material("MushroomSpots", "#d7f3ca")
    mb.frustum((0, 0, 0.05), (0, 0, 0.50), 0.13, 0.10, stem, sides=8)
    mb.sphere((0, 0, 0.53), (0.38, 0.27, 0.20), cap, segments=10, rings=6)
    for x, z in ((-0.18, 0.57), (0.08, 0.62), (0.23, 0.53)):
        mb.sphere((x, -0.22, z), (0.07, 0.03, 0.05), spots, segments=6, rings=4)
    return [mb.finish()]


def moonshard():
    mb = lp.MeshBuilder("Moonshard")
    dark = lp.material("ShardDark", "#426a96")
    light = lp.material("ShardLight", "#a6e7f0")
    mb.hull([(-0.26, 0, 0.27), (0.20, 0, 0.23), (0.13, 0, 0.74), (-0.02, 0, 0.98),
             (0, -0.18, 0.45), (0, 0.18, 0.45)], dark)
    mb.hull([(-0.26, -0.01, 0.27), (0, -0.18, 0.45), (-0.02, -0.01, 0.98)], light)
    return [mb.finish()]


def fang(charm=False, broken=False):
    """A curved ivory tooth; a cord and smaller beads make the quest charm distinct."""
    mb = lp.MeshBuilder("Fang")
    ivory = lp.material("Ivory", "#ead9aa")
    shade = lp.material("ToothBase", "#a8946b")
    cord = lp.material("Cord", "#705038")
    path = [((-0.17, 0, 0.16), 0.14), ((-0.19, 0, 0.34), 0.13), ((-0.12, 0, 0.54), 0.10)]
    if broken:
        path.append(((0.01, 0, 0.68), 0.055))
    else:
        path.extend([((0.03, 0, 0.72), 0.06), ((0.20, 0, 0.83), 0.008)])
    mb.tube(path, ivory, sides=8)
    mb.frustum((-0.17, 0, 0.16), (-0.19, 0, 0.25), 0.14, 0.14, shade, sides=8)
    if charm:
        mb.tube([((-0.25, 0.02, 0.22), 0.025), ((-0.36, 0.02, 0.42), 0.025),
                 ((-0.30, 0.02, 0.69), 0.025), ((0, 0.02, 0.88), 0.025),
                 ((0.28, 0.02, 0.69), 0.025)], cord, sides=6)
        for x, z in ((-0.33, 0.52), (-0.22, 0.75), (0.15, 0.78)):
            mb.sphere((x, -0.02, z), 0.055, ivory, segments=6, rings=4)
    return [mb.finish()]


def coin():
    mb = lp.MeshBuilder("Coin")
    edge = lp.material("CoinEdge", "#987032")
    gold = lp.material("CoinFace", "#e2b657")
    shine = lp.material("CoinMark", "#f4db91")
    mb.frustum((0, 0.04, 0.48), (0, -0.04, 0.48), 0.37, 0.37, edge, sides=12)
    mb.frustum((0, -0.045, 0.48), (0, -0.055, 0.48), 0.31, 0.31, gold, sides=12)
    mb.box((0, -0.065, 0.48), (0.07, 0.02, 0.37), shine)
    mb.box((0, -0.07, 0.48), (0.32, 0.02, 0.06), shine)
    return [mb.finish()]


def fishing_net():
    mb = lp.MeshBuilder("FishingNet")
    rope = lp.material("NetRope", "#c5ad75")
    dark = lp.material("NetEdge", "#796744")
    # A torn diamond mesh, with a heavier rope along the top.
    mb.tube([((-0.38, 0, 0.78), 0.035), ((0.0, 0, 0.88), 0.035), ((0.38, 0, 0.78), 0.035)], dark, sides=5)
    for x in (-0.30, -0.15, 0, 0.15, 0.30):
        end = max(0.16, 0.32 - abs(x) * 0.45)
        mb.tube([((x, 0, 0.80), 0.018), ((x + 0.07, 0, 0.48), 0.018), ((x, 0, end), 0.018)], rope, sides=5)
    for z in (0.64, 0.49, 0.34):
        width = 0.35 * (z - 0.1) / 0.7
        mb.tube([((-width, -0.01, z), 0.018), ((0, -0.01, z - 0.05), 0.018), ((width, -0.01, z), 0.018)], rope, sides=5)
    for x in (-0.3, 0.0, 0.3):
        mb.sphere((x, 0, 0.82), 0.055, lp.material("NetFloat", "#d8b98a"), segments=6, rings=4)
    return [mb.finish()]


def pearl():
    mb = lp.MeshBuilder("SingingPearl")
    shell = lp.material("Shell", "#768fa8")
    pearl_mat = lp.material("Pearl", "#f4e8db")
    glint = lp.material("PearlGlint", "#ffffff")
    mb.sphere((0, 0.06, 0.30), (0.36, 0.15, 0.19), shell, segments=10, rings=6)
    mb.sphere((0, -0.05, 0.47), 0.23, pearl_mat, segments=10, rings=7)
    mb.sphere((-0.08, -0.25, 0.57), 0.045, glint, segments=6, rings=4)
    return [mb.finish()]


def venom_sac():
    mb = lp.MeshBuilder("VenomSac")
    sac = lp.material("Venom", "#a4bd59")
    dark = lp.material("VenomDark", "#5a773c")
    shine = lp.material("VenomGlint", "#e5e9a1")
    mb.sphere((0, 0, 0.42), (0.29, 0.20, 0.36), sac, segments=9, rings=7)
    mb.frustum((0, 0, 0.70), (0, 0, 0.85), 0.10, 0.045, dark, sides=7)
    mb.sphere((-0.12, -0.18, 0.50), (0.055, 0.03, 0.11), shine, segments=6, rings=4)
    return [mb.finish()]


def pickaxe():
    mb = lp.MeshBuilder("Pickaxe")
    wood = lp.material("PickWood", "#98683c")
    steel = lp.material("PickSteel", "#aab3bc")
    dark = lp.material("PickBinding", "#594637")
    mb.tube([((-0.23, 0, 0.10), 0.045), ((0.12, 0, 0.74), 0.045)], wood, sides=7)
    mb.tube([((0.08, 0, 0.68), 0.065), ((0.15, 0, 0.80), 0.065)], dark, sides=7)
    mb.hull([(-0.30, 0, 0.69), (-0.12, 0, 0.84), (0.12, -0.06, 0.82),
             (0.12, 0.06, 0.82), (0.42, 0, 0.72), (0.29, 0, 0.87), (0.10, 0, 0.91)], steel)
    return [mb.finish()]


def shard_core():
    mb = lp.MeshBuilder("ShardCore")
    dark = lp.material("CoreOuter", "#55638f")
    light = lp.material("CoreLight", "#ade8e7")
    gold = lp.material("CoreBands", "#c9a352")
    mb.sphere((0, 0, 0.46), (0.30, 0.20, 0.32), dark, segments=8, rings=5)
    mb.sphere((0, -0.17, 0.46), (0.19, 0.07, 0.21), light, segments=8, rings=5)
    for z in (0.21, 0.72):
        mb.frustum((0, 0, z - 0.035), (0, 0, z + 0.035), 0.23, 0.23, gold, sides=8)
    return [mb.finish()]


def smoked_eel():
    mb = lp.MeshBuilder("SmokedEel")
    fish = lp.material("SmokedFish", "#b77843")
    dark = lp.material("FishFins", "#6f4935")
    eye = lp.material("FishEye", "#efdc9b")
    mb.tube([((-0.30, 0, 0.22), 0.09), ((-0.12, 0, 0.28), 0.15),
             ((0.12, 0, 0.46), 0.14), ((0.20, 0, 0.67), 0.10), ((0.14, 0, 0.78), 0.06)], fish, sides=8)
    mb.hull([(-0.31, 0, 0.22), (-0.46, 0, 0.34), (-0.43, 0, 0.12), (-0.27, 0.04, 0.22)], dark)
    mb.sphere((0.17, -0.09, 0.68), 0.035, eye, segments=6, rings=4)
    return [mb.finish()]


for icon, color, shape in (
    (4002, "#70b8cf", "tall"), (4003, "#dc4d46", "round"),
    (4004, "#498cdb", "round"), (4008, "#df9c3d", "square"),
    (4009, "#a66cdc", "square"), (4010, "#7aa857", "tall"),
    (4011, "#62bac6", "square"),
):
    ICONS["ItemIcon" + str(icon)] = (lambda color=color, shape=shape: potion(color, shape), 0.0, 0.0)

ICONS.update({
    "ItemIcon4001": (bread, 0.0, 0.0),
    "ItemIcon4005": (bowl, 0.0, 25.0),
    "ItemIcon4006": (inn, 18.0, 14.0),
    "ItemIcon3002": (moonshard, 0.0, 0.0),
    "ItemIcon3009": (mushroom, 0.0, 0.0),
    "ItemIcon2002": (lambda: fang(broken=True), 0.0, 0.0),
    "ItemIcon2004": (coin, 0.0, 0.0),
    "ItemIcon3001": (lambda: fang(charm=True), 0.0, 0.0),
    "ItemIcon3003": (fang, 0.0, 0.0),
    "ItemIcon3004": (fishing_net, 0.0, 0.0),
    "ItemIcon3005": (pearl, 0.0, 0.0),
    "ItemIcon3006": (venom_sac, 0.0, 0.0),
    "ItemIcon3007": (pickaxe, 0.0, 0.0),
    "ItemIcon3008": (shard_core, 0.0, 0.0),
    "ItemIcon4007": (smoked_eel, 0.0, 0.0),
})


def spell_icon(icon):
    """Small, deliberately distinct silhouettes for the player abilities, facing the camera."""
    mb = lp.MeshBuilder("SpellIcon")
    steel = lp.material("IconSteel", "#dce8ec")
    steel_dark = lp.material("IconSteelDark", "#8eabc4")
    gold = lp.material("IconGold", "#f4c95b")
    red = lp.material("IconRed", "#e75c4f")
    blue = lp.material("IconBlue", "#87d7f4")
    violet = lp.material("IconViolet", "#c6a3f5")
    green = lp.material("IconGreen", "#92d36e")
    brown = lp.material("IconBrown", "#99643c")

    def poly(points, mat, layer=0):
        mb.add([(x, -0.02 * layer, z) for x, z in points], [tuple(range(len(points)))], mat, double_sided=True)

    def line(a, b, width, mat, layer=1):
        dx, dz = b[0] - a[0], b[1] - a[1]
        length = math.hypot(dx, dz)
        sx, sz = -dz / length * width / 2, dx / length * width / 2
        poly([(a[0] + sx, a[1] + sz), (a[0] - sx, a[1] - sz),
              (b[0] - sx, b[1] - sz), (b[0] + sx, b[1] + sz)], mat, layer)

    def radial(cx, cz, outer, inner, points, mat, layer=0):
        poly([(cx + math.sin(math.pi * i / points) * (outer if i % 2 == 0 else inner),
               cz + math.cos(math.pi * i / points) * (outer if i % 2 == 0 else inner))
              for i in range(points * 2)], mat, layer)

    def ring(cx, cz, radius, mat, width=0.045, count=20, layer=0):
        for i in range(count):
            a, b = i * 2 * math.pi / count, (i + 1) * 2 * math.pi / count
            line((cx + math.sin(a) * radius, cz + math.cos(a) * radius),
                 (cx + math.sin(b) * radius, cz + math.cos(b) * radius), width, mat, layer)

    def sword():
        poly([(-0.23, 0.23), (0.17, 0.76), (0.32, 0.89), (0.29, 0.68), (-0.12, 0.14)], steel)
        line((-0.20, 0.21), (0.28, 0.82), 0.025, steel_dark, 1)
        line((-0.28, 0.12), (-0.06, 0.27), 0.065, gold, 2)
        line((-0.33, -0.04), (-0.19, 0.17), 0.075, brown, 2)

    def crystal(mat, x=0, z=0.42, scale=1, layer=0):
        poly([(x, z + 0.42 * scale), (x + 0.22 * scale, z + 0.09 * scale),
              (x + 0.13 * scale, z - 0.29 * scale), (x - 0.13 * scale, z - 0.29 * scale),
              (x - 0.22 * scale, z + 0.09 * scale)], mat, layer)
        poly([(x, z + 0.42 * scale), (x, z - 0.29 * scale), (x - 0.22 * scale, z + 0.09 * scale)], steel, layer + 1)

    def leaf():
        poly([(0, 0.02), (-0.29, 0.26), (-0.31, 0.52), (0, 0.89), (0.31, 0.52), (0.29, 0.26)], green)
        line((0, 0.05), (0, 0.79), 0.04, gold)
        for z in (0.30, 0.49):
            line((0, z), (-0.20, z + 0.14), 0.025, gold)
            line((0, z), (0.20, z + 0.14), 0.025, gold)

    def shield():
        poly([(-0.34, 0.82), (0.34, 0.82), (0.31, 0.35), (0, 0.04), (-0.31, 0.35)], gold)
        poly([(-0.25, 0.73), (0.25, 0.73), (0.22, 0.39), (0, 0.16), (-0.22, 0.39)], blue, 1)

    def bolt(mat, layer=0):
        poly([(0.04, 0.89), (-0.27, 0.43), (-0.07, 0.43), (-0.15, 0.06),
              (0.29, 0.57), (0.08, 0.57), (0.20, 0.89)], mat, layer)

    if icon in (1, 100):
        sword()
        if icon == 100: radial(0.22, 0.72, 0.24, 0.07, 6, gold, 2)
    elif icon in (101, 102):
        for k in range(3):
            x = -0.24 + k * 0.22
            poly([(x - 0.08, 0.78), (x + 0.05, 0.85), (x + 0.12, 0.30), (x + 0.04, 0.08)], red if icon == 101 else steel)
        if icon == 102: line((-0.30, 0.22), (0.32, 0.22), 0.065, gold, 2)
    elif icon == 103:
        poly([(-0.35, 0.22), (-0.26, 0.65), (0.18, 0.70), (0.37, 0.85), (0.29, 0.35), (-0.15, 0.35)], gold)
        line((-0.21, 0.34), (-0.25, 0.13), 0.08, brown, 2)
        for x in (0.40, 0.49): line((x, 0.40), (x + 0.04, 0.65), 0.025, steel)
    elif icon == 104:
        bolt(gold)
        for x in (-0.32, 0.32): line((x, 0.18), (x * 1.35, 0.06), 0.05, steel)
    elif icon in (200, 201, 202, 204):
        if icon == 202:
            radial(0, 0.43, 0.46, 0.13, 8, blue)
            crystal(steel, scale=0.50, layer=1)
        elif icon == 204:
            poly([(-0.32, 0.12), (-0.40, 0.38), (-0.24, 0.71), (-0.12, 0.48),
                  (0.05, 0.93), (0.28, 0.59), (0.35, 0.27), (0.19, 0.10)], red)
            crystal(gold, z=0.36, scale=0.65, layer=1)
        else:
            crystal(violet if icon == 200 else blue)
            if icon == 200:
                line((-0.42, 0.19), (-0.23, 0.34), 0.04, violet)
                line((-0.40, 0.36), (-0.27, 0.46), 0.03, steel)
            else:
                for a in range(0, 360, 60):
                    t = math.radians(a)
                    line((0, 0.43), (math.cos(t) * 0.44, 0.43 + math.sin(t) * 0.44), 0.03, steel, 2)
    elif icon == 203:
        radial(0, 0.44, 0.45, 0.19, 8, violet)
        bolt(gold, layer=1)
    elif icon == 205:
        ring(0, 0.44, 0.31, violet, 0.05)
        poly([(-0.34, 0.44), (-0.20, 0.60), (0, 0.67), (0.20, 0.60), (0.34, 0.44),
              (0.20, 0.30), (0, 0.23), (-0.20, 0.30)], steel, 1)
        radial(0, 0.44, 0.13, 0.08, 8, violet, 2)
    elif icon in (300, 301, 303, 304):
        if icon == 303: shield()
        elif icon == 304:
            ring(0, 0.46, 0.37, gold, 0.06)
            radial(0, 0.46, 0.30, 0.11, 8, steel, 1)
        elif icon == 301:
            radial(0, 0.45, 0.42, 0.14, 6, gold)
            bolt(steel, layer=1)
        else:
            radial(0, 0.45, 0.43, 0.17, 8, gold)
            line((-0.18, 0.45), (0.18, 0.45), 0.12, steel, 2)
            line((0, 0.27), (0, 0.63), 0.12, steel, 2)
    elif icon in (302, 305):
        leaf()
        if icon == 302:
            line((-0.34, 0.19), (-0.14, 0.19), 0.06, steel, 2)
            line((-0.24, 0.09), (-0.24, 0.29), 0.06, steel, 2)
        else:
            radial(0.16, 0.25, 0.16, 0.13, 8, red, 2)
    return [mb.finish()]


for spell_id in (1, 100, 101, 102, 103, 104, 200, 201, 202, 203, 204, 205, 300, 301, 302, 303, 304, 305):
    ICONS["SpellIcon" + str(spell_id)] = (lambda spell_id=spell_id: spell_icon(spell_id), 0.0, 0.0)


def clothing_icon(icon):
    """Starter clothes and bags as clear, front-facing silhouettes."""
    mb = lp.MeshBuilder("ClothingIcon")
    cloth_colors = {1101: "#816c4a", 1102: "#6585a0", 1103: "#ded5ae", 1301: "#806951",
                    1302: "#785339", 1303: "#d3c399", 5001: "#c7ad7b", 5002: "#8e7154",
                    5003: "#566f55", 5004: "#b29760"}
    cloth = lp.material("IconCloth", cloth_colors[icon])
    dark = lp.material("IconTrim", "#594535")
    light = lp.material("IconStitch", "#eed39a")
    metal = lp.material("IconBuckle", "#e1b855")

    def poly(points, mat, layer=0):
        mb.add([(x, -0.025 * layer, z) for x, z in points], [tuple(range(len(points)))], mat, double_sided=True)

    def rect(x, z, w, h, mat, layer=1):
        poly([(x - w/2, z - h/2), (x + w/2, z - h/2), (x + w/2, z + h/2), (x - w/2, z + h/2)], mat, layer)

    if icon in (1101, 1102, 1103):
        hem = 0.48 if icon == 1101 else 0.67
        poly([(-0.15, 0.90), (-0.33, 0.86), (-0.48, 0.65), (-0.34, 0.53),
              (-0.25, 0.62), (-hem/2, 0.08), (hem/2, 0.08), (0.25, 0.62),
              (0.34, 0.53), (0.48, 0.65), (0.33, 0.86), (0.15, 0.90), (0, 0.78)], cloth)
        poly([(-0.15, 0.90), (0, 0.76), (0.15, 0.90), (0.10, 0.90), (0, 0.83), (-0.10, 0.90)], light, 1)
        rect(0, 0.46, 0.48, 0.06, dark)
        rect(0, 0.46, 0.09, 0.09, metal, 2)
        if icon != 1101:
            rect(0, 0.23, 0.06, 0.31, light)
    elif icon == 1301:
        poly([(-0.29, 0.86), (0.29, 0.86), (0.27, 0.50), (0.34, 0.08), (0.08, 0.08),
              (0, 0.47), (-0.08, 0.08), (-0.34, 0.08), (-0.27, 0.50)], cloth)
        rect(0, 0.80, 0.57, 0.08, dark)
        rect(0, 0.80, 0.08, 0.08, metal, 2)
    elif icon == 1302:
        poly([(-0.20, 0.90), (0.17, 0.90), (0.17, 0.45), (0.36, 0.34), (0.42, 0.12),
              (-0.27, 0.12), (-0.26, 0.24), (-0.20, 0.39)], cloth)
        rect(-0.015, 0.79, 0.38, 0.10, dark)
        rect(0.07, 0.13, 0.69, 0.08, dark)
        for z in (0.62, 0.52, 0.42): rect(-0.02, z, 0.20, 0.025, light)
    elif icon == 1303:
        poly([(-0.24, 0.10), (0.19, 0.10), (0.22, 0.37), (0.36, 0.48), (0.34, 0.59),
              (0.22, 0.57), (0.19, 0.82), (0.10, 0.88), (0.04, 0.68), (-0.02, 0.92),
              (-0.12, 0.88), (-0.14, 0.67), (-0.22, 0.82), (-0.30, 0.76), (-0.28, 0.48)], cloth)
        rect(-0.03, 0.18, 0.47, 0.10, dark)
        rect(-0.03, 0.18, 0.10, 0.10, metal, 2)
    else:
        if icon == 5001:
            poly([(-0.25, 0.12), (-0.34, 0.36), (-0.25, 0.66), (-0.15, 0.75),
                  (-0.22, 0.90), (0.22, 0.90), (0.15, 0.75), (0.25, 0.66), (0.34, 0.36), (0.25, 0.12)], cloth)
            rect(0, 0.73, 0.37, 0.06, dark)
            rect(0.12, 0.64, 0.05, 0.18, light)
        else:
            poly([(-0.32, 0.12), (-0.36, 0.28), (-0.32, 0.70), (-0.22, 0.83),
                  (0.22, 0.83), (0.32, 0.70), (0.36, 0.28), (0.32, 0.12)], cloth)
            poly([(-0.30, 0.75), (0.30, 0.75), (0.27, 0.52), (0, 0.45), (-0.27, 0.52)], dark, 1)
            rect(0, 0.48, 0.08, 0.18, light, 2)
            rect(0, 0.45, 0.12, 0.10, metal, 3)
            if icon == 5003:
                rect(0, 0.27, 0.34, 0.16, dark, 1)
                rect(0, 0.27, 0.22, 0.08, cloth, 2)
            if icon == 5004:
                for x in (-0.20, 0.0, 0.20): rect(x, 0.29, 0.025, 0.27, light)
                for z in (0.20, 0.30, 0.40): rect(0, z, 0.58, 0.025, light)
    return [mb.finish()]


for item_icon in (1101, 1102, 1103, 1301, 1302, 1303, 5001, 5002, 5003, 5004):
    ICONS["ItemIcon" + str(item_icon)] = (lambda item_icon=item_icon: clothing_icon(item_icon), 0.0, 0.0)


def render(name):
    builder, yaw, pitch = ICONS[name]
    lp.reset_scene()
    scene = bpy.context.scene
    objects = builder()
    bpy.context.view_layer.update()

    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_object_outline = True
    scene.display.shading.object_outline_color = (0.05, 0.035, 0.02)
    scene.display.render_aa = "16"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.exposure = 0.45
    scene.render.film_transparent = True
    scene.render.resolution_x = SIZE
    scene.render.resolution_y = SIZE
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"

    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        for corner in obj.bound_box:
            p = obj.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
    center = (lo + hi) / 2
    size = max(hi.x - lo.x, hi.z - lo.z, 0.1)
    cam_data = bpy.data.cameras.new("IconCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = size * 1.3  # room for the outline
    cam = bpy.data.objects.new("IconCam", cam_data)
    scene.collection.objects.link(cam)
    y, p = math.radians(yaw), math.radians(pitch)
    direction = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
    cam.location = center + direction * 10
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam

    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name + ".png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    add_outline(path)
    print("[icons] " + path)


def add_outline(path):
    """Puts a dark rim of OUTLINE_PX pixels around the icon's shape."""
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    alpha = px[:, :, 3]
    grown = alpha.copy()
    for dy in range(-OUTLINE_PX, OUTLINE_PX + 1):
        for dx in range(-OUTLINE_PX, OUTLINE_PX + 1):
            if dx * dx + dy * dy <= OUTLINE_PX * OUTLINE_PX:
                grown = np.maximum(grown, np.roll(np.roll(alpha, dy, 0), dx, 1))
    a = alpha[:, :, None]
    out_a = a + grown[:, :, None] * (1 - a)
    rgb = (px[:, :, :3] * a + np.array(OUTLINE, dtype=np.float32) * grown[:, :, None] * (1 - a)) / np.maximum(out_a, 1e-4)
    img.pixels = np.concatenate([rgb, out_a], axis=2).ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or list(ICONS)
    unknown = [n for n in names if n not in ICONS]
    if unknown:
        print("Unknown icons: " + ", ".join(unknown) + ". Known: " + ", ".join(ICONS))
        sys.exit(1)
    for name in names:
        render(name)


main()
