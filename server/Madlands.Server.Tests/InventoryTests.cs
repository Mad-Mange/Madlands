using Madlands.Content;
using Madlands.Server.Combat;
using Madlands.Server.Items;
using Madlands.Shared;
using Madlands.Shared.Game;

namespace Madlands.Server.Tests;

/// <summary>The inventory rules on their own: stacking, moving, equipping, bags, money.</summary>
public class InventoryTests
{
    private const int Warrior = 1, Mage = 2;

    private static readonly ItemDefinition Bread = new() { Id = 1, Name = "Bread", Class = ItemClass.Consumable, StackSize = 20 };
    private static readonly ItemDefinition Pelt = new() { Id = 2, Name = "Pelt", Class = ItemClass.TradeGoods, StackSize = 10 };
    private static readonly ItemDefinition Sword = new()
    {
        Id = 3, Name = "Sword", Class = ItemClass.Weapon, Slot = InventoryType.MainHand, Weapon = new ItemWeaponDefinition { MinDamage = 3, MaxDamage = 6 },
    };
    private static readonly ItemDefinition Axe = new()
    {
        Id = 4, Name = "Axe", Class = ItemClass.Weapon, Slot = InventoryType.TwoHand, RequiredLevel = 5, Classes = { Warrior },
        Weapon = new ItemWeaponDefinition { MinDamage = 10, MaxDamage = 20, SpeedMs = 3200 }, Stats = { Strength = 3 },
    };
    private static readonly ItemDefinition Shield = new() { Id = 5, Name = "Shield", Class = ItemClass.Armor, Slot = InventoryType.OffHand, Stats = { Armor = 20 } };
    private static readonly ItemDefinition Ring = new() { Id = 6, Name = "Ring", Class = ItemClass.Armor, Slot = InventoryType.Finger };
    private static readonly ItemDefinition Pouch = new() { Id = 7, Name = "Pouch", Class = ItemClass.Container, Slot = InventoryType.Bag, BagSlots = 6 };
    private static readonly ItemDefinition Satchel = new() { Id = 8, Name = "Satchel", Class = ItemClass.Container, Slot = InventoryType.Bag, BagSlots = 8 };

    private static ItemPos Pack(byte slot) => new(ItemPos.Backpack, slot);
    private static ItemPos Equip(EquipSlot slot) => ItemPos.Equip(slot);

    private static ActionError Move(Inventory inv, ItemPos from, ItemPos to, int count = 0, int classId = Warrior, int level = 10) =>
        inv.Move(from, to, count, classId, level, out _);

    [Fact]
    public void Add_fills_stacks_then_empty_slots()
    {
        var inv = new Inventory();
        Assert.Equal(15, inv.Add(Pelt, 15));
        Assert.Equal(10, inv.Get(Pack(0))!.Count);
        Assert.Equal(5, inv.Get(Pack(1))!.Count);
        Assert.Equal(7, inv.Add(Pelt, 7));
        Assert.Equal(10, inv.Get(Pack(1))!.Count); // topped up first
        Assert.Equal(2, inv.Get(Pack(2))!.Count);
        Assert.Equal(22, inv.Count(Pelt.Id));
        Assert.Equal(13 * 10 + 8, inv.SpaceFor(Pelt));
        Assert.Contains(Pack(2), inv.Changed);
        Assert.True(inv.BagsChanged);
        Assert.False(inv.EquipmentChanged);
    }

    [Fact]
    public void Full_bags_take_only_what_fits()
    {
        var inv = new Inventory();
        Assert.Equal(GameConstants.BackpackSlots, inv.Add(Sword, 100));
        Assert.Equal(0, inv.Add(Bread, 1));
        Assert.Equal(0, inv.SpaceFor(Bread));
        Assert.False(inv.CanAddAll(new[] { (Bread, 1) }));
        Assert.False(inv.TryFindFreeSlot(out _));
    }

