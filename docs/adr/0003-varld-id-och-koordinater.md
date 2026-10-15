# ADR 0003 – Världsindelning, id:n och koordinater

**Status:** Beslutad 2026-10-16 (genomförd i Fas 1)

## Beslut

**Världsindelning**

- Varje entitet tillhör en `(mapId, instanceId)`.
- Instans 1 är den öppna världen.
- Dungeons, raids och overflow-"layers" blir fler instanser av samma map.

**Id:n**

- `EntityGuid` är 64 bitar: 8 bitar typ (Player, Creature, GameObject, Item) och 56 bitar räknare.
- De högsta bitarna i räknaren är reserverade för realm/shard.

**Koordinater**

- Meter, +Y uppåt, yaw 0 = +Z. Samma som Unity, och samma på servern.
- En map ligger inom ±8192 m från origo.

**Area of interest (AoI)**

- Rutnät med 64 m stora celler.
- En spelare ser 150 m och tappar entiteter vid 170 m.

## Varför

- Med instans-id från början blir dungeons och layering ingen ombyggnad.
- Med typen i id:t behöver ingen slå upp vad ett id avser.
- Med samma koordinater som Unity försvinner alla konverteringsbuggar.
