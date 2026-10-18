# Roadmap

Every phase ends in something playable and tested. Architectural decisions are in [architecture.md](architecture.md) and [adr/](adr/).

## ✅ Phase 0 – Foundation (done 2026-10-16)

- [x] .NET 10 SDK, `.gitignore`, `.gitattributes` (LFS), `CLAUDE.md`
- [x] Solution: Shared, ClientCore, Server, Content, Bots, Tests and ContentTool. All build output goes to `/artifacts`
- [x] Unity 6 project (URP) with Shared and ClientCore as local packages, plus an asmdef guard against engine code
- [x] `deploy/docker-compose.yml` (Postgres + Seq) for Phase 2
- [x] Docs: architecture, ADR 0001–0007, game design draft

## ✅ Phase 1 – Walking skeleton (done 2026-10-16)

- [x] Server
  - [x] 20 Hz loop and LiteNetLib with a handshake and protocol version
  - [x] Dev login
  - [x] (mapId, instanceId), AoI grid, spawn/despawn
  - [x] EntityMoves chunked per MTU
- [x] Movement validation: clock, speed, map bounds, height map and NaN. Corrections with acknowledgement
- [x] Rate limits, violation counting, and kicks with a reason
- [x] Client
  - [x] WoW-style controls and camera, jumping without air control, autorun
  - [x] Interpolation and dead reckoning of other players
  - [x] Clock sync and a debug overlay (F3)
- [x] Code-generated test world and Export Map (height map) to `content/maps/0001`
- [x] Bots: 200 of them, using the same client code as the game
- [x] 46 tests, including real UDP integration
- [x] **Measured:** 200 bots + 1 client, 0 corrections, average tick 0.4–0.9 ms

**Open:** playtest controls and feel with two clients (see README).

## ✅ CI

- [x] GitHub Actions: build, all tests (with a PostgreSQL service) and content validation on every push and PR

## ✅ Phase 2 – Accounts, characters, persistence (done 2026-10-16)

- [x] PostgreSQL with DbUp migrations (embedded SQL, applied at startup). `--db memory` for runs without Docker
- [x] Accounts: register and log in over the HTTP account API with Argon2id hashes (PHC strings) and session tokens
  (SHA-256 hashed at rest, 24 h). In production the API sits behind a TLS reverse proxy
- [x] Brute-force protection: failed logins limited per account, requests limited per IP, constant-time "no such user"
- [x] Characters: create, list, select and soft-delete. Names unique per realm and case-insensitive. 10 per account.
  Races and classes are content (`content/races.json`, `classes.json`)
- [x] Player entity ids are persistent (`EntityGuid` = Player + character id)
- [x] Write-behind saving on logout, every 60 s and on shutdown. The game tick never waits for the database
- [x] One character per account in the world: a new login drops the old connection
- [x] Login, character select and create screens in UI Toolkit, plus an Esc menu. The client loads the map scene the server names
- [x] 72 tests, including a full realm restart on PostgreSQL. CI on GitHub Actions
- [ ] ~~Localization~~ moved to Phase 5: all UI text is already in UXML, ready for string tables

## ✅ Phase 3 – Core combat (done 2026-10-16)

- [x] Map export part 2: static colliders → `collision.bin`, `CreatureSpawnPoint` markers → `spawns.json` (ADR 0009)
- [x] Navmesh built at server startup with DotRecast (0.5 s for the test map); line of sight against terrain and obstacles
- [x] Targeting (click, right-click attack, Tab), auto-attack with the melee hit table (miss, dodge, parry, crit), armor
- [x] Spells and auras per ADR 0005: cast times, interrupts, cooldowns, global cooldown, projectiles, area effects,
  damage and heal over time, slows, roots, stuns, absorbs, stat buffs. 19 spells as data
- [x] Stats from class, race and level plus aura modifiers. Mana, rage (from dealing and taking damage) and regeneration
- [x] Three classes with 5–6 abilities each: Warrior (rage), Shardmage (mana, ranged), Warden (mana, healer)
- [x] Creatures as data: 5 templates, spawns from the map, AI (wander, aggro with line of sight, call for help, chase
  along the navmesh, threat, leash/evade), corpses and respawns, a training dummy, a caster that keeps its distance
