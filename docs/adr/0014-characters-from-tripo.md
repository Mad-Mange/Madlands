# ADR 0014 – Characters from Tripo

**Status:** Decided 2026-11-04. First model: the Mire Croaker (`art/blender/tripo.py`, `ArtImporter`).

## Context

The project owner wants livelier NPCs than the scripted models of ADR 0012 and plans to replace the NPC models step by
step with models made in [Tripo](https://www.tripo3d.ai), on a paid (Pro) plan. A Tripo export is one unrigged mesh in
a T-pose: about 72,000 triangles, four 4096x4096 PBR textures (about 18 MB in all) and no animations.

## Decision

**A Tripo model becomes an ordinary humanoid character.** One row in `tripo.MODELS` (source file, height in the game, the
joints read off front and side renders) and `build.py <Name>` makes the rest:

- Three levels of detail (8,000, 3,000 and 1,000 triangles) named `<Name>Body_LOD<n>`, for the client's LOD group.
- The humanoid bones under Unity's names and the two weapon grips, so the model plays the shared `HumanoidAnimations`
  clips and can hold weapons. Blender's automatic weights on the finest level, copied to the coarser ones.
- `Art/Characters/<Name>.fbx` with one plain material called `<Name>`, and `Art/Textures/<Name>_basecolor.png`,
  `_normal.png` and `_ms.png` (metallic in R, smoothness in A) at 1024x1024, the base color optionally recolored
  (`hue_shift`: the croakers' skin was too golden in the sun, and hues separate it from their leather). `ArtImporter` finds them by that name,
  makes URP Lit use them and computes tangents for the normal map.
- The build logs the triangle counts and how far test poses stretch the skin's edges, to catch bad weights early.

**The originals stay outside the repository**, in `MADLANDS_TRIPO_DIR` (default `D:\3D_Stuff\Characters`), because each
would add about 18 MB to Git LFS. The results are committed as with all art, so building the client never needs them.
A build without the original skips the model and keeps its committed files.

**Only models made on a paid plan go into this public repository.** Tripo's help pages disagree on who owns what a free
plan produces, so those models stay out. `CREDITS.md` lists where the models come from.

## Consequences

- This departs from ADR 0012's "no textures" for these models: they are detailed and painted, while the scripted world is
  plain. How well the two mix is judged in the game, one model at a time.
- A model with textures has nothing to recolor: `Tint` and the `Gear` pieces do not apply, so it cannot show worn armor.
  Each look is its own model and textures, about 4 MB in the repository.
- The joints are read by hand per model. The model must stand in a T-pose facing -Y with one mesh; anything else needs
  fixing first.
- Automatic weights treat cloth and tassels as stiff parts of whatever they hang from: a tassel hanging straight down in
  the T-pose points inward when the arm comes down.
- The scripted characters and their clips stay. Models that are not humanoid (beasts) are not covered yet: they would
  need their own skeleton and clips.
