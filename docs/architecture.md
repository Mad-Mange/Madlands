# Arkitektur

## Översikt

```
 Unity-klient (bara "vy")                       Madlands.Server (.NET 10, en process i början)
 ┌────────────────────────┐   UDP / LiteNetLib   ┌──────────────────────────────────────────┐
 │ PlayerController       │ ◄──────────────────► │ GameServer: nätverk + 20 Hz-loop         │
 │  (prediktion av egen   │                      │  └ WorldManager                          │
 │   rörelse)             │                      │     └ MapInstance (mapId, instanceId)    │
 │ WorldView (interpol.)  │                      │        ├ AoiGrid (64 m celler)           │
 │ CameraController, UI   │                      │        ├ MovementValidator per spelare   │
 └──────────┬─────────────┘                      │        └ Spawn/Despawn/EntityMoves       │
            │ samma kod som botarna              └───────────────┬──────────────────────────┘
 ┌──────────┴─────────────┐                                      │
 │ Madlands.ClientCore    │                      Madlands.Content (laddar + validerar content/)
 │ GameClient, ClockSync, │                                      │
 │ InterpolationBuffer,   │                      content/maps/0001/{map.json, height.bin}
 │ MovementSender         │                        ▲ skrivs av Unity: Madlands > Export Map
 └──────────┬─────────────┘
 ┌──────────┴──────────────────────────────────────────────────────────────┐
 │ Madlands.Shared: protokoll (opcodes, meddelanden, PacketWriter/Reader),  │
 │ MovementState/-flaggor, EntityGuid, Vec3, GameConstants, HeightMap       │
 └──────────────────────────────────────────────────────────────────────────┘
```

**Grundprincip:** Servern bestämmer över allt. Spellogiken ligger i servern och i Shared, och klienten är en vy. Därför går det att byta klientmotor utan att skriva om spelet.

## Dataflöde: rörelse

1. Klienten flyttar sin gubbe direkt, utan att vänta på servern.
2. `MovementSender` avgör när ett `MoveUpdate` ska skickas:
   - direkt när flaggorna ändras (start/stopp, hopp),
   - högst 10 Hz när man svänger med musen,
   - en heartbeat var 500:e ms medan man rör sig,
   - när den faktiska positionen avviker från det andra klienter räknar fram.
3. Servern (`MovementValidator`) kontrollerar fem saker:
   - att klientklockan inte går fortare än serverns (stoppar speedhack),
   - att sträckan ryms inom springhastigheten,
   - att positionen ligger inom kartan,
   - att positionen är nära marken (höjdkartan),
   - att inga värden är NaN.

   Ett omöjligt drag ger en `MoveCorrection`. Klienten kvitterar korrigeringens id i sina följande uppdateringar, så att uppdateringar som redan var på väg ignoreras.
4. Var 2:a tick (10 Hz) går servern igenom varje spelares närområde:
   - Den skickar `EntitySpawn`/`EntityDespawn` (sikt 150 m, despawn vid 170 m).
   - Den skickar `EntityMoves` med entiteter som ändrats den senaste sekunden. Det går unreliable, delas upp så att varje paket ryms i en MTU och skickas om i 1 s så att tappade paket inte märks.
5. Andra klienter renderar 100 ms bakåt i tiden och interpolerar mellan sampel. Efter senaste samplet dead-reckonar de (max 1 s). Gubbar som står på marken följer klientens terräng.

## Trådmodell

All spellogik körs på servens loop-tråd. LiteNetLib:s händelser körs från `PollEvents()` i början av varje tick. Map-instanser delar inget muterbart tillstånd och har egna `PacketWriter`. Därför kan de flyttas till egna trådar eller processer senare (Fas 8) utan att logiken behöver skrivas om.

## Prestanda (mätt 2026-10-16, Fas 1)

- **Lasten:** 200 botar och 1 Unity-klient inom 120 m från varandra, så att alla ser alla (värsta fallet).
- **Servertick:** i snitt 0,4–0,9 ms, p99 2–8 ms, max 17 ms. Målet är under 5 ms i snitt.
- **Utgående trafik:** cirka 15 MB/s totalt. Det beror på 200 × 190 synliga entiteter × 10 Hz med okomprimerade flyttal. Kvantisering och lägre sändtakt på avstånd finns i roadmap.
- **Rörelser:** 0 korrigeringar under 75 s.

## Beslut som är dyra att ändra

Se [adr/](adr/). Kort sammanfattning:

| # | Beslut |
|---|---|
| 0001 | Unity-klient som vy, motoroberoende .NET-server, delad C# |
| 0002 | Serverauktoritet; klienten förutser egen rörelse, servern validerar |
| 0003 | Världen delas in i (mapId, instanceId); 64-bitars EntityGuid; koordinater som i Unity |
| 0004 | Innehåll som data med stabila id:n; servern skickar displayId |
| 0005 | Spells/auror som listor av effekter (WoW-modellen) – genomförs i Fas 3 |
| 0006 | LiteNetLib + egen binär serialisering + protokollversion |
| 0007 | PostgreSQL; write-behind; värdeöverföringar i transaktioner – genomförs i Fas 2 |

## Kända förenklingar i Fas 1 (medvetna)

- **Dev-inloggning med bara ett namn.** Konton och tokens kommer i Fas 2.
- **Testvärlden genereras av kod** (`TestWorldBuilder`). Riktiga zoner byggs för hand i Fas 5.
- **Debug-UI och namnskyltar använder IMGUI.** UI Toolkit kommer i Fas 2–3.
- **Addressables och Localization är inte installerade ännu.** Paketen läggs till när de används (Fas 2/5). Principerna ligger fast redan nu: displayId i stället för prefab-namn, och texter som nycklar.
- **Tangenterna läses direkt från tangentbordet.** Ombindningsbara kontroller kommer i Fas 5.
- **Bara en terrängruta, ingen streaming.**
