# Architecture

## Overview

```
 Unity client ("just a view")                   Madlands.Server (.NET 10, one process for now)
 ┌────────────────────────┐   UDP / LiteNetLib   ┌──────────────────────────────────────────────┐
 │ PlayerController       │ ◄──────────────────► │ GameServer: networking + 20 Hz loop          │
 │  (predicts own move)   │                      │  ├ ChatHandler (say, GM commands)            │
 │ WorldView, UnitView    │                      │  └ WorldManager                              │
 │ HudController (UI Tk)  │                      │     └ MapInstance (mapId, instanceId)        │
 │ TargetingController    │                      │        ├ AoiGrid (64 m cells)                │
 │ SpellEffects           │                      │        ├ CombatSystem · SpellSystem ·        │
 │ Inventory/Quest/Loot/  │                      │        │  AuraSystem                         │
 │  VendorUi (windows)    │                      │        ├ ItemSystem · LootSystem ·           │
 └──────────┬─────────────┘                      │        │  QuestSystem                        │
            │ same code as the bots              │        ├ Players (MovementValidator each)    │
 ┌──────────┴─────────────┐                      │        ├ Creatures (CreatureAI + Mover)      │
 │ Madlands.ClientCore    │                      │        └ NavQuery · CollisionWorld (LOS)     │
 │ GameClient, ClientWorld│                      └───────────────┬──────────────────────────────┘
 │ ClientGameData, Clock, │                      Madlands.Content (loads + validates content/)
 │ Interpolation, Sender, │                      Madlands.Navigation (DotRecast navmesh, LOS)
 │ ClientInventory/Quests │                        ▲ content/*.json, maps/<id>/{height,collision}.bin,
 └──────────┬─────────────┘                          spawns.json (written by Unity: Export Map)
 ┌──────────┴─────────────────────────────────────┐
 │ Madlands.Shared: protocol (opcodes, messages,  │
 │ PacketWriter/Reader), UnitValues, FactionTable,│
 │ SpellInfo, ItemInfo, QuestInfo, ItemRules,     │
 │ MovementState, EntityGuid, Vec3,               │
 │ GameConstants, HeightMap, CollisionMesh        │
 └────────────────────────────────────────────────┘
```

**Core principle:** the server is the authority over everything. Game logic lives in the server and in Shared, and the client is a view. That is why the client engine can be replaced without rewriting the game.

One realm process (`ServerHost`) contains three parts:

- the UDP game server (`GameServer`),
- the HTTP account API (`Api/AccountApi`, Kestrel),
- the write-behind saver (`Persistence/CharacterSaver`).

All three share one `IGameDatabase`: PostgreSQL in production, or in-memory for tests and `--db memory`. The integration tests start a `ServerHost` too, so they exercise exactly the production wiring.

## Data flow: login and entering the world

```
 Client                         Account API (HTTP)                 Game server (UDP)            Database
   │ POST /api/v1/login ──────────► verify Argon2id hash ───────────────────────────────────► accounts
   │ ◄──────────── AuthResponse(session token, realm host:port)    create session ───────────► sessions (SHA-256 of token)
   │ POST /characters/list|create ─► Bearer token ──────────────────────────────────────────► characters
   │
   │ UDP connect + Hello(token, characterId) ─────────────────────► Authenticating:
   │                                                                 token, character, GM level, items and quests loaded off-thread
   │                                                                 result → MainThreadQueue → loop thread
   │ ◄──────────────────────────────────────────── GameData (spells, factions, classes, XP table, items, quests; ADR 0008)
   │ ◄──────────────────────────────────────────── Welcome(guid, name, map, position, own UnitValues)
   │ ◄──────────────────────────────────────────── Spellbook, PlayerStats, InventoryUpdate, MoneyUpdate, QuestLogUpdate
   │ load scene World_<mapId> additively, spawn local player, show HUD
```

