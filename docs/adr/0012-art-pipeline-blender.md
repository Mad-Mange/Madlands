# ADR 0012 – Art pipeline: low-poly models from Blender scripts

**Status:** Decided and implemented 2026-10-18 (Phase 5: `art/`, `client/Assets/Madlands/Art`, `ArtImporter`, `UnitAnimator`).

## Context

Phase 5 needs real characters, creatures and props instead of Unity primitives. The repository is public, so
paid asset packs can never be committed, and the art should be easy to change while the game is still finding
its look. The project owner wants to use Blender and to start very low-poly, refining later.

## Decision

**Models are made by Python scripts that run in Blender**

- `art/blender/build.py` builds every asset (or the named ones) with `blender -b --factory-startup -P art/blender/build.py -- [asset ...]`.
  Each asset is a Python function (`humanoid.py`, ...) built from simple parts with the helpers in `lowpoly.py`.
- A build writes the FBX into the Unity project (`client/Assets/Madlands/Art/<Characters|Creatures|Items|Props>/`)
  and a `.blend` into `art/blend/` (not committed) for looking at and editing in Blender. `--preview DIR` renders
  pictures of each asset and its animations to check them without Unity.
- The FBX files are committed (Git LFS), so building the client never needs Blender.
- A model that someone refines by hand in Blender stops being generated: its `.blend` moves to `art/source/`
  (committed) and the build only exports it. Until then the script is the source.

**Look**

- Flat-shaded low-poly, no textures. Every part has a named material with a plain color. Rigged parts follow one
  bone each (rigid skinning).
- Material names are a contract with the client: the gear regions of characters (`Skin`, `Hair`, `Eyes`, `Chest`,
  `Arms`, `Hands`, `Waist`, `Legs`, `Feet`) are recolored at runtime by name. `Metal` in a name makes a material
  metallic, `Glow` makes it emissive.
- Interface symbols are small low-poly models too, rendered from the front to transparent PNGs with a dark rim
  (`art/blender/icons.py`), so they match the world.

**Conventions**

- Meters, +Z up, models face -Y (Blender's front view), feet at the origin.
- Humanoid races share one skeleton layout with Unity's humanoid bone names (`Hips`, `LeftUpperArm`, ...) in a
  T-pose, plus `MainHandGrip` and `OffHandGrip` bones for held items. Unity maps the avatar without manual setup.
- All humanoid clips are made once on the Brightvale Human skeleton and exported on their own
  (`HumanoidAnimations.fbx`); Unity's humanoid retargeting plays them on every race. Creatures have generic rigs
  with their own clips in their own file.
- Clips are in place (the server moves units). Names are fixed: `Idle`, `Walk`, `Run`, `RunBack`, `StrafeLeft`,
  `StrafeRight`, `Fall`, `Jump`, `Sit`, `Attack1H`, `Attack2H`, `AttackUnarmed`, `CastLoop`, `CastRelease`, `Hit`,
  `Death` (creatures: `Attack` instead of the three attacks). A missing clip is simply not played.

**Unity side**

- `ArtImporter` (an `AssetPostprocessor`) sets the import rules for everything under `Assets/Madlands/Art`:
  scale 1, Blender's axes baked, humanoid / generic / static by folder, URP Lit materials with the Blender color,
  clip names and looping, and the avatar of animation-only files copied from the model they were made on.
  It turns each model's root around so every model faces +Z, Unity's forward.
- `ArtCatalog` (`Resources/ArtCatalog.asset`, rebuilt by the importer and before builds) lists models and clips by
  name, so runtime code finds them without editor APIs.
- `EntityVisuals` maps the server's `displayId` to a model plus recolors and attachments; `UnitView` plays the
  animations through `UnitAnimator`, a small Playables graph (no Animator Controller assets): cross-faded
  locomotion picked from the movement flags and speed, an upper-body action layer (attacks, casts, hits) and a
  full-body layer (jump, death).
- Weapons hang on the grip bones and follow every swing.

## Consequences

- New art is a Python function plus one line in `build.py`; Unity needs no manual setup for it.
- Scripted models are easy to diff and to change everywhere at once (proportions, palette), but have a plain look.
  That is intended for now; individual models can move to hand editing without changing the pipeline.
- Any modular gear shows as recolored regions first; extra meshes (helmets, shoulders) attach to bones later.
- The player's model comes from the race (`races.json` `displayId`), sent in `Welcome` (protocol v7).
