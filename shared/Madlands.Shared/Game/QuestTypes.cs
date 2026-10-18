using System.Collections.Generic;
using Madlands.Shared.Net;

namespace Madlands.Shared.Game
{
    // Quests (Phase 4, ADR 0011). Enum values are stable: append, never renumber.

    public enum ObjectiveType : byte
    {
        /// <summary>Kill a number of creatures of one kind.</summary>
        Kill = 0,
        /// <summary>Have a number of an item in the bags (counted, and taken when the quest is turned in).</summary>
        Collect = 1,
        /// <summary>Speak with an NPC.</summary>
        Talk = 2,
        /// <summary>Reach a place.</summary>
        Explore = 3,
    }

    /// <summary>The mark an NPC shows a player: "!" for a quest to take, "?" for one to turn in.</summary>
    public enum QuestGiverStatus : byte
    {
        None = 0,
        /// <summary>Gray "?": the player is on a quest that ends here, but has not finished it.</summary>
        Incomplete = 1,
        /// <summary>Yellow "!".</summary>
        Available = 2,
        /// <summary>Yellow "?".</summary>
        Reward = 3,
    }

    /// <summary>A quest's state in the player's quest log.</summary>
    public enum QuestLogState : byte
    {
        /// <summary>Left the log (turned in or abandoned).</summary>
        Removed = 0,
        Active = 1,
        /// <summary>All objectives done: ready to turn in.</summary>
        Complete = 2,
    }

    public struct QuestObjectiveInfo
    {
        public ObjectiveType Type;
        /// <summary>Creature id (kill, talk) or item id (collect); 0 for explore.</summary>
        public uint Target;
        public ushort Count;
        /// <summary>Quest log line, e.g. "Mire Frog slain" or "Scout the bandit camp".</summary>
        public string Text;
        /// <summary>Explore: the place to reach (map, center, radius in meters), drawn on the world map. 0 otherwise.</summary>
        public ushort Map;
        public float X;
        public float Z;
        public float Radius;
    }

    public struct QuestItemReward
    {
        public uint Item;
        public ushort Count;
    }

    /// <summary>Quest texts, objectives and rewards for dialogs and the quest log. Sent in <see cref="GameData"/>.</summary>
    public sealed class QuestInfo
    {
        public const int MaxTitleBytes = 96;
        public const int MaxTextBytes = 2048;
        public const int MaxObjectives = 4;
        public const int MaxRewards = 6;

        public uint Id;
        public string Title;
        /// <summary>The level the quest is meant for (shown, and colors the title).</summary>
        public byte Level;
        /// <summary>The story told when the quest is offered.</summary>
        public string Details;
        /// <summary>The short summary of what to do.</summary>
        public string Objectives;
        /// <summary>What the quest ender says while the quest is not finished.</summary>
        public string Progress;
        /// <summary>What the quest ender says when the quest is turned in.</summary>
        public string Completion;
        /// <summary>Who to return to (quest log: "Return to ...").</summary>
        public string EnderName;
        public readonly List<QuestObjectiveInfo> ObjectiveList = new List<QuestObjectiveInfo>();
        public int RewardXp;
        public uint RewardMoney;
        /// <summary>Always given.</summary>
        public readonly List<QuestItemReward> RewardItems = new List<QuestItemReward>();
        /// <summary>The player picks one of these.</summary>
        public readonly List<QuestItemReward> RewardChoices = new List<QuestItemReward>();

        public void Write(PacketWriter w)
        {
            w.WriteUInt(Id);
            w.WriteString(Title);
            w.WriteByte(Level);
            w.WriteString(Details);
            w.WriteString(Objectives);
            w.WriteString(Progress);
            w.WriteString(Completion);
            w.WriteString(EnderName);
            w.WriteByte((byte)ObjectiveList.Count);
            foreach (var o in ObjectiveList)
            {
                w.WriteByte((byte)o.Type);
                w.WriteUInt(o.Target);
                w.WriteUShort(o.Count);
                w.WriteString(o.Text);
                w.WriteUShort(o.Map);
                w.WriteFloat(o.X);
                w.WriteFloat(o.Z);
                w.WriteFloat(o.Radius);
            }
            w.WriteInt(RewardXp);
            w.WriteUInt(RewardMoney);
            WriteRewards(w, RewardItems);
            WriteRewards(w, RewardChoices);
        }

        public static QuestInfo Read(ref PacketReader r)
        {
            var q = new QuestInfo();
            q.Id = r.ReadUInt();
            q.Title = r.ReadString(MaxTitleBytes);
            q.Level = r.ReadByte();
            q.Details = r.ReadString(MaxTextBytes);
            q.Objectives = r.ReadString(MaxTextBytes);
            q.Progress = r.ReadString(MaxTextBytes);
            q.Completion = r.ReadString(MaxTextBytes);
            q.EnderName = r.ReadString(Hello.MaxNameBytes);
            int objectives = r.ReadByte();
            if (objectives > MaxObjectives) throw new PacketException("Quest with " + objectives + " objectives.");
            for (int i = 0; i < objectives; i++)
            {
                QuestObjectiveInfo o;
                o.Type = (ObjectiveType)r.ReadByte();
                o.Target = r.ReadUInt();
                o.Count = r.ReadUShort();
                o.Text = r.ReadString(MaxTitleBytes);
                o.Map = r.ReadUShort();
                o.X = r.ReadFloat();
                o.Z = r.ReadFloat();
                o.Radius = r.ReadFloat();
                q.ObjectiveList.Add(o);
            }
            q.RewardXp = r.ReadInt();
            q.RewardMoney = r.ReadUInt();
            ReadRewards(ref r, q.RewardItems);
            ReadRewards(ref r, q.RewardChoices);
            return q;
        }

        private static void WriteRewards(PacketWriter w, List<QuestItemReward> rewards)
        {
            w.WriteByte((byte)rewards.Count);
            foreach (var reward in rewards)
            {
                w.WriteUInt(reward.Item);
                w.WriteUShort(reward.Count);
            }
        }

        private static void ReadRewards(ref PacketReader r, List<QuestItemReward> rewards)
        {
            int count = r.ReadByte();
            if (count > MaxRewards) throw new PacketException("Quest with " + count + " rewards.");
            for (int i = 0; i < count; i++) rewards.Add(new QuestItemReward { Item = r.ReadUInt(), Count = r.ReadUShort() });
        }
    }
}
