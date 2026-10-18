# Madlands art

All models are low-poly and made by Python scripts that run in [Blender](https://www.blender.org/) (5.x).
The decision and its conventions are in [ADR 0012](../docs/adr/0012-art-pipeline-blender.md).

## Building

```powershell
$blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
& $blender -b --factory-startup -P art/blender/build.py                              # everything
& $blender -b --factory-startup -P art/blender/build.py -- Korrin HumanoidAnimations # some assets
& $blender -b --factory-startup -P art/blender/build.py -- Korrin --preview builds/art   # + pictures
```

Each asset is written twice:

- `client/Assets/Madlands/Art/<folder>/<Name>.fbx`: what Unity uses (committed, Git LFS). Unity imports it with
  the rules in `client/Assets/Madlands/Editor/ArtImporter.cs`; nothing needs setting up by hand.
- `art/blend/<Name>.blend`: the same scene for Blender (not committed).

`--preview DIR` renders the model from four sides and, for animated assets, five moments of every clip.
In Unity, **Madlands > Art Report** logs what Unity made of each model (size, avatar, materials, clips, hand pose).

## Files

| File | What |
|---|---|
| `blender/build.py` | The asset list (name, Unity folder, builder) and the build entry point. |
| `blender/lowpoly.py` | Helpers: flat-colored materials, boxes, tubes, spheres, convex hulls, armatures, keyframed actions, FBX export. |
| `blender/humanoid.py` | The humanoid races (proportions, body, colors) and all humanoid animations. |
| `blender/preview.py` | The preview renders. |

## Conventions

- Meters, +Z up, the model faces -Y (Blender's front view), feet at the origin.
- Flat shading, no textures. Material names matter: the client recolors the gear regions of characters by name
  (`Skin`, `Hair`, `Eyes`, `Chest`, `Arms`, `Hands`, `Waist`, `Legs`, `Feet`). A name with `Metal` is metallic,
  with `Glow` emissive.
- Humanoids use Unity's humanoid bone names (`Hips`, `Spine`, `Chest`, `LeftUpperArm`, ...) in a T-pose, plus
  `MainHandGrip` and `OffHandGrip` where held items go. Rigged parts follow one bone each.
- Humanoid animations exist once, in `HumanoidAnimations` (Brightvale skeleton); Unity retargets them to every race.
- Animation rotations in the scripts are given about the armature's rest axes: X = the model's left, -Y = forward,
  Z = up, relative to the rest pose (`lowpoly.keyframe_action`). `lowpoly.mirror` gives the other side.
- Clip names the game knows: `Idle`, `Walk`, `Run`, `RunBack`, `StrafeLeft`, `StrafeRight`, `Fall`, `Jump`, `Sit`,
  `Attack1H`, `Attack2H`, `AttackUnarmed`, `CastLoop`, `CastRelease`, `Hit`, `Death` (creatures: `Attack`).
  Looping ones are listed in `ArtImporter.LoopingClips`.

## Editing a model by hand

The scripts are the source until someone refines a model in Blender. Then move its `.blend` to `art/source/`
(committed), export the FBX from there, and take the asset out of the generated list in `build.py`, so a build
never overwrites the hand-made version.

## Adding an asset

1. Write a function that builds it (return the root object: the armature for rigged models).
2. Add it to `ASSETS` in `build.py` with its Unity folder (`Characters` humanoid, `Creatures` generic rig,
   `Items`/`Props` static).
3. Build it, check the preview, then use it from `EntityVisuals` (units) or the map builder (props).
