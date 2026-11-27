using System;
using System.Collections.Generic;
using Madlands.Shared.Game;

namespace Madlands.Shared.Net
{
    // The developer GM menu (docs/GMMenu): typed GM commands with structured results, the GM's state and
    // catalogs, and overlay geometry. Only GM accounts get answers other than Unauthorized.
    // Every message has a roundtrip test in Madlands.Server.Tests/ProtocolTests.cs.

    /// <summary>Client -> server: run a GM command by its stable id (<see cref="GmCommands"/>).</summary>
    public sealed class GmCommand : IMessage
    {
        public uint RequestId;
        public string CommandId;
        public readonly List<KeyValuePair<string, string>> Args = new List<KeyValuePair<string, string>>();

        public Opcode Opcode => Opcode.GmCommand;

        public void Write(PacketWriter w)
        {
            w.WriteUInt(RequestId);
            w.WriteString(CommandId);
            w.WriteByte((byte)Args.Count);
            foreach (var a in Args)
            {
                w.WriteString(a.Key);
                w.WriteString(a.Value);
            }
        }

        public void Read(ref PacketReader r)
        {
            Args.Clear();
            RequestId = r.ReadUInt();
            CommandId = r.ReadString(GmCommands.MaxIdBytes);
            int count = r.ReadByte();
            if (count > GmCommands.MaxArgs) throw new PacketException("Too many GM arguments: " + count + ".");
            for (int i = 0; i < count; i++)
                Args.Add(new KeyValuePair<string, string>(r.ReadString(GmCommands.MaxKeyBytes), r.ReadString(GmCommands.MaxValueBytes)));
        }
    }

    /// <summary>Server -> client: the answer to one <see cref="GmCommand"/>.</summary>
    public sealed class GmCommandResult : IMessage
    {
        public const int MaxMessageBytes = 1024;
        public const int MaxData = 32;
        public const int MaxDataValueBytes = 256;

        public uint RequestId;
        public string CommandId;
        public GmResultCode Code;
        public string Message;
        /// <summary>Structured details (for example "level" = "10", or a target's AI state).</summary>
        public readonly List<KeyValuePair<string, string>> Data = new List<KeyValuePair<string, string>>();

        public bool Ok => Code == GmResultCode.Ok || Code == GmResultCode.Partial;
        public Opcode Opcode => Opcode.GmCommandResult;

        public void Write(PacketWriter w)
        {
            w.WriteUInt(RequestId);
            w.WriteString(CommandId ?? "");
            w.WriteByte((byte)Code);
            w.WriteString(Message ?? "");
            w.WriteByte((byte)Data.Count);
            foreach (var d in Data)
            {
                w.WriteString(d.Key);
                w.WriteString(d.Value);
            }
        }

        public void Read(ref PacketReader r)
        {
            Data.Clear();
            RequestId = r.ReadUInt();
            CommandId = r.ReadString(GmCommands.MaxIdBytes);
            Code = (GmResultCode)r.ReadByte();
            Message = r.ReadString(MaxMessageBytes);
            int count = r.ReadByte();
            if (count > MaxData) throw new PacketException("Too many GM result fields: " + count + ".");
            for (int i = 0; i < count; i++)
                Data.Add(new KeyValuePair<string, string>(r.ReadString(GmCommands.MaxKeyBytes), r.ReadString(MaxDataValueBytes)));
        }
    }

    /// <summary>
    /// Server -> client: the player's GM rights and active overrides. Sent to GMs on entering the world and
    /// after every change, so the menu's toggles show the server's state.
    /// </summary>
    public sealed class GmStatus : IMessage
    {
        public byte GmLevel;
        public GmOverrides Overrides;
        public ushort SpeedPercent = 100;
        public ushort DamagePercent = 100;
        /// <summary>Creatures this GM spawned that are still in the world.</summary>
        public ushort DebugSpawns;

        public Opcode Opcode => Opcode.GmStatus;

        public void Write(PacketWriter w)
        {
            w.WriteByte(GmLevel);
            w.WriteUShort((ushort)Overrides);
            w.WriteUShort(SpeedPercent);
            w.WriteUShort(DamagePercent);
            w.WriteUShort(DebugSpawns);
        }

        public void Read(ref PacketReader r)
        {
            GmLevel = r.ReadByte();
            Overrides = (GmOverrides)r.ReadUShort();
            SpeedPercent = r.ReadUShort();
            DamagePercent = r.ReadUShort();
            DebugSpawns = r.ReadUShort();
        }

        public void CopyFrom(GmStatus other)
        {
            GmLevel = other.GmLevel;
            Overrides = other.Overrides;
            SpeedPercent = other.SpeedPercent;
            DamagePercent = other.DamagePercent;
            DebugSpawns = other.DebugSpawns;
        }
    }

    public struct GmCreatureEntry
    {
        public uint Id;
        public string Name;
        public byte MinLevel;
        public byte MaxLevel;
        /// <summary>How the creature's faction stands to the GM's.</summary>
        public Reaction Reaction;
    }

    public enum GmDestinationKind : byte { Start = 0, Npc = 1, Creature = 2, Rest = 3 }

