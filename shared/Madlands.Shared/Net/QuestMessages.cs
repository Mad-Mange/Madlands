using System.Collections.Generic;
using Madlands.Shared.Game;

namespace Madlands.Shared.Net
{
    // Phase 4: NPC dialogs and quests (ADR 0011). Quest texts are in GameData; these messages carry ids and state.
    // Every message has a roundtrip test in Madlands.Server.Tests/ProtocolTests.cs.

    // ---------------------------------------------------------------- Client -> Server

    public sealed class QuestAccept : IMessage
    {
        public EntityGuid Npc;
        public uint QuestId;

        public Opcode Opcode => Opcode.QuestAccept;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Npc);
            w.WriteUInt(QuestId);
        }

        public void Read(ref PacketReader r)
        {
            Npc = r.ReadGuid();
            QuestId = r.ReadUInt();
        }
    }

    /// <summary>Turns in a finished quest. <see cref="Choice"/> picks one of the reward choices (<see cref="NoChoice"/> if it has none).</summary>
    public sealed class QuestTurnIn : IMessage
    {
        public const byte NoChoice = 255;

        public EntityGuid Npc;
        public uint QuestId;
        public byte Choice = NoChoice;

        public Opcode Opcode => Opcode.QuestTurnIn;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Npc);
            w.WriteUInt(QuestId);
            w.WriteByte(Choice);
        }

        public void Read(ref PacketReader r)
        {
            Npc = r.ReadGuid();
            QuestId = r.ReadUInt();
            Choice = r.ReadByte();
        }
    }

    public sealed class QuestAbandon : IMessage
    {
        public uint QuestId;

        public Opcode Opcode => Opcode.QuestAbandon;
        public void Write(PacketWriter w) => w.WriteUInt(QuestId);
        public void Read(ref PacketReader r) => QuestId = r.ReadUInt();
    }

    // ---------------------------------------------------------------- Server -> Client

    public struct GossipQuest
    {
        public uint QuestId;
        /// <summary>Available (can be taken), Incomplete (in progress) or Reward (ready to turn in).</summary>
        public QuestGiverStatus Status;
    }

    /// <summary>What an NPC says when spoken to, the quests it offers or takes back, and whether it sells things.</summary>
    public sealed class GossipMenu : IMessage
    {
        public const int MaxQuests = 32;

        public EntityGuid Npc;
        public string Text;
        public NpcFlags Flags;
        public readonly List<GossipQuest> Quests = new List<GossipQuest>();

        public Opcode Opcode => Opcode.GossipMenu;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Npc);
            w.WriteString(Text);
            w.WriteByte((byte)Flags);
            w.WriteByte((byte)Quests.Count);
            foreach (var q in Quests)
            {
                w.WriteUInt(q.QuestId);
                w.WriteByte((byte)q.Status);
            }
        }

        public void Read(ref PacketReader r)
        {
            Quests.Clear();
            Npc = r.ReadGuid();
            Text = r.ReadString(QuestInfo.MaxTextBytes);
            Flags = (NpcFlags)r.ReadByte();
            int count = r.ReadByte();
            if (count > MaxQuests) throw new PacketException("Too many gossip quests: " + count + ".");
            for (int i = 0; i < count; i++) Quests.Add(new GossipQuest { QuestId = r.ReadUInt(), Status = (QuestGiverStatus)r.ReadByte() });
        }
    }

    public struct QuestLogEntry
    {
        public uint QuestId;
        public QuestLogState State;
        /// <summary>Per objective: how many are done (kills, items, 1 for talk/explore).</summary>
        public ushort[] Progress;
    }

    /// <summary>
    /// Quest log changes. With <see cref="Full"/> (login) the entries replace the log and
    /// <see cref="Completed"/> lists every quest the player has turned in.
    /// </summary>
    public sealed class QuestLogUpdate : IMessage
    {
        public const int MaxCompleted = 8192;

        public bool Full;
        public readonly List<QuestLogEntry> Entries = new List<QuestLogEntry>();
        public readonly List<uint> Completed = new List<uint>();

        public Opcode Opcode => Opcode.QuestLogUpdate;

        public void Write(PacketWriter w)
        {
            w.WriteBool(Full);
            w.WriteByte((byte)Entries.Count);
            foreach (var e in Entries)
            {
                w.WriteUInt(e.QuestId);
                w.WriteByte((byte)e.State);
                var progress = e.Progress ?? new ushort[0];
                w.WriteByte((byte)progress.Length);
                foreach (var p in progress) w.WriteUShort(p);
            }
            if (!Full) return;
            w.WriteUShort((ushort)Completed.Count);
            foreach (var id in Completed) w.WriteUInt(id);
        }

        public void Read(ref PacketReader r)
        {
            Entries.Clear();
            Completed.Clear();
            Full = r.ReadBool();
            int count = r.ReadByte();
            if (count > GameConstants.MaxQuestLog * 2) throw new PacketException("Too many quest log entries: " + count + ".");
            for (int i = 0; i < count; i++)
            {
                QuestLogEntry e;
                e.QuestId = r.ReadUInt();
                e.State = (QuestLogState)r.ReadByte();
                int objectives = r.ReadByte();
                if (objectives > QuestInfo.MaxObjectives) throw new PacketException("Too many objectives: " + objectives + ".");
                e.Progress = new ushort[objectives];
                for (int o = 0; o < objectives; o++) e.Progress[o] = r.ReadUShort();
                Entries.Add(e);
            }
            if (!Full) return;
            int completed = r.ReadUShort();
            if (completed > MaxCompleted || completed * 4 > r.Remaining) throw new PacketException("Too many completed quests: " + completed + ".");
            for (int i = 0; i < completed; i++) Completed.Add(r.ReadUInt());
        }
    }

    public struct QuestGiverEntry
    {
        public EntityGuid Npc;
        public QuestGiverStatus Status;
    }

    /// <summary>The quest marks ("!" and "?") NPCs show the receiver. Private, recomputed when the player's quests change.</summary>
    public sealed class QuestGiverStatusUpdate : IMessage
    {
        public const int MaxEntries = 256;

        public readonly List<QuestGiverEntry> Entries = new List<QuestGiverEntry>();

        public Opcode Opcode => Opcode.QuestGiverStatusUpdate;

        public void Write(PacketWriter w)
        {
            w.WriteUShort((ushort)Entries.Count);
            foreach (var e in Entries)
            {
                w.WriteGuid(e.Npc);
                w.WriteByte((byte)e.Status);
            }
        }

        public void Read(ref PacketReader r)
        {
            Entries.Clear();
            int count = r.ReadUShort();
            if (count > MaxEntries) throw new PacketException("Too many quest giver entries: " + count + ".");
            for (int i = 0; i < count; i++) Entries.Add(new QuestGiverEntry { Npc = r.ReadGuid(), Status = (QuestGiverStatus)r.ReadByte() });
        }
    }

    /// <summary>A quest was turned in (chat: "Quest completed", the rewards).</summary>
    public sealed class QuestCompleted : IMessage
    {
        public uint QuestId;
        public int Xp;
        public long Money;

        public Opcode Opcode => Opcode.QuestCompleted;

        public void Write(PacketWriter w)
        {
            w.WriteUInt(QuestId);
            w.WriteInt(Xp);
            w.WriteLong(Money);
        }

        public void Read(ref PacketReader r)
        {
            QuestId = r.ReadUInt();
            Xp = r.ReadInt();
            Money = r.ReadLong();
        }
    }
}
