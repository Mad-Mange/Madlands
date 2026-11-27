# Developer GM Menu: discovery (phase 1)

What the repository already has, what each planned command maps to, and the approach chosen. Written
before the implementation; `README.md` in this folder describes what was finally built.

## What exists

| Area | Where | Relevant for the menu |
|---|---|---|
| Engine and UI | Unity 6 (URP), UI Toolkit. In-game windows are plain C# classes owned by `HudController`, built in code and styled by USS (`UI/Hud.uss`, `UI/Windows.uss`). Text through `Loc.T` (`Resources/Localization/en.json`, ADR 0013). | The menu is another window class owned by `HudController`; its styles load from `Resources`. |
| Input | `KeyBindings` (rebindable `GameAction`s), `InputGate.MenuOpen` (front-end screens and the Esc menu take mouse and keyboard) and `InputGate.Typing` (a text field owns the keyboard). `UiPointer` keeps clicks on UI elements from reaching the world. | The menu is non-modal like the bags: clicks stay in the panel, typing in its fields sets `Typing`. |
| Esc and the pause menu | `FrontEndController.Update`: Esc closes the game menu, else asks `HudController.HandleEscape()` (chat line, held item, loot, vendor, windows, target), else opens the game menu (`game-menu`, settings, log out). Settings handle their own Esc. | The menu slots into this chain; the game menu stays reachable. |
| Authority | The server is authoritative (`GameServer`, one `MapInstance` per map). GM rights: `Player.GmLevel` from the account (`set-gm`) or `--gm-all` in development. Clients never learn their GM level today. | Authorization stays on the server. The client is told its level only to show or hide the menu. |
| GM console | `server/Madlands.Server/Commands/ChatHandler.cs`: chat lines starting with `.` (`.god`, `.heal`, `.revive`, `.kill`, `.level`, `.xp`, `.money`, `.additem`, `.tele`, `.spawn`, `.respawn`, `.cd`, `.speed`, `.quest`, `.info` ...), rate limited by `ClientSession.GmCommandLimiter`, logged with Serilog. Replies are free text. | The typed commands reuse the same gameplay calls; the shared logic moves into one service that both the console and the menu use. |
| Combat and progression | `CombatSystem` (`DealDamage` honours `Player.GodMode`, `Kill`, `Resurrect`, `SetLevel`, `GiveXp`), `SpellSystem` (costs, cooldowns, `ResetCooldowns`), `StatFormulas`. | God mode, heal, revive, kill, level, XP, no cooldown, damage multiplier. |
| Items and money | `ItemSystem.Give` (bag rules, partial results), `GiveMoney`. Item catalog on the client: `ClientGameData.Items`. | Add item and add gold, item selector from the real catalog. |
| Quests | `QuestLog` / `QuestSystem` (`SendLog`, `RefreshStatuses`), `.quest add/complete/reset`. Client catalog: `ClientGameData.Quests`. | Quest start/complete/reset. |
| World | `MapInstance`: `SpawnCreature`, `RemoveEntity`, `Teleport`, `RespawnNear`, `GroundHeight`, `QueryUnits`; spawn points in `LoadedMap.Spawns`; obstacles in `LoadedMap.Collision`; navmesh built at startup (`Madlands.Navigation.NavMesh`, DotRecast). | Spawn, despawn, teleport destinations, collision and navmesh overlays (the client has neither: both are server data). |
| Creature AI | `CreatureAI` (state `Ai.Current`, threat list, home), shown by `.info`. | Inspect target (AI state, threat, home). |
| Debug UI | `DebugOverlay` (F3): FPS, ping, map, entity count, position; draggable. | `debug.fps` toggles it; frame time and memory are added. |
| Persistence | Characters saved by `CharacterSaver` (PostgreSQL or in-memory). GM overrides live only on `Player` and are never saved. | Temporary overrides end with the session; awarded items and levels are persistent and labelled so. |
| AI Critic | `tools/AICritic`: the Playtest Controller (`Scripts/Playtest`, `MADLANDS_PLAYTEST` builds only) runs allowlisted actions from a mailbox; scenarios may send fixed GM chat lines as setup; Codex never sends GM commands itself. | A `gm_command` controller action runs the same client command service; scenario scripts only. |
| Tests | xUnit with real realms (`TestRealm`, UDP + HTTP), `ProtocolTests` roundtrips for every message; AICritic's own Python tests. | New messages, the service and the client adapter get tests there. |

Not present: a world clock that can be set (the day/night cycle in the working tree is uncommitted work and
derives from server time), weather, flying movement, a world time scale or pause, debug save slots,
talents. Commands that would need them are deferred instead of simulated.

## Command map

