# ADR 0005 – Spells och auror som effektlistor

**Status:** Beslutad 2026-10-16 (genomförs i Fas 3)

## Beslut

**Spell**

- Består av kasttid, cooldown, GCD, kostnad, räckvidd, målval och en lista av **effekter**.
- Exempel på effekter: SchoolDamage, Heal, ApplyAura, Teleport.

**Aura**

- Består av varaktighet, intervall, regler för stackning och en lista av effekter.
- Exempel på effekter: PeriodicDamage, ModStat, ModSpeed, Stun.

**På servern**

- Ett kast är en tillståndsmaskin: Preparing → Casting → Done eller Interrupted.
- Varje effekttyp har sin hanterare i ett register.
- Specialfall får skript-hooks.

**Stats**

- Stats räknas fram ur stackar av modifierare: bas + utrustning + auror, platt + procent.

## Varför

Modellen har burit tusentals spells i WoW. Nya förmågor blir då mest data, inte kod.