- [x] Factions and reactions (hostile, neutral, friendly), shared by server and client
- [x] Death and release, experience (classic formula, gray levels), level-ups that teach spells, levels 1–10
- [x] HUD in UI Toolkit: unit frames, auras, cast bars, action bar with cooldowns, experience bar, tooltips, errors,
  chat, death panel, nameplates and floating combat text
- [x] GM commands (`.tele`, `.spawn`, `.level`, `.xp`, `.kill`, `.god`, `.aura`, `.cast`, `.info` …) and say chat
- [x] The server sends the client its display data at login (ADR 0008): content changes need no client rebuild
- [x] Persistence of experience, health and power; GM levels on accounts (migration 0002, `set-gm` command)
- [x] Bots fight creatures (`--mode fight`). Measured: 200 fighting bots, 0.8–1.1 ms average tick, p99 2–7 ms, 0 corrections
- [x] 98 tests, including combat end to end on a real realm (auto-attack kills, aggro and evade, casts and interrupts,
  refused casts, auras, death and release, chat, persistence)

**Open:** playtest the feel of combat (numbers, pacing, controls). All numbers are in `content/` and `StatFormulas.cs`.

## ✅ Phase 4 – Items, loot, economy, quests (done 2026-10-18)

- [x] Items as content (`content/items.json`, ADR 0010): quality, equip slot, stats, weapon damage and speed, stacks, prices, use
  spells and bags. 45 items, schemas for all new content files
- [x] Bags and inventory: 20 equipment slots, a 16-slot backpack and four bag slots. Move, swap, stack, split and destroy;
  two-handed weapons push the shield into the bags; only empty bags move. All rules in one tested class (`Inventory`)
- [x] Equipment that affects stats and the weapon swing; weapons and shields are visible in hand (`displayId`)
- [x] Loot tables (`content/loot.json`) rolled for the tapper; quest items only drop while a quest needs them; loot window,
  sparkling corpses, shift-right-click takes everything
- [x] Vendors and money (copper, silver, gold): buy, sell, sell all junk
- [x] Consumables: food and drink (break on combat, new `periodicEnergize` aura effect), healing and mana draughts
- [x] A data-driven quest system (`content/quests.json`, ADR 0011): givers and enders, prerequisites and chains, kill, collect,
  talk and explore objectives, rewards with a choice, experience falling off for high levels
- [x] Quest log (L), quest tracker, NPC dialogs, "!" and "?" over NPCs computed per player by the server
- [x] The Mirewatch Post at the spawn: Captain Edda Brand, Old Tamsin and Quartermaster Holt with 8 quests and a vendor
- [x] UI: bags (B), character sheet (C), item tooltips, loot, vendor and quest windows, drag and drop, a micro menu
- [x] Persistence: money, items and quests saved in one transaction (migration 0003); class starting kit on first login
- [x] GM commands `.additem`, `.money`, `.quest add|complete|reset`
- [x] Bots loot (and throw away junk when full). Measured: 200 bots, 0.9–1.5 ms average tick, p99 2–7.6 ms, 0 corrections
- [x] 132 tests, including loot, vendors, all four objective types, consumables and relogging on a real realm

**Open:** playtest the item and quest loop: drop rates, prices, quest experience and the reward choices. All numbers are in `content/`.

## Phase 5 – Vertical slice: the first real zone (the "is it fun?" gate)

- [ ] A coherent set of Synty-style asset packs, including a modular character system so gear is visible on the character
- [ ] Localization (Unity Localization package, English first)
- [x] Rebindable key bindings (two keys per action) and a settings window: mouse sensitivity and inversion, interface
  scale, window mode, resolution, vsync and quality (Esc > Settings, or on the login screen)
- [ ] Animations retargeted to a shared humanoid rig
- [ ] 1–2 races and 3 classes (tank, DPS, healer)
- [ ] A level 1–10 zone with a hub village, 20–30 quests, 10–15 creature types and a world boss
- [x] Minimap (north up, zoom, players, NPCs and quest marks) and world map (M) with the places active quests send you
  to. Export Map renders the map image from above
