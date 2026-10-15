# ADR 0006 – Nätverk och protokoll

**Status:** Beslutad 2026-10-16 (genomförd i Fas 1)

## Beslut

**Transport**

- UDP via LiteNetLib (MIT).
- Samma version på server och klient, i dag 2.1.4.

**Paketformat**

- Ett meddelande per paket: `ushort opcode` + payload.
- Little-endian.
- Explicit `Write`/`Read` per meddelandeklass i Shared.
- Läsaren kontrollerar gränser och kastar `PacketException`.
- Varje meddelande har ett roundtrip-test.

**Opcodes**

- Klient→server använder 0x0100–0x7FFF.
- Server→klient använder 0x8000–0xFFFF.
- Opcodes numreras aldrig om.

**Version och handskakning**

- `ProtocolVersion.Current` kontrolleras i `Hello`.
- Gammal klient får beskedet "uppdatera".
- Avvisningar skickas som disconnect-data, så att beskedet garanterat kommer fram.

**Leveranssätt**

- Tillståndsändringar går reliable-ordered.
- Massrörelser (`EntityMoves`) går unreliable och ryms i en MTU.

**Takt**

- Servern tickar i 20 Hz.
- Broadcasts sker i 10 Hz.
- Klienten renderar andra spelare 100 ms bakåt i tiden.

**Skydd**

- Rate limit per meddelandetyp.
- Överträdelser räknas, och den som har för många blir utkastad.

## Varför

- Ingen magi och inga beroenden i Shared.
- Formatet är identiskt i Unity och .NET och lätt att testa.
- Transporten är inkapslad, så LiteNetLib kan bytas.
