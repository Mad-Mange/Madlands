# ADR 0008 – The server sends the client its game data

**Status:** Decided and implemented 2026-10-16 (Phase 3).

## Decision

**What**

- Right before `Welcome`, the server sends one `GameData` message with what the client needs to *show* the game:
  - spells (name, description, icon id, school, ranges, times, costs, flags, aura duration),
  - factions (for reactions: hostile, neutral, friendly),
  - classes (name, power type),
  - the experience table.
- The server builds the message once at startup from `content/` and sends the same bytes to everyone.

**What stays on the server**

- Effects, formulas, creature templates, AI parameters and loot. The client never needs them.

**Names and localization**

- Names in `GameData` are the English names from content.
- When localization arrives (Phase 5), the client looks up translated names by id in its string tables and falls back to the server's name.

**Assets**

- `GameData` carries ids (`icon`, `displayId`), never asset names (ADR 0004).

## Why

- **No client rebuild for content changes.** Changing a spell's cost or adding a creature only needs a server restart. That matters most now, while content changes daily.
- **Client and server can never disagree** about a spell's range or cooldown, which would cause confusing "out of range" errors.
- **One loader.** Content is parsed once, by `Madlands.Content` on the server. Unity does not need a JSON library or a copy of the content files.

## Consequences

- Login costs a few kilobytes more. With thousands of items later, the message can be cached on the client by a content hash and only resent when it changes.
- Offline tools in Unity (a spellbook preview, say) would need a running server or their own export. Not needed so far.
