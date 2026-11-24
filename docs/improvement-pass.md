# Hollowmere improvement pass

- [x] Give the player abilities distinct illustrated icons, keeping the existing fallback for other spells.
- [x] Make Ruukhar's Cinder Quake wind-up and its danger area clear, including interrupted casts.
- [x] Add recognizable, nonblocking landmarks along existing routes without changing collision or server map data.
- [x] Illustrate the early armor and bags, keeping a fallback for other items.
- [x] Exercise a new character's opening journey and document observations and remaining playtest limits.
- [ ] Have a person play the opening levels and assess controls, combat feel, quest directions and pacing.
- [x] Add a local startup shortcut that starts the persistent realm when it is not already running.

## Evidence and limits

- `OpeningJourneyTests` uses the real Hollowmere terrain, collision and NPC placements with an in-memory
  realm: a fresh warrior walks from spawn to Edda, accepts Supplies and Sense, visits Holt, returns and
  receives the draughts. Movement is scripted; it does not assess how the controls or combat feel.
- A second opening journey accepts Tamsin's croaker quest, follows the server navmesh to an isolated real
  croaker spawn, defeats it without GM commands and observes quest progress. The scripted client knows the
  target's location and only covers one kill; it does not prove that a new player can find or finish the quest.
- `CombatIntegrationTests.New_character_can_defeat_a_croaker_without_gm_help` exercises level-one fights
  for all three classes against a croaker on a flat test map with the repository's combat data; it does not measure difficulty
  across random encounters or the experience of finding enemies in Hollowmere.
- `CombatIntegrationTests.Interrupted_cinder_quake_clears_the_observers_cast_state` checks the client
  receives the interrupted cast. `ContentLoaderTests.Cinder_quake_warning_matches_authoritative_damage_radius`
  guards the client hint against changes to the authoritative radius in `content/spells.json`. The marker
  shows the radius projected onto terrain; server height and line-of-sight checks still determine hits.
- `ImprovementVisualCheck.Run` checks the art catalog, swapping an item to a spell and back to an initials
  fallback, a food effect's shared picture, eight nonblocking markers and the warning mesh. It renders previews under `builds/`. It does
  not replace checking the warning in a live boss encounter.
- Waystones are visual scene objects with no colliders. The existing collision and spawn exports are not
  regenerated; `HollowmereBuilder` recreates the markers on a future world rebuild.
- `tools/start-local.Tests.ps1` uses mocks for startup, occupied port and failure paths. The shortcut also
  detected the existing local account API, and its PostgreSQL readiness command was checked against the
  running container. A cold start of Docker and PostgreSQL has not been exercised in this pass.
