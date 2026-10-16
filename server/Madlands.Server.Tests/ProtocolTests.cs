using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;

namespace Madlands.Server.Tests;

public class ProtocolTests
{
    private static readonly MovementState SampleState = new(
        new Vec3(12.5f, 3.25f, -40f), 1.5f, new Vec3(7f, 2f, 0f), MovementFlags.Forward | MovementFlags.Airborne);

    internal static T Roundtrip<T>(T message) where T : IMessage, new()
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
        var copy = Roundtrip(new Hello { ProtocolVersion = 7, SessionToken = "abc-123_XYZ", CharacterId = 42 });
        Assert.Equal(7, copy.ProtocolVersion);
        Assert.Equal("abc-123_XYZ", copy.SessionToken);
        Assert.Equal(42UL, copy.CharacterId);
    }

    [Fact]
    public void Account_requests_roundtrip()
    {
        var register = Roundtrip(new RegisterRequest { Username = "åsa", Password = "hunter22!" });
        Assert.Equal(("åsa", "hunter22!"), (register.Username, register.Password));
        var login = Roundtrip(new LoginRequest { Username = "bob", Password = "secret-pw" });
        Assert.Equal(("bob", "secret-pw"), (login.Username, login.Password));
        Roundtrip(new CharacterListRequest());
        var create = Roundtrip(new CreateCharacterRequest { Name = "Alice", Race = 2, Class = 3 });
        Assert.Equal(("Alice", (byte)2, (byte)3), (create.Name, create.Race, create.Class));
        Assert.Equal(99UL, Roundtrip(new DeleteCharacterRequest { CharacterId = 99 }).CharacterId);
        Roundtrip(new LogoutRequest());
    }

    [Fact]
    public void Account_responses_roundtrip()
    {
        var auth = Roundtrip(new AuthResponse
        {
            Result = ApiResultCode.Ok, Message = "", SessionToken = "tok", RealmName = "Realm", RealmHost = "10.0.0.1", RealmPort = 7777,
        });
        Assert.Equal(("tok", "Realm", "10.0.0.1", (ushort)7777), (auth.SessionToken, auth.RealmName, auth.RealmHost, auth.RealmPort));
        Assert.True(auth.Ok);

        var character = new CharacterSummary { Id = 5, Name = "Alice", Race = 1, Class = 2, Level = 3, MapId = 1 };
        var list = new CharacterListResponse { Result = ApiResultCode.Ok, Message = "", MaxCharacters = 10 };
        list.Characters.Add(character);
        list.Races.Add(new NamedOption(1, "Brightvale Human"));
        list.Classes.Add(new NamedOption(2, "Shardmage"));
        var listCopy = Roundtrip(list);
        Assert.Equal(10, listCopy.MaxCharacters);
        Assert.Equal(character, listCopy.Characters.Single());
        Assert.Equal("Brightvale Human", listCopy.Races.Single().Name);
        Assert.Equal(2, listCopy.Classes.Single().Id);

        var created = Roundtrip(new CreateCharacterResponse { Result = ApiResultCode.Ok, Message = "", Character = character });
        Assert.Equal(character, created.Character);

        var error = Roundtrip(new ApiResult { Result = ApiResultCode.NameTaken, Message = "taken" });
        Assert.Equal((ApiResultCode.NameTaken, "taken"), (error.Result, error.Message));
        Assert.False(error.Ok);
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
        var welcome = new Welcome { PlayerGuid = guid, Name = "Alice", ServerTimeMs = 99, MapId = 2, InstanceId = 3, Spawn = SampleState };
        welcome.Self.CopyFrom(CombatProtocolTests.SampleValues);
        var copy = Roundtrip(welcome);
        Assert.Equal(guid, copy.PlayerGuid);
        Assert.Equal("Alice", copy.Name);
        Assert.True(copy.Self.ValueEquals(CombatProtocolTests.SampleValues));
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
        var message = new EntitySpawn { Guid = guid, Name = "Wolf", DisplayId = 12, StateServerTimeMs = 555, State = SampleState };
        message.Unit.CopyFrom(CombatProtocolTests.SampleValues);
        var spawn = Roundtrip(message);
        Assert.True(spawn.Unit.ValueEquals(CombatProtocolTests.SampleValues));
        Assert.Equal(guid, spawn.Guid);
        Assert.Equal("Wolf", spawn.Name);
        Assert.Equal(12u, spawn.DisplayId);
        Assert.Equal(555, spawn.StateServerTimeMs);
        Assert.Equal(SampleState, spawn.State);
        Assert.Equal(guid, Roundtrip(new EntityDespawn { Guid = guid }).Guid);

        // Non-unit entities carry no unit values.
        var objectGuid = EntityGuid.Create(EntityType.GameObject, 3);
        Assert.Equal(objectGuid, Roundtrip(new EntitySpawn { Guid = objectGuid, Name = "Chest", DisplayId = 5, State = SampleState }).Guid);
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
