using Madlands.Content;
using Madlands.Shared;
using Madlands.Shared.Game;

namespace Madlands.Server.Items;

public sealed class LootItem
{
    public LootItem(byte slot, ItemDefinition item, int count)
    {
        Slot = slot;
        Item = item;
        Count = count;
    }

    /// <summary>Stable position in the loot window while it is open.</summary>
    public byte Slot { get; }
    public ItemDefinition Item { get; }
    public int Count { get; }
}

/// <summary>What a corpse holds for the player who may loot it.</summary>
public sealed class Loot
{
    public Loot(EntityGuid owner)
    {
        Owner = owner;
    }

    public EntityGuid Owner { get; }
    /// <summary>Copper.</summary>
    public long Money;
    public List<LootItem> Items { get; } = new();
    public bool IsEmpty => Money <= 0 && Items.Count == 0;

    /// <summary>
    /// Rolls a loot table: money, each item by its chance (quest items only if
    /// <paramref name="wantsQuestItem"/> says the looter still needs them), and one item per group.
    /// </summary>
    public static Loot Roll(LootTableDefinition table, IReadOnlyDictionary<uint, ItemDefinition> items, Random random,
        EntityGuid owner, Func<ItemDefinition, bool> wantsQuestItem)
    {
        var loot = new Loot(owner);
        if (table.Money is { } money) loot.Money = random.Next(money.Min, money.Max + 1);
        foreach (var entry in table.Items)
        {
            if (!items.TryGetValue(entry.Item, out var item)) continue;
            if (item.Class == ItemClass.Quest && !wantsQuestItem(item)) continue;
            if (random.NextSingle() >= entry.Chance) continue;
            loot.Add(item, random.Next(entry.Min, entry.Max + 1));
        }
        foreach (var group in table.Groups)
        {
            if (random.NextSingle() >= group.Chance || group.Items.Count == 0) continue;
            if (items.TryGetValue(group.Items[random.Next(group.Items.Count)], out var item)) loot.Add(item, 1);
        }
        return loot;
    }

    private void Add(ItemDefinition item, int count)
    {
        if (Items.Count >= Shared.Net.LootWindow.MaxEntries) return;
        Items.Add(new LootItem((byte)Items.Count, item, count));
    }
}
