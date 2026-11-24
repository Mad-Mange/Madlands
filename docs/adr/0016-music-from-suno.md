# ADR 0016 – Music from Suno

**Status:** Decided and implemented 2026-11-24 (`art/audio/suno.py`, `SoundSystem`). The same day: a title theme,
music for the Greywood and Reedfen, and boss music.

## Context

The three area themes were synthesized by `art/audio/music.py` from chords and a seeded melody: short loops (90 to
115 s) that sound like a synthesizer. The project owner has a [Suno](https://suno.com) Pro plan, and Suno can be
driven from the development tools (an MCP server), so music can be generated from a written prompt and checked
without leaving the project.

## Decision

- **Area music is generated with Suno**: model v6, instrumental, about three minutes, from the style prompts in
  `suno.py` (`PROMPTS`). Each generation makes two takes, and both become variants of the area's music
  (`music_hollowmere_1`, `_2`). A first check uses Suno's listening agent (no voice, a soft start, nothing loud or busy
  enough to distract); takes with even wordless humming are left out, and the project owner judges the rest in the
  game. The login and character screens play `music_title`.
- **The originals stay outside the repository**, as WAV downloads named by their Suno id in `MADLANDS_SUNO_DIR`
  (default `D:\3D_Stuff\Music\MadLands`), like the Tripo originals (ADR 0014). Each download costs one of the plan's
  download credits, so keep them. `suno.py` (run in Blender for its encoder) trims silence, fades in over 50 ms and out
  over 4 s, brings each song to one loudness (RMS 0.12, about -18 dBFS, where the synthesized music sat; peaks at most
  0.89) and writes OGG Vorbis into `Audio/Music`.
- **Music plays as a playlist, not a loop.** The songs end in a fade instead of looping seamlessly, so `SoundSystem`
  plays a take to its end, waits 15 to 30 s while the ambience goes on, then plays another take of the same music
  (`SoundCatalog.Pick` avoids the one just played). Ambience still loops. Areas name their music in
  `MapArea.music`.
- **Bosses have their own music.** While Ruukhar or Gloomjaw is in combat within 50 m of a living player (not dead, not
  running home), `GameSounds` plays `music_boss` instead of the area's music, gapless from take to take, and goes back
  when the fight ends. The client knows a boss by its display id, like its model; there is no boss flag in the content
  yet. The boss music ends on a hit, so `suno.py` fades it out over only 0.5 s.
- **Only songs made on a paid plan go into this public repository.** Suno's terms give paid subscribers ownership of
  the songs they make while subscribed, commercial use included; songs from the free plan belong to Suno. The songs
  stay private on suno.com. `CREDITS.md` lists them.
- `music.py` and the synthesized themes are removed (they remain in the history). Sound effects, jingles and the
  ambience loops stay synthesized or CC0 (ADR 0012, Kenney).

## Consequences

- Twelve songs take about 30 MB in Git LFS instead of 3.5 MB; Unity streams music, so memory does not grow.
- New music: add a prompt to `PROMPTS`, generate, download the takes as WAV into the originals folder, add their ids to
  `TRACKS`, run `suno.py -- <clips>` and name the music on the map's area. A new boss gets the boss music by adding
  its display id to `GameSounds.BossDisplays`.
- Whether AI-generated music is protected by copyright is unsettled in several countries; the right to use and ship
  the songs rests on Suno's terms. Should those change, the synthesized themes can be restored from the history.
