# Architecture

## Overview

```
 Unity client ("just a view")                   Madlands.Server (.NET 10, one process for now)
 ┌────────────────────────┐   UDP / LiteNetLib   ┌──────────────────────────────────────────┐
 │ PlayerController       │ ◄──────────────────► │ GameServer: networking + 20 Hz loop      │
 │  (predicts own         │                      │  └ WorldManager                          │
 │   movement)            │                      │     └ MapInstance (mapId, instanceId)    │
 │ WorldView (interpol.)  │                      │        ├ AoiGrid (64 m cells)            │
 │ CameraController, UI   │                      │        ├ MovementValidator per player    │
 └──────────┬─────────────┘                      │        └ Spawn/Despawn/EntityMoves       │
            │ same code as the bots              └───────────────┬──────────────────────────┘
 ┌──────────┴─────────────┐                                      │
 │ Madlands.ClientCore    │                      Madlands.Content (loads + validates content/)
 │ GameClient, ClockSync, │                                      │
 │ InterpolationBuffer,   │                      content/maps/0001/{map.json, height.bin}
 │ MovementSender         │                        ▲ written by Unity: Madlands > Export Map
 └──────────┬─────────────┘
 ┌──────────┴──────────────────────────────────────────────────────────────┐
 │ Madlands.Shared: protocol (opcodes, messages, PacketWriter/Reader),      │
 │ MovementState/flags, EntityGuid, Vec3, GameConstants, HeightMap          │
 └──────────────────────────────────────────────────────────────────────────┘
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
   │                                                                 token + character loaded off-thread ─► sessions, characters
   │                                                                 result → MainThreadQueue → loop thread
   │ ◄──────────────────────────────────────────── Welcome(guid = Player + characterId, mapId, saved position)
   │ load scene World_<mapId> additively, spawn local player
```

- **Account API bodies** use the same binary messages as the game connection (`Shared/Net/AccountMessages.cs`), so the client needs no JSON library. Every response is either the expected type or an `ApiResult` with an error code.
- **Production security:** the API runs behind a TLS-terminating reverse proxy. Dev uses plain HTTP on loopback.
- **Brute-force protection:**
  - Failed logins are limited per account.
  - Requests are limited per IP.
  - Unknown accounts are checked against a dummy hash, so timing does not reveal which account names exist.
- **One character per account in the world:** a new login drops the older connection (`LoggedInElsewhere`).
- **Saving:**
  - On logout, every 60 s, and for everyone on shutdown, the loop thread enqueues a snapshot.
  - `CharacterSaver` writes the newest snapshot per character on a background task.
  - The game tick never waits for the database.

## Data flow: movement

1. The client moves its own character immediately, without waiting for the server.
2. `MovementSender` decides when a `MoveUpdate` must be sent:
   - immediately when the movement flags change (start/stop, jump),
   - at most 10 Hz while turning with the mouse,
   - a heartbeat every 500 ms while moving,
   - whenever the real position drifts from what other clients dead-reckon.
3. The server (`MovementValidator`) checks five things:
   - that the client clock does not run faster than the server's (stops speed hacks),
   - that the distance fits within run speed,
   - that the position is inside the map,
   - that the position is near the ground (height map),
   - that no values are NaN.

   An impossible move triggers a `MoveCorrection`. The client acknowledges the correction id in its following updates, so updates that were already in flight are ignored.
4. Every 2nd tick (10 Hz) the server goes through each player's surroundings:
   - It sends `EntitySpawn`/`EntityDespawn` (view range 150 m, despawn at 170 m).
   - It sends `EntityMoves` for entities that changed within the last second. These are unreliable, chunked so each packet fits one MTU, and repeated for 1 s so a lost packet goes unnoticed.
5. Other clients render 100 ms in the past and interpolate between samples. Past the newest sample they dead-reckon (max 1 s). Grounded characters follow the client's own terrain.

## Threading model

All game logic runs on the server's loop thread. At the start of each tick, two things are dispatched:

- the results of background work (database lookups), from the `MainThreadQueue`,
- LiteNetLib events, from `PollEvents()`.

Background code never touches game state directly. The account API runs on Kestrel's threads and only touches the database and immutable content. The one exception is a thread-safe "is this character online" set.

Map instances share no mutable state and own their own `PacketWriter`. Because of that, they can later move to their own threads or processes (Phase 8) without rewriting the logic.

## Client scenes

- **`FrontEnd`** (build index 0) is always loaded. It holds the camera, networking (`NetworkController`), the world view, and the UI Toolkit document (login, character select and create, Esc menu).
- **Map scenes** (`World_0001`, …) contain only the world: terrain, props, light and fog, plus a `MapInfo` component.
  - They are loaded additively when the server's `Welcome` names the map, and unloaded on logout.
  - The map scene is set active so its lighting and fog apply.
- **Editor:** pressing Play always boots from `FrontEnd`.

## Performance (measured 2026-10-16, Phase 1)

- **Load:** 200 bots and 1 Unity client within 120 m of each other, so everyone sees everyone (the worst case).
- **Server tick:** 0.4–0.9 ms average, p99 2–8 ms, max 17 ms. The target is below 5 ms average.
- **Outgoing traffic:** about 15 MB/s in total. That comes from 200 × 190 visible entities × 10 Hz with uncompressed floats. Quantization and a lower send rate at distance are on the roadmap.
- **Movement:** 0 corrections over 75 s.

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

## Known simplifications (deliberate)

- **The test world is generated by code** (`TestWorldBuilder`). Real zones are built by hand in Phase 5.
- **The debug overlay and nameplates still use IMGUI.** The front-end is UI Toolkit, and the HUD (Phase 3) will be too.
- **The account API is plain HTTP in dev.** TLS comes via a reverse proxy before the first public deployment.
- **Addressables and Localization are not installed yet.** The packages are added when first used (Phase 5). The principles are already fixed: displayIds instead of prefab names, and all UI text in UXML ready for string tables.
- **Keys are read directly from the keyboard.** Rebindable controls come in Phase 5.
- **Only one terrain tile, no streaming.**
