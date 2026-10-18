# Deploying a realm

How to run a Madlands realm on a server that friends can reach over the internet (the Phase 5 playtests).
For local development see the README.

## What runs where

| Part | Listens on | Reachable from |
|---|---|---|
| Game server (UDP) | `0.0.0.0:7777` | the internet (firewall: UDP 7777) |
| Account API (HTTP) | `127.0.0.1:7780` | only Caddy on the same machine |
| Caddy (TLS) | `:443` (and `:80` for certificates) | the internet: `https://<domain>/api/...` |
| PostgreSQL | `127.0.0.1:5432` (Docker) | only the realm server |

Passwords only ever travel over HTTPS. The realm server refuses to start if `--public-host` is a public
address while its account API would accept plain HTTP from outside (override: `--allow-plain-http`).

## Steps (Linux VPS)

1. **DNS:** point a domain (e.g. `realm.example.com`) at the server.
2. **Firewall:** allow TCP 80 and 443 and UDP 7777; nothing else from outside.
3. **PostgreSQL:** pick a real password and start the database:
   ```bash
   export MADLANDS_PG_PASSWORD='<a long random password>'
   docker compose -f deploy/docker-compose.yml up -d postgres
   ```
4. **Realm server:** publish it with the content files next to it:
   ```bash
   dotnet publish server/Madlands.Server -c Release -o /opt/madlands/server
   cp -r content /opt/madlands/content
   ```
5. **Run it** (as a systemd service, see below):
   ```bash
   /opt/madlands/server/Madlands.Server --content /opt/madlands/content \
       --bind 0.0.0.0 --api-bind 127.0.0.1 --trust-proxy --public-host realm.example.com \
       --connection-string "Host=127.0.0.1;Port=5432;Username=madlands;Password=$MADLANDS_PG_PASSWORD;Database=madlands" \
       --realm-name "Madlands playtest"
   ```
   `--trust-proxy` makes the per-IP rate limits use the players' addresses (from `X-Forwarded-For`)
   instead of Caddy's. GM commands: `Madlands.Server set-gm <account> 3` with the same connection string.
6. **TLS:** install [Caddy](https://caddyserver.com/) and run it with `deploy/Caddyfile`:
   ```bash
   MADLANDS_DOMAIN=realm.example.com caddy run --config deploy/Caddyfile
   ```
7. **Players** enter `https://realm.example.com` in the login screen's server field.

### systemd

```ini
# /etc/systemd/system/madlands.service
[Unit]
Description=Madlands realm
After=network-online.target docker.service

[Service]
WorkingDirectory=/opt/madlands
Environment=MADLANDS_PG_PASSWORD=<the password>
ExecStart=/opt/madlands/server/Madlands.Server --content /opt/madlands/content --bind 0.0.0.0 --api-bind 127.0.0.1 --trust-proxy --public-host realm.example.com --connection-string "Host=127.0.0.1;Port=5432;Username=madlands;Password=${MADLANDS_PG_PASSWORD};Database=madlands"
Restart=on-failure
# The server saves everyone on SIGTERM; give it time.
TimeoutStopSec=30

[Install]
WantedBy=multi-user.target
```

## Housekeeping

- Expired login sessions are deleted at startup and every hour.
- Back up the database: `docker exec madlands-postgres-1 pg_dump -U madlands madlands > backup.sql` (automated backups come in Phase 8).

## Known limits before a wider public test

- **The game connection (UDP) is not encrypted.** The session token from the HTTPS login travels in the
  first UDP message; someone who can read the traffic could reuse it until it expires (24 hours). Fine for
  playtests with friends; before a public test the handshake needs a key exchanged over HTTPS.
- The development quick-login button only appears for servers on this machine or a private network, so it
  never shows for a public realm.