    [Fact]
    public void Remove_takes_from_the_smallest_stacks()
    {
        var inv = new Inventory();
        inv.Add(Pelt, 10);
        inv.Restore(Pack(5), new ItemStack(Pelt, 3));
        Assert.Equal(4, inv.Remove(Pelt.Id, 4));
        Assert.Null(inv.Get(Pack(5)));
        Assert.Equal(9, inv.Get(Pack(0))!.Count);
        Assert.Equal(9, inv.Remove(Pelt.Id, 50));
        Assert.Equal(0, inv.Count(Pelt.Id));
    }

    [Fact]
    public void CanAddAll_counts_partial_stacks_and_empty_slots()
    {
        var inv = new Inventory();
        inv.Add(Sword, 14); // 2 free slots left
        inv.Restore(Pack(14), new ItemStack(Pelt, 8));
        Assert.True(inv.CanAddAll(new[] { (Pelt, 2), (Bread, 20) }));
        Assert.True(inv.CanAddAll(new[] { (Pelt, 12) }));
        Assert.False(inv.CanAddAll(new[] { (Pelt, 3), (Bread, 1), (Sword, 1) }));
        Assert.False(inv.CanAddAll(new[] { (Bread, 21), (Sword, 1) }));
    }

    [Fact]
    public void Moving_merges_swaps_and_splits()
    {
        var inv = new Inventory();
        inv.Restore(Pack(0), new ItemStack(Pelt, 6));
        inv.Restore(Pack(1), new ItemStack(Pelt, 7));
        inv.Restore(Pack(2), new ItemStack(Bread, 4));

        Assert.Equal(ActionError.None, Move(inv, Pack(0), Pack(1)));
        Assert.Equal(10, inv.Get(Pack(1))!.Count);
        Assert.Equal(3, inv.Get(Pack(0))!.Count); // the rest stays

        Assert.Equal(ActionError.None, Move(inv, Pack(1), Pack(2)));
        Assert.Equal(Bread, inv.Get(Pack(1))!.Item);
        Assert.Equal(Pelt, inv.Get(Pack(2))!.Item);

        Assert.Equal(ActionError.None, Move(inv, Pack(2), Pack(9), count: 4));
        Assert.Equal((6, 4), (inv.Get(Pack(2))!.Count, inv.Get(Pack(9))!.Count));
        Assert.Equal(ActionError.None, Move(inv, Pack(2), Pack(0), count: 2));
        Assert.Equal((4, 5), (inv.Get(Pack(2))!.Count, inv.Get(Pack(0))!.Count));
        Assert.Equal(ActionError.CantDoThatNow, Move(inv, Pack(2), Pack(1), count: 1)); // can't split onto bread
        Assert.Equal(ActionError.ItemNotFound, Move(inv, Pack(15), Pack(3)));
        Assert.Equal(ActionError.ItemNotFound, Move(inv, Pack(0), new ItemPos(ItemPos.FirstBag, 0))); // no bag there
    }

    [Fact]
    public void Equipping_checks_slot_class_and_level()
    {
        var inv = new Inventory();
        inv.Restore(Pack(0), new ItemStack(Axe, 1));
        inv.Restore(Pack(1), new ItemStack(Ring, 1));
        inv.Restore(Pack(2), new ItemStack(Bread, 5));

        Assert.Equal(ActionError.CantEquipThere, Move(inv, Pack(0), Equip(EquipSlot.OffHand)));
        Assert.Equal(ActionError.WrongClass, Move(inv, Pack(0), Equip(EquipSlot.MainHand), classId: Mage));
        Assert.Equal(ActionError.LevelTooLow, inv.Move(Pack(0), Equip(EquipSlot.MainHand), 0, Warrior, 4, out int needed));
        Assert.Equal(5, needed);
        Assert.Equal(ActionError.CantEquipThere, Move(inv, Pack(2), Equip(EquipSlot.Head)));
        Assert.Equal(ActionError.NotABag, Move(inv, Pack(2), Equip(EquipSlot.Bag1)));

        Assert.Equal(ActionError.None, Move(inv, Pack(1), Equip(EquipSlot.Finger2)));
        Assert.Equal(ActionError.None, Move(inv, Pack(0), Equip(EquipSlot.MainHand)));
        Assert.True(inv.EquipmentChanged);
        Assert.Equal(Axe, inv.Equipped(EquipSlot.MainHand)!.Item);
        // Unequipping into a slot holding an item that cannot go back is refused.
        Assert.Equal(ActionError.CantEquipThere, Move(inv, Equip(EquipSlot.MainHand), Pack(2)));
        Assert.Equal(ActionError.None, Move(inv, Equip(EquipSlot.MainHand), Pack(0)));
    }

