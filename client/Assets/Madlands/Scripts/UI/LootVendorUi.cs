using System.Collections.Generic;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>The loot window of an opened corpse: click an entry to take it, or take everything.</summary>
    public sealed class LootUi
    {
        private readonly GameClient _client;
        private readonly ItemTooltip _tooltip;
        private readonly GameWindow _window;
        private readonly VisualElement _list;

        public LootUi(VisualElement layer, GameClient client, ItemTooltip tooltip)
        {
            _client = client;
            _tooltip = tooltip;
            _window = new GameWindow(layer, Loc.T("window.loot"), "loot");
            _list = new VisualElement { pickingMode = PickingMode.Ignore };
            _window.Body.Add(_list);
            _window.AddFooterButton(Loc.T("loot.takeAll"), () => _client.TakeLoot(LootTake.AllSlots), "primary");
            _window.Closed += () =>
            {
                _tooltip.Hide();
                _client.CloseLoot();
            };
        }

        public bool IsOpen => _window.IsOpen;

        public void Close() => _window.Close();

        /// <summary>Follows the client's loot state (opened, an entry taken, closed by the server).</summary>
        public void Refresh()
        {
            var loot = _client.Loot;
            if (!loot.IsOpen)
            {
                if (_window.IsOpen) _window.Close();
                return;
            }
            _window.Title = _client.World.TryGet(loot.Corpse, out var corpse) ? corpse.Name : Loc.T("window.loot");
            _list.Clear();
            _tooltip.Hide();
            if (loot.Money > 0)
            {
                var row = Row(() => _client.TakeLoot(LootTake.MoneySlot));
                var coins = new ItemSlotView(Loc.T("loot.coins"), "small");
                coins.Root.pickingMode = PickingMode.Ignore;
                coins.Root.AddToClassList("coin-icon");
                row.Add(coins.Root);
                var money = MoneyView.Create(loot.Money, "list-price");
                money.style.marginLeft = 8;
                row.Add(money);
            }
            foreach (var entry in loot.Items)
            {
                byte slot = entry.Slot;
                var item = _client.Data.GetItem(entry.ItemId);
                var row = Row(() => _client.TakeLoot(slot));
                var view = new ItemSlotView(null, "small");
                view.Set(item, entry.Count);
                view.Root.pickingMode = PickingMode.Ignore;
                row.Add(view.Root);
                var name = GameWindow.Text(row, item != null ? item.Name : "?", "list-name");
                if (item != null) ItemIcons.SetQuality(name, item.Quality, text: true);
                int count = entry.Count;
                row.RegisterCallback<PointerEnterEvent>(_ => _tooltip.Show(item, count, row, _client));
                row.RegisterCallback<PointerLeaveEvent>(_ => _tooltip.Hide());
            }
            if (!_window.IsOpen) _window.Open();
        }

        private VisualElement Row(System.Action onClick)
        {
            var row = new VisualElement { pickingMode = PickingMode.Position };
            row.AddToClassList("list-row");
            row.RegisterCallback<ClickEvent>(_ => onClick());
            _list.Add(row);
            return row;
        }
    }

    /// <summary>
    /// A vendor's wares: click buys one (shift-click a full stack). While it is open, right-clicking
    /// items in the bags sells them; "Sell junk" sells every gray item at once.
    /// </summary>
    public sealed class VendorUi
    {
        /// <summary>Walking further away than this closes the window (the server checks the real range).</summary>
        private const float CloseDistance = GameConstants.InteractRange + 3f;

        private readonly GameClient _client;
        private readonly ItemTooltip _tooltip;
        private readonly InventoryUi _inventory;
        private readonly GameWindow _window;
        private readonly VisualElement _grid;
        private EntityGuid _vendor;

        public VendorUi(VisualElement layer, GameClient client, ItemTooltip tooltip, InventoryUi inventory)
        {
            _client = client;
            _tooltip = tooltip;
            _inventory = inventory;
            _window = new GameWindow(layer, Loc.T("window.vendor"), "dialog");
            _grid = new VisualElement { pickingMode = PickingMode.Ignore };
            _grid.AddToClassList("vendor-grid");
            _window.Body.Add(_grid);
            GameWindow.Text(_window.Body, Loc.T("vendor.hint"), "window-hint");
            _window.AddFooterButton(Loc.T("vendor.sellJunk"), SellJunk);
            _window.Closed += () =>
            {
                _vendor = EntityGuid.Empty;
                _inventory.Vendor = EntityGuid.Empty;
                _tooltip.Hide();
            };
        }

        public bool IsOpen => _window.IsOpen;

        public void Close() => _window.Close();

        public void Open(VendorInventory wares)
        {
            _vendor = wares.Vendor;
            _inventory.Vendor = wares.Vendor;
            _window.Title = _client.World.TryGet(wares.Vendor, out var npc) ? npc.Name : Loc.T("window.vendor");
            _grid.Clear();
            foreach (var id in wares.ItemIds)
            {
                var item = _client.Data.GetItem(id);
                if (item == null) continue;
                var row = new VisualElement { pickingMode = PickingMode.Position };
                row.AddToClassList("list-row");
                var view = new ItemSlotView(null, "small");
                view.Set(item, 1);
                view.Root.pickingMode = PickingMode.Ignore;
                row.Add(view.Root);
                var column = new VisualElement { pickingMode = PickingMode.Ignore };
                column.style.flexGrow = 1;
                var name = GameWindow.Text(column, item.Name, "list-name");
                ItemIcons.SetQuality(name, item.Quality, text: true);
                var price = MoneyView.Create(item.BuyPrice, "list-price");
                price.style.marginLeft = 8;
                column.Add(price);
                row.Add(column);
                row.RegisterCallback<ClickEvent>(e =>
                {
                    bool stack = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
                    _client.BuyItem(_vendor, item.Id, stack ? item.StackSize : 1);
                });
                row.RegisterCallback<PointerEnterEvent>(_ => _tooltip.Show(item, 1, row, _client, item.BuyPrice));
                row.RegisterCallback<PointerLeaveEvent>(_ => _tooltip.Hide());
                _grid.Add(row);
            }
            _window.Open();
            _inventory.OpenBags();
        }

        /// <summary>Closes when the player walks away from the vendor (or it disappears).</summary>
        public void Update()
        {
            if (!_window.IsOpen) return;
            if (!_client.TryGetPosition(_vendor, out var p) || Vec3.Distance(p, _client.LocalMovement.Position) > CloseDistance) Close();
        }

        private void SellJunk()
        {
            var junk = new List<ItemPos>();
            foreach (var pos in _client.Inventory.BagPositions())
            {
                var stack = _client.Inventory.Get(pos);
                if (!stack.IsEmpty && _client.Data.TryGetItem(stack.ItemId, out var item) && item.Quality == ItemQuality.Poor && item.SellPrice > 0)
                    junk.Add(pos);
            }
            foreach (var pos in junk) _client.SellItem(_vendor, pos);
        }
    }
}
