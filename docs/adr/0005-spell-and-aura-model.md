# ADR 0005 – Spells and auras as lists of effects

**Status:** Decided 2026-10-16, implemented in Phase 3 (`server/Madlands.Server/Combat`, `content/spells.json`).

## Decision

**Spell**

- Consists of cast time, cooldown, global cooldown, cost, range, a target type, flags and a list of **effects**.
- Effect types: `schoolDamage`, `weaponDamage`, `heal`, `applyAura`, `energize`. More get added as handlers, never as special cases in the cast code.
- Each effect picks its own targets: the spell's target, the caster, or enemies/allies in a radius.

**Aura**

- An aura belongs to a spell (its id is the spell id) and has a duration, a tick interval, stacking and a list of effects.
- Aura effect types: `periodicDamage`, `periodicHeal`, `modStat`, `modSpeed`, `modDamageDone`, `stun`, `root`, `absorb`.
- Harmful auras stack per caster (two players can both bleed a wolf); beneficial ones replace each other.
- Amounts are fixed when the aura is applied (from the caster's level at that moment).

**On the server**

- A cast is a state machine: validate → cast (if it has a cast time) → release → hit.
- A cast is interrupted by moving, jumping, a stun or the caster's death.
- At release the cost and cooldown are paid. Projectiles hit after distance / speed; everything else hits at once.
- Hostile targets roll to hit once per cast: the melee table (miss, dodge, parry, crit) for weapon spells, a spell hit roll otherwise.
- Each effect type has a handler (`SpellSystem.ApplyEffect`, `AuraSystem`). Creature abilities use the same system.

**Stats**

- Stats are computed from a base (class, race and level, or creature template and level) plus modifiers from auras: flat first, then percent (`UnitStats`, `AuraModifiers`, `StatFormulas`).
- All formulas live in one file, `StatFormulas.cs`, so balancing means editing that file and content.

**The client**

- Gets a display copy of each spell (`SpellInfo`: name, costs, ranges, times, flags) in `GameData` at login (ADR 0008).
- Predicts nothing except the global cooldown, which it rolls back if the server refuses the cast.

## Why

This model has carried thousands of spells in WoW. New abilities then become mostly data, not code. Phase 3 has 19 spells (three classes plus creature abilities) and needed no special cases.
