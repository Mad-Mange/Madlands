using System.Collections.Generic;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;

namespace Madlands.ClientCore
{
    public struct ClientItem
    {
        public uint ItemId;
        public int Count;

        public bool IsEmpty => ItemId == 0;
    }

    /// <summary>
    /// The local player's items and money as the server last reported them (ADR 0010). The client
    /// never changes this itself: requests go to the server, and its InventoryUpdate brings the result.
    /// </summary>
    public sealed class ClientInventory
    {
        private readonly Dictionary<ItemPos, ClientItem> _slots = new Dictionary<ItemPos, ClientItem>();
        private readonly ClientGameData _data;

        internal ClientInventory(ClientGameData data)
        {
            _data = data;
        }

        /// <summary>Copper.</summary>
        public long Money { get; internal set; }

        public ClientItem Get(ItemPos pos) => _slots.TryGetValue(pos, out var item) ? item : default;

        public ClientItem Equipped(EquipSlot slot) => Get(ItemPos.Equip(slot));

        /// <summary>Slots in a container: equipment and backpack are fixed, a bag's size comes from the bag in its bag slot.</summary>
        public int ContainerSize(byte container)
        {
            if (container == ItemPos.Equipment) return (int)EquipSlot.Bag4 + 1;
            if (container == ItemPos.Backpack) return GameConstants.BackpackSlots;
            if (container >= ItemPos.ContainerCount) return 0;
            var bag = Equipped(ItemPos.BagSlotOf(container));
            return !bag.IsEmpty && _data.TryGetItem(bag.ItemId, out var info) ? info.BagSlots : 0;
        }

        /// <summary>Every occupied position.</summary>
        public IEnumerable<KeyValuePair<ItemPos, ClientItem>> All => _slots;

        /// <summary>Bag positions in fill order: the backpack, then bags 1-4.</summary>
        public IEnumerable<ItemPos> BagPositions()
        {
            for (byte c = ItemPos.Backpack; c < ItemPos.ContainerCount; c++)
            {
                int size = ContainerSize(c);
                for (byte s = 0; s < size; s++) yield return new ItemPos(c, s);
            }
        }

        /// <summary>How many of an item are in the bags.</summary>
        public int Count(uint itemId)
        {
            int n = 0;
            foreach (var pos in BagPositions())
            {
                var item = Get(pos);
                if (item.ItemId == itemId) n += item.Count;
            }
            return n;
        }

        public int FreeSlots()
        {
            int n = 0;
            foreach (var pos in BagPositions()) if (Get(pos).IsEmpty) n++;
            return n;
        }

        internal void Apply(InventoryUpdate update)
        {
            if (update.Full) _slots.Clear();
            foreach (var e in update.Entries)
            {
                if (e.ItemId == 0 || e.Count == 0) _slots.Remove(e.Pos);
                else _slots[e.Pos] = new ClientItem { ItemId = e.ItemId, Count = e.Count };
            }
        }

        internal void Clear()
        {
            _slots.Clear();
            Money = 0;
        }
    }

    /// <summary>A quest in the local player's log.</summary>
    public sealed class ClientQuest
    {
        public uint QuestId;
        public QuestLogState State;
        public ushort[] Progress = new ushort[0];

        public bool IsComplete => State == QuestLogState.Complete;
    }

    /// <summary>The local player's quest log and the quests they have turned in.</summary>
    public sealed class ClientQuestLog
    {
        private readonly List<ClientQuest> _active = new List<ClientQuest>();
        private readonly HashSet<uint> _completed = new HashSet<uint>();

        /// <summary>In the order they were taken.</summary>
        public IReadOnlyList<ClientQuest> Active => _active;
        public bool IsCompleted(uint questId) => _completed.Contains(questId);

        public ClientQuest Find(uint questId)
        {
            foreach (var q in _active) if (q.QuestId == questId) return q;
            return null;
        }

        internal void Clear()
        {
            _active.Clear();
            _completed.Clear();
        }

        /// <summary>Applies an update; reports objective counts that went up and quests that changed state.</summary>
        internal void Apply(QuestLogUpdate update, List<(uint Quest, int Objective, int Count)> progressed, List<(uint Quest, QuestLogState State)> changed)
        {
            if (update.Full)
            {
                _active.Clear();
                _completed.Clear();
                foreach (var id in update.Completed) _completed.Add(id);
            }
            foreach (var e in update.Entries)
            {
                var quest = Find(e.QuestId);
                if (e.State == QuestLogState.Removed)
                {
                    if (quest == null) continue;
                    _active.Remove(quest);
                    if (!update.Full) changed.Add((e.QuestId, QuestLogState.Removed));
                    continue;
                }
                var progress = e.Progress ?? new ushort[0];
                if (quest == null)
                {
                    quest = new ClientQuest { QuestId = e.QuestId, State = e.State, Progress = progress };
                    _active.Add(quest);
                    if (!update.Full) changed.Add((e.QuestId, e.State));
                    continue;
                }
                for (int i = 0; i < progress.Length; i++)
                    if (!update.Full && (i >= quest.Progress.Length || progress[i] > quest.Progress[i])) progressed.Add((e.QuestId, i, progress[i]));
                if (quest.State != e.State && !update.Full) changed.Add((e.QuestId, e.State));
                quest.State = e.State;
                quest.Progress = progress;
            }
        }

        internal void MarkCompleted(uint questId) => _completed.Add(questId);
    }

    /// <summary>The open loot window.</summary>
    public sealed class ClientLoot
    {
        public EntityGuid Corpse;
        public long Money;
        public readonly List<LootEntry> Items = new List<LootEntry>();

        public bool IsOpen => !Corpse.IsEmpty;
        public bool IsEmpty => Money <= 0 && Items.Count == 0;
    }
}
