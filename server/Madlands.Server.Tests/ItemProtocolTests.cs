using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;
using static Madlands.Server.Tests.ProtocolTests;

namespace Madlands.Server.Tests;

/// <summary>Roundtrips of the Phase 4 messages (inventory, loot, vendors, NPC dialogs, quests).</summary>
public class ItemProtocolTests
{
    private static readonly EntityGuid Npc = EntityGuid.Create(EntityType.Creature, 12);
    private static readonly EntityGuid Wolf = EntityGuid.Create(EntityType.Creature, 77);
    private static readonly ItemPos Bag = new(ItemPos.FirstBag + 1, 7);

    [Fact]
    public void Item_requests_roundtrip()
    {
        var move = Roundtrip(new MoveItem { From = Bag, To = ItemPos.Equip(EquipSlot.MainHand), Count = 3 });
        Assert.Equal((Bag, ItemPos.Equip(EquipSlot.MainHand), (ushort)3), (move.From, move.To, move.Count));
        var use = Roundtrip(new UseItem { Pos = Bag, Target = Wolf, CastId = 4 });
        Assert.Equal((Bag, Wolf, (byte)4), (use.Pos, use.Target, use.CastId));
        Assert.Equal(Bag, Roundtrip(new DestroyItem { Pos = Bag }).Pos);
        Assert.Equal(Bag, Roundtrip(new AutoEquip { Pos = Bag }).Pos);
        var interact = Roundtrip(new Interact { Npc = Npc, Option = InteractOption.Vendor });
        Assert.Equal((Npc, InteractOption.Vendor), (interact.Npc, interact.Option));
        Assert.Equal(Wolf, Roundtrip(new LootOpen { Corpse = Wolf }).Corpse);
        Assert.Equal(LootTake.MoneySlot, Roundtrip(new LootTake { Slot = LootTake.MoneySlot }).Slot);
        Roundtrip(new LootClose());
        var buy = Roundtrip(new VendorBuy { Vendor = Npc, ItemId = 4001, Count = 20 });
        Assert.Equal((Npc, 4001u, (ushort)20), (buy.Vendor, buy.ItemId, buy.Count));
        var sell = Roundtrip(new VendorSell { Vendor = Npc, Pos = Bag });
        Assert.Equal((Npc, Bag), (sell.Vendor, sell.Pos));
    }

    [Fact]
    public void Quest_requests_roundtrip()
    {
        var accept = Roundtrip(new QuestAccept { Npc = Npc, QuestId = 7 });
        Assert.Equal((Npc, 7u), (accept.Npc, accept.QuestId));
        var turnIn = Roundtrip(new QuestTurnIn { Npc = Npc, QuestId = 7, Choice = 2 });
        Assert.Equal((Npc, 7u, (byte)2), (turnIn.Npc, turnIn.QuestId, turnIn.Choice));
        Assert.Equal(QuestTurnIn.NoChoice, Roundtrip(new QuestTurnIn { Npc = Npc, QuestId = 1 }).Choice);
        Assert.Equal(3u, Roundtrip(new QuestAbandon { QuestId = 3 }).QuestId);
    }

    [Fact]
    public void Inventory_messages_roundtrip()
    {
        var update = new InventoryUpdate { Full = true };
        update.Entries.Add(new InventoryEntry { Pos = ItemPos.Equip(EquipSlot.Bag2), ItemId = 5001, Count = 1 });
        update.Entries.Add(new InventoryEntry { Pos = Bag, ItemId = 0, Count = 0 });
        var copy = Roundtrip(update);
        Assert.True(copy.Full);
        Assert.Equal(update.Entries, copy.Entries);

        var money = Roundtrip(new MoneyUpdate { Money = GameConstants.MaxMoney, Delta = -250, Source = ReceiveSource.Vendor });
        Assert.Equal((GameConstants.MaxMoney, -250L, ReceiveSource.Vendor), (money.Money, money.Delta, money.Source));
        var received = Roundtrip(new ItemReceived { ItemId = 3001, Count = 2, Source = ReceiveSource.Loot });
        Assert.Equal((3001u, (ushort)2, ReceiveSource.Loot), (received.ItemId, received.Count, received.Source));
        var failed = Roundtrip(new ActionFailed { Error = ActionError.LevelTooLow, Value = 5 });
        Assert.Equal((ActionError.LevelTooLow, 5), (failed.Error, failed.Value));
    }

