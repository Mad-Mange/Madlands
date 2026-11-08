# ADR 0014 – Characters from Tripo

**Status:** Decided 2026-11-04. First model: the Mire Croaker (`art/blender/tripo.py`, `ArtImporter`). Extended the same
day to the classes' own models, A-poses and models rigged by Tripo ([below](#the-classes-a-poses-and-tripos-rig)).

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

**The originals stay outside the repository**, in `MADLANDS_TRIPO_DIR` (default `D:\3D_Stuff\Characters\MadLands`), because each
would add about 18 MB to Git LFS. The results are committed as with all art, so building the client never needs them.
A build without the original skips the model and keeps its committed files.

**Only models made on a paid plan go into this public repository.** Tripo's help pages disagree on who owns what a free
plan produces, so those models stay out. `CREDITS.md` lists where the models come from.

## Consequences

- This departs from ADR 0012's "no textures" for these models: they are detailed and painted, while the scripted world is
  plain. How well the two mix is judged in the game, one model at a time.
- A model with textures has nothing to recolor: `Tint` and the `Gear` pieces do not apply, so it cannot show worn armor.
  Each look is its own model and textures, about 4 MB in the repository.
- The joints of an unrigged model are read by hand. The model must stand in a T-pose or an A-pose facing -Y with one
  mesh; anything else needs fixing first.
- Automatic weights treat cloth and tassels as stiff parts of whatever they hang from: a tassel hanging straight down in
  the T-pose points inward when the arm comes down.
- The scripted characters and their clips stay. Models that are not humanoid (beasts) are not covered yet: they would
  need their own skeleton and clips.

## The classes, A-poses and Tripo's rig

**A class can have its own model.** `displayId` on a class in `content/classes.json` wins over its race's: a player looks
like their class, whatever their race. No wire change, the display id already travels. Warrior, Shardmage and Warden show
the models `Warrior`, `Shardmage` and `KorrinWarden` (displays 3, 4 and 5); the scripted races stay for NPCs and as the
fallback of a class without a model.

- A race no longer changes how a player looks, only their stats: a Korrin warrior looks like the human `Warrior`, a human
  warden like the Korrin `KorrinWarden`. More looks per class and race can come later.
- Worn armor no longer shows on players, since a painted model has its armor in its textures. Held weapons and shields do.

**An A-pose is baked into a T-pose in the build**, because Unity's humanoid clips are measured from a T-pose and the weapon
grips assume one. The weights come from the A-pose, where arms are clear of the body, and bring the arms back down in
the game. Hands hanging at the hip need help, though, as automatic weights tie them and the belt together:

- `arm_reach` and `trunk_x`: nothing beyond an arm's reach follows it, and a hand and forearm are weighted by where they lie
  along the arm, because heat also reaches them from further up and a bent elbow would pull them into long spikes.
- `skirt`: cloth hangs from the hips and follows the thighs towards the hem by how far to the side it hangs, so a long
  stride does not tear it apart between the legs. What follows an arm stays out of it.

- A cloak hanging from a shoulder bone swings out on that long lever when the bone turns (the Warden's did), so cloth
  further down hangs from the chest (`SHOULDER_HANGS`).
- `_free_arms`: where a sleeve or hand has grown into the clothes beside it, weights cannot part them, so the faces between
  what follows an arm and what does not are removed below the armpit. Each side keeps its surface; the small gap lies
  where they touched.

The build logs how many faces were cut, how many vertices follow both an arm and a leg (it must be 0) and how far test
poses stretch the skin.

**A model rigged by Tripo** (`"rigged": True`, Tripo's Mixamo skeleton) brings its joints and weights, so nothing is read by
hand. Its bones get our names, Mixamo's upper two spine bones become our `Chest`, and the arms are baked straight out. It
keeps its finger bones: a hand closes round what it holds (`DisplayModel.CloseHand`, no clip moves the fingers), with the
weapon's grip in the palm. All three classes and the Mire Croaker are rigged this way.

- The Mire Croaker (a T-pose), measured against its automatic weights in the same test poses: elbows and wrists bend better
  (worst edge 1.6x against 2.3x), but its arm weights reach across the back, so raised arms stretch more there (12 torn
  edges overhead against none). It looked about the same in renders of those poses.
- How a model stands matters more than who rigs it. The first Shardmage, with its hands and sleeves at the hip, had 153
  vertices following both an arm and a leg in Tripo's rig, and clothes at the hip weighted to the fingers. Remade in a
  wider A-pose, with air between the arms and the clothes, it has none and takes no corrections at all. The skirt
  correction even makes it worse there (133 torn edges in a stride against 49), so Tripo's own weights stay.
- The Warrior and the Warden kept their closer A-pose: 643 and 2,711 such vertices, the Warden's with clothes on its
  fingers. They take the corrections, which keep a rig's weights for the arm's bones and fingers and only remove what is
  mixed in.

## A boss: Ruukhar the Slagborn

Ruukhar (2026-11-08) is the first Tripo model much larger than a hero: an unrigged one-eyed brute in a T-pose, its joints
read off front and side renders with a grid. Unity's humanoid clips scale a stride with the avatar's hip height, while
the client plays them at speeds measured on a human (`DisplayModel.WalkSpeed`). So he is built with a human's hip
height (2.37 m tall, being short-legged) and scaled up 1.3 times in `EntityVisuals`, which speeds the clips up to match.

- He trades his own clips (`brute.py`, gone) for the heroes': his attacks are a warrior's swings, and Cinder Quake's
  wind-up raises the cleaver like any cast. The telegraph still reads, but a brute of his own build moves like a hero.
- His cleaver is the Slagborn Cleaver he drops, held as creature equipment and shown at 1.5 times its size
  (`DisplayModel.WeaponScale`), on top of his own scale.
- A model without Tripo's rig has no finger bones: his hand does not close round the haft. Rigged by Tripo it would.
