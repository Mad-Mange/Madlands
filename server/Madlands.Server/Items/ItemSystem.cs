using Madlands.Content;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;

namespace Madlands.Server.Items;

/// <summary>
/// Item requests for one map instance (ADR 0010): moving, equipping, using, destroying, buying and
/// selling. Every request is checked against the player's real inventory; changes go out as
/// <see cref="InventoryUpdate"/> deltas from <see cref="Flush"/>.
/// </summary>
public sealed class ItemSystem
{
    private readonly MapInstance _map;
    private readonly InventoryUpdate _inventoryMsg = new();
    private readonly MoneyUpdate _moneyMsg = new();
    private readonly ItemReceived _receivedMsg = new();
    private readonly ActionFailed _failedMsg = new();
    private readonly VendorInventory _vendorMsg = new();

    public ItemSystem(MapInstance map)
    {
        _map = map;
    }

    private ContentDatabase Content => _map.Content;

    public void Fail(Player player, ActionError error, int value = 0)
    {
        if (error == ActionError.None) return;
        _failedMsg.Error = error;
        _failedMsg.Value = value;
        _map.Send(player, _failedMsg);
    }

    // ------------------------------------------------------------------ requests

    public void Move(Player player, ItemPos from, ItemPos to, int count)
    {
        if (player.IsDead && (from.IsEquipment || to.IsEquipment))
        {
            Fail(player, ActionError.CantDoThatNow);
            return;
        }
        var error = player.Inventory.Move(from, to, count, player.ClassId, player.Level, out int value);
        Fail(player, error, value);
        Flush(player);
    }

    /// <summary>Right-click: equips a bag item where it fits, or puts an equipped item into the bags.</summary>
    public void AutoEquip(Player player, ItemPos pos)
    {
        var inventory = player.Inventory;
        if (inventory.Get(pos) is not { } stack)
        {
            Fail(player, ActionError.ItemNotFound);
            return;
        }
        if (pos.IsEquipment)
        {
            byte exclude = pos.IsBagSlot ? ItemPos.ContainerOf((EquipSlot)pos.Slot) : byte.MaxValue;
            if (!inventory.TryFindFreeSlot(out var free, exclude))
            {
                Fail(player, ActionError.InventoryFull);
                return;
            }
            Move(player, pos, free, 0);
            return;
        }
        var slots = ItemRules.SlotsFor(stack.Item.Slot);
        if (slots.Length == 0)
        {
            Fail(player, ActionError.CantEquipThere);
            return;
        }
        var slot = slots.FirstOrDefault(s => inventory.Equipped(s) is null, slots[0]);
        Move(player, pos, ItemPos.Equip(slot), 0);
    }

    public void Destroy(Player player, ItemPos pos)
    {
        Fail(player, player.Inventory.Destroy(pos));
        Flush(player);
    }

    /// <summary>Uses a consumable: casts its spell like a known spell and takes one from the stack if the cast happened.</summary>
    public void Use(Player player, ItemPos pos, EntityGuid target, byte castId, long now)
    {
        if (pos.IsEquipment || player.Inventory.Get(pos) is not { } stack)
        {
            Fail(player, ActionError.ItemNotFound);
            return;
        }
        var item = stack.Item;
        if (item.UseSpell == 0 || !Content.Spells.ContainsKey(item.UseSpell))
        {
            Fail(player, ActionError.NotUsable);
            return;
        }
        if (!item.AllowsClass(player.ClassId))
        {
            Fail(player, ActionError.WrongClass);
            return;
        }
        if (player.Level < item.RequiredLevel)
        {
            Fail(player, ActionError.LevelTooLow, item.RequiredLevel);
            return;
        }
        // Use spells are instant (content validation), so success means the spell went off.
        if (_map.Spells.TryCast(player, item.UseSpell, target, castId, now, ignoreKnown: true) != CastResult.Success) return;
        player.Inventory.Consume(pos);
        Flush(player);
    }

    // ------------------------------------------------------------------ vendors

    public void SendVendor(Player player, Creature vendor)
    {
        _vendorMsg.Vendor = vendor.Guid;
        _vendorMsg.ItemIds.Clear();
        _vendorMsg.ItemIds.AddRange(vendor.Template.Vendor);
        _map.Send(player, _vendorMsg);
    }

    public void Buy(Player player, EntityGuid vendorGuid, uint itemId, int count)
    {
        if (FindVendor(player, vendorGuid) is not { } vendor) return;
        if (!vendor.Template.Vendor.Contains(itemId) || !Content.Items.TryGetValue(itemId, out var item))
        {
            Fail(player, ActionError.ItemNotFound);
            return;
        }
        count = Math.Clamp(count, 1, item.StackSize);
        long price = (long)item.BuyPrice * count;
        if (player.Inventory.Money < price)
        {
            Fail(player, ActionError.NotEnoughMoney);
            return;
        }
        if (player.Inventory.SpaceFor(item) < count)
        {
            Fail(player, ActionError.InventoryFull);
            return;
        }
        GiveMoney(player, -price, ReceiveSource.Vendor);
        Give(player, item, count, ReceiveSource.Vendor);
        Flush(player);
    }

