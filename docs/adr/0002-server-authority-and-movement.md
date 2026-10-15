# ADR 0002 – Server authority and WoW-style movement

**Status:** Decided 2026-10-16 (implemented in Phase 1)

## Decision

The server is the authority over all gameplay. The only exception is the player's own movement: the client predicts it and the server validates it.

**How movement is sent:**

- as state (position, yaw, velocity, flags),
- immediately when something changes,
- with a heartbeat every 500 ms.

**What the server checks:**

- that the client clock does not run ahead,
- that the speed is plausible,
- that the position is inside the map and near the ground.

An impossible move triggers a correction with an acknowledgement id.

## Why

Tab-target combat tolerates latency. Full server-side prediction with reconciliation would add a lot of complexity without the player noticing any difference. WoW has used this model for 20 years.

## Consequences

**Weaknesses:**

- A client can "slide" up to the tolerance (15 % plus 0.3 m per step).
- The server only knows the terrain height, not walls and trees.

Line of sight and a navmesh exported from Unity come in Phase 3 and make it possible to stop movement through walls.
