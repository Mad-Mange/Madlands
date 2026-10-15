# ADR 0007 – Persistens

**Status:** Beslutad 2026-10-16 (genomförs i Fas 2)

## Beslut

**Databas**

- PostgreSQL, via Npgsql och Dapper med explicit SQL.
- SQL-migreringar körs med DbUp från dag 1.

**Sparning**

- Vanligt spelartillstånd (position, XP) sparas med **write-behind**: periodiskt, vid utloggning och vid viktiga händelser.
- Sparningen ligger i en kö och blockerar aldrig spel-ticken.
- **Allt som flyttar värde mellan spelare** (trade, post, auktionshus, loot) skrivs **direkt i en DB-transaktion**.

**Inloggning**

- Inloggning sker över HTTPS med Argon2id-hashade lösenord.
- Inloggningen ger en sessionstoken som används för UDP-anslutningen.
- Den ger också en sessionsnyckel som gör att trafiken kan krypteras senare.

**Lokal utveckling**

- Lokalt körs Postgres och Seq via `deploy/docker-compose.yml`.

## Varför

Med transaktioner för värdeöverföringar kan föremål inte dupliceras, som i det klassiska MMO-exploitet. Write-behind gör att databasens latens inte påverkar spelet.
