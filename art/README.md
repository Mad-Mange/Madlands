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
| `blender/lowpoly.py` | Helpers: flat-colored materials, boxes, tubes, spheres, convex hulls, lofts through cross-sections (`loft`, `ring`), limbs along a path (`tube`), surfaces through rows of points (`grid`) and with a lining (`shell`), weights over several bones, armatures, keyframed actions, FBX export. |
| `blender/humanoid.py` | The humanoid races (proportions, body cross-sections, face, hair, beard, colors) and all humanoid animations. Stylized and heroic: big deltoids, a tapering torso, chunky fists and boots, a strong brow. |
| `blender/gear.py` | Worn gear, built into every race's model: helmets, hoods and hats wrapped around the head's hull, shoulder pads around the shoulder, cloaks, robe skirts, breastplates and aprons from the trunk's sections. Each piece is its own mesh (`GearHood`, `GearCloak` ...) the client hides until it is worn (`GearVisuals.cs`). |
| `blender/creatures.py` | Creatures with generic rigs and their own clips: wolf, frog, training dummy. |
| `blender/items.py` | Held items: origin at the grip, pointing along +Z, edge along X; shields face +X. |
| `blender/preview.py` | The preview renders. |
| `blender/buildings.py` | Detailed buildings built from shingle courses, planks and beams (`GroveHouse`: dark timber, a swaybacked slate roof, cross gables with horned bargeboards and lanterns). |
| `blender/icons.py` | Interface symbols (what an NPC is: general goods, innkeeper, weaponsmith), modelled like everything else and rendered to transparent PNGs with a dark rim in `client/Assets/Madlands/Art/Icons/`: `blender -b --factory-startup -P art/blender/icons.py -- [icon ...]`. |

Reference pictures for modelling go in `art/inspiration/` (git-ignored: screenshots of other games must never reach the
public repo). Use them for the feel, then build our own shapes.

## Conventions

- Meters, +Z up, the model faces -Y (Blender's front view), feet at the origin.
- Flat shading, no textures. Material names matter: the client recolors the gear regions of characters by name
  (`Skin`, `Hair`, `Eyes`, `Chest`, `Arms`, `Hands`, `Waist`, `Legs`, `Feet`). A region's `Trim` material
  (`ChestTrim`, `ArmsTrim`, `FeetTrim`: collars, cuffs, soles) gets a darker tone of the same color. `Hair` is the
  hair on the scalp, which the client hides under helmets and hoods; `Beard` (beard and brows) is tinted with it and
  stays visible. Some NPCs hide `Beard` and `BeardMetal` (the braid rings). `Legs` is the hips and thighs (hidden
  under a robe's skirt, which moves with them), `LegsLower` the knees and shins (tinted with `Legs`). A name with
  `Metal` is metallic, with `Glow` emissive.
- Gear pieces are meshes named `Gear` + the piece. Their `Gear` materials take the item's color (`GearTrim` a darker,
  `GearDark` a much darker tone: linings; `GearMetal`, `GearMetalTrim` the same, metallic); every other material keeps
  its color (`BuckleMetal` rivets and clasps, `Bone`, `LampGlow`). Cloth that bends between bones (skirts, cloaks,
  aprons) is a surface without thickness with a lining facing the other way (`shell(thickness=0)`): with a
  thickness, shearing tips the surface through its lining. A part below a joint must not follow the bone above it.
- Humanoids use Unity's humanoid bone names (`Hips`, `Spine`, `Chest`, `LeftUpperArm`, ...) in a T-pose, plus
  `MainHandGrip` and `OffHandGrip` where held items go, `HeadTop` at the top of the skull and `Cloak` behind the
  shoulders (a cloak's hem hangs from it; the client swings it back while running, out behind while sitting). Rigged parts follow one bone each. `ArtImporter` maps every humanoid bone by name and measures the
  avatar again when a race's proportions change.
- Humanoid animations exist once, in `HumanoidAnimations` (Brightvale skeleton); Unity retargets them to every race.
- Animation rotations in the scripts are given about the armature's rest axes: X = the model's left, -Y = forward,
  Z = up, relative to the rest pose (`lowpoly.keyframe_action`). `lowpoly.mirror` gives the other side.
- Clip names the game knows: `Idle`, `Walk`, `Run`, `RunBack`, `StrafeLeft`, `StrafeRight`, `Fall`, `Jump`, `Sit`,
  `Attack1H`, `Attack2H`, `AttackUnarmed`, `CastLoop`, `CastRelease`, `Hit`, `Death` (creatures: `Attack`).
  Looping ones are listed in `ArtImporter.LoopingClips`.

## Sound and music

The music, the ambience loops, the spell sounds, swings, splashes and jingles are synthesized by
`art/audio/*.py` (numpy, run inside Blender for its OGG encoder). Footsteps, impacts, coins, cloth, books and
interface clicks are CC0 sounds from Kenney ([CREDITS.md](../CREDITS.md)).

```powershell
& $blender -b --factory-startup -P art/audio/build.py                          # everything (about 25 s)
& $blender -b --factory-startup -P art/audio/build.py -- music_mirewatch cast_fire  # some
python art/audio/kenney.py                                                      # re-import the Kenney sounds
```

| File | What |
|---|---|
| `audio/synth.py` | The synthesizer: envelopes, noise shaped in the frequency domain, convolution reverb, instruments (pluck, pad, bell, flute, drone). |
| `audio/sounds.py` | Effects, jingles and ambience loops (`SOUNDS`: clip name -> folder, builder). |
| `audio/music.py` | The three music loops as data: key, tempo, chords, parts; melodies from a seeded random walk. |
| `audio/kenney.py` | Which Kenney sounds we use and the names they get. |

Clips land in `client/Assets/Madlands/Audio/{Sfx,Ambience,Music,Kenney}` as OGG (Git LFS). The client plays
them by name (`SoundSystem`); numbered variants (`step_grass_1` .. `_5`) are picked at random by their base
name. Music and ambience per area are set on the map (`MapArea.music` / `ambience`, from `HollowmereBuilder`).

## Editing a model by hand

The scripts are the source until someone refines a model in Blender. Then move its `.blend` to `art/source/`
(committed), export the FBX from there, and take the asset out of the generated list in `build.py`, so a build
never overwrites the hand-made version.

## Adding an asset

1. Write a function that builds it (return the root object: the armature for rigged models).
2. Add it to `ASSETS` in `build.py` with its Unity folder (`Characters` humanoid, `Creatures` generic rig,
   `Items`/`Props` static).
3. Build it, check the preview, then use it from `EntityVisuals` (units) or the map builder (props).
