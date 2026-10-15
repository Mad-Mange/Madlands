# ADR 0004 – Innehåll som data

**Status:** Beslutad 2026-10-16 (påbörjad i Fas 1: maps)

## Beslut

**Var innehållet finns**

- Allt spelinnehåll ligger som text i `content/`, med JSON Schema i `content/schemas/`.
- Det gäller maps, spells, items, creatures, quests och loot.

**Id:n**

- Id:n är stabila heltal som aldrig återanvänds.

**Validering**

- `Madlands.Content` validerar allt.
- Alla fel samlas in och rapporteras samtidigt.
- Referenser mellan filer kontrolleras.
- ContentTool kör samma validering.

**Kartor**

- Kartgeometri exporteras från Unity-scenen med `Madlands > Export Map`.
- Scenen är källan.

**Klientens utseende**

- Servern skickar `displayId`.
- Klienten slår upp displayId i en tabell som pekar på Addressable-nycklar (Fas 5).

## Varför

- Innehåll är flaskhalsen i en MMO.
- Textdata kan skrivas och granskas av både människor och AI, och kan diffas i git.
- Ingen kod behöver kompileras om när innehåll ändras.
