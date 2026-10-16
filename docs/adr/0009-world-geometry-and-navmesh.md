# ADR 0009 – World geometry: exported colliders, navmesh built at startup

**Status:** Decided and implemented 2026-10-16 (Phase 3).

## Decision

**Export (Unity, `Madlands > Export Map`)**

- `height.bin`: the terrain as a height map (Phase 1).
- `collision.bin`: every static, non-trigger collider in the map scene as world-space triangles.
  - Box, sphere and capsule colliders become low-poly shapes of the same size; mesh colliders keep their triangles.
  - Terrain colliders are skipped (the height map covers them), and so are triggers.
- `spawns.json`: every `CreatureSpawnPoint` marker in the scene.
- The scene stays the source of truth: designers place colliders and spawn markers in Unity.

**Server (`server/Madlands.Navigation`)**

- **Navmesh:** built with DotRecast (Recast/Detour, zlib license) **at server startup** from the height map and the obstacle triangles.
  - Obstacle triangles are rasterized as non-walkable, so creatures never path over rocks, trees or walls.
  - Tiled and built in parallel: the 512 × 512 m test map with 49 000 obstacle triangles takes about 0.5 s.
  - Built once per map and shared read-only by all instances of that map. Each instance has its own query object.
- **Line of sight:** ray casts against the height map (sampled along the ray) and the obstacle triangles (in an 8 m grid). Spells, aggro and area effects check it.
- Creatures walk navmesh paths; their height comes from the height map, like players.

## Why

- **The scene is the source of truth**, so there is no second level editor.
- **Building at startup** keeps derived binaries out of git and can never go stale. If a bigger world makes startup slow, we cache the navmesh by a hash of its inputs. That is a local change.
- **Our own LOS** instead of Detour's navmesh ray cast: navmesh rays answer "can I walk there", not "can I see there". Spells need the second question.

## Consequences

- Players are still only validated against the terrain height, not walls. A cheater could walk through a palisade. Rejecting moves that cross obstacle triangles is the next step, together with tolerance for the client's capsule sliding along colliders.
- Tree crowns have no colliders, so they block neither line of sight nor the camera.