    [Fact]
    public void A_two_handed_weapon_pushes_the_shield_into_the_bags()
    {
        var inv = new Inventory();
        inv.Restore(Equip(EquipSlot.MainHand), new ItemStack(Sword, 1));
        inv.Restore(Equip(EquipSlot.OffHand), new ItemStack(Shield, 1));
        inv.Restore(Pack(0), new ItemStack(Axe, 1));

        Assert.Equal(ActionError.None, Move(inv, Pack(0), Equip(EquipSlot.MainHand)));
        Assert.Equal(Axe, inv.Equipped(EquipSlot.MainHand)!.Item);
        Assert.Null(inv.Equipped(EquipSlot.OffHand));
        Assert.Equal(Sword, inv.Get(Pack(0))!.Item);
        Assert.Equal(Shield, inv.Get(Pack(1))!.Item);

        // Nothing goes into the off hand beside a two-handed weapon.
        Assert.Equal(ActionError.TwoHandEquipped, Move(inv, Pack(1), Equip(EquipSlot.OffHand)));
    }

    [Fact]
    public void A_two_handed_weapon_needs_room_for_the_shield()
    {
        var inv = new Inventory();
        inv.Add(Bread, 20 * GameConstants.BackpackSlots);
        inv.Remove(Bread.Id, 20);
        inv.Restore(Pack(0), new ItemStack(Axe, 1));
        inv.Restore(Equip(EquipSlot.MainHand), new ItemStack(Sword, 1));
        inv.Restore(Equip(EquipSlot.OffHand), new ItemStack(Shield, 1));
        Assert.Equal(ActionError.InventoryFull, Move(inv, Pack(0), Equip(EquipSlot.MainHand)));

        // With empty hands the shield takes the axe's old slot.
        inv.Destroy(Equip(EquipSlot.MainHand));
        Assert.Equal(ActionError.None, Move(inv, Pack(0), Equip(EquipSlot.MainHand)));
        Assert.Equal(Shield, inv.Get(Pack(0))!.Item);
    }

    [Fact]
    public void Bags_add_slots_and_must_be_empty_to_move()
    {
        var inv = new Inventory();
        inv.Restore(Pack(0), new ItemStack(Pouch, 1));
        inv.Restore(Pack(1), new ItemStack(Satchel, 1));
        Assert.Equal(0, inv.ContainerSize(ItemPos.FirstBag));

        Assert.Equal(ActionError.None, Move(inv, Pack(0), Equip(EquipSlot.Bag1)));
        Assert.Equal(6, inv.ContainerSize(ItemPos.FirstBag));
        var inBag = new ItemPos(ItemPos.FirstBag, 5);
        Assert.Equal(ActionError.None, Move(inv, Pack(1), inBag)); // a bag inside a bag is fine
        Assert.Equal(ActionError.BagNotEmpty, Move(inv, Equip(EquipSlot.Bag1), Pack(4)));
        Assert.Equal(ActionError.BagNotEmpty, inv.Destroy(Equip(EquipSlot.Bag1)));
        Assert.Equal(ActionError.BagNotEmpty, Move(inv, inBag, Equip(EquipSlot.Bag1))); // swap out a full bag

        Assert.Equal(ActionError.None, Move(inv, inBag, Equip(EquipSlot.Bag2)));
        Assert.Equal(8, inv.ContainerSize(ItemPos.FirstBag + 1));
        Assert.Equal(ActionError.BagInItself, Move(inv, Equip(EquipSlot.Bag2), new ItemPos(ItemPos.FirstBag + 1, 0)));

        // Swapping an empty equipped bag with another bag resizes the container.
        inv.Restore(Pack(3), new ItemStack(Satchel, 1));
        Assert.Equal(ActionError.None, Move(inv, Pack(3), Equip(EquipSlot.Bag1)));
        Assert.Equal(8, inv.ContainerSize(ItemPos.FirstBag));
        Assert.Equal(Pouch, inv.Get(Pack(3))!.Item);

        // Bags extend the space for adding.
        Assert.Equal(16 + 8 + 8, inv.BagPositions().Count());
    }

