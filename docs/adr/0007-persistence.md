# ADR 0007 – Persistence

**Status:** Decided 2026-10-16 (implemented in Phase 2)

## Decision

**Database**

- PostgreSQL, via Npgsql and Dapper with explicit SQL.
- SQL migrations run with DbUp from day one.

**Saving**

- Regular player state (position, XP) is saved **write-behind**: periodically, on logout and on important events.
- Saves go through a queue and never block the game tick.
- **Anything that moves value between players** (trade, mail, auction house, loot) is written **immediately in a DB transaction**.

**Login**

- Login happens over HTTPS with Argon2id-hashed passwords.
- Login returns a session token used for the UDP connection.
- It also returns a session key, so traffic can be encrypted later.

**Local development**

- Postgres and Seq run locally via `deploy/docker-compose.yml`.

## Why

Transactions for value transfers make item duplication impossible, which is the classic MMO exploit. Write-behind keeps database latency from affecting gameplay.
