# ADR 0004 – Content as data

**Status:** Decided 2026-10-16 (started in Phase 1: maps)

## Decision

**Where content lives**

- All game content is stored as text in `content/`, with JSON Schemas in `content/schemas/`.
- That covers maps, spells, items, creatures, quests and loot.

**Ids**

- Ids are stable integers that are never reused.

**Validation**

- `Madlands.Content` validates everything.
- All errors are collected and reported together.
- References between files are checked.
- ContentTool runs the same validation.

**Maps**

- Map geometry is exported from the Unity scene with `Madlands > Export Map`.
- The scene is the source of truth.

**Client visuals**

- The server sends a `displayId`.
- The client looks the displayId up in a table pointing to Addressable keys (Phase 5).

## Why

- Content is the bottleneck in an MMO.
- Text data can be written and reviewed by both humans and AI, and diffed in git.
- No code needs recompiling when content changes.
