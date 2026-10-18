using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;

namespace Madlands.Server.Items;

/// <summary>
/// Corpse loot for one map instance (ADR 0010). Loot is rolled when a creature dies, for the player
/// who tapped it (groups share it later); that player sees the corpse sparkle, opens it in range and
/// takes the money and items one by one or all at once.
/// </summary>
public sealed class LootSystem
{
    /// <summary>A corpse with loot stays at least this long.</summary>
    public const long LootCorpseMs = 90_000;
    /// <summary>A looted corpse disappears this soon (unless its normal time is later).</summary>
    public const long EmptiedCorpseMs = 3_000;

    private readonly MapInstance _map;
    private readonly LootWindow _windowMsg = new();
    private readonly LootableUpdate _lootableMsg = new();
    private readonly List<Player> _closeScratch = new();

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

    /// <summary>The corpse is leaving the map: close its loot window.</summary>
    public void OnCreatureRemoved(Creature creature)
    {
        if (creature.Looter is { } looter) Close(looter, notify: true);
        creature.Loot = null;
    }

    public void Open(Player player, EntityGuid corpseGuid)
    {
        if (player.Looting is not null) Close(player, notify: false);
        var creature = _map.FindInteractable(player, corpseGuid, out var error, corpse: true);
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
        player.Looting = creature;
        creature.Looter = player;
        SendWindow(player, creature, loot);
    }

    public void Take(Player player, byte slot)
    {
        if (player.Looting is not { Loot: { } loot } creature) return;
        if (Vec3.Distance(player.Position, creature.Position) > GameConstants.InteractRange + MapInstance.InteractLeeway || player.IsDead)
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

        if (loot.IsEmpty)
        {
            creature.Loot = null;
            creature.DespawnAtMs = Math.Min(creature.DespawnAtMs,
                Math.Max(_map.NowMs + EmptiedCorpseMs, creature.DiedAtMs + creature.Template.CorpseSeconds * 1000L));
            Close(player, notify: true);
            SendLootable(player, creature, false);
        }
        else
        {
            SendWindow(player, creature, loot);
        }
    }

    /// <summary>Closes the player's loot window; with <paramref name="notify"/> the client is told (the server closed it).</summary>
    public void Close(Player player, bool notify)
    {
        if (player.Looting is not { } creature) return;
        player.Looting = null;
        if (creature.Looter == player) creature.Looter = null;
        if (!notify) return;
        _windowMsg.Corpse = creature.Guid;
        _windowMsg.Open = false;
        _windowMsg.Money = 0;
        _windowMsg.Entries.Clear();
        _map.Send(player, _windowMsg);
    }

    /// <summary>Closes loot windows whose looter walked away or died.</summary>
    public void Update()
    {
        _closeScratch.Clear();
        foreach (var player in _map.Players)
        {
            if (player.Looting is not { } creature) continue;
            if (player.IsDead || creature.Map != _map ||
                Vec3.Distance(player.Position, creature.Position) > GameConstants.InteractRange + MapInstance.InteractLeeway)
                _closeScratch.Add(player);
        }
        foreach (var player in _closeScratch) Close(player, notify: true);
    }

    private void SendWindow(Player player, Creature creature, Loot loot)
    {
        _windowMsg.Corpse = creature.Guid;
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
