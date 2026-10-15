using Madlands.Shared;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;

namespace Madlands.Server.Tests;

public class ProtocolTests
{
    private static readonly MovementState SampleState = new(
        new Vec3(12.5f, 3.25f, -40f), 1.5f, new Vec3(7f, 2f, 0f), MovementFlags.Forward | MovementFlags.Airborne);

    private static T Roundtrip<T>(T message) where T : IMessage, new()
    {
        var writer = new PacketWriter(8);
        Packet.Encode(writer, message);
        var reader = new PacketReader(writer.AsSpan());
        Assert.Equal(message.Opcode, Packet.ReadOpcode(ref reader));
        var copy = new T();
        copy.Read(ref reader);
        Assert.Equal(0, reader.Remaining);
        return copy;
    }

    [Fact]
    public void Primitives_roundtrip()
    {
        var w = new PacketWriter(4); // forces growth
        w.WriteByte(250);
        w.WriteBool(true);
        w.WriteUShort(65000);
        w.WriteShort(-1234);
        w.WriteUInt(4_000_000_000);
        w.WriteInt(-5);
        w.WriteULong(ulong.MaxValue - 1);
        w.WriteLong(long.MinValue + 7);
        w.WriteFloat(-0.125f);
        w.WriteVec3(new Vec3(1, 2, 3));
        w.WriteGuid(EntityGuid.Create(EntityType.Creature, 99));
        w.WriteString("Räksmörgås ⚔"); // deliberate non-ASCII test data (multi-byte UTF-8)
        w.WriteString(null!);

        var r = new PacketReader(w.AsSpan());
        Assert.Equal(250, r.ReadByte());
        Assert.True(r.ReadBool());
        Assert.Equal(65000, r.ReadUShort());
        Assert.Equal(-1234, r.ReadShort());
        Assert.Equal(4_000_000_000u, r.ReadUInt());
        Assert.Equal(-5, r.ReadInt());
        Assert.Equal(ulong.MaxValue - 1, r.ReadULong());
        Assert.Equal(long.MinValue + 7, r.ReadLong());
        Assert.Equal(-0.125f, r.ReadFloat());
        Assert.Equal(new Vec3(1, 2, 3), r.ReadVec3());
        Assert.Equal(EntityGuid.Create(EntityType.Creature, 99), r.ReadGuid());
        Assert.Equal("Räksmörgås ⚔", r.ReadString());
        Assert.Equal("", r.ReadString());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void Truncated_packet_throws_PacketException()
    {
        var w = new PacketWriter();
        w.WriteUShort((ushort)Opcode.Ping);
        w.WriteInt(1); // Ping expects a long
        Assert.Throws<PacketException>(() =>
        {
            var r = new PacketReader(w.AsSpan());
            Packet.ReadOpcode(ref r);
            new Ping().Read(ref r);
        });
    }

    [Fact]
    public void Oversized_string_is_rejected()
    {
        var w = new PacketWriter();
        w.WriteString(new string('x', 100));
        Assert.Throws<PacketException>(() =>
        {
            var r = new PacketReader(w.AsSpan());
            r.ReadString(maxBytes: 64);
        });
    }

    [Fact]
    public void EntityMoves_with_lying_count_is_rejected()
    {
        var w = new PacketWriter();
        w.WriteLong(1000);
        w.WriteUShort(500); // claims 500 entries, has none
        Assert.Throws<PacketException>(() =>
        {
            var r = new PacketReader(w.AsSpan());
            new EntityMoves().Read(ref r);
        });
    }

    [Fact]
    public void Hello_roundtrips()
    {
        var copy = Roundtrip(new Hello { ProtocolVersion = 7, Name = "Åsa" });
        Assert.Equal(7, copy.ProtocolVersion);
        Assert.Equal("Åsa", copy.Name);
    }

    [Fact]
    public void MoveUpdate_roundtrips()
    {
        var copy = Roundtrip(new MoveUpdate { CorrectionAck = 3, ClientTimeMs = 123456789, State = SampleState });
        Assert.Equal(3, copy.CorrectionAck);
        Assert.Equal(123456789, copy.ClientTimeMs);
        Assert.Equal(SampleState, copy.State);
    }

    [Fact]
    public void Ping_and_Pong_roundtrip()
    {
        Assert.Equal(42, Roundtrip(new Ping { ClientTimeMs = 42 }).ClientTimeMs);
        var pong = Roundtrip(new Pong { ClientTimeMs = 1, ServerTimeMs = 2 });
        Assert.Equal((1, 2), (pong.ClientTimeMs, pong.ServerTimeMs));
    }

    [Fact]
    public void Welcome_roundtrips()
    {
        var guid = EntityGuid.Create(EntityType.Player, 5);
        var copy = Roundtrip(new Welcome { PlayerGuid = guid, ServerTimeMs = 99, MapId = 2, InstanceId = 3, Spawn = SampleState });
        Assert.Equal(guid, copy.PlayerGuid);
        Assert.Equal(99, copy.ServerTimeMs);
        Assert.Equal(2, copy.MapId);
        Assert.Equal(3u, copy.InstanceId);
        Assert.Equal(SampleState, copy.Spawn);
    }

    [Fact]
    public void Rejected_roundtrips()
    {
        var copy = Roundtrip(new Rejected { Reason = RejectReason.ProtocolMismatch, Message = "update" });
        Assert.Equal(RejectReason.ProtocolMismatch, copy.Reason);
        Assert.Equal("update", copy.Message);
    }

    [Fact]
    public void EntitySpawn_and_Despawn_roundtrip()
    {
        var guid = EntityGuid.Create(EntityType.Creature, 77);
        var spawn = Roundtrip(new EntitySpawn { Guid = guid, Name = "Wolf", DisplayId = 12, StateServerTimeMs = 555, State = SampleState });
        Assert.Equal(guid, spawn.Guid);
        Assert.Equal("Wolf", spawn.Name);
        Assert.Equal(12u, spawn.DisplayId);
        Assert.Equal(555, spawn.StateServerTimeMs);
        Assert.Equal(SampleState, spawn.State);
        Assert.Equal(guid, Roundtrip(new EntityDespawn { Guid = guid }).Guid);
    }

    [Fact]
    public void EntityMoves_roundtrips_and_respects_mtu()
    {
        var msg = new EntityMoves { ServerTimeMs = 10_000 };
        for (int i = 0; i < 3; i++)
            msg.Entries.Add(new EntityMoveEntry { Guid = EntityGuid.Create(EntityType.Player, (ulong)i + 1), AgeMs = (ushort)(i * 10), State = SampleState });
        var copy = Roundtrip(msg);
        Assert.Equal(10_000, copy.ServerTimeMs);
        Assert.Equal(msg.Entries, copy.Entries);

        int max = EntityMoves.MaxEntriesFor(1200);
        Assert.True(EntityMoves.HeaderSize + max * EntityMoveEntry.WireSize <= 1200);
        Assert.True(EntityMoves.HeaderSize + (max + 1) * EntityMoveEntry.WireSize > 1200);
    }

    [Fact]
    public void MoveCorrection_roundtrips()
    {
        var copy = Roundtrip(new MoveCorrection { CorrectionId = 9, State = SampleState });
        Assert.Equal(9, copy.CorrectionId);
        Assert.Equal(SampleState, copy.State);
    }

    [Fact]
    public void EntityGuid_packs_type_and_counter()
    {
        var guid = EntityGuid.Create(EntityType.Item, 123456789);
        Assert.Equal(EntityType.Item, guid.Type);
        Assert.Equal(123456789UL, guid.Counter);
        Assert.Throws<ArgumentOutOfRangeException>(() => EntityGuid.Create(EntityType.Player, EntityGuid.CounterMask + 1));
    }
}
