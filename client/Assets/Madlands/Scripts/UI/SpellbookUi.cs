using System;
using System.Collections.Generic;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// The spellbook: every spell the character knows, in the order they are learned. Drag a spell onto the
    /// action bar (it may go on more than one slot); click it to cast.
    /// </summary>
    public sealed class SpellbookUi
    {
        private readonly GameClient _client;
        private readonly GameWindow _window;
        private readonly VisualElement _list;
        private readonly SpellDrag _drag;
        private readonly Action<uint> _cast;
        private readonly Action<uint, VisualElement> _showTooltip;
        private readonly Action _hideTooltip;

        public SpellbookUi(VisualElement layer, GameClient client, SpellDrag drag, Action<uint> cast,
            Action<uint, VisualElement> showTooltip, Action hideTooltip)
        {
            _client = client;
            _drag = drag;
            _cast = cast;
            _showTooltip = showTooltip;
            _hideTooltip = hideTooltip;
            _window = new GameWindow(layer, Loc.T("window.spellbook"), "spellbook");
            _list = new VisualElement { pickingMode = PickingMode.Ignore };
            _list.AddToClassList("spellbook-list");
            _window.Body.Add(_list);
            GameWindow.Text(_window.Body, Loc.T("spellbook.hint"), "window-hint");
            client.SpellbookChanged += Refresh;
            client.GameDataReceived += Refresh;
        }

        public bool IsOpen => _window.IsOpen;

        public void Toggle()
        {
            _window.Toggle();
            if (_window.IsOpen) Refresh();
        }

        public void Close() => _window.Close();

        public bool HandleEscape()
        {
            if (!_window.IsOpen) return false;
            _window.Close();
            return true;
        }

        /// <summary>The spells to show, in learn order (auto-attack first); also the order new spells go onto the bar.</summary>
        public static List<SpellInfo> KnownInOrder(GameClient client)
        {
            var spells = new List<SpellInfo>();
            foreach (var id in client.KnownSpells)
                if (client.Data.TryGetSpell(id, out var s) && !s.Has(SpellFlags.Hidden)) spells.Add(s);
            spells.Sort((a, b) =>
            {
                if (a.IsAutoAttack != b.IsAutoAttack) return a.IsAutoAttack ? -1 : 1;
                int byLevel = a.LearnLevel.CompareTo(b.LearnLevel);
                return byLevel != 0 ? byLevel : a.Id.CompareTo(b.Id);
            });
            return spells;
        }

        private void Refresh()
        {
            if (!_window.IsOpen) return;
            _list.Clear();
            foreach (var spell in KnownInOrder(_client))
            {
                uint id = spell.Id;
                var row = new VisualElement { pickingMode = PickingMode.Position };
                row.AddToClassList("spellbook-row");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("spellbook-icon");
                icon.Add(SpellIcons.Create(spell));
                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.AddToClassList("spellbook-text");
                GameWindow.Text(text, spell.Name, "spellbook-name");
                GameWindow.Text(text, spell.IsAutoAttack ? Loc.T("spell.melee") : Loc.T("spellbook.learned", spell.LearnLevel), "spellbook-meta");
                row.Add(icon);
                row.Add(text);
                _drag.Attach(row, () => id, -1, () => _cast(id));
                row.RegisterCallback<PointerEnterEvent>(_ => _showTooltip(id, row));
                row.RegisterCallback<PointerLeaveEvent>(_ => _hideTooltip());
                _list.Add(row);
            }
        }
    }
}