| Command | Implementation | Status |
|---|---|---|
| `player.god_mode` | `Player.GodMode` (existing: `CombatSystem.DealDamage` and `CreatureAI` honour it; it also skips spell costs and cooldowns) | P0 |
| `player.heal_full` | full health; mana and energy to full (`.heal`) | P0 |
| `player.restore_resources` | power to full where the power type fills (mana, energy); rage is left alone | P0 |
| `player.revive` | `CombatSystem.Resurrect` on the spot (`.revive`) | P0 |
| `combat.kill_target` | `CombatSystem.Kill` on the selected living unit (`.kill`) | P0 |
| `player.no_cooldown` | new `Player.GmNoCooldowns`: `SpellSystem` starts no spell cooldowns and current ones are cleared; the global cooldown stays | P0 |
| `player.set_level` | `CombatSystem.SetLevel` within 1..max level (`.level`) | P0 |
| `player.add_xp` | `CombatSystem.GiveXp` (`.xp`) | P0 |
| `player.add_gold` | `ItemSystem.GiveMoney` (`.money`), in copper | P0 |
| `player.add_item` | `ItemSystem.Give` (bag rules; partial = `PARTIAL`) | P0 |
| `teleport.location` | `MapInstance.Teleport` to a destination from the server catalog: the map start, an NPC's or a creature's spawn point | P0 |
| `spawn.entity` | `MapInstance.SpawnCreature` (no respawn), count 1..10, tracked as debug spawns | P0 |
| `debug.fps` | `DebugOverlay` visibility | P0 |
| `debug.collision` | server sends the obstacle triangles (`collision.bin`, what movement and line of sight use) near the player as lines | P0 |
| `debug.navmesh` | server sends navmesh polygon edges near the player as lines | P0 |
| `player.speed_multiplier` | `Player.GmSpeed` (`.speed`) | P1 |
| `combat.damage_multiplier` | new `Player.GmDamagePercent` applied in `CombatSystem.DealDamage` for the GM's own damage | P1 |
| `teleport.coordinates` | `.tele x z`: inside the map, placed on the ground | P1 |
| `teleport.target` | next to the selected unit | P1 |
| `spawn.despawn_debug` | `MapInstance.RemoveEntity` for this GM's tracked debug spawns only | P1 |
| `quest.start` / `quest.complete` / `quest.reset` | `.quest add/complete/reset` logic | P1 |
| `debug.inspect_target` | the `.info` data as structured fields (AI state, threat, home) | P1 |
| `debug.spawn_points` | spawn points near the player as markers | P1 |
| `debug.performance` | FPS, frame time, memory and entity count in the F3 panel | P1 |
| `world.respawn` | `MapInstance.RespawnNear` (`.respawn`) | P1 |
| `dev.reset_overrides` | god mode, no cooldown, speed and damage back to normal; client overlays off | P0 (cleanup) |
| `world.time_of_day`, `world.weather`, `world.time_scale`, `world.pause` | no settable world clock, no weather, and a shared realm must not be paused | deferred |
| `player.fly_mode` | no flying movement mode; the server's movement validation would reject it | deferred |
| `debug.ai_path` | creature paths are internal to `CreatureAI`; would need a new path export | deferred |
| `dev.save_state` / `load_state` / `reset_world` | no isolated debug save slots; a whole-world reset is not supported by `MapInstance` | deferred |
| `dev.verbose_logs` | the menu keeps a bounded history; no separate log level switch | deferred |
| `player.learn_skills` / `reset_talents` | spells come from class and level; there are no talents | deferred |

## Approach

- **One command service on the server** (`Commands/GmCommandService.cs`): typed registry keyed by stable
  ids; checks the GM level (default deny), validates arguments against a shared schema, runs on the game
  thread (network messages are handled between ticks), logs command id, sanitized arguments, result code and
  player, and answers with a structured result. The chat console keeps its text syntax but calls the same
  gameplay code.
- **Shared schema** (`Madlands.Shared/Game/GmCommands.cs`): command ids, categories, argument types and
  bounds, result codes. Server validation, client validation, the menu and the automation catalog all read it.
- **New messages** (protocol version bump): `GmCommand` (client to server), `GmCommandResult`, `GmStatus`
  (GM level and active overrides: the menu's toggles bind to this, not to their own booleans), `GmCatalog`
  (creatures and teleport destinations from server content), `GmDebugGeometry` (overlay lines).
- **One client command service** (`Madlands.ClientCore/GmCommandClient.cs`): validates, sends, tracks
  requests (no duplicate in-flight command), keeps a bounded history, and runs client-side commands (overlays)
  through registered handlers. The menu and the Playtest Controller both call it.
- **The menu** (`Scripts/UI/DeveloperMenu.cs`, styles in `Resources/DeveloperMenu.uss`): left-docked,
  collapsible sections remembered in `Preferences`, catalogs from real data, visible results and history.
- **Esc**: in a GM session Esc first closes what it closes today (chat, held item, loot, vendor, windows,
  target), then the developer menu; with nothing open it opens the developer menu instead of the game menu.
  The game menu stays one click away (header button) and on Shift+Esc. Without GM rights Esc is unchanged.

## Risks

- Uncommitted work by another agent touches `Protocol.cs`, `GameServer.cs`, `Player.cs`, `SpellSystem.cs`,
  `FrontEndController.cs`, `DebugOverlay.cs`, `HudController.cs` and `en.json`. Only this feature's hunks may
  be committed; every client build used for verification is built from the committed tree plus this
  feature only.
- A GM's actions are real on a shared realm (items, levels, kills). The menu says which actions are
  persistent; overrides end with the session and debug spawns are removed when the GM leaves.