    /// <summary>Sells the stack at a bag position (equipped items must be taken off first).</summary>
    public void Sell(Player player, EntityGuid vendorGuid, ItemPos pos)
    {
        if (FindVendor(player, vendorGuid) is null) return;
        if (pos.IsEquipment || player.Inventory.Get(pos) is not { } stack)
        {
            Fail(player, ActionError.ItemNotFound);
            return;
        }
        if (stack.Item.SellPrice <= 0 || stack.Item.Class == ItemClass.Quest)
        {
            Fail(player, ActionError.CantSellItem);
            return;
        }
        long price = (long)stack.Item.SellPrice * stack.Count;
        player.Inventory.Destroy(pos);
        GiveMoney(player, price, ReceiveSource.Vendor);
        Flush(player);
    }

    private Creature? FindVendor(Player player, EntityGuid guid)
    {
        var vendor = _map.FindInteractable(player, guid, out var error);
        if (vendor is not null && (vendor.NpcFlags & NpcFlags.Vendor) == 0) error = ActionError.CantInteract;
        if (error == ActionError.None) return vendor;
        Fail(player, error);
        return null;
    }

    // ------------------------------------------------------------------ gathering

    /// <summary>Right-click on a gatherable object: its item goes into the bags, and it grows back later.</summary>
    public void Gather(Player player, EntityGuid guid, long now)
    {
        if (_map.Find(guid) is not WorldObject obj || player.IsDead)
        {
            Fail(player, ActionError.CantInteract);
            return;
        }
        if (Vec3.Distance(player.Position, obj.Position) > GameConstants.InteractRange + MapInstance.InteractLeeway)
        {
            Fail(player, ActionError.TooFarAway);
            return;
        }
        var template = obj.Template;
        if (template.QuestOnly && !_map.Quests.NeedsItem(player, template.Item))
        {
            Fail(player, ActionError.NotNeeded);
            return;
        }
        if (!_map.Content.Items.TryGetValue(template.Item, out var item)) return;
        int count = _map.Random.Next(template.MinCount, template.MaxCount + 1);
        if (Give(player, item, count, ReceiveSource.Loot) == 0)
        {
            Fail(player, ActionError.InventoryFull);
            return;
        }
        Flush(player);
        _map.Gathered(obj, now);
    }

    // ------------------------------------------------------------------ giving

    /// <summary>Puts items into the bags (as many as fit) and tells the player. Call <see cref="Flush"/> afterwards.</summary>
    public int Give(Player player, ItemDefinition item, int count, ReceiveSource source)
    {
        int added = player.Inventory.Add(item, count);
        if (added <= 0) return 0;
        _receivedMsg.ItemId = item.Id;
        _receivedMsg.Count = (ushort)Math.Min(added, ushort.MaxValue);
        _receivedMsg.Source = source;
        _map.Send(player, _receivedMsg);
        return added;
    }

    /// <summary>Adds (or takes, if negative) money and tells the player.</summary>
    public void GiveMoney(Player player, long amount, ReceiveSource source)
    {
        long delta = player.Inventory.AddMoney(amount);
        if (delta != 0) SendMoney(player, delta, source);
    }

    /// <summary>Hands out the class's starting items once: worn where they fit, the rest into the bags.</summary>
    public void GiveStartingItems(Player player)
    {
        var inventory = player.Inventory;
        foreach (var start in player.Class.StartingItems)
        {
            if (!Content.Items.TryGetValue(start.Item, out var item)) continue;
            bool equipped = false;
            if (item.IsEquippable)
            {
                foreach (var slot in ItemRules.SlotsFor(item.Slot))
                {
                    if (inventory.Equipped(slot) is not null || Inventory.CanEquip(item, slot, player.ClassId, player.Level, out _) != ActionError.None) continue;
                    equipped = inventory.Restore(ItemPos.Equip(slot), new ItemStack(item, 1));
                    break;
                }
            }
            if (!equipped) inventory.Add(item, start.Count);
        }
        player.StarterKitGiven = true;
    }

    // ------------------------------------------------------------------ sending

    /// <summary>Sends the changed slots, then updates stats and looks (equipment) and quest counts (bags).</summary>
    public void Flush(Player player)
    {
        var inventory = player.Inventory;
        if (inventory.Changed.Count > 0)
        {
            _inventoryMsg.Full = false;
            _inventoryMsg.Entries.Clear();
            foreach (var pos in inventory.Changed) _inventoryMsg.Entries.Add(Entry(pos, inventory.Get(pos)));
            _map.Send(player, _inventoryMsg);
        }
        bool equipment = inventory.EquipmentChanged;
        bool bags = inventory.BagsChanged;
        inventory.ClearChanges();
        if (equipment) player.RecalculateStats();
        if (bags) _map.Quests.OnItemsChanged(player);
    }

    /// <summary>Everything the player carries and their money (entering the world).</summary>
    public void SendAll(Player player)
    {
        var inventory = player.Inventory;
        _inventoryMsg.Full = true;
        _inventoryMsg.Entries.Clear();
        foreach (var (pos, stack) in inventory.All()) _inventoryMsg.Entries.Add(Entry(pos, stack));
        _map.Send(player, _inventoryMsg);
        inventory.ClearChanges();
        SendMoney(player, 0, ReceiveSource.Other);
    }

    private void SendMoney(Player player, long delta, ReceiveSource source)
    {
        _moneyMsg.Money = player.Inventory.Money;
        _moneyMsg.Delta = delta;
        _moneyMsg.Source = source;
        _map.Send(player, _moneyMsg);
    }

    private static InventoryEntry Entry(ItemPos pos, ItemStack? stack) => new()
    {
        Pos = pos,
        ItemId = stack?.Item.Id ?? 0,
        Count = (ushort)Math.Clamp(stack?.Count ?? 0, 0, ushort.MaxValue),
    };
}
