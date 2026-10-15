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

## 🔜 Phase 2 – Accounts, characters, persistence

- [ ] PostgreSQL and DbUp migrations
- [ ] Accounts: registration and login over HTTPS with Argon2id, session token
- [ ] Characters: create, select and delete (name, placeholder race and class). Player ids come from the database
- [ ] Saving position and state with write-behind
- [ ] Login and character-select screens in UI Toolkit
- [ ] Localization: text keys, English first (install the Localization package)

## Phase 3 – Core combat

- [ ] Map export part 2: static geometry → navmesh and line of sight (DotRecast)
- [ ] Targeting and auto-attack. The spell and aura system per ADR 0005. Stats and modifiers
- [ ] NPCs: spawns as data, AI (aggro, chase, leash, threat), death and respawn, XP and levels
- [ ] HUD: unit frames, action bar, cast bar, floating combat text, nameplates
- [ ] GM commands (`.tele`, `.spawn`, `.level` …)

## Phase 4 – Items, loot, economy, quests

- [ ] Bags and inventory
- [ ] Equipment slots that affect stats, with a `displayId` for visuals
- [ ] Loot tables and loot window, vendors and gold
- [ ] A data-driven quest system: givers, prerequisites, objectives (kill, collect, talk, explore), rewards and chains
- [ ] Quest log, quest tracker and NPC dialogue

## Phase 5 – Vertical slice: the first real zone (the "is it fun?" gate)

- [ ] A coherent set of Synty-style asset packs, including a modular character system so gear is visible on the character
- [ ] Animations retargeted to a shared humanoid rig
- [ ] 1–2 races and 3 classes (tank, DPS, healer)
- [ ] A level 1–10 zone with a hub village, 20–30 quests, 10–15 creature types and a world boss
- [ ] Minimap, world map, sound, music, settings and key bindings
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

- **Bandwidth:** `EntityMoves` sends uncompressed floats. That adds up to about 15 MB/s out when 200 players see each other.
  - Quantize position, yaw and velocity.
  - Send less often at distance.
  - Do it before Phase 5 at the latest.
- **Tick spikes:** p99 and max sometimes reached 8–17 ms under bot load. Profile GC and scheduling, and run the bots on another machine when measuring.
- **Movement timestamps:** the server stamps with receive time, which adds some jitter over the internet. Consider mapping the client's (already validated) time instead.
- **The server only knows heights, not walls and trees.** Line of sight and navmesh come in Phase 3.
- **IMGUI is used for the debug UI and nameplates.** Move to UI Toolkit in Phases 2–3.
- **LiteNetLib reports 0 RTT in threaded mode.** The overlay uses our own Ping/Pong measurement instead.
- **Addressables and Localization** get installed when first used.
- **CI** (GitHub Actions: build, test, content validate).
