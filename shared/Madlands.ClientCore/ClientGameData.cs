using System.Collections.Generic;
using Madlands.Shared.Game;
using Madlands.Shared.Net;

namespace Madlands.ClientCore
{
    /// <summary>The display data the server sent at login (<see cref="GameData"/>), indexed for lookups.</summary>
    public sealed class ClientGameData
    {
        private readonly Dictionary<uint, SpellInfo> _spells = new Dictionary<uint, SpellInfo>();
        private readonly Dictionary<byte, ClassInfo> _classes = new Dictionary<byte, ClassInfo>();
        private readonly Dictionary<uint, ItemInfo> _items = new Dictionary<uint, ItemInfo>();
        private readonly Dictionary<uint, QuestInfo> _quests = new Dictionary<uint, QuestInfo>();
        private readonly List<int> _xpToNext = new List<int>();

        public FactionTable Factions { get; private set; } = new FactionTable();
        public IEnumerable<SpellInfo> Spells => _spells.Values;
        public IEnumerable<ItemInfo> Items => _items.Values;
        public IEnumerable<QuestInfo> Quests => _quests.Values;
        public int MaxLevel => _xpToNext.Count + 1;

        public bool TryGetSpell(uint id, out SpellInfo spell) => _spells.TryGetValue(id, out spell);

        public SpellInfo GetSpell(uint id) => _spells.TryGetValue(id, out var s) ? s : null;

        public bool TryGetItem(uint id, out ItemInfo item) => _items.TryGetValue(id, out item);

        public ItemInfo GetItem(uint id) => _items.TryGetValue(id, out var i) ? i : null;

        public bool TryGetQuest(uint id, out QuestInfo quest) => _quests.TryGetValue(id, out quest);

        public QuestInfo GetQuest(uint id) => _quests.TryGetValue(id, out var q) ? q : null;

        public string ClassName(byte id) => _classes.TryGetValue(id, out var c) ? c.Name : "";

        /// <summary>Experience from <paramref name="level"/> to the next; 0 at the cap.</summary>
        public int XpToNext(int level) => level >= 1 && level - 1 < _xpToNext.Count ? _xpToNext[level - 1] : 0;

        public Reaction GetReaction(ushort a, ushort b) => Factions.GetReaction(a, b);

        internal void Load(GameData data)
        {
            _spells.Clear();
            foreach (var s in data.Spells) _spells[s.Id] = s;
            _classes.Clear();
            foreach (var c in data.Classes) _classes[c.Id] = c;
            Factions = new FactionTable(data.Factions);
            _xpToNext.Clear();
            _xpToNext.AddRange(data.XpToNextLevel);
            _items.Clear();
            foreach (var i in data.Items) _items[i.Id] = i;
            _quests.Clear();
            foreach (var q in data.Quests) _quests[q.Id] = q;
        }
    }
}