    [Fact]
    public void Loot_and_vendor_messages_roundtrip()
    {
        var window = new LootWindow { Corpse = Wolf, Open = true, Money = 17 };
        window.Entries.Add(new LootEntry { Slot = 0, ItemId = 2003, Count = 1 });
        window.Entries.Add(new LootEntry { Slot = 2, ItemId = 3001, Count = 3 });
        var copy = Roundtrip(window);
        Assert.Equal((Wolf, true, 17L), (copy.Corpse, copy.Open, copy.Money));
        Assert.Equal(window.Entries, copy.Entries);

        var lootable = Roundtrip(new LootableUpdate { Corpse = Wolf, Lootable = true });
        Assert.Equal((Wolf, true), (lootable.Corpse, lootable.Lootable));

        var vendor = new VendorInventory { Vendor = Npc };
        vendor.ItemIds.AddRange(new uint[] { 4001, 4002, 5001 });
        var vendorCopy = Roundtrip(vendor);
        Assert.Equal(Npc, vendorCopy.Vendor);
        Assert.Equal(vendor.ItemIds, vendorCopy.ItemIds);
    }

    [Fact]
    public void Quest_messages_roundtrip()
    {
        var gossip = new GossipMenu { Npc = Npc, Text = "Welcome, traveller.", Flags = NpcFlags.Gossip | NpcFlags.QuestGiver };
        gossip.Quests.Add(new GossipQuest { QuestId = 3, Status = QuestGiverStatus.Reward });
        gossip.Quests.Add(new GossipQuest { QuestId = 4, Status = QuestGiverStatus.Available });
        var gossipCopy = Roundtrip(gossip);
        Assert.Equal((Npc, "Welcome, traveller.", NpcFlags.Gossip | NpcFlags.QuestGiver), (gossipCopy.Npc, gossipCopy.Text, gossipCopy.Flags));
        Assert.Equal(gossip.Quests, gossipCopy.Quests);

        var log = new QuestLogUpdate { Full = true };
        log.Entries.Add(new QuestLogEntry { QuestId = 7, State = QuestLogState.Active, Progress = new ushort[] { 3, 0, 4 } });
        log.Entries.Add(new QuestLogEntry { QuestId = 5, State = QuestLogState.Complete, Progress = new ushort[] { 1 } });
        log.Completed.AddRange(new uint[] { 1, 2, 3 });
        var logCopy = Roundtrip(log);
        Assert.True(logCopy.Full);
        Assert.Equal(log.Entries.Select(e => (e.QuestId, e.State, string.Join(",", e.Progress))),
            logCopy.Entries.Select(e => (e.QuestId, e.State, string.Join(",", e.Progress))));
        Assert.Equal(log.Completed, logCopy.Completed);

        // A delta carries no completed list.
        var delta = new QuestLogUpdate();
        delta.Entries.Add(new QuestLogEntry { QuestId = 7, State = QuestLogState.Removed, Progress = Array.Empty<ushort>() });
        delta.Completed.Add(99);
        Assert.Empty(Roundtrip(delta).Completed);

        var status = new QuestGiverStatusUpdate();
        status.Entries.Add(new QuestGiverEntry { Npc = Npc, Status = QuestGiverStatus.Incomplete });
        Assert.Equal(status.Entries, Roundtrip(status).Entries);

        var done = Roundtrip(new QuestCompleted { QuestId = 7, Xp = 900, Money = 150 });
        Assert.Equal((7u, 900, 150L), (done.QuestId, done.Xp, done.Money));
    }