- **Account API bodies** use the same binary messages as the game connection (`Shared/Net/AccountMessages.cs`), so the client needs no JSON library. Every response is either the expected type or an `ApiResult` with an error code.
- **Production security:** the API runs behind a TLS-terminating reverse proxy. Dev uses plain HTTP on loopback.
- **Brute-force protection:**
  - Failed logins are limited per account.
  - Requests are limited per IP.
  - Unknown accounts are checked against a dummy hash, so timing does not reveal which account names exist.
- **One character per account in the world:** a new login drops the older connection (`LoggedInElsewhere`).
- **Saving:**
  - Position, level, experience, health, power, money, items and quests are saved on logout, every 60 s, and for everyone on shutdown.
  - The loop thread enqueues a snapshot; `CharacterSaver` writes the newest snapshot per character on a background task, in one transaction per character (ADR 0010).
  - The game tick never waits for the database.
- **GM commands** need `accounts.gm_level > 0` (set with `Madlands.Server set-gm <account> <level>`), or `--gm-all` in development.

## Data flow: movement

1. The client moves its own character immediately, without waiting for the server.
2. `MovementSender` decides when a `MoveUpdate` must be sent:
   - immediately when the movement flags change (start/stop, jump),
   - at most 10 Hz while turning with the mouse,
   - a heartbeat every 500 ms while moving,
   - whenever the real position drifts from what other clients dead-reckon.
3. The server (`MovementValidator`) checks:
   - that the client clock does not run faster than the server's (stops speed hacks),
   - that the distance fits within the unit's current speed: slows, roots, stuns and death lower it after a 1 s grace period (the client learns about them one round trip later),
   - that the position is inside the map and near the ground (height map),
   - that no values are NaN.

   An impossible move triggers a `MoveCorrection`. The client acknowledges the correction id in its following updates, so updates that were already in flight are ignored. Teleports (GM `.tele`, releasing after death) use the same message.
4. Every 2nd tick (10 Hz) the server goes through each player's surroundings:
   - It sends `EntitySpawn`/`EntityDespawn` (view range 150 m, despawn at 170 m). Units carry their full `UnitValues`, then their auras.
   - It sends `EntityMoves` for entities that changed within the last second. These are unreliable, chunked so each packet fits one MTU, and repeated for 1 s so a lost packet goes unnoticed.
   - It sends `UnitUpdates`: the changed fields (health, power, flags, target, ...) of the player itself and every unit it sees, in one batch.
5. Other clients render 100 ms in the past and interpolate between samples. Past the newest sample they dead-reckon (max 1 s). Grounded characters follow the client's own terrain.
6. **Creatures** move along navmesh paths (`CreatureMover`). Their position is updated every tick on the server, but observers get a new sample only when the direction changes and as a 500 ms heartbeat, exactly like players.

## Data flow: combat

```
 Client                                    Server (map instance thread)
   │ SetTarget(guid) ─────────────────────► player.Target (visible to others as a unit field)
   │ CastSpell(id, target, castId) ───────► SpellSystem.TryCast: known? alive? not stunned? GCD/cooldown?
   │                                        cost? target valid and in range? line of sight? facing?
   │ ◄─────────────────── SpellFailed(castId, reason)          (refused: the client undoes its GCD guess)
   │ ◄─────── SpellStart → observers (cast bars)               (cast time > 0; moving interrupts it)
   │ ◄─────── SpellGo → observers (projectiles start)          (cost and cooldown paid at release)
   │                                        effects: hit roll per target, then each effect's handler
   │ ◄─────── CombatEvent → observers of the target (damage, heals, misses: floating combat text)
   │ ◄─────── AuraUpdate → observers (buffs and debuffs with remaining time)
   │ ◄─────── UnitUpdates at the next broadcast (health bars)
   │ ◄─────── ExperienceGained, Spellbook, PlayerStats (own player only)
```

- **Units** (`Unit`: `Player` and `Creature`) hold the public `UnitValues` with a dirty mask, computed stats, auras, the current cast, cooldowns and the auto-attack target.
- **Systems** per map instance hold the rules:
  - `CombatSystem`: the melee hit table, damage after armor and absorbs, healing, threat, rage, death, kill credit and experience, regeneration, levels.
  - `SpellSystem`: cast validation, cast times and interrupts, projectiles, effect handlers (ADR 0005).
  - `AuraSystem`: applying, stacking, ticking and removing auras; stat recalculation when they change.
