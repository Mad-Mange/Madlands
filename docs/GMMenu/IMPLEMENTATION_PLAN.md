# MadLands — Developer / GM Menu

## 1. Objective
Implement a development-only in-game GM and debug menu inspired by `design-reference.png` (concept 3). The menu opens on the left with Escape and gives developers and automated test tools fast access to existing gameplay systems. Preserve existing gameplay, HUD, architecture and assets. The concept is a visual reference, not a requirement to reproduce its fictional world, item names or every pictured feature.

## 2. First inspect the repository
Before coding, read repository instructions (AGENTS.md, CLAUDE.md and applicable project documentation). Identify the engine/UI framework, input handling, pause/menu stack, player controller, combat, inventory, progression, quest, spawning, world, persistence, networking and existing debug tools. Locate AI Critic and test infrastructure if present. Record findings and map each planned command to a real service/API. Do not invent parallel gameplay systems to make a button work. Mark unsupported features as deferred; do not silently simulate success. Keep unrelated changes out of the implementation.

## 3. UX and visual design
- Left-anchored overlay, approximately 27–30% of reference viewport width; responsive width with sensible minimum/maximum, vertical scrolling on smaller screens. Keep the world, minimap and action bar visible where possible.
- Match concept 3: dark textured metal, restrained red section headers, warm gold icons, subtle rivets and hazard-stripe accents. Reuse existing MadLands typography, icons and UI assets first. Do not use the screenshot as a flattened interactive UI or copy its background.
- Header: MadLands, Developer Debug Menu, ESC hint, close button. Clearly mark DEV/GM mode.
- Collapsible sections: GM Commands, Player Tools, World Settings, Spawn Entities, Debugging, plus Teleport, Quest Tools and Developer Tools as needed. Remember expanded sections locally if straightforward.
- Prefer checkboxes for persistent toggles, buttons for actions, numeric fields with validation, sliders for bounded continuous settings, searchable selectors for actual game definitions. Show selected target, action feedback, disabled reason and active toggle state. Provide a compact recent command history.
- Escape opens/closes the overlay in development mode without breaking the existing pause menu. Respect modal dialogs and text input focus. Outside authorized mode Escape retains its normal behavior. When open, release pointer capture as appropriate and prevent gameplay inputs from firing through the UI. Restore focus/cursor/input state on close, including after scene transitions. Default: simulation continues; optional Pause World only where supported. Do not globally pause multiplayer.
- Verify at 1920x1080, 2560x1440 and ultrawide (including 32:9 where supported). Ensure controls remain readable, accessible and scrollable.

## 4. Architecture
Adapt names and layout to existing conventions rather than enforcing a new framework.

```text
DeveloperMenu UI ──┐
Automation adapter ─┼─> GMCommandService / registry
Existing console ───┘       │ authorization + validation
                            │ dispatch to game thread / authority
                            ▼
                     Existing gameplay services
                            │
                     Result + audit log + UI refresh
```

Create a typed command registry or extend the existing one. Each command has a stable ID, category, display name, typed argument schema, authorization, availability predicate, execution handler and structured result. Use existing dependency injection/lifecycle patterns. UI and tests must call the same implementation. Keep the UI free of direct gameplay mutations. Bind toggles to authoritative runtime state, not independent UI-only booleans. Dispatch on the required engine thread; prevent duplicate in-flight actions. Handle missing player/target/world, unloaded scenes, invalid definitions and exceptions gracefully. Reset or rebind scene-specific subscriptions; avoid leaks.

Example logical contract (not an imposed programming language):

```json
{"commandId":"player.set_level","args":{"level":10},"requestId":"example-001"}
```

```json
{"requestId":"example-001","ok":true,"code":"OK","message":"Level updated","data":{"level":10}}
```

Return explicit codes such as UNAUTHORIZED, INVALID_ARGUMENT, NOT_AVAILABLE, NO_TARGET and EXECUTION_FAILED. Expose read-only command discovery and state inspection for tooling. Never execute arbitrary shell commands or arbitrary code via this interface.

## 5. Command backlog
Implement P0 first; P1/P2 only after confirming underlying systems. IDs are proposed and may be adapted consistently.

| Priority | Command ID | Inputs / behavior |
|---|---|---|
| P0 | player.god_mode | bool; use existing damage pipeline |
| P0 | player.heal_full | Restore HP via existing health API |
| P0 | player.restore_resources | Restore supported mana/energy resources |
| P0 | player.revive | Existing respawn/revive flow |
| P0 | combat.kill_target | Require valid selected enemy; existing death pipeline |
| P0 | player.no_cooldown | bool; clearly define cooldown semantics |
| P0 | player.set_level | Valid level range; preserve progression invariants |
| P0 | player.add_xp | Valid amount; existing XP pipeline |
| P0 | player.add_gold | Valid amount; existing currency service |
| P0 | player.add_item | Actual item ID and bounded quantity; existing inventory rules |
| P0 | teleport.location | Existing safe destination ID |
| P0 | spawn.entity | Existing creature/NPC ID, bounded count; valid nearby positions |
| P0 | debug.fps | bool; frame rate and frame time overlay |
| P0 | debug.collision | bool; engine-supported visualization |
| P0 | debug.navmesh | bool; engine-supported navigation visualization |
| P1 | player.speed_multiplier | Bounded multiplier; reset to default |
| P1 | player.fly_mode | bool; restore movement mode safely |
| P1 | combat.damage_multiplier | Bounded multiplier; reset to default |
| P1 | world.time_of_day | Valid hour/minute; current world time |
| P1 | world.weather | Actual supported weather definition |
| P1 | world.time_scale | Bounded scale; reset to 1x |
| P1 | world.pause | Local/offline only unless existing server policy permits |
| P1 | teleport.coordinates | Finite coordinates; safe placement, collision and terrain checks |
| P1 | teleport.target | Valid target and safe offset |
| P1 | spawn.despawn_debug | Remove only tracked debug-spawned entities |
| P1 | quest.start / complete / reset | Actual quest ID; use quest state machine |
| P1 | debug.ai_state / aggro / path | Selected entity or scoped visualization |
| P1 | debug.spawn_points | bool; show actual spawn locations |
| P1 | debug.performance | FPS, frame time, memory and entity count where available |
| P2 | dev.save_state / load_state | Dedicated debug slots, never overwrite normal saves |
| P2 | dev.reset_world | Explicit confirmation; development session only |
| P2 | dev.verbose_logs | bool; bounded history and filters |
| P2 | player.learn_skills / reset_talents | Existing skill/talent system only |

