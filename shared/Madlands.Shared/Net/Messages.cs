using System.Collections.Generic;
using Madlands.Shared.Game;
using Madlands.Shared.Movement;

namespace Madlands.Shared.Net
{
    // Message classes are reusable: receivers keep one instance per type and call Read() into it.
    // Every message has a roundtrip test in Madlands.Server.Tests/ProtocolTests.cs.

    // ---------------------------------------------------------------- Client -> Server

    /// <summary>
    /// First message after connecting: the session token from the account API login plus the
    /// character to play. The server loads the character from the database before Welcome.
    /// </summary>
    public sealed class Hello : IMessage
    {
        public const int MaxNameBytes = 64;
        public const int MaxTokenBytes = 128;

        public ushort ProtocolVersion;
        public string SessionToken;
        public ulong CharacterId;

        public Opcode Opcode => Opcode.Hello;

        public void Write(PacketWriter w)
        {
            w.WriteUShort(ProtocolVersion);
            w.WriteString(SessionToken);
            w.WriteULong(CharacterId);
        }

        public void Read(ref PacketReader r)
        {
            ProtocolVersion = r.ReadUShort();
            SessionToken = r.ReadString(MaxTokenBytes);
            CharacterId = r.ReadULong();
        }
    }

    /// <summary>
    /// Own movement, sent on state changes plus heartbeats. <see cref="CorrectionAck"/> echoes the
    /// last <see cref="MoveCorrection.CorrectionId"/> applied, so the server can ignore updates
    /// that were already in flight when it corrected the client.
    /// </summary>
    public sealed class MoveUpdate : IMessage
    {
        public ushort CorrectionAck;
        public long ClientTimeMs;
        public MovementState State;

        public Opcode Opcode => Opcode.MoveUpdate;

        public void Write(PacketWriter w)
        {
            w.WriteUShort(CorrectionAck);
            w.WriteLong(ClientTimeMs);
            State.Write(w);
        }

        public void Read(ref PacketReader r)
        {
            CorrectionAck = r.ReadUShort();
            ClientTimeMs = r.ReadLong();
            State = MovementState.Read(ref r);
        }
    }

    public sealed class Ping : IMessage
    {
        public long ClientTimeMs;

        public Opcode Opcode => Opcode.Ping;
        public void Write(PacketWriter w) => w.WriteLong(ClientTimeMs);
        public void Read(ref PacketReader r) => ClientTimeMs = r.ReadLong();
    }

    // ---------------------------------------------------------------- Server -> Client

    /// <summary>
    /// The server accepted the player into the world: who they are, where, and their own unit
    /// values. <see cref="GameData"/> always arrives just before this.
    /// </summary>
    public sealed class Welcome : IMessage
    {
        public EntityGuid PlayerGuid;
        public string Name;
        public long ServerTimeMs;
        public ushort MapId;
        public uint InstanceId;
        /// <summary>The player's own model (from the race); others see it in the spawn message.</summary>
        public uint DisplayId;
        public MovementState Spawn;
        public readonly UnitValues Self = new UnitValues();

        public Opcode Opcode => Opcode.Welcome;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(PlayerGuid);
            w.WriteString(Name);
            w.WriteLong(ServerTimeMs);
            w.WriteUShort(MapId);
            w.WriteUInt(InstanceId);
            w.WriteUInt(DisplayId);
            Spawn.Write(w);
            Self.Write(w, UnitFieldMask.All);
        }