- **Creature AI** (`CreatureAI`): idle and wander → aggro (hostile players within the aggro radius, with line of sight; weaker for higher-level players) → call nearby friends → chase along the navmesh and fight the unit with the most threat (110 % rule to switch) → evade home with full health when pulled 60 m from where the fight started or the target cannot be reached for 6 s.
- **Death:** creatures leave a corpse, give experience to the first player who damaged them, and respawn from their spawn point. Players release to the map's spawn point with half health.
- **Reactions** come from `factions.json` via `FactionTable` (shared): the same code colors nameplates on the client and decides who may attack whom on the server.

## Data flow: items, loot, vendors and quests

```
 Client                                    Server (map instance thread)
   │ MoveItem / AutoEquip / DestroyItem ──► ItemSystem → Inventory rules (slot, class, level, stacks, bags, two-hand)
   │ ◄──────── InventoryUpdate (changed slots) · ActionFailed(error) · UnitUpdates (weapon looks) · PlayerStats
   │ UseItem(pos, target, castId) ────────► the item's use spell via SpellSystem; one taken from the stack if cast
   │                                        creature dies → LootSystem rolls its loot table for the tapper
   │ ◄──────── LootableUpdate (private: the corpse sparkles for its owner only)
   │ LootOpen / LootTake / LootClose ─────► in reach? owner? room in the bags?
   │ ◄──────── LootWindow · ItemReceived · MoneyUpdate
   │ Interact(npc) ───────────────────────► QuestSystem: talk objectives; GossipMenu, or the vendor's wares
   │ ◄──────── GossipMenu(greeting, quests with status) · VendorInventory
   │ VendorBuy / VendorSell ──────────────► ItemSystem: in reach, price, room
   │ QuestAccept / QuestTurnIn / QuestAbandon ► QuestSystem: giver/ender, prerequisites, objectives, rewards fit
   │ ◄──────── QuestLogUpdate (progress) · QuestCompleted · QuestGiverStatusUpdate ("!" and "?" per NPC)
```

- **Items** (ADR 0010): equipment, backpack and bags live in `Player.Inventory`, a pure rules class. Worn items feed the stat formulas; the main-hand weapon sets the swing.
- **Loot** belongs to the player who tapped the creature; quest items only drop while a quest still needs them.
- **Quests** (ADR 0011): kill, collect, talk and explore objectives; prerequisites make chains; collect objectives mirror the bags. NPC quest marks are computed per player by the server.
- All of these are requests: the client shows the server's answer and never changes its own inventory or quest log.

## Threading model

All game logic runs on the server's loop thread. At the start of each tick, two things are dispatched:

- the results of background work (database lookups), from the `MainThreadQueue`,
- LiteNetLib events, from `PollEvents()`.

Background code never touches game state directly. The account API runs on Kestrel's threads and only touches the database and immutable content. The one exception is a thread-safe "is this character online" set.

Map instances share no mutable state and own their own `PacketWriter`, random generator, navmesh query and systems. The navmesh and collision geometry of a map are immutable and shared. Because of that, instances can later move to their own threads or processes (Phase 8) without rewriting the logic.

## Client scenes and UI

