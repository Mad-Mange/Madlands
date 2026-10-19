# ADR 0011 – Quests and NPC dialogs

**Status:** Decided and implemented 2026-10-18 (Phase 4: `server/Madlands.Server/Quests`, `content/quests.json`).

## Decision

**Quests are content**

- `content/quests.json` defines every quest: title, level and minimum level, the creature that offers it (giver) and the one that takes it back (ender), prerequisites (quests that must be turned in first: this is how chains work), allowed classes, the texts (details, a one-line summary, progress and completion), up to four objectives and the rewards.
- Objective types: `kill` (a number of one creature), `collect` (a number of one item in the bags, taken at turn-in), `talk` (speak with an NPC) and `explore` (reach a circle on a map). New kinds of objectives are new types with a handler, like spell effects (ADR 0005).
- Rewards: experience (falling off for players more than five levels above the quest), money, items that are always given, and items to choose one from.
- Content validation checks every reference, that prerequisite chains do not loop, that explore points lie on the map and that every collect item drops or is sold somewhere.
- The client gets the texts, objectives and rewards (`QuestInfo`) in `GameData` at login (ADR 0008).

**On the server**

- Each player has a quest log (20 quests) with a counter per objective, and the set of quests turned in.
- Kills count for the tapper within 100 m (the same rule as experience). Talking counts when the player speaks with the NPC. Explore objectives are checked once per second. Collect objectives always mirror the bags, so dropping or destroying the items lowers the count again.
- Taking, turning in and abandoning are requests the server checks: the NPC must be in reach and actually give or end that quest, prerequisites must be met, the objectives done and the rewards must fit in the bags (the collected items are handed in first and put back if they still do not fit).
- Abandoning a quest destroys its quest-only items unless another quest needs them.
- The quest log is saved with the character (ADR 0010).
- Each objective carries the areas where it can be done, worked out from content at startup (`QuestAreas`): the explore circle, or circles around the spawns of the creatures to kill, of the creatures whose loot drops the item to collect, or of the NPC to talk to (spawns within 70 m share a circle, at most four per objective). The world map draws them until the objective is done.

**NPC dialogs**

- Right-clicking a friendly NPC sends `Interact`. The server answers with a `GossipMenu`: the NPC's greeting, its quests (to take, in progress, ready to turn in) and whether it sells things. An NPC that only sells opens its wares directly.
- Talking to an NPC in reach counts for talk objectives.
- Quest marks are private per player: the server computes each quest NPC's mark for a player (`QuestGiverStatus`: available "!", ready "?", in progress gray "?") when it comes into view and whenever the player's quests or level change.

**On the client**

- The dialog window shows the greeting and quest list, a quest's story with Accept, its progress text, and the reward screen with the choice. The quest log (L) and the tracker on the right show the objectives. Chat and the screen center announce progress ("Mire Frog slain: 3/6").

## Why

- Quests are the core of the "is it fun?" loop (Phase 5) and there will be hundreds. They must be data that designers (and AI) can write, review and validate without code changes.
- Computing availability and marks on the server keeps one source of truth for prerequisites and levels; the client cannot unlock quests by lying.
- Sending the texts once in `GameData` keeps dialogs instant. With hundreds of quests the message grows; ADR 0008's planned content-hash cache covers that.

## Consequences

- Shared kill credit for groups (Phase 6) needs the tapping rule extended to a group, in one place.
- Escort, timed, repeatable and daily quests are not supported yet; they will be new objective types or quest flags.
- Quest items that start a quest ("This item begins a quest") are not supported yet.
