using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Madlands.ClientCore
{
    /// <summary>
    /// Which spell sits in which action bar slot, as the player arranged it. Spells learned since the layout was
    /// saved go into the first empty slots (as in WoW); a spell the player took off the bar stays off. Saved as
    /// text per character by the client.
    /// </summary>
    public sealed class ActionBarLayout
    {
        public const int SlotCount = 12;

        private readonly uint[] _slots = new uint[SlotCount];
        /// <summary>Spells the layout has already seen (placed once): not added again on their own.</summary>
        private readonly HashSet<uint> _seen = new HashSet<uint>();

        public uint this[int slot] => slot >= 0 && slot < SlotCount ? _slots[slot] : 0;

        public void Swap(int a, int b)
        {
            if (!Valid(a) || !Valid(b) || a == b) return;
            uint t = _slots[a];
            _slots[a] = _slots[b];
            _slots[b] = t;
        }

        /// <summary>Puts a spell into a slot (from the spellbook), replacing what was there.</summary>
        public void Set(int slot, uint spellId)
        {
            if (!Valid(slot)) return;
            _slots[slot] = spellId;
            if (spellId != 0) _seen.Add(spellId);
        }

        public void Clear(int slot)
        {
            if (Valid(slot)) _slots[slot] = 0;
        }

        /// <summary>Places spells it has not seen yet into the first empty slots, in the given order. True if anything changed.</summary>
        public bool AddNew(IEnumerable<uint> known)
        {
            bool changed = false;
            foreach (var spell in known)
            {
                if (spell == 0 || !_seen.Add(spell)) continue;
                changed = true;
                int free = Array.IndexOf(_slots, 0u);
                if (free >= 0) _slots[free] = spell;
            }
            return changed;
        }

        /// <summary>"slots|seen": twelve spell ids, then the spells seen, comma separated.</summary>
        public string Save()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < SlotCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_slots[i].ToString(CultureInfo.InvariantCulture));
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
                if (uint.TryParse(slots[i], NumberStyles.None, CultureInfo.InvariantCulture, out uint spell)) layout._slots[i] = spell;
            if (parts.Length > 1)
                foreach (var s in parts[1].Split(','))
                    if (uint.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out uint spell)) layout._seen.Add(spell);
            foreach (var spell in layout._slots)
                if (spell != 0) layout._seen.Add(spell);
            return layout;
        }

        private static bool Valid(int slot) => slot >= 0 && slot < SlotCount;
    }
}
