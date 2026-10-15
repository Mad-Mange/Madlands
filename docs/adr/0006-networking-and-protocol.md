# ADR 0006 – Networking and protocol

**Status:** Decided 2026-10-16 (implemented in Phase 1)

## Decision

**Transport**

- UDP via LiteNetLib (MIT).
- The same version on server and client, currently 2.1.4.

**Packet format**

- One message per packet: `ushort opcode` + payload.
- Little-endian.
- Explicit `Write`/`Read` per message class in Shared.
- The reader checks bounds and throws `PacketException`.
- Every message has a roundtrip test.

**Opcodes**

- Client→server uses 0x0100–0x7FFF.
- Server→client uses 0x8000–0xFFFF.
- Opcodes are never renumbered.

**Version and handshake**

- `ProtocolVersion.Current` is checked in `Hello`.
- An outdated client is told to update.
- Rejections are sent as disconnect data, so the reason is guaranteed to arrive.

**Delivery**

- State changes are reliable-ordered.
- Bulk movement (`EntityMoves`) is unreliable and fits in one MTU.

**Rates**

- The server ticks at 20 Hz.
- Broadcasts happen at 10 Hz.
- Clients render other players 100 ms in the past.

**Protection**

- A rate limit per message type.
- Violations are counted, and a client with too many is kicked.

## Why

- No magic and no dependencies in Shared.
- The format is identical in Unity and .NET and easy to test.
- The transport is encapsulated, so LiteNetLib can be replaced.
