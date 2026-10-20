using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Madlands.ClientCore
{
    /// <summary>
    /// What sits in which action bar slot, as the player arranged it: a spell, or an item to use (a potion, food, an
    /// elixir: the slot stands for every stack of that item in the bags, as in WoW). Spells learned since the layout
    /// was saved go into the first empty slots; a spell the player took off the bar stays off. Saved as text per
    /// character by the client.
    /// </summary>
    public sealed class ActionBarLayout
    {
        /// <summary>Two rows of twelve: 0-11 the bottom row (keys 1 to =), 12-23 the row above it.</summary>
        public const int SlotCount = 24;
        public const int RowLength = 12;

        /// <summary>Saved item slots start with this letter ("i1501"); spell slots are plain numbers.</summary>
        private const char ItemPrefix = 'i';

        private readonly uint[] _spells = new uint[SlotCount];
        private readonly uint[] _items = new uint[SlotCount];
        /// <summary>Spells the layout has already seen (placed once): not added again on their own.</summary>
        private readonly HashSet<uint> _seen = new HashSet<uint>();

        /// <summary>The spell in a slot (0 if it is empty or holds an item).</summary>
        public uint this[int slot] => Valid(slot) ? _spells[slot] : 0;

        /// <summary>The item in a slot (0 if it is empty or holds a spell).</summary>
        public uint ItemAt(int slot) => Valid(slot) ? _items[slot] : 0;

        public void Swap(int a, int b)
        {
            if (!Valid(a) || !Valid(b) || a == b) return;
            uint spell = _spells[a], item = _items[a];
            _spells[a] = _spells[b];
            _items[a] = _items[b];
            _spells[b] = spell;
            _items[b] = item;
        }

        /// <summary>Puts a spell into a slot (from the spellbook), replacing what was there.</summary>
        public void Set(int slot, uint spellId)
        {
            if (!Valid(slot)) return;
            _spells[slot] = spellId;
            _items[slot] = 0;
            if (spellId != 0) _seen.Add(spellId);
        }

        /// <summary>Puts an item into a slot (from the bags), replacing what was there.</summary>
        public void SetItem(int slot, uint itemId)
        {
            if (!Valid(slot)) return;
            _items[slot] = itemId;
            _spells[slot] = 0;
        }

        public void Clear(int slot)
        {
            if (!Valid(slot)) return;
            _spells[slot] = 0;
            _items[slot] = 0;
        }

        /// <summary>Places spells it has not seen yet into the first empty slots, in the given order. True if anything changed.</summary>
        public bool AddNew(IEnumerable<uint> known)
        {
            bool changed = false;
            foreach (var spell in known)
            {
                if (spell == 0 || !_seen.Add(spell)) continue;
                changed = true;
                for (int i = 0; i < SlotCount; i++)
                {
                    if (_spells[i] != 0 || _items[i] != 0) continue;
                    _spells[i] = spell;
                    break;
                }
            }
            return changed;
        }

        /// <summary>"slots|seen": the slots (a spell id, or "i" and an item id), then the spells seen, comma separated.</summary>
        public string Save()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < SlotCount; i++)
            {
                if (i > 0) sb.Append(',');
                if (_items[i] != 0) sb.Append(ItemPrefix).Append(_items[i].ToString(CultureInfo.InvariantCulture));
                else sb.Append(_spells[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append('|');
            bool first = true;
            foreach (var spell in _seen)
            {
                if (!first) sb.Append(',');
                sb.Append(spell.ToString(CultureInfo.InvariantCulture));
                first = false;
            }
            return sb.ToString();
        }

        /// <summary>A saved layout; empty (everything new) if the text is missing or broken.</summary>
        public static ActionBarLayout Load(string text)
        {
            var layout = new ActionBarLayout();
            if (string.IsNullOrEmpty(text)) return layout;
            var parts = text.Split('|');
            var slots = parts[0].Split(',');
            for (int i = 0; i < SlotCount && i < slots.Length; i++)
            {
                string s = slots[i];
                if (s.Length > 1 && s[0] == ItemPrefix)
                {
                    if (uint.TryParse(s.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out uint item)) layout._items[i] = item;
                }
                else if (uint.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out uint spell))
                {
                    layout._spells[i] = spell;
                }
            }
            if (parts.Length > 1)
                foreach (var s in parts[1].Split(','))
                    if (uint.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out uint spell)) layout._seen.Add(spell);
            foreach (var spell in layout._spells)
                if (spell != 0) layout._seen.Add(spell);
            return layout;
        }

        private static bool Valid(int slot) => slot >= 0 && slot < SlotCount;
    }
}
