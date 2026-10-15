using System.Collections.Generic;
using Madlands.Shared.Movement;

namespace Madlands.Shared.Net
{
    // Message classes are reusable: receivers keep one instance per type and call Read() into it.
    // Every message has a roundtrip test in Madlands.Server.Tests/ProtocolTests.cs.

    // ---------------------------------------------------------------- Client -> Server

    /// <summary>First message after connecting. Phase 1: dev login by name only.</summary>
    public sealed class Hello : IMessage
    {
        public const int MaxNameBytes = 64;

        public ushort ProtocolVersion;
        public string Name;

        public Opcode Opcode => Opcode.Hello;

        public void Write(PacketWriter w)
        {
            w.WriteUShort(ProtocolVersion);
            w.WriteString(Name);
        }

        public void Read(ref PacketReader r)
        {
            ProtocolVersion = r.ReadUShort();
            Name = r.ReadString(MaxNameBytes);
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

    public sealed class Welcome : IMessage
    {
        public EntityGuid PlayerGuid;
        public long ServerTimeMs;
        public ushort MapId;
        public uint InstanceId;
        public MovementState Spawn;

        public Opcode Opcode => Opcode.Welcome;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(PlayerGuid);
            w.WriteLong(ServerTimeMs);
            w.WriteUShort(MapId);
            w.WriteUInt(InstanceId);
            Spawn.Write(w);
        }

        public void Read(ref PacketReader r)
        {
            PlayerGuid = r.ReadGuid();
            ServerTimeMs = r.ReadLong();
            MapId = r.ReadUShort();
            InstanceId = r.ReadUInt();
            Spawn = MovementState.Read(ref r);
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

    /// <summary>An entity entered the receiver's area of interest.</summary>
    public sealed class EntitySpawn : IMessage
    {
        public EntityGuid Guid;
        public string Name;
        /// <summary>Client looks this up in its display table; the server never knows about prefabs.</summary>
        public uint DisplayId;
        public long StateServerTimeMs;
        public MovementState State;

        public Opcode Opcode => Opcode.EntitySpawn;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Guid);
            w.WriteString(Name);
            w.WriteUInt(DisplayId);
            w.WriteLong(StateServerTimeMs);
            State.Write(w);
        }

        public void Read(ref PacketReader r)
        {
            Guid = r.ReadGuid();
            Name = r.ReadString(Hello.MaxNameBytes);
            DisplayId = r.ReadUInt();
            StateServerTimeMs = r.ReadLong();
            State = MovementState.Read(ref r);
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
        public const int WireSize = 8 + 2 + MovementState.WireSize;

        public EntityGuid Guid;
        /// <summary>How long before the packet's ServerTimeMs this state was set (capped at 65535).</summary>
        public ushort AgeMs;
        public MovementState State;
    }

    /// <summary>
    /// Batched movement of nearby entities. Sent unreliably; the server repeats recently changed
    /// states for a while, so a lost packet only costs smoothness. Must fit one MTU: use
    /// <see cref="MaxEntriesFor"/> when filling.
    /// </summary>
    public sealed class EntityMoves : IMessage
    {
        public const int HeaderSize = 2 + 8 + 2; // opcode + server time + count

        public long ServerTimeMs;
        public readonly List<EntityMoveEntry> Entries = new List<EntityMoveEntry>();

        public Opcode Opcode => Opcode.EntityMoves;

        public static int MaxEntriesFor(int maxPacketBytes) => (maxPacketBytes - HeaderSize) / EntityMoveEntry.WireSize;

        public void Write(PacketWriter w)
        {
            w.WriteLong(ServerTimeMs);
            w.WriteUShort((ushort)Entries.Count);
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                w.WriteGuid(e.Guid);
                w.WriteUShort(e.AgeMs);
                e.State.Write(w);
            }
        }

        public void Read(ref PacketReader r)
        {
            Entries.Clear();
            ServerTimeMs = r.ReadLong();
            int count = r.ReadUShort();
            if (count * EntityMoveEntry.WireSize > r.Remaining)
                throw new PacketException("EntityMoves count " + count + " exceeds packet size.");
            for (int i = 0; i < count; i++)
            {
                EntityMoveEntry e;
                e.Guid = r.ReadGuid();
                e.AgeMs = r.ReadUShort();
                e.State = MovementState.Read(ref r);
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
