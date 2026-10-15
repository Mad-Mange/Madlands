# Färdplan

Varje fas slutar i något spelbart och testat. Hela analysen finns i den ursprungliga planen. Arkitekturbesluten finns i [architecture.md](architecture.md).

## ✅ Fas 0 – Grunden (klar 2026-10-16)

- [x] .NET 10 SDK, `.gitignore`, `.gitattributes` (LFS) och `CLAUDE.md`
- [x] Solution: Shared, ClientCore, Server, Content, Bots, Tests och ContentTool. All bygg-output hamnar i `/artifacts`
- [x] Unity 6-projekt (URP) med Shared och ClientCore som lokala paket, plus asmdef-spärr mot motorkod
- [x] `deploy/docker-compose.yml` (Postgres + Seq) för Fas 2
- [x] Dokumentation: arkitektur, ADR 0001–0007 och GDD-utkast

## ✅ Fas 1 – Gående skelett (klar 2026-10-16)

- [x] Server
  - [x] 20 Hz-loop och LiteNetLib med handskakning och protokollversion
  - [x] Dev-inloggning
  - [x] (mapId, instanceId), AoI-rutnät och spawn/despawn
  - [x] EntityMoves uppdelade per MTU
- [x] Rörelsevalidering: klocka, hastighet, kartgräns, höjdkarta och NaN. Korrigering med kvittering
- [x] Rate limits, räkning av överträdelser och utkastning med orsak
- [x] Klient
  - [x] Styrning och kamera i WoW-stil, hopp utan luftstyrning, autorun
  - [x] Interpolering och dead reckoning för andra spelare
  - [x] Klocksynk och debugpanel (F3)
- [x] Testvärld som genereras av kod, och Export Map (höjdkarta) till `content/maps/0001`
- [x] Botar: 200 st, samma klientkod som spelet
- [x] 46 tester, bland annat riktig UDP-integration
- [x] **Uppmätt:** 200 botar och 1 klient, 0 korrigeringar, tick i snitt 0,4–0,9 ms

**Väntar på dig:** speltesta styrning och känsla med två klienter, se README. Säg vad som känns fel.

## Fas 2 – Konton, karaktärer, persistens

- [ ] PostgreSQL och DbUp-migreringar
- [ ] Konton: registrering och inloggning över HTTPS med Argon2id, sessionstoken
- [ ] Karaktärer: skapa, välja och ta bort (namn, ras och klass som platshållare). Spelar-id kommer från databasen
- [ ] Sparning av position och tillstånd med write-behind
- [ ] Inloggnings- och karaktärsvalsskärmar i UI Toolkit
- [ ] Lokalisering: textnycklar för sv och en (installera Localization-paketet)

## Fas 3 – Kärnstrid

- [ ] Map-export del 2: statisk geometri → navmesh och LoS (DotRecast)
- [ ] Välja mål och autoattack. Spell- och aurasystemet enligt ADR 0005. Stats och modifierare
- [ ] NPC:er: spawns som data, AI (aggro, jakt, leash, threat), död och respawn, XP och nivåer
- [ ] HUD: unit frames, action bar, castbar, stridstext, namnplattor
- [ ] GM-kommandon (`.tele`, `.spawn`, `.level` …)

## Fas 4 – Items, loot, quests
## Fas 5 – Vertikal skiva: startzonen (grinden för "är det kul?")
## Fas 6 – Socialt · Fas 7 – Dungeons · Fas 8 – Större värld och drift · Fas 9 – Djup · Fas 10 – Lansering

Se den ursprungliga planen för innehållet i varje fas.

## Teknisk skuld och uppföljning

- **Bandbredd:** `EntityMoves` skickar okomprimerade flyttal. Det blir cirka 15 MB/s ut när 200 spelare ser varandra.
  - Kvantisera position, yaw och hastighet.
  - Glesare uppdateringar på avstånd.
  - Gör det senast före Fas 5.
- **Tick-spikar:** p99 och max nådde ibland 8–17 ms under botlast. Mät GC och schemaläggning, och flytta botarna till en annan maskin vid mätning.
- **Tidsstämplar för rörelse:** servern stämplar med mottagningstid, vilket ger lite jitter över internet. Överväg att mappa klientens tid (redan validerad).
- **Servern känner bara höjder, inte väggar och träd.** LoS och navmesh i Fas 3.
- **IMGUI används för debug-UI och namnskyltar.** Byt till UI Toolkit i Fas 2–3.
- **Addressables och Localization** installeras när de första gången används.
- **CI** (GitHub Actions: build, test, content validate) när repot har en remote.
