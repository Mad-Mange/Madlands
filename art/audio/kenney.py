"""
Imports the CC0 sound effects we use from Kenney's audio packs (https://kenney.nl) into
client/Assets/Madlands/Audio/Kenney, renamed to the clip names the client plays. Plain Python:

    python art/audio/kenney.py [folder with the downloaded zips]

Without a folder the packs are downloaded. CC0: no attribution is required, but CREDITS.md
thanks Kenney anyway.
"""

import io
import os
import sys
import urllib.request
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "client", "Assets", "Madlands", "Audio", "Kenney")

PACKS = {
    "impact-sounds": "https://kenney.nl/media/pages/assets/impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip",
    "rpg-audio": "https://kenney.nl/media/pages/assets/rpg-audio/8e99002d76-1677590336/kenney_rpg-audio.zip",
    "interface-sounds": "https://kenney.nl/media/pages/assets/interface-sounds/fa43c1dd4d-1677589452/kenney_interface-sounds.zip",
}


def numbered(prefix, count, start=0, digits=3, sep="_"):
    return ["%s%s%0*d.ogg" % (prefix, sep if sep else "", digits, i) for i in range(start, start + count)]


# our clip name prefix -> (pack, source file names); variants get _1, _2, ...
CLIPS = {
    "step_grass": ("impact-sounds", numbered("footstep_grass", 5)),
    "step_wood": ("impact-sounds", numbered("footstep_wood", 5)),
    "hit_flesh": ("impact-sounds", numbered("impactPunch_medium", 5)),
    "hit_flesh_heavy": ("impact-sounds", numbered("impactPunch_heavy", 5)),
    "hit_wood": ("impact-sounds", numbered("impactWood_medium", 5)),
    "hit_metal": ("impact-sounds", numbered("impactMetal_medium", 5)),
    "block": ("impact-sounds", numbered("impactPlate_medium", 5)),
    "fall": ("impact-sounds", numbered("impactSoft_heavy", 5)),
    "coins": ("rpg-audio", ["handleCoins.ogg", "handleCoins2.ogg"]),
    "cloth": ("rpg-audio", ["cloth1.ogg", "cloth2.ogg", "cloth3.ogg", "cloth4.ogg"]),
    "bag_open": ("rpg-audio", ["handleSmallLeather.ogg", "handleSmallLeather2.ogg"]),
    "bag_close": ("rpg-audio", ["clothBelt.ogg", "clothBelt2.ogg"]),
    "equip_weapon": ("rpg-audio", ["drawKnife1.ogg", "drawKnife2.ogg", "drawKnife3.ogg"]),
    "loot_item": ("rpg-audio", ["dropLeather.ogg", "beltHandle1.ogg", "beltHandle2.ogg"]),
    "book_open": ("rpg-audio", ["bookOpen.ogg"]),
    "book_close": ("rpg-audio", ["bookClose.ogg"]),
    "page": ("rpg-audio", ["bookFlip1.ogg", "bookFlip2.ogg", "bookFlip3.ogg"]),
    "ui_click": ("interface-sounds", numbered("click", 5, start=1)),
    "ui_open": ("interface-sounds", numbered("open", 4, start=1)),
    "ui_close": ("interface-sounds", numbered("close", 4, start=1)),
    "ui_error": ("interface-sounds", numbered("error", 2, start=1)),
    "ui_select": ("interface-sounds", numbered("select", 3, start=1)),
}


def open_pack(name, folder):
    if folder:
        return zipfile.ZipFile(os.path.join(folder, "kenney_%s.zip" % name))
    print("Downloading " + PACKS[name])
    with urllib.request.urlopen(PACKS[name]) as response:
        return zipfile.ZipFile(io.BytesIO(response.read()))


def main():
    folder = sys.argv[1] if len(sys.argv) > 1 else None
    os.makedirs(OUT, exist_ok=True)
    packs = {name: open_pack(name, folder) for name in PACKS}
    for prefix, (pack, files) in CLIPS.items():
        archive = packs[pack]
        members = {os.path.basename(m): m for m in archive.namelist()}
        for i, source in enumerate(files, start=1):
            target = "%s_%d.ogg" % (prefix, i) if len(files) > 1 else prefix + ".ogg"
            with archive.open(members[source]) as src, open(os.path.join(OUT, target), "wb") as dst:
                dst.write(src.read())
    license_member = next(m for m in packs["rpg-audio"].namelist() if m.endswith("License.txt"))
    with packs["rpg-audio"].open(license_member) as src, open(os.path.join(OUT, "LICENSE.txt"), "wb") as dst:
        dst.write(src.read())
    print("Imported %d clips into %s" % (sum(len(f) for _, f in CLIPS.values()), OUT))


if __name__ == "__main__":
    main()
