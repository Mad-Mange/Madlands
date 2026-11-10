using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;

namespace Madlands.Server.Items;

/// <summary>
/// Loot of corpses and chests for one map instance (ADR 0010). A creature's loot is rolled when it dies, for the
/// player who tapped it (groups share it later); that player sees the corpse sparkle, opens it in range and takes
/// the money and items one by one or all at once. A chest is first come, first served: its loot is rolled when
/// someone first opens it, anyone may take what is left (one player at a time), and once it is empty it
/// disappears and comes back later, at another place of its pool.
/// </summary>
public sealed class LootSystem
{
    /// <summary>A corpse with loot stays at least this long.</summary>
    public const long LootCorpseMs = 90_000;
    /// <summary>A looted corpse disappears this soon (unless its normal time is later).</summary>
    public const long EmptiedCorpseMs = 3_000;
    /// <summary>A chest that was opened but not emptied disappears this long after it was first opened.</summary>
    public const long OpenedChestMs = 120_000;

    private readonly MapInstance _map;
    private readonly LootWindow _windowMsg = new();
    private readonly LootableUpdate _lootableMsg = new();
    private readonly List<Player> _closeScratch = new();
    /// <summary>Chests that were opened and still hold something.</summary>
    private readonly List<WorldObject> _openedChests = new();

    public LootSystem(MapInstance map)
    {
        _map = map;
    }

    /// <summary>Rolls the creature's loot for <paramref name="owner"/> and tells them the corpse can be looted.</summary>
    public void OnCreatureKilled(Creature creature, Player? owner, long now)
    {
        creature.Loot = null;
        if (owner is null || creature.Template.Loot == 0 || !_map.Content.LootTables.TryGetValue(creature.Template.Loot, out var table)) return;
        var loot = Loot.Roll(table, _map.Content.Items, _map.Random, owner.Guid, item => _map.Quests.NeedsItem(owner, item.Id));
        if (loot.IsEmpty) return;
        creature.Loot = loot;
        creature.DespawnAtMs = Math.Max(creature.DespawnAtMs, now + LootCorpseMs);
        SendLootable(owner, creature, true);
    }

    /// <summary>A corpse came into a player's view: show the sparkle again if it is theirs.</summary>
    public void OnCorpseSeen(Player observer, Creature creature)
    {
        if (creature.Loot is { } loot && loot.Owner == observer.Guid) SendLootable(observer, creature, true);
    }

    /// <summary>The corpse or chest is leaving the map: close its loot window.</summary>
    public void OnSourceRemoved(ILootSource source)
    {
        if (source.Looter is { } looter) Close(looter, notify: true);
        source.Loot = null;
        if (source is WorldObject chest) _openedChests.Remove(chest);
    }

    /// <summary>The player asks to open a corpse (or a chest) by its guid.</summary>
    public void Open(Player player, EntityGuid guid)
    {
        if (_map.Find(guid) is WorldObject chest)
        {
            OpenChest(player, chest);
            return;
        }
        if (player.Looting is not null) Close(player, notify: false);
        var creature = _map.FindInteractable(player, guid, out var error, corpse: true);
        if (creature is null)
        {
            _map.Items.Fail(player, error);
            return;
        }
        if (creature.Loot is not { } loot || loot.IsEmpty)
        {
            _map.Items.Fail(player, ActionError.LootNotYours);
            return;
        }
        if (loot.Owner != player.Guid)
        {
            _map.Items.Fail(player, ActionError.LootNotYours);
            return;
        }
        if (creature.Looter is { } other && other != player) Close(other, notify: true);
        Show(player, creature, loot);
    }

    /// <summary>Right-click on a chest: the first opener rolls its loot; later ones see what is left.</summary>
    public void OpenChest(Player player, WorldObject chest)
    {
        if (player.Looting is not null) Close(player, notify: false);
        if (!chest.Template.IsChest || chest.Map != _map || player.IsDead)
        {
            _map.Items.Fail(player, ActionError.CantInteract);
            return;
        }
        if (Vec3.Distance(player.Position, chest.Position) > GameConstants.InteractRange + MapInstance.InteractLeeway)
        {
            _map.Items.Fail(player, ActionError.TooFarAway);
            return;
        }
        if (chest.Looter is { } other && other != player)
        {
            _map.Items.Fail(player, ActionError.LootBusy);
            return;
        }
        var loot = chest.Loot;
        if (loot is null)
        {
            if (!_map.Content.LootTables.TryGetValue(chest.Template.Loot, out var table))
            {
                _map.Items.Fail(player, ActionError.CantInteract);
                return;
            }
            // Chest tables hold no quest items (content validation), so nothing depends on who opens it first.
            loot = chest.Loot = Loot.Roll(table, _map.Content.Items, _map.Random, EntityGuid.Empty, _ => false);
            chest.OpenedAtMs = _map.NowMs;
            _openedChests.Add(chest);
        }
        if (loot.IsEmpty)
        {
            _map.Gathered(chest, _map.NowMs);
            return;
        }
        Show(player, chest, loot);
    }

