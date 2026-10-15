# ADR 0003 – World partitioning, ids and coordinates

**Status:** Decided 2026-10-16 (implemented in Phase 1)

## Decision

**World partitioning**

- Every entity belongs to a `(mapId, instanceId)`.
- Instance 1 is the open world.
- Dungeons, raids and overflow "layers" become additional instances of the same map.

**Ids**

- `EntityGuid` is 64 bits: 8 bits of type (Player, Creature, GameObject, Item) and a 56-bit counter.
- The highest bits of the counter are reserved for realm/shard.

**Coordinates**

- Meters, +Y up, yaw 0 = +Z. The same as Unity, and the same on the server.
- A map stays within ±8192 m of the origin.

**Area of interest (AoI)**

- A grid of 64 m cells.
- A player sees 150 m and drops entities at 170 m.

## Why

- With an instance id from the start, dungeons and layering need no rebuild.
- With the type inside the id, nobody needs a lookup to know what an id refers to.
- With Unity's coordinates everywhere, all conversion bugs disappear.