        public void Read(ref PacketReader r)
        {
            PlayerGuid = r.ReadGuid();
            Name = r.ReadString(Hello.MaxNameBytes);
            ServerTimeMs = r.ReadLong();
            MapId = r.ReadUShort();
            InstanceId = r.ReadUInt();
            DisplayId = r.ReadUInt();
            Spawn = MovementState.Read(ref r);
            Self.Read(ref r, UnitFieldMask.All);
        }
    }

    public sealed class Rejected : IMessage
    {
        public RejectReason Reason;
        public string Message;

        public Opcode Opcode => Opcode.Rejected;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Reason);
            w.WriteString(Message);
        }

        public void Read(ref PacketReader r)
        {
            Reason = (RejectReason)r.ReadByte();
            Message = r.ReadString();
        }
    }

    public sealed class Pong : IMessage
    {
        public long ClientTimeMs;
        public long ServerTimeMs;

        public Opcode Opcode => Opcode.Pong;

        public void Write(PacketWriter w)
        {
            w.WriteLong(ClientTimeMs);
            w.WriteLong(ServerTimeMs);
        }

        public void Read(ref PacketReader r)
        {
            ClientTimeMs = r.ReadLong();
            ServerTimeMs = r.ReadLong();
        }
    }

    /// <summary>
    /// An entity entered the receiver's area of interest. Units (players, creatures) carry their
    /// full <see cref="UnitValues"/>; their auras follow in an <see cref="AuraUpdate"/>.
    /// </summary>
    public sealed class EntitySpawn : IMessage
    {
        public EntityGuid Guid;
        public string Name;
        /// <summary>Client looks this up in its display table; the server never knows about prefabs.</summary>
        public uint DisplayId;
        public long StateServerTimeMs;
        public MovementState State;
        /// <summary>Only written and read when <see cref="EntityGuid.IsUnit"/>.</summary>
        public readonly UnitValues Unit = new UnitValues();

        public Opcode Opcode => Opcode.EntitySpawn;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Guid);
            w.WriteString(Name);
            w.WriteUInt(DisplayId);
            w.WriteLong(StateServerTimeMs);
            State.Write(w);
            if (Guid.IsUnit) Unit.Write(w, UnitFieldMask.All);
        }

        public void Read(ref PacketReader r)
        {
            Guid = r.ReadGuid();
            Name = r.ReadString(Hello.MaxNameBytes);
            DisplayId = r.ReadUInt();
            StateServerTimeMs = r.ReadLong();
            State = MovementState.Read(ref r);
            if (Guid.IsUnit) Unit.Read(ref r, UnitFieldMask.All);
        }
    }

    public sealed class EntityDespawn : IMessage
    {
        public EntityGuid Guid;

        public Opcode Opcode => Opcode.EntityDespawn;
        public void Write(PacketWriter w) => w.WriteGuid(Guid);
        public void Read(ref PacketReader r) => Guid = r.ReadGuid();
    }

    public struct EntityMoveEntry
    {
        /// <summary>Largest entry: packed guid (type + up to 8 counter bytes), age, compact state.</summary>
        public const int MaxWireSize = 1 + 8 + 2 + MovementState.CompactWireSize;
        /// <summary>Smallest entry (a one-byte counter).</summary>
        public const int MinWireSize = 1 + 1 + 2 + MovementState.CompactWireSize;

        public EntityGuid Guid;
        /// <summary>How long before the packet's ServerTimeMs this state was set (capped at 65535).</summary>
        public ushort AgeMs;
        public MovementState State;
    }

    /// <summary>
    /// Batched movement of nearby entities. Sent unreliably; the server repeats recently changed
    /// states for a while, so a lost packet only costs smoothness. Must fit one MTU: use
    /// <see cref="MaxEntriesFor"/> when filling. Guids are packed and states compact
    /// (<see cref="MovementState.WriteCompact"/>): about 24 bytes per entry.
    /// </summary>
    public sealed class EntityMoves : IMessage
    {
        public const int HeaderSize = 2 + 8 + 2; // opcode + server time + count

        public long ServerTimeMs;
        public readonly List<EntityMoveEntry> Entries = new List<EntityMoveEntry>();

        public Opcode Opcode => Opcode.EntityMoves;

        public static int MaxEntriesFor(int maxPacketBytes) => (maxPacketBytes - HeaderSize) / EntityMoveEntry.MaxWireSize;

        public void Write(PacketWriter w)
        {
            w.WriteLong(ServerTimeMs);
            w.WriteUShort((ushort)Entries.Count);
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                w.WritePackedGuid(e.Guid);
                w.WriteUShort(e.AgeMs);
                e.State.WriteCompact(w);
            }
        }

        public void Read(ref PacketReader r)
        {
            Entries.Clear();
            ServerTimeMs = r.ReadLong();
            int count = r.ReadUShort();
            if (count * EntityMoveEntry.MinWireSize > r.Remaining)
                throw new PacketException("EntityMoves count " + count + " exceeds packet size.");
            for (int i = 0; i < count; i++)
            {
                EntityMoveEntry e;
                e.Guid = r.ReadPackedGuid();
                e.AgeMs = r.ReadUShort();
                e.State = MovementState.ReadCompact(ref r);
                Entries.Add(e);
            }
        }
    }

    /// <summary>Authoritative reset of the receiver's own position after a rejected move.</summary>
    public sealed class MoveCorrection : IMessage
    {
        public ushort CorrectionId;
        public MovementState State;

        public Opcode Opcode => Opcode.MoveCorrection;

        public void Write(PacketWriter w)
        {
            w.WriteUShort(CorrectionId);
            State.Write(w);
        }

        public void Read(ref PacketReader r)
        {
            CorrectionId = r.ReadUShort();
            State = MovementState.Read(ref r);
        }
    }
}
