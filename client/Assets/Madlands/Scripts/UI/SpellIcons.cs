using System.Collections.Generic;
using System.Text;
using Madlands.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// Illustrated player ability icons with a school-colored tile and initials for other spells.
    /// </summary>
    public static class SpellIcons
    {
        // Effects that share a recognizable item or ability reuse its illustration. Unlisted spells
        // still look up their own SpellIcon by icon ID, then fall back to initials if no art exists.
        private static readonly Dictionary<uint, string> SharedIcons = new Dictionary<uint, string>
        {
            { 500, "ItemIcon4001" }, { 501, "ItemIcon4002" }, { 502, "ItemIcon4003" }, { 503, "ItemIcon4004" },
            { 504, "ItemIcon4005" }, { 505, "ItemIcon4008" }, { 506, "ItemIcon4009" },
            { 507, "ItemIcon4010" }, { 508, "ItemIcon4011" },
            { 900, "SpellIcon101" }, { 901, "SpellIcon200" }, { 902, "SpellIcon100" },
            { 903, "ItemIcon3003" }, { 904, "ItemIcon3006" }, { 905, "SpellIcon101" },
            { 906, "SpellIcon201" }, { 908, "SpellIcon201" }, { 909, "SpellIcon202" },
            { 910, "ItemIcon3003" }, { 911, "SpellIcon102" }, { 912, "SpellIcon100" },
            { 913, "SpellIcon204" }, { 914, "SpellIcon204" }, { 915, "SpellIcon203" },
        };

        public static VisualElement Create(SpellInfo spell)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("icon");
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("icon-letters");
            icon.Add(label);
            Apply(icon, spell);
            return icon;
        }

        public static void Apply(VisualElement icon, SpellInfo spell)
        {
            var catalog = ArtCatalog.Instance;
            Texture2D picture = null;
            if (spell != null && catalog != null)
            {
                string name = SharedIcons.TryGetValue(spell.Icon, out var shared) ? shared : "SpellIcon" + spell.Icon;
                picture = catalog.GetIcon(name);
            }
            // Action-bar slots are shared with items; always clear the previous image.
            icon.style.backgroundImage = picture != null ? new StyleBackground(picture) : new StyleBackground(StyleKeyword.None);
            var label = icon.Q<Label>();
            if (spell == null)
            {
                icon.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.25f));
                if (label != null) label.text = "?";
                return;
            }
            // Same school, slightly different shade per spell, so neighbouring icons stay distinguishable.
            Color.RGBToHSV(SchoolColors.Get(spell.School), out float h, out float s, out float v);
            float shift = ((spell.Id * 37) % 11) / 11f - 0.5f;
            var color = Color.HSVToRGB(Mathf.Repeat(h + shift * 0.08f, 1f), Mathf.Clamp01(s * 0.85f), Mathf.Clamp01(v * (0.55f + 0.15f * shift)));
            icon.style.backgroundColor = new StyleColor(color);
            if (label != null) label.text = picture != null ? "" : Initials(spell.Name);
        }

        public static string Initials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var sb = new StringBuilder(2);
            foreach (var word in name.Split(' '))
            {
                if (word.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(word[0]));
                if (sb.Length == 2) break;
            }
            if (sb.Length == 1 && name.Length > 1) sb.Append(char.ToLowerInvariant(name[1]));
            return sb.ToString();
        }
    }
}