    [Fact]
    public void GameData_carries_items_and_quests()
    {
        var data = new GameData();
        var axe = new ItemInfo
        {
            Id = 1020, Name = "Palisade Breaker", Description = "Big.", Icon = 1020, DisplayId = 2009, Quality = ItemQuality.Uncommon,
            Class = ItemClass.Weapon, InventoryType = InventoryType.TwoHand, RequiredLevel = 5, AllowedClasses = new byte[] { 1, 3 },
            StackSize = 1, BuyPrice = 0, SellPrice = 110, MinDamage = 13, MaxDamage = 20, SpeedMs = 3200,
        };
        axe.Stats.Add(new ItemStat { Stat = StatType.Strength, Value = 3 });
        axe.Stats.Add(new ItemStat { Stat = StatType.Stamina, Value = 2 });
        data.Items.Add(axe);
        data.Items.Add(new ItemInfo { Id = 5001, Name = "Pouch", Class = ItemClass.Container, InventoryType = InventoryType.Bag, BagSlots = 6 });

        var quest = new QuestInfo
        {
            Id = 7, Title = "Shards in the Wrong Hands", Level = 5, Details = "Go.", Objectives = "Kill and collect.", Progress = "Well?",
            Completion = "Thanks.", EnderName = "Captain Edda Brand", RewardXp = 900, RewardMoney = 150,
        };
        quest.EnderPlaces.Add(new QuestArea { Map = 1, X = 20, Z = -163, Radius = 10 });
        quest.ObjectiveList.Add(new QuestObjectiveInfo { Type = ObjectiveType.Kill, Target = 3, Count = 6, Text = "Shard Bandit slain" });
        quest.ObjectiveList.Add(new QuestObjectiveInfo
        {
            Type = ObjectiveType.Explore, Target = 0, Count = 1, Text = "Scout the camp",
            Areas = new List<QuestArea> { new QuestArea { Map = 1, X = 95, Z = 75, Radius = 22 }, new QuestArea { Map = 2, X = -3, Z = 4, Radius = 30 } },
        });
        quest.RewardItems.Add(new QuestItemReward { Item = 4003, Count = 2 });
        quest.RewardChoices.Add(new QuestItemReward { Item = 1020, Count = 1 });
        quest.RewardChoices.Add(new QuestItemReward { Item = 1021, Count = 1 });
        data.Quests.Add(quest);

        var copy = Roundtrip(data);
        var item = copy.Items[0];
        Assert.Equal((axe.Id, axe.Name, axe.Description, axe.DisplayId, axe.Quality, axe.InventoryType, axe.RequiredLevel),
            (item.Id, item.Name, item.Description, item.DisplayId, item.Quality, item.InventoryType, item.RequiredLevel));
        Assert.Equal(axe.AllowedClasses, item.AllowedClasses);
        Assert.Equal((13, 20, 3200, 110u), (item.MinDamage, item.MaxDamage, item.SpeedMs, item.SellPrice));
        Assert.Equal(3, item.GetStat(StatType.Strength));
        Assert.True(item.AllowsClass(3));
        Assert.False(item.AllowsClass(2));
        Assert.Equal(6, copy.Items[1].BagSlots);
        Assert.True(copy.Items[1].AllowsClass(2));

        var q = copy.Quests.Single();
        Assert.Equal((quest.Title, quest.Level, quest.Details, quest.Objectives, quest.Progress, quest.Completion, quest.EnderName),
            (q.Title, q.Level, q.Details, q.Objectives, q.Progress, q.Completion, q.EnderName));
        Assert.Equal(quest.ObjectiveList.Select(o => (o.Type, o.Target, o.Count, o.Text)), q.ObjectiveList.Select(o => (o.Type, o.Target, o.Count, o.Text)));
        Assert.Equal(quest.EnderPlaces, q.EnderPlaces);
        Assert.Empty(q.ObjectiveList[0].Areas);
        Assert.Equal(quest.ObjectiveList[1].Areas, q.ObjectiveList[1].Areas);
        Assert.Equal(quest.RewardItems, q.RewardItems);
        Assert.Equal(quest.RewardChoices, q.RewardChoices);
        Assert.Equal((900, 150u), (q.RewardXp, q.RewardMoney));
    }

    [Fact]
    public void Oversized_lists_are_rejected()
    {
        var w = new PacketWriter();
        w.WriteUShort((ushort)Opcode.InventoryUpdate);
        w.WriteBool(false);
        w.WriteUShort(InventoryUpdate.MaxEntries + 1);
        var bytes = w.ToArray();
        Assert.Throws<PacketException>(() =>
        {
            var r = new PacketReader(bytes);
            Packet.ReadOpcode(ref r);
            new InventoryUpdate().Read(ref r);
        });
    }

    [Fact]
    public void Item_rules_and_money_formatting()
    {
        Assert.True(ItemRules.Fits(InventoryType.TwoHand, EquipSlot.MainHand));
        Assert.False(ItemRules.Fits(InventoryType.TwoHand, EquipSlot.OffHand));
        Assert.True(ItemRules.Fits(InventoryType.Finger, EquipSlot.Finger2));
        Assert.Equal(new[] { EquipSlot.Bag1, EquipSlot.Bag2, EquipSlot.Bag3, EquipSlot.Bag4 }, ItemRules.SlotsFor(InventoryType.Bag));
        Assert.Equal(new[] { EquipSlot.Chest }, ItemRules.SlotsFor(InventoryType.Chest));
        Assert.Empty(ItemRules.SlotsFor(InventoryType.None));
        Assert.Equal(ItemPos.FirstBag + 2, ItemPos.ContainerOf(EquipSlot.Bag3));
        Assert.Equal(EquipSlot.Bag3, ItemPos.BagSlotOf(ItemPos.FirstBag + 2));
        Assert.True(ItemPos.Equip(EquipSlot.Bag1).IsBagSlot);
        Assert.False(ItemPos.Equip(EquipSlot.OffHand).IsBagSlot);

        Assert.Equal("0c", Money.Format(0));
        Assert.Equal("45c", Money.Format(45));
        Assert.Equal("1s 5c", Money.Format(105));
        Assert.Equal("2g 30s", Money.Format(23000));
        Assert.Equal("12g 3s 4c", Money.Format(120304));
    }
}
