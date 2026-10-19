using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// Placeholder item icons until real art exists: a tile colored by what the item is, with its
    /// initials, framed in its quality color. The icon id the server sends maps to a sprite later.
    /// </summary>
    public static class ItemIcons
    {
        public static string QualityName(ItemQuality quality) => quality.ToString().ToLowerInvariant();

        /// <summary>Border class (q-uncommon) for slots.</summary>
        public static string BorderClass(ItemQuality quality) => "q-" + QualityName(quality);

        /// <summary>Text color class (qt-uncommon) for names.</summary>
        public static string TextClass(ItemQuality quality) => "qt-" + QualityName(quality);

        public static readonly string[] BorderClasses = { "q-poor", "q-common", "q-uncommon", "q-rare", "q-epic" };
        public static readonly string[] TextClasses = { "qt-poor", "qt-common", "qt-uncommon", "qt-rare", "qt-epic" };

        public static void SetQuality(VisualElement element, ItemQuality? quality, bool text)
        {
            var classes = text ? TextClasses : BorderClasses;
            foreach (var c in classes) element.RemoveFromClassList(c);
            if (quality != null) element.AddToClassList(text ? TextClass(quality.Value) : BorderClass(quality.Value));
        }

        public static VisualElement Create(ItemInfo item)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("icon");
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("icon-letters");
            icon.Add(label);
            Apply(icon, item);
            return icon;
        }

        public static void Apply(VisualElement icon, ItemInfo item)
        {
            var label = icon.Q<Label>();
            if (item == null)
            {
                icon.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.25f));
                if (label != null) label.text = "?";
                return;
            }
            Color.RGBToHSV(BaseColor(item), out float h, out float s, out float v);
            float shift = ((item.Id * 37) % 11) / 11f - 0.5f;
            icon.style.backgroundColor = new StyleColor(Color.HSVToRGB(Mathf.Repeat(h + shift * 0.06f, 1f), s, Mathf.Clamp01(v * (0.9f + 0.2f * shift))));
            if (label != null) label.text = SpellIcons.Initials(item.Name);
        }

        private static Color BaseColor(ItemInfo item)
        {
            switch (item.Class)
            {
                case ItemClass.Weapon: return new Color(0.36f, 0.42f, 0.55f);
                case ItemClass.Armor: return item.InventoryType == InventoryType.Finger || item.InventoryType == InventoryType.Neck || item.InventoryType == InventoryType.Trinket
                    ? new Color(0.55f, 0.42f, 0.2f) : new Color(0.45f, 0.32f, 0.22f);
                case ItemClass.Consumable: return new Color(0.25f, 0.5f, 0.3f);
                case ItemClass.Container: return new Color(0.55f, 0.45f, 0.3f);
                case ItemClass.Quest: return new Color(0.6f, 0.5f, 0.15f);
                case ItemClass.TradeGoods: return new Color(0.45f, 0.35f, 0.55f);
                default: return new Color(0.3f, 0.3f, 0.32f);
            }
        }
    }

    /// <summary>One item slot: icon, stack count, a cooldown shade, and a placeholder text when empty.</summary>
    public sealed class ItemSlotView
    {
        public readonly VisualElement Root;
        private readonly VisualElement _icon;
        private readonly Label _count;
        private readonly Label _placeholder;
        private readonly VisualElement _shade;

        public ItemSlotView(string placeholder = null, string sizeClass = null)
        {
            Root = new VisualElement { pickingMode = PickingMode.Position };
            Root.AddToClassList("item-slot");
            if (sizeClass != null) Root.AddToClassList(sizeClass);
            _placeholder = new Label(placeholder ?? "") { pickingMode = PickingMode.Ignore };
            _placeholder.AddToClassList("slot-placeholder");
            _icon = ItemIcons.Create(null);
            _icon.style.display = DisplayStyle.None;
            _shade = new VisualElement { pickingMode = PickingMode.Ignore };
            _shade.AddToClassList("item-shade");
            _count = new Label { pickingMode = PickingMode.Ignore };
            _count.AddToClassList("item-count");
            Root.Add(_placeholder);
            Root.Add(_icon);
            Root.Add(_shade);
            Root.Add(_count);
        }

        public ItemInfo Item { get; private set; }
        public int Count { get; private set; }

        public void Set(ItemInfo item, int count)
        {
            Item = item;
            Count = count;
            bool empty = item == null || count <= 0;
            _icon.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            _placeholder.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            _count.text = !empty && count > 1 ? count.ToString(CultureInfo.InvariantCulture) : "";
            ItemIcons.SetQuality(Root, empty ? (ItemQuality?)null : item.Quality, text: false);
            if (!empty) ItemIcons.Apply(_icon, item);
            if (empty) SetCooldown(0f);
        }

        /// <summary>0..1 of the slot shaded (an item's use spell is cooling down).</summary>
        public void SetCooldown(float fraction) => _shade.style.height = Length.Percent(Mathf.Clamp01(fraction) * 100f);
    }

    /// <summary>A window with a title bar and a close button. Content goes into <see cref="Body"/>.</summary>
    public class GameWindow
    {
        public readonly VisualElement Root;
        public readonly VisualElement Body;
        public readonly VisualElement Footer;
        private readonly Label _title;
        private readonly string _kind;

        /// <summary>Any window opened (true) or closed (false), by its kind (the css class): for sounds.</summary>
        public static event Action<string, bool> Toggled;

        public GameWindow(VisualElement parent, string title, string cssClass)
        {
            _kind = cssClass;
            Root = new VisualElement { pickingMode = PickingMode.Position };
            Root.AddToClassList("window");
            Root.AddToClassList(cssClass);
            Root.AddToClassList("hidden");
            var header = new VisualElement { pickingMode = PickingMode.Position };
            header.AddToClassList("window-header");
            _title = new Label(title) { pickingMode = PickingMode.Ignore };
            _title.AddToClassList("window-title");
            var close = new Button(Close) { text = "X" };
            close.AddToClassList("window-close");
            header.Add(_title);
            header.Add(close);
            Body = new VisualElement { pickingMode = PickingMode.Position };
            Body.AddToClassList("window-body");
            Footer = new VisualElement { pickingMode = PickingMode.Position };
            Footer.AddToClassList("window-footer");
            Footer.style.display = DisplayStyle.None;
            Root.Add(header);
            Root.Add(Body);
            Root.Add(Footer);
            parent.Add(Root);
        }

        public bool IsOpen => !Root.ClassListContains("hidden");
        public string Title { get => _title.text; set => _title.text = value; }

        public event Action Closed;

        public void Open()
        {
            bool was = IsOpen;
            Root.RemoveFromClassList("hidden");
            Root.BringToFront();
            if (!was) Toggled?.Invoke(_kind, true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            Root.AddToClassList("hidden");
            Closed?.Invoke();
            Toggled?.Invoke(_kind, false);
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public Button AddFooterButton(string text, Action onClick, string cssClass = null)
        {
            Footer.style.display = DisplayStyle.Flex;
            var button = new Button(onClick) { text = text };
            button.AddToClassList("button");
            if (cssClass != null) button.AddToClassList(cssClass);
            Footer.Add(button);
            return button;
        }

        public static Label Text(VisualElement parent, string text, string cssClass)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cssClass);
            parent.Add(label);
            return label;
        }
    }

    /// <summary>Gold, silver and copper, each in its color.</summary>
    public static class MoneyView
    {
        public static VisualElement Create(long copper, string cssClass = "money")
        {
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.AddToClassList(cssClass);
            Set(root, copper);
            return root;
        }

        public static void Set(VisualElement root, long copper)
        {
            root.Clear();
            long gold = Money.Gold(copper), silver = Money.Silver(copper), cop = Money.Copper(copper);
            if (gold > 0) Coin(root, gold + "g", "gold");
            if (silver > 0) Coin(root, silver + "s", "silver");
            if (cop > 0 || copper <= 0) Coin(root, cop + "c", "copper");
        }

        private static void Coin(VisualElement root, string text, string metal)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("coin");
            label.AddToClassList(metal);
            root.Add(label);
        }
    }

    /// <summary>The item tooltip: name in its quality color, slot, damage, armor, stats, requirements, use text and price.</summary>
    public sealed class ItemTooltip
    {
        private readonly VisualElement _root;
        private readonly VisualElement _layer;

        public ItemTooltip(VisualElement layer)
        {
            _layer = layer;
            _root = new VisualElement { pickingMode = PickingMode.Ignore };
            _root.AddToClassList("item-tooltip");
            _root.AddToClassList("hidden");
            layer.Add(_root);
        }

        public void Hide() => _root.AddToClassList("hidden");

        /// <param name="price">Shown as the vendor price (buying), else the sell price is shown.</param>
        public void Show(ItemInfo item, int count, VisualElement anchor, GameClient client, long? price = null)
        {
            if (item == null)
            {
                Hide();
                return;
            }
            _root.Clear();
            var title = Line(ItemName(item, count), "tip-title");
            ItemIcons.SetQuality(title, item.Quality, text: true);

            string slot = SlotName(item);
            if (slot.Length > 0) Line(slot, "tip-line");
            if (item.IsWeapon)
            {
                var row = Row();
                Line(Loc.T("tip.damage", item.MinDamage, item.MaxDamage), "tip-line", row);
                Line(Loc.T("tip.speed", (item.SpeedMs / 1000f).ToString("0.00", CultureInfo.InvariantCulture)), "tip-line", row);
                Line(Loc.T("tip.dps", item.Dps.ToString("0.0", CultureInfo.InvariantCulture)), "tip-line");
            }
            int armor = item.GetStat(StatType.Armor);
            if (armor > 0) Line(Loc.T("tip.armor", armor), "tip-line");
            foreach (var stat in item.Stats)
                if (stat.Stat != StatType.Armor) Line((stat.Value > 0 ? "+" : "") + stat.Value + " " + StatName(stat.Stat), "tip-line stat");
            if (item.BagSlots > 0) Line(Loc.T("tip.bagSlots", item.BagSlots), "tip-line");

            var self = client.Self.Values;
            if (item.RequiredLevel > 1)
                Line(Loc.T("tip.requiresLevel", item.RequiredLevel), self.Level < item.RequiredLevel ? "tip-line red" : "tip-line");
            if (item.AllowedClasses.Length > 0)
            {
                var names = new List<string>();
                foreach (var c in item.AllowedClasses) names.Add(client.Data.ClassName(c));
                Line(Loc.T("tip.classes", string.Join(", ", names)), item.AllowsClass(self.ClassId) ? "tip-line" : "tip-line red");
            }
            if (!string.IsNullOrEmpty(item.Description))
            {
                bool use = item.Description.StartsWith("Use:", StringComparison.Ordinal);
                Line(use ? item.Description : "\"" + item.Description + "\"", use ? "tip-line use" : "tip-line flavor");
            }
            if (item.Class == ItemClass.Quest) Line(Loc.T("tip.questItem"), "tip-line dim");
            if (price != null) PriceLine(Loc.T("tip.price"), price.Value);
            else if (item.SellPrice > 0) PriceLine(Loc.T("tip.sellPrice"), (long)item.SellPrice * Math.Max(1, count));

            _root.RemoveFromClassList("hidden");
            Place(anchor);
        }

        /// <summary>A plain text tooltip (quest rewards of money, buttons).</summary>
        public void ShowText(string title, string body, VisualElement anchor)
        {
            _root.Clear();
            Line(title, "tip-title");
            if (!string.IsNullOrEmpty(body)) Line(body, "tip-line dim");
            _root.RemoveFromClassList("hidden");
            Place(anchor);
        }

        private void Place(VisualElement anchor)
        {
            // Away from the screen edge: right of things on the left half, left of things on the right half.
            var bound = anchor.worldBound;
            var layer = _layer.layout;
            float left = bound.center.x < layer.width * 0.5f ? bound.xMax + 8f : bound.x - 308f;
            _root.style.left = Mathf.Max(4f, left);
            _root.style.top = Mathf.Clamp(bound.y, 4f, Mathf.Max(4f, layer.height - 260f));
        }

        private void PriceLine(string label, long copper)
        {
            var row = Row();
            Line(label, "tip-line dim", row);
            row.Add(MoneyView.Create(copper, "list-price"));
        }

        private VisualElement Row()
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("tip-row");
            _root.Add(row);
            return row;
        }

        private Label Line(string text, string classes, VisualElement parent = null)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var c in classes.Split(' ')) label.AddToClassList(c);
            (parent ?? _root).Add(label);
            return label;
        }

        public static string ItemName(ItemInfo item, int count) => count > 1 ? item.Name + " x" + count : item.Name;

        public static string SlotName(ItemInfo item)
        {
            if (item.InventoryType != InventoryType.None) return Loc.T("slot." + item.InventoryType);
            return item.Class == ItemClass.Consumable ? Loc.T("itemClass.consumable") : item.Class == ItemClass.TradeGoods ? Loc.T("itemClass.tradeGoods") : "";
        }

        public static string StatName(StatType stat)
        {
            return Loc.T("stat." + stat);
        }

        /// <summary>"Mire Frog Leg x2" style chat text with the item's name.</summary>
        public static string Describe(ClientGameData data, uint itemId, int count)
        {
            var item = data.GetItem(itemId);
            var sb = new StringBuilder();
            sb.Append('[').Append(item != null ? item.Name : Loc.T("tip.unknownItem", itemId)).Append(']');
            if (count > 1) sb.Append(" x").Append(count);
            return sb.ToString();
        }
    }
}
