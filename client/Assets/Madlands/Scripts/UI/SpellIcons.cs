using System.Text;
using Madlands.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// Placeholder spell icons until real art exists: a tile in the spell school's color with the
    /// spell's initials. Later the icon id the server sends maps to a sprite; callers stay the same.
    /// </summary>
    public static class SpellIcons
    {
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
            if (label != null) label.text = Initials(spell.Name);
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