    public struct GmDestination
    {
        /// <summary>The id <see cref="GmCommands.TeleportLocation"/> takes: "start", "npc:15", "creature:1", "rest:0".</summary>
        public string Id;
        public string Name;
        public GmDestinationKind Kind;
        public float X;
        public float Z;
    }

    /// <summary>Server -> client (GMs only): spawnable creatures and teleport destinations from server content.</summary>
    public sealed class GmCatalog : IMessage
    {
        public const int MaxEntries = 1024;
        public const int MaxNameBytes = 128;

        public ushort MapId;
        public readonly List<GmCreatureEntry> Creatures = new List<GmCreatureEntry>();
        public readonly List<GmDestination> Destinations = new List<GmDestination>();

        public Opcode Opcode => Opcode.GmCatalog;

        public void Write(PacketWriter w)
        {
            w.WriteUShort(MapId);
            w.WriteUShort((ushort)Creatures.Count);
            foreach (var c in Creatures)
            {
                w.WriteUInt(c.Id);
                w.WriteString(c.Name);
                w.WriteByte(c.MinLevel);
                w.WriteByte(c.MaxLevel);
                w.WriteByte((byte)c.Reaction);
            }
            w.WriteUShort((ushort)Destinations.Count);
            foreach (var d in Destinations)
            {
                w.WriteString(d.Id);
                w.WriteString(d.Name);
                w.WriteByte((byte)d.Kind);
                w.WriteFloat(d.X);
                w.WriteFloat(d.Z);
            }
        }

        public void Read(ref PacketReader r)
        {
            Creatures.Clear();
            Destinations.Clear();
            MapId = r.ReadUShort();
            int creatures = r.ReadUShort();
            if (creatures > MaxEntries) throw new PacketException("Too many catalog creatures: " + creatures + ".");
            for (int i = 0; i < creatures; i++)
                Creatures.Add(new GmCreatureEntry
                {
                    Id = r.ReadUInt(),
                    Name = r.ReadString(MaxNameBytes),
                    MinLevel = r.ReadByte(),
                    MaxLevel = r.ReadByte(),
                    Reaction = (Reaction)r.ReadByte(),
                });
            int destinations = r.ReadUShort();
            if (destinations > MaxEntries) throw new PacketException("Too many catalog destinations: " + destinations + ".");
            for (int i = 0; i < destinations; i++)
                Destinations.Add(new GmDestination
                {
                    Id = r.ReadString(GmCommands.MaxValueBytes),
                    Name = r.ReadString(MaxNameBytes),
                    Kind = (GmDestinationKind)r.ReadByte(),
                    X = r.ReadFloat(),
                    Z = r.ReadFloat(),
                });
        }
    }

    public enum GmGeometryKind : byte { Collision = 0, NavMesh = 1, SpawnPoints = 2 }

    /// <summary>
    /// Server -> client (GMs only): line segments of server-side data around a point, for the debug overlays.
    /// Points are relative to <see cref="Center"/> in 1/16 m, so they stay small on the wire.
    /// </summary>
    public sealed class GmDebugGeometry : IMessage
    {
        public const int MaxSegments = 12000;
        public const float Resolution = 16f;
        /// <summary>The farthest a point may lie from the center (what a short holds at the resolution).</summary>
        public const float MaxOffset = short.MaxValue / Resolution;

        public GmGeometryKind Kind;
        public Vec3 Center;
        public float Radius;
        /// <summary>More data was there than <see cref="MaxSegments"/>: the overlay is incomplete.</summary>
        public bool Truncated;
        /// <summary>Pairs of points: segment i runs from Points[2i] to Points[2i+1].</summary>
        public readonly List<Vec3> Points = new List<Vec3>();

        public Opcode Opcode => Opcode.GmDebugGeometry;

        public int SegmentCount => Points.Count / 2;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Kind);
            w.WriteVec3(Center);
            w.WriteFloat(Radius);
            w.WriteBool(Truncated);
            int segments = Math.Min(SegmentCount, MaxSegments);
            w.WriteUShort((ushort)segments);
            for (int i = 0; i < segments * 2; i++)
            {
                var p = Points[i];
                w.WriteShort(Quantize(p.X - Center.X));
                w.WriteShort(Quantize(p.Y - Center.Y));
                w.WriteShort(Quantize(p.Z - Center.Z));
            }
        }

        public void Read(ref PacketReader r)
        {
            Points.Clear();
            Kind = (GmGeometryKind)r.ReadByte();
            Center = r.ReadVec3();
            Radius = r.ReadFloat();
            Truncated = r.ReadBool();
            int segments = r.ReadUShort();
            if (segments > MaxSegments) throw new PacketException("Too many debug segments: " + segments + ".");
            for (int i = 0; i < segments * 2; i++)
            {
                float x = r.ReadShort() / Resolution, y = r.ReadShort() / Resolution, z = r.ReadShort() / Resolution;
                Points.Add(new Vec3(Center.X + x, Center.Y + y, Center.Z + z));
            }
        }

        private static short Quantize(float offset) =>
            (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(offset * Resolution)));
    }
}
