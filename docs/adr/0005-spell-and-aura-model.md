# ADR 0005 – Spells and auras as lists of effects

**Status:** Decided 2026-10-16 (implemented in Phase 3)

## Decision

**Spell**

- Consists of cast time, cooldown, GCD, cost, range, targeting and a list of **effects**.
- Example effects: SchoolDamage, Heal, ApplyAura, Teleport.

**Aura**

- Consists of duration, tick interval, stacking rules and a list of effects.
- Example effects: PeriodicDamage, ModStat, ModSpeed, Stun.

**On the server**

- A cast is a state machine: Preparing → Casting → Done or Interrupted.
- Each effect type has a handler in a registry.
- Special cases get script hooks.

**Stats**

- Stats are computed from stacks of modifiers: base + gear + auras, flat + percent.

## Why

This model has carried thousands of spells in WoW. New abilities then become mostly data, not code.