    public void Take(Player player, byte slot)
    {
        if (player.Looting is not { Loot: { } loot } source) return;
        if (Vec3.Distance(player.Position, source.Position) > GameConstants.InteractRange + MapInstance.InteractLeeway || player.IsDead)
        {
            Close(player, notify: true);
            _map.Items.Fail(player, ActionError.TooFarAway);
            return;
        }

        bool changed = false;
        if ((slot == LootTake.MoneySlot || slot == LootTake.AllSlots) && loot.Money > 0)
        {
            _map.Items.GiveMoney(player, loot.Money, ReceiveSource.Loot);
            loot.Money = 0;
            changed = true;
        }
        for (int i = 0; i < loot.Items.Count; i++)
        {
            var entry = loot.Items[i];
            if (slot != LootTake.AllSlots && entry.Slot != slot) continue;
            if (player.Inventory.SpaceFor(entry.Item) < entry.Count)
            {
                _map.Items.Fail(player, ActionError.InventoryFull);
                break;
            }
            _map.Items.Give(player, entry.Item, entry.Count, ReceiveSource.Loot);
            loot.Items.RemoveAt(i--);
            changed = true;
        }
        if (!changed) return;
        _map.Items.Flush(player);

        if (!loot.IsEmpty)
        {
            SendWindow(player, source, loot);
            return;
        }
        source.Loot = null;
        Close(player, notify: true);
        if (source is Creature creature)
        {
            creature.DespawnAtMs = Math.Min(creature.DespawnAtMs,
                Math.Max(_map.NowMs + EmptiedCorpseMs, creature.DiedAtMs + creature.Template.CorpseSeconds * 1000L));
            SendLootable(player, creature, false);
        }
        else if (source is WorldObject chest)
        {
            _map.Gathered(chest, _map.NowMs);
        }
    }

    /// <summary>Closes the player's loot window; with <paramref name="notify"/> the client is told (the server closed it).</summary>
    public void Close(Player player, bool notify)
    {
        if (player.Looting is not { } source) return;
        player.Looting = null;
        if (source.Looter == player) source.Looter = null;
        if (!notify) return;
        _windowMsg.Corpse = source.Guid;
        _windowMsg.Open = false;
        _windowMsg.Money = 0;
        _windowMsg.Entries.Clear();
        _map.Send(player, _windowMsg);
    }

    /// <summary>Closes loot windows whose looter walked away or died, and takes away chests left open too long.</summary>
    public void Update()
    {
        _closeScratch.Clear();
        foreach (var player in _map.Players)
        {
            if (player.Looting is not { } source) continue;
            if (player.IsDead || source.Map != _map ||
                Vec3.Distance(player.Position, source.Position) > GameConstants.InteractRange + MapInstance.InteractLeeway)
                _closeScratch.Add(player);
        }
        foreach (var player in _closeScratch) Close(player, notify: true);

        for (int i = _openedChests.Count - 1; i >= 0; i--)
        {
            var chest = _openedChests[i];
            if (_map.NowMs - chest.OpenedAtMs < OpenedChestMs) continue;
            _openedChests.RemoveAt(i);
            if (chest.Map == _map) _map.Gathered(chest, _map.NowMs);
        }
    }

    private void Show(Player player, ILootSource source, Loot loot)
    {
        player.Looting = source;
        source.Looter = player;
        SendWindow(player, source, loot);
    }

    private void SendWindow(Player player, ILootSource source, Loot loot)
    {
        _windowMsg.Corpse = source.Guid;
        _windowMsg.Open = true;
        _windowMsg.Money = loot.Money;
        _windowMsg.Entries.Clear();
        foreach (var item in loot.Items)
            _windowMsg.Entries.Add(new LootEntry { Slot = item.Slot, ItemId = item.Item.Id, Count = (ushort)item.Count });
        _map.Send(player, _windowMsg);
    }

    private void SendLootable(Player player, Creature creature, bool lootable)
    {
        _lootableMsg.Corpse = creature.Guid;
        _lootableMsg.Lootable = lootable;
        _map.Send(player, _lootableMsg);
    }
}
