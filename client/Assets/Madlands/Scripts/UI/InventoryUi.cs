using System;
using System.Collections.Generic;
using System.Globalization;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// The bags (B) and the character sheet (C). Items move WoW-style: click (or press) a slot to pick
    /// its item up, click another slot to put it down, swap or stack; drop it outside the windows to
    /// destroy it (after asking). Right-click uses, equips, unequips or sells. Every move is only a
    /// request: the slots show what the server's InventoryUpdate says.
    /// </summary>
    public sealed class InventoryUi
    {
        private static readonly (EquipSlot Slot, string Name)[] LeftColumn =
        {
            (EquipSlot.Head, "equip.Head"), (EquipSlot.Neck, "equip.Neck"), (EquipSlot.Shoulders, "equip.Shoulders"), (EquipSlot.Back, "equip.Back"),
            (EquipSlot.Chest, "equip.Chest"), (EquipSlot.Wrists, "equip.Wrists"), (EquipSlot.Hands, "equip.Hands"),
        };
        private static readonly (EquipSlot Slot, string Name)[] RightColumn =
        {
            (EquipSlot.Waist, "equip.Waist"), (EquipSlot.Legs, "equip.Legs"), (EquipSlot.Feet, "equip.Feet"), (EquipSlot.Finger1, "equip.Finger1"),
            (EquipSlot.Finger2, "equip.Finger2"), (EquipSlot.Trinket1, "equip.Trinket1"), (EquipSlot.Trinket2, "equip.Trinket2"),
        };

        private readonly GameClient _client;
        private readonly ItemTooltip _tooltip;
        private readonly Action<string> _error;
        private readonly GameWindow _bags;
        private readonly GameWindow _character;
        private readonly GameWindow _confirm;
        private readonly Label _confirmText;
        private readonly VisualElement _bagContainers;
        private readonly VisualElement _money;
        private readonly VisualElement _stats;
        private readonly VisualElement _cursor;
        private readonly Dictionary<ItemPos, ItemSlotView> _slots = new Dictionary<ItemPos, ItemSlotView>();
        private readonly List<ItemPos> _bagSlotPositions = new List<ItemPos>();
        private ItemPos? _held;
        private ItemPos _pendingDestroy;
        private int _builtLayout = -1;
        private int _heldFrame;
        private bool _besideDialog;

        /// <param name="layer">Where the windows go.</param>
        /// <param name="overlay">A layer above the windows, for the item on the cursor.</param>
        public InventoryUi(VisualElement layer, VisualElement overlay, GameClient client, ItemTooltip tooltip, Action<string> error)
        {
            _client = client;
            _tooltip = tooltip;
            _error = error;

            _bags = new GameWindow(layer, Loc.T("window.bags"), "bags");
            var bagHeader = new VisualElement { pickingMode = PickingMode.Ignore };
            bagHeader.AddToClassList("bag-header");
            GameWindow.Text(bagHeader, Loc.T("bags.bagSlots"), "section-title");
            var bagSlots = new VisualElement { pickingMode = PickingMode.Ignore };
            bagSlots.AddToClassList("bag-slots");
            for (var slot = EquipSlot.Bag1; slot <= EquipSlot.Bag4; slot++)
                bagSlots.Add(MakeSlot(ItemPos.Equip(slot), Loc.T("bags.bag"), "small").Root);
            bagHeader.Add(bagSlots);
            _bags.Body.Add(bagHeader);
            _bagContainers = new VisualElement { pickingMode = PickingMode.Ignore };
            _bags.Body.Add(_bagContainers);
            _money = MoneyView.Create(0);
            _bags.Body.Add(_money);
            _bags.Closed += CancelHeld;

            _character = new GameWindow(layer, Loc.T("window.character"), "character");
            var doll = new VisualElement { pickingMode = PickingMode.Ignore };
            doll.AddToClassList("paperdoll");
            doll.Add(Column(LeftColumn));
            _stats = new VisualElement { pickingMode = PickingMode.Ignore };
            _stats.AddToClassList("paperdoll-middle");
            doll.Add(_stats);
            doll.Add(Column(RightColumn));
            _character.Body.Add(doll);
            var weapons = new VisualElement { pickingMode = PickingMode.Ignore };
            weapons.AddToClassList("weapon-row");
            weapons.Add(MakeSlot(ItemPos.Equip(EquipSlot.MainHand), Loc.T("character.mainHand"), "large").Root);
            weapons.Add(MakeSlot(ItemPos.Equip(EquipSlot.OffHand), Loc.T("character.offHand"), "large").Root);
            _character.Body.Add(weapons);
            _character.Closed += CancelHeld;

            _confirm = new GameWindow(layer, Loc.T("window.destroy"), "confirm");
            _confirmText = GameWindow.Text(_confirm.Body, "", "window-text");
            _confirm.AddFooterButton(Loc.T("destroy.destroy"), ConfirmDestroy, "danger");
            _confirm.AddFooterButton(Loc.T("common.cancel"), () => _confirm.Close());

            _cursor = new VisualElement { pickingMode = PickingMode.Ignore };
            _cursor.AddToClassList("cursor-item");
            _cursor.AddToClassList("hidden");
            overlay.Add(_cursor);
        }

        /// <summary>Set while a vendor window is open: right-click sells.</summary>
        public EntityGuid Vendor { get; set; }
        public bool BagsOpen => _bags.IsOpen;
        public bool CharacterOpen => _character.IsOpen;
        public bool IsHolding => _held != null;

        public void ToggleBags()
        {
            _bags.Toggle();
            Refresh();
        }

        public void ToggleCharacter()
        {
            _character.Toggle();
            Refresh();
        }

        /// <summary>The character sheet sits on the left, or right of an open dialog window.</summary>
        public void PlaceCharacter(bool besideDialog)
        {
            if (besideDialog == _besideDialog) return;
            _besideDialog = besideDialog;
            _character.Root.style.left = besideDialog ? 492f : 18f;
        }

        public void OpenBags()
        {
            if (!_bags.IsOpen) ToggleBags();
        }

        public void CloseAll()
        {
            _bags.Close();
            _character.Close();
            _confirm.Close();
            CancelHeld();
        }

        /// <summary>Esc: drops the held item, closes the confirm box, then the windows. False if nothing was open.</summary>
        public bool HandleEscape()
        {
            if (_held != null)
            {
                CancelHeld();
                return true;
            }
            if (_confirm.IsOpen)
            {
                _confirm.Close();
                return true;
            }
            if (_bags.IsOpen || _character.IsOpen)
            {
                _bags.Close();
                _character.Close();
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ refresh

        public void Refresh()
        {
            var inventory = _client.Inventory;
            int layout = 0;
            for (byte c = ItemPos.FirstBag; c < ItemPos.ContainerCount; c++) layout = layout * 64 + inventory.ContainerSize(c);
            if (layout != _builtLayout) BuildBagContainers();

            foreach (var (pos, view) in _slots)
            {
                var item = inventory.Get(pos);
                view.Set(item.IsEmpty ? null : _client.Data.GetItem(item.ItemId), item.Count);
                view.Root.EnableInClassList("held", _held == pos);
            }
            MoneyView.Set(_money, inventory.Money);
            if (_held != null && inventory.Get(_held.Value).IsEmpty) CancelHeld();
            if (_character.IsOpen) RefreshStats();
        }

        private void BuildBagContainers()
        {
            var inventory = _client.Inventory;
            foreach (var pos in _bagSlotPositions) _slots.Remove(pos);
            _bagSlotPositions.Clear();
            _bagContainers.Clear();
            int layout = 0;
            for (byte c = ItemPos.Backpack; c < ItemPos.ContainerCount; c++)
            {
                int size = inventory.ContainerSize(c);
                if (c >= ItemPos.FirstBag) layout = layout * 64 + size;
                if (size == 0) continue;
                string name = c == ItemPos.Backpack ? Loc.T("bags.backpack")
                    : _client.Data.GetItem(inventory.Equipped(ItemPos.BagSlotOf(c)).ItemId)?.Name ?? Loc.T("bags.bag");
                GameWindow.Text(_bagContainers, name, "section-title");
                var grid = new VisualElement { pickingMode = PickingMode.Ignore };
                grid.AddToClassList("item-grid");
                for (byte s = 0; s < size; s++)
                {
                    var pos = new ItemPos(c, s);
                    grid.Add(MakeSlot(pos, null, null).Root);
                    _bagSlotPositions.Add(pos);
                }
                _bagContainers.Add(grid);
            }
            _builtLayout = layout;
        }

        private void RefreshStats()
        {
            var stats = _client.Stats;
            var self = _client.Self.Values;
            _character.Title = Loc.T("character.title", _client.LocalName, self.Level, _client.Data.ClassName(self.ClassId));
            _stats.Clear();
            GameWindow.Text(_stats, Loc.T("character.attributes"), "section-title");
            for (int i = 0; i <= (int)StatType.Spirit; i++) Stat(Loc.T("stat." + (StatType)i), stats.Stats[i].ToString(CultureInfo.InvariantCulture));
            GameWindow.Text(_stats, Loc.T("character.combat"), "section-title");
            Stat(Loc.T("character.armor"), stats.Stats[(int)StatType.Armor].ToString(CultureInfo.InvariantCulture));
            Stat(Loc.T("character.damage"), stats.MeleeMinDamage + " - " + stats.MeleeMaxDamage);
            Stat(Loc.T("character.speed"), (stats.MeleeSpeedMs / 1000f).ToString("0.00", CultureInfo.InvariantCulture));
            Stat(Loc.T("character.attackPower"), stats.Stats[(int)StatType.AttackPower].ToString(CultureInfo.InvariantCulture));
            Stat(Loc.T("character.spellPower"), stats.Stats[(int)StatType.SpellPower].ToString(CultureInfo.InvariantCulture));
            Stat(Loc.T("character.crit"), stats.CritChance.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            Stat(Loc.T("character.dodge"), stats.DodgeChance.ToString("0.0", CultureInfo.InvariantCulture) + "%");
        }

        private void Stat(string name, string value)
        {
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("stat-line");
            GameWindow.Text(line, name, "stat-name");
            GameWindow.Text(line, value, "stat-value");
            _stats.Add(line);
        }

        private VisualElement Column((EquipSlot Slot, string Name)[] slots)
        {
            var column = new VisualElement { pickingMode = PickingMode.Ignore };
            column.AddToClassList("paperdoll-column");
            foreach (var (slot, name) in slots) column.Add(MakeSlot(ItemPos.Equip(slot), Loc.T(name), null).Root);
            return column;
        }

        private ItemSlotView MakeSlot(ItemPos pos, string placeholder, string size)
        {
            var view = new ItemSlotView(placeholder, size);
            view.Root.RegisterCallback<PointerDownEvent>(e => OnPointerDown(pos, e));
            view.Root.RegisterCallback<PointerUpEvent>(e => OnPointerUp(pos, e));
            view.Root.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (view.Item != null) _tooltip.Show(view.Item, view.Count, view.Root, _client, compare: pos.Container != ItemPos.Equipment);
            });
            view.Root.RegisterCallback<PointerLeaveEvent>(_ => _tooltip.Hide());
            _slots[pos] = view;
            return view;
        }

        // ------------------------------------------------------------------ clicking and moving

        private void OnPointerDown(ItemPos pos, PointerDownEvent e)
        {
            e.StopPropagation();
            if (e.button == 1)
            {
                RightClick(pos);
                return;
            }
            if (e.button != 0) return;
            if (_held == null)
            {
                if (_client.Inventory.Get(pos).IsEmpty) return;
                _held = pos;
                _heldFrame = Time.frameCount;
                ShowCursor(pos);
            }
            else
            {
                Drop(pos);
            }
            Refresh();
        }

        /// <summary>Releasing over another slot after pressing on an item drops it there (drag and drop).</summary>
        private void OnPointerUp(ItemPos pos, PointerUpEvent e)
        {
            if (e.button != 0 || _held == null || _held.Value == pos) return;
            Drop(pos);
            Refresh();
        }

        private void Drop(ItemPos pos)
        {
            var from = _held.Value;
            CancelHeld();
            if (from == pos) return;
            _client.MoveItem(from, pos);
        }

        private void RightClick(ItemPos pos)
        {
            CancelHeld();
            var stack = _client.Inventory.Get(pos);
            if (stack.IsEmpty || !_client.Data.TryGetItem(stack.ItemId, out var item)) return;
            if (!pos.IsEquipment && !Vendor.IsEmpty)
            {
                _client.SellItem(Vendor, pos);
                return;
            }
            if (!pos.IsEquipment && item.UseSpell != 0)
            {
                _client.UseItem(pos, _client.Target.IsEmpty ? _client.LocalGuid : _client.Target);
                return;
            }
            if (pos.IsEquipment || item.IsEquippable)
            {
                _client.AutoEquip(pos);
                return;
            }
            _error(Loc.T("error.notUsable"));
        }

        private void CancelHeld()
        {
            if (_held == null) return;
            _held = null;
            _cursor.AddToClassList("hidden");
            foreach (var view in _slots.Values) view.Root.RemoveFromClassList("held");
        }

        private void ShowCursor(ItemPos pos)
        {
            _cursor.Clear();
            var stack = _client.Inventory.Get(pos);
            var item = _client.Data.GetItem(stack.ItemId);
            var icon = ItemIcons.Create(item);
            _cursor.Add(icon);
            ItemIcons.SetQuality(_cursor, item?.Quality, text: false);
            _cursor.RemoveFromClassList("hidden");
        }

        /// <summary>Per frame: the held item follows the mouse; dropping it on the world asks to destroy it; cooldowns.</summary>
        public void Update(IPanel panel)
        {
            if (_held != null && Mouse.current != null && panel != null)
            {
                var screen = Mouse.current.position.ReadValue();
                var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
                _cursor.style.left = p.x - 22f;
                _cursor.style.top = p.y - 22f;
                if (Mouse.current.leftButton.wasReleasedThisFrame && Time.frameCount != _heldFrame && !UiPointer.IsOverInteractive(screen))
                    AskDestroy(_held.Value);
            }
            if (!_bags.IsOpen) return;
            foreach (var (pos, view) in _slots)
            {
                var item = view.Item;
                if (item == null || item.UseSpell == 0 || !_client.TryGetCooldown(item.UseSpell, out var left, out var total) || total <= 0)
                {
                    if (item != null) view.SetCooldown(0f);
                    continue;
                }
                view.SetCooldown((float)(left / total));
            }
        }

        /// <summary>Shows the tooltip of the first slot holding an item (scripted screenshot runs).</summary>
        public void ShowTooltipFor(uint itemId)
        {
            foreach (var (pos, view) in _slots)
            {
                if (view.Item == null || view.Item.Id != itemId) continue;
                _tooltip.Show(view.Item, view.Count, view.Root, _client, compare: pos.Container != ItemPos.Equipment);
                return;
            }
        }

        private void AskDestroy(ItemPos pos)
        {
            var stack = _client.Inventory.Get(pos);
            CancelHeld();
            if (stack.IsEmpty) return;
            _pendingDestroy = pos;
            var item = _client.Data.GetItem(stack.ItemId);
            _confirmText.text = Loc.T("destroy.confirm", ItemTooltip.Describe(_client.Data, stack.ItemId, stack.Count));
            if (item != null && item.Class == ItemClass.Quest) _confirmText.text += " It is needed for a quest.";
            _confirm.Open();
            Refresh();
        }

        private void ConfirmDestroy()
        {
            _confirm.Close();
            if (!_client.Inventory.Get(_pendingDestroy).IsEmpty) _client.DestroyItem(_pendingDestroy);
        }
    }
}
