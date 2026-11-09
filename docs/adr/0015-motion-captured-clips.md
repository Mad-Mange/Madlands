# ADR 0015 – Motion-captured humanoid clips from Quaternius

**Status:** Decided and implemented 2026-11-09 (`art/blender/motions.py`, `EntityVisuals.HumanoidClipSet`).

## Context

Every humanoid played 16 clips keyed by hand in `humanoid.py` (ADR 0012). They read well from afar but move stiffly
next to the painted Tripo characters (ADR 0014). Mixamo's motion capture would fit, and the Tripo models even carry
its skeleton, but Mixamo's terms forbid sharing the raw animation files, and the repository is public: its clips
could never be committed, so a clone of the repository would show other animations than our builds.

Quaternius's Universal Animation Library 1 and 2 are motion captured, made for retargeting onto one humanoid rig, and
CC0: they may be committed like our own art. Their free Standard packs cover idling, a jog, jumping, sword and fist
attacks, a spell, hits and deaths, but no walk at our speed, no running backwards or strafing, and only sitting on a
chair.

## Decision

- `motions.py` imports the two libraries, gives their skeleton our bone names (the hips are the top bone, the middle
  spine bone and the fingers are left out of the mapping) and writes the chosen clips under our clip names into
  `Art/Characters/HumanoidMotions.fbx`. `ArtImporter` maps it like `HumanoidAnimations`, so Unity retargets its clips
  onto every humanoid avatar. The libraries' files stay outside the repository (`MADLANDS_ANIMATION_DIR`), like the
  Tripo originals; a build without them keeps the committed file.
- Humanoids take a clip from `HumanoidMotions` where there is one and the scripted one otherwise
  (`EntityVisuals.HumanoidClipSet`): today Idle, Run, Jump, Fall, the three attacks, the cast, Hit and Death come from
  the library; Walk, RunBack, the strafes and Sit stay scripted. A model can keep scripted clips where its look depends
  on them (Ruukhar raises his cleaver for Cinder Quake, which his quest warns of).
- The library's jog moves 5.36 m/s at normal speed (measured from its root-motion files), the Run's nominal speed for
  humanoids. Its walk moves 0.97 m/s: at our 2.5 m/s it would play 2.6 times too fast, so Walk stays scripted.
- The library holds a weapon hand as if it were empty. Out of an action, `UnitView` turns the forearm about its own line
  until a held weapon points forward, and stands a staff upright in world space instead of relative to the hand.

## Consequences

- Unity measures an animation-only file's avatar in the first frame of its first clip. The library's clips start in
  their own poses, so `motions.py` writes a rest pose clip, `A_TPose`, that comes first; without it every clip came out
  turned (arms swung out to the sides). A wrongly measured avatar keeps its stored rotations: delete the `.meta` to
  measure it from scratch.
- Clips from two sources mix: the walk and the strafes look keyed by hand next to the motion-captured run. Lowering
  the walking speed or the paid Pro packs (more locomotion) would close that gap.
- Quaternius's library has more than we use (sitting on a chair, eating, chopping wood, farming, swimming): new clips are
  one line in `CLIPS`.
