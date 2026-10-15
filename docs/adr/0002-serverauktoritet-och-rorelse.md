# ADR 0002 – Serverauktoritet och rörelse i WoW-stil

**Status:** Beslutad 2026-10-16 (genomförd i Fas 1)

## Beslut

Servern bestämmer över allt spelinnehåll. Det enda undantaget är spelarens egen rörelse: den förutses av klienten och valideras av servern.

**Hur rörelse skickas:**

- som tillstånd (position, yaw, hastighet, flaggor),
- direkt när något ändras,
- med en heartbeat var 500:e ms.

**Vad servern kontrollerar:**

- att klientklockan inte springer före,
- att hastigheten är rimlig,
- att positionen ligger inom kartan och nära marken.

Ett omöjligt drag ger en korrigering med kvitterings-id.

## Varför

Tab-target-strid tål latens. Ren serverprediktion med reconciliation hade gett mycket mer komplexitet utan att märkas för spelaren. WoW har använt samma modell i 20 år.

## Konsekvenser

**Svagheter:**

- Klienten kan "glida" upp till toleransen (15 % plus 0,3 m per steg).
- Servern känner bara terrängens höjd, inte väggar och träd.

LoS och navmesh från Unity-export kommer i Fas 3 och gör det möjligt att stoppa wallhack-rörelse.