- [ ] Sound and music
- [ ] First deployment to a VPS and playtests with friends

## Phase 6 – Social

- [ ] Chat channels: say, yell, whisper, party, guild and general channels
- [ ] Groups with shared XP and loot, plus party frames
- [ ] Friends list and guilds
- [ ] Player-to-player trade and mail, both transactional

## Phase 7 – Instances and dungeons

- [ ] Instance servers. They reuse `(mapId, instanceId)`, so the model already exists
- [ ] Boss scripting with phases, timers and abilities
- [ ] Loot rules (need/greed) and lockouts
- [ ] The first 5-player dungeon

## Phase 8 – Bigger world and operations

- [ ] Several zones, seamless transitions and flight paths
- [ ] Mounts, graveyards and hearthstone
- [ ] **Splitting the monolith** into login and one or more world servers along the module boundaries
- [ ] Layering when a zone fills up
- [ ] Monitoring (Prometheus/Grafana), backups and a launcher/patcher (Addressables)

## Phase 9 – Depth (ongoing)

- [ ] Talents and specializations
- [ ] Professions: gathering and crafting
- [ ] Auction house and reputation
- [ ] PvP: duels first, then battlegrounds
- [ ] Achievements and raids

## Phase 10 – Launch and live ops (ongoing)

- [ ] Alpha and beta
- [ ] Load tests with bots
- [ ] Basic anti-cheat
- [ ] Account security
- [ ] Legal: GDPR, terms of service, asset licenses and possibly payments

## Tech debt and follow-ups

- **Players are not checked against walls**, only the terrain. Reject moves that cross obstacle triangles, with
  tolerance for the client capsule sliding along colliders (ADR 0009).
- **Combat balance is a first guess.** Tune with real playtests; the formulas are in `StatFormulas.cs`.
- **Action bar layout is automatic** (spells in learn order). Let players drag spells around and save the layout on the server.
- **No spell queueing:** pressing a key just before the global cooldown ends does nothing (WoW queues it).
- **Kill credit and loot go to one player.** Groups (Phase 6) need shared credit, tapping rules and loot rules.
- **Items have no per-instance ids or properties** (durability, enchantments). Trade and mail (Phase 6) need instance ids
  and transactional transfers (ADR 0010).
- **No buyback at vendors:** a mis-sold item is gone.
- **Consumables cannot go on the action bar** (comes with player-arranged action bars).
- **Quests:** no quest-starting items, escort, timed, repeatable or daily quests yet (ADR 0011).
- **Only weapons and shields are visible on characters.** Armor visuals come with the modular characters of Phase 5.
- **Tree crowns have no colliders**, so they block neither line of sight nor the camera.

- ~~**Bandwidth**~~ (done 2026-10-18): `EntityMoves` entries are packed (guid as type + varint, ~2 mm positions, 16-bit
  yaw and velocity: about 24 bytes instead of 42) and far entities are sent at 5 Hz (50–100 m) or 2.5 Hz (beyond 100 m).
  200 bots seeing each other: 2–3 MB/s out instead of 8–15 MB/s.
- **Tick spikes:** p99 and max sometimes reached 8–17 ms under bot load. Profile GC and scheduling, and run the bots on another machine when measuring.
- **Movement timestamps:** the server stamps with receive time, which adds some jitter over the internet. Consider mapping the client's (already validated) time instead.
- **IMGUI is still used for the debug overlay (F3).** Everything else is UI Toolkit.
- **LiteNetLib reports 0 RTT in threaded mode.** The overlay uses our own Ping/Pong measurement instead.
- **Addressables and Localization** get installed when first used.
- **TLS for the account API:** dev uses plain HTTP on loopback. Before the first public deployment (Phase 5), put
  the API behind a TLS reverse proxy (e.g. Caddy) and refuse non-loopback plain HTTP.
- **Session cleanup:** expired rows in `sessions` are never deleted yet. Add a periodic cleanup job.
- **Quick-login button** on the login screen (playtest shortcut: one account, the public dev password, local-network
  servers only). Remove it before any public test.