Use friendly names plus stable IDs in selectors; source options from real catalogs/registries. Do not assume the reference image's Rockbuster or Shard Frog exists. Cap spawn counts and prevent runaway entity creation. Report inventory-full or partial completion accurately. Destructive world operations require explicit confirmation; automation must opt in explicitly to these actions. Provide Reset Debug Overrides and clean up temporary state on session exit. Clearly distinguish persistent gameplay changes (e.g. awarded items) from temporary overrides.

## 6. Authorization and persistence
- Default deny in production/release. Prefer compiling out developer UI and handlers where supported; also enforce authorization at the command service/authoritative server, not merely via hidden buttons.
- Local development sessions may enable the tools through an explicit development configuration. Multiplayer, if present, must require server-side GM authorization; clients cannot grant themselves privileges. No public remote command endpoint.
- Do not persist temporary cheats into normal game saves. Use isolated debug save slots and test profiles for irreversible progression actions where feasible. Avoid secrets in logs. Log command ID, sanitized arguments, result, timestamp, actor/session if available. Bound log storage.

## 7. AI Critic / automated testing integration
If the existing AI Critic infrastructure is present, integrate using its established conventions and permissions. Do not replace its current review workflow or require a new AI framework. Otherwise provide a documented local in-process adapter and example tests; do not add a remote server just for this task.

Provide a discoverable list of supported commands and typed schemas; execute by stable ID and retrieve structured results and current state. Example exploration sequence: load isolated test session, teleport to a valid quarry/test location, set level, spawn a supported enemy, inspect AI state, run combat test, collect evidence, remove debug spawns and restore overrides. Use actual repository IDs, not illustrative names. Distinguish testing with cheats enabled from ordinary gameplay tests. Keep critic scoring/evidence independent of GM commands and never count command success alone as proof that gameplay works.

## 8. Implementation phases
1. Discovery: map existing systems, command feasibility and input/menu conflict risks; document actual paths and chosen approach.
2. Vertical slice: registry, authorization, log, left-side menu shell, Escape routing, God Mode, Heal, FPS; test end-to-end before expanding.
3. P0: connect supported player/combat/teleport/spawn/debug controls; dynamic catalogs and validation; implement cleanup and state synchronization.
4. P1: world/quest/AI debug and richer controls where existing APIs support them. Add missing features to backlog rather than creating unrelated subsystems.
5. P2 and automation: isolated persistence tools, confirmations, AI Critic adapter, command discovery and documented sample workflow.
6. Polish: style against reference image, responsive layout, keyboard/focus behavior, performance, regression and release-build security checks.

After each phase, run relevant existing tests and build checks. Use small reviewable changes; do not claim tests were run if they were not. Stop and report blockers for unsupported features rather than masking them with stubs.

## 9. Acceptance tests
- Escape opens and closes exactly once, restores cursor/input, and does not regress pause, inventory, chat or modal handling.
- While interacting with menu, clicks and typing do not trigger movement/combat; simulation continues by default.
- UI shows actual state after command, scene change, respawn and failed command. Repeated open/close does not leak listeners or create duplicate menus.
- God mode prevents damage through supported pipelines and restores normal damage when disabled; toggles reset correctly.
- Level, XP, gold and inventory use existing constraints; invalid and excessive inputs fail safely with visible feedback.
- Spawn valid entities; invalid IDs/counts fail; cleanup does not delete ordinary entities. Teleport lands safely.
- Debug overlays can be toggled and removed; disabled tools impose negligible overhead and do not spam logs.
- Unauthorized/release invocations fail even when called directly, including via automation/network paths if present. Debug saves do not overwrite normal saves.
- Command adapter produces the same result as UI for equivalent calls; structured failure codes are covered by tests.
- Screenshots at standard and ultrawide resolutions demonstrate readable, left-aligned menu with gameplay still visible.

## 10. Deliverables and completion report
Deliver implemented code/assets and tests following repo conventions, command reference with real IDs/arguments, instructions to enable the menu and use automation, and a short implementation report. Include files changed, supported/deferred commands, architecture decisions, screenshots, build/test commands and actual outcomes, security limitations and any manual verification still required. Keep this design reference and plan under docs/GMMenu. Do not commit unrelated changes or modify existing AI Critic behavior without a concrete integration need.