- **`FrontEnd`** (build index 0) is always loaded. It holds the camera, networking (`NetworkController`), the world view, targeting, spell effects and one UI Toolkit document: login, character select and create, the Esc menu and the in-game HUD.
- **The HUD** (`HudController`): player and target frames, auras, cast bars, the action bar (keys 1-0, -, =), the experience bar, tooltips, error texts, chat (Enter; lines starting with `.` are GM commands), nameplates and floating combat text. It only shows ClientCore state and asks the server for actions.
- **Windows** (Phase 4, plain C# classes owned by the HUD): bags (B), character sheet (C), quest log (L), loot, vendor and NPC dialogs, the quest tracker and item tooltips (`InventoryUi`, `LootUi`, `VendorUi`, `QuestUi`, `ItemWidgets`). Items move WoW-style: click to pick up, click to put down (or drag); dropping outside the windows asks to destroy. Right-click on a unit attacks, talks or loots.
- **Map scenes** (`World_0001`, …) contain only the world: terrain, props, colliders, light and fog, creature spawn markers (editor only) and a `MapInfo` component.
  - They are loaded additively when the server's `Welcome` names the map, and unloaded on logout.
  - The map scene is set active so its lighting and fog apply.
- **Editor:** pressing Play always boots from `FrontEnd`.

## Performance

**Phase 1** (2026-10-16): 200 bots and 1 Unity client within 120 m of each other, so everyone sees everyone (the worst case).

- Server tick 0.4–0.9 ms average, p99 2–8 ms. Outgoing traffic about 15 MB/s (uncompressed floats; on the roadmap).
- 0 movement corrections over 75 s.

**Phase 3** (2026-10-16, Release build): 200 bots fighting the creatures of the test map (`Madlands.Bots --mode fight`).

- Server tick 0.8–1.1 ms average, p99 2–7 ms, max 10 ms, with combat, auras, AI and pathfinding running.
- 0 movement corrections. The bots killed creatures, died and released, and gained experience.
- Outgoing traffic 8–16 MB/s with most bots seeing each other (still uncompressed; on the roadmap).
- The navmesh of the 512 × 512 m map with 49 000 obstacle triangles builds in about 0.5 s at startup.

**Phase 4** (2026-10-18, Release build): 200 bots fighting and looting (`Madlands.Bots`, they also pick up loot and throw away junk when their bags are full).

- Server tick 0.9–1.5 ms average, p99 2–7.6 ms, 0 movement corrections over 75 s.
- 200 periodic saves with items and quests, none failed.

## Decisions that are expensive to change

See [adr/](adr/). Summary:

| # | Decision |
|---|---|
| 0001 | Unity client as a view, engine-agnostic .NET server, shared C# |
| 0002 | Server authority; the client predicts its own movement, the server validates |
| 0003 | The world is split into (mapId, instanceId); 64-bit EntityGuid; Unity's coordinate convention |
| 0004 | Content as data with stable IDs; the server sends displayIds |
| 0005 | Spells/auras as lists of effects (the WoW model) – implemented in Phase 3 |
| 0006 | LiteNetLib + custom binary serialization + protocol version |
| 0007 | PostgreSQL; write-behind; value transfers in transactions – implemented in Phase 2 |
| 0008 | The server sends the client its display data (spells, factions, levels) at login |
| 0009 | World geometry: colliders exported from Unity, navmesh built at server startup |
| 0010 | Items, inventory, loot and money: items as content, one inventory rules class, loot for the tapper, saves in one transaction |
| 0011 | Quests and NPC dialogs: quests as content (kill, collect, talk, explore), server-computed quest marks |

## Known simplifications (deliberate)

- **The test world is generated by code** (`TestWorldBuilder`). Real zones are built by hand in Phase 5.
- **All art is placeholder:** capsules, spheres and boxes per displayId (`EntityVisuals`), spell icons are colored tiles with initials. Real models and icons arrive in Phase 5; the ids stay.
- **The debug overlay is IMGUI.** Everything else is UI Toolkit.
- **The account API is plain HTTP in dev.** TLS comes via a reverse proxy before the first public deployment.
- **Addressables and Localization are not installed yet.** The packages are added when first used (Phase 5).
- **Keys are read directly from the keyboard.** Rebindable controls come in Phase 5.
- **Only one terrain tile, no streaming.**
- **Players are not checked against walls**, only against the terrain (ADR 0009).
- **No groups yet**: kill credit and loot go to the first player who damaged a creature.
- **Items have no per-instance data** (durability, enchantments) and no per-instance ids yet (ADR 0010). Only weapons and shields are visible on characters.
- **No buyback, no dual wielding, no item use from the action bar.**