    [Fact]
    public void Money_stays_within_bounds()
    {
        var inv = new Inventory();
        Assert.Equal(500, inv.AddMoney(500));
        Assert.Equal(-500, inv.AddMoney(-900));
        Assert.Equal(0, inv.Money);
        inv.AddMoney(long.MaxValue / 2);
        Assert.Equal(GameConstants.MaxMoney, inv.Money);
    }

    [Fact]
    public void Worn_items_add_stats_and_set_the_weapon()
    {
        var cls = new ClassDefinition
        {
            Id = Warrior, Name = "Warrior", BaseStats = { Strength = 20, Agility = 10 },
            Unarmed = new WeaponDefinition { MinDamage = 1, MaxDamage = 2, SpeedMs = 2000 },
        };
        var race = new RaceDefinition { Id = 1, Name = "Human" };
        var stats = new UnitStats();
        var inv = new Inventory();
        StatFormulas.ComputePlayer(stats, cls, race, 10, new AuraModifiers(), inv);
        Assert.Equal((1f, 2f, 2000), (stats.WeaponMin, stats.WeaponMax, stats.AttackSpeedMs));
        float armorBefore = stats.Armor;

        inv.Restore(Equip(EquipSlot.MainHand), new ItemStack(Axe, 1));
        inv.Restore(Equip(EquipSlot.OffHand), new ItemStack(Shield, 1));
        inv.Restore(Pack(0), new ItemStack(Ring, 1)); // in the bags: no effect
        StatFormulas.ComputePlayer(stats, cls, race, 10, new AuraModifiers(), inv);
        Assert.Equal((10f, 20f, 3200), (stats.WeaponMin, stats.WeaponMax, stats.AttackSpeedMs));
        Assert.Equal(23f, stats.Strength);
        Assert.Equal(armorBefore + 20, stats.Armor);
    }

    [Fact]
    public void Loot_rolls_quest_items_only_for_those_who_need_them()
    {
        var questItem = new ItemDefinition { Id = 9, Name = "Fang", Class = ItemClass.Quest, StackSize = 10 };
        var items = new[] { Pelt, questItem, Ring, Shield }.ToDictionary(i => i.Id);
        var table = new LootTableDefinition
        {
            Id = 1, Name = "Wolf", Money = new LootMoney { Min = 5, Max = 9 },
            Items = { new LootItemDefinition { Item = Pelt.Id, Chance = 1f, Min = 2, Max = 3 }, new LootItemDefinition { Item = questItem.Id, Chance = 1f } },
            Groups = { new LootGroupDefinition { Chance = 1f, Items = { Ring.Id, Shield.Id } } },
        };
        var owner = EntityGuid.Create(EntityType.Player, 1);
        var random = new Random(7);

        var without = Loot.Roll(table, items, random, owner, _ => false);
        Assert.InRange(without.Money, 5, 9);
        Assert.Contains(without.Items, i => i.Item == Pelt && i.Count is 2 or 3);
        Assert.DoesNotContain(without.Items, i => i.Item == questItem);
        Assert.Single(without.Items, i => i.Item == Ring || i.Item == Shield);
        Assert.Equal(Enumerable.Range(0, without.Items.Count).Select(i => (byte)i), without.Items.Select(i => i.Slot));

        var with = Loot.Roll(table, items, random, owner, item => item == questItem);
        Assert.Contains(with.Items, i => i.Item == questItem);
        Assert.Equal(owner, with.Owner);
    }

    [Fact]
    public void Quest_experience_falls_off_for_much_higher_levels()
    {
        Assert.Equal(400, Experience.ForQuest(5, 3, 400));
        Assert.Equal(400, Experience.ForQuest(8, 3, 400));
        Assert.Equal(320, Experience.ForQuest(9, 3, 400));
        Assert.Equal(40, Experience.ForQuest(20, 3, 400));
    }
}
