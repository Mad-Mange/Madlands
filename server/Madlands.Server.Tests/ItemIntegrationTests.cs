using Madlands.ClientCore;
using Madlands.Content;
using Madlands.Server.Persistence;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;

namespace Madlands.Server.Tests;

/// <summary>
/// Items, loot, vendors and quests end to end: a real realm with the repository's content on a flat
/// map, the Mirewatch Post NPCs placed next to the spawn (0, 0), and real clients over UDP.
/// </summary>
public class ItemIntegrationTests
{
    private const int MireFrog = 1, TrainingDummy = 5, CaptainEdda = 10, OldTamsin = 11, QuartermasterHolt = 12;
    private const uint WornShortsword = 1001, RecruitsTunic = 1101, FrogskinBoots = 1202, SlimyFrogSkin = 2001, MireFrogLeg = 3001;
    private const uint Hearthbread = 4001, MinorHealingDraught = 4003, SmallLinenPouch = 5001;
    private const uint FrogQuest = 1, FrogLegQuest = 2, TalkQuest = 5, ScoutQuest = 6;
    private const byte Warrior = 1;

    private sealed class Session : IDisposable
    {
        public Session(TestRealm realm, GameClient client)
        {
            Realm = realm;
            Client = client;
            client.ChatReceived += c => Chat.Add(c.Text);
            client.ActionFailed += (e, _) => Errors.Add(e);
            client.SpellFailed += f => CastErrors.Add(f.Result);
            client.ItemReceived += i => Received.Add((i.ItemId, i.Count, i.Source));
            client.QuestProgressed += (q, o, n) => Progress.Add((q, o, n));
            client.QuestCompleted += q => Completed.Add(q.QuestId);
            client.GossipOpened += g => Gossips++;
            client.VendorOpened += v => Vendors++;
        }

        public TestRealm Realm { get; }
        public GameClient Client { get; }
        public List<string> Chat { get; } = new();
        public List<ActionError> Errors { get; } = new();
        public List<CastResult> CastErrors { get; } = new();
        public List<(uint Item, int Count, ReceiveSource Source)> Received { get; } = new();
        public List<(uint Quest, int Objective, int Count)> Progress { get; } = new();
        public List<uint> Completed { get; } = new();
        public int Gossips;
        public int Vendors;

        public void Pump(Func<bool> until, int timeoutMs = 8000) => Realm.Pump(new[] { Client }, until, timeoutMs);

        public RemoteEntity Find(string name)
        {
            RemoteEntity? found = null;
            Pump(() => (found = Client.World.Entities.FirstOrDefault(e => e.Name == name && e.Unit is { Values.IsDead: false })) is not null);
            return found!;
        }

        public void Command(string text)
        {
            int before = Chat.Count;
            Client.SendChat(text);
            Pump(() => Chat.Count > before);
        }

        /// <summary>Spawns one creature 6 m in front of the player and kills it (the player gets the tap).</summary>
        public RemoteEntity SpawnAndKill(int creatureId, string name)
        {
            var before = Client.World.Entities.Where(e => e.Name == name).Select(e => e.Guid).ToHashSet();
            Command($".spawn {creatureId}");
            RemoteEntity? spawned = null;
            Pump(() => (spawned = Client.World.Entities.FirstOrDefault(e => e.Name == name && !before.Contains(e.Guid))) is not null);
            Client.SetTarget(spawned!.Guid);
            Command(".damage 9999");
            Pump(() => spawned.Unit!.Values.IsDead);
            return spawned;
        }

        /// <summary>Waits for the next error of a request.</summary>
        public void ExpectError(ActionError error, Action request)
        {
            int before = Errors.Count;
            request();
            Pump(() => Errors.Count > before);
            Assert.Equal(error, Errors[^1]);
        }

        public int Count(uint itemId) => Client.Inventory.Count(itemId);

        public ItemPos Find(uint itemId) => Client.Inventory.All.First(e => e.Value.ItemId == itemId).Key;

        public void Dispose() => Client.Dispose();
    }

    private static SpawnDefinition At(int creature, float x, float z, float yaw = 180) => new() { Creature = creature, X = x, Z = z, YawDegrees = yaw };

    /// <summary>The post's NPCs within talking range of the spawn.</summary>
    private static readonly SpawnDefinition[] Post =
    {
        At(CaptainEdda, 0, 4), At(OldTamsin, 3, 2), At(QuartermasterHolt, -3, 2),
    };

    private static async Task<(TestRealm Realm, Session Session)> StartAsync(IGameDatabase? database = null, params SpawnDefinition[] extra)
    {
        var realm = await TestRealm.StartAsync(database, TestRealm.CombatContent(Post.Concat(extra).ToArray()), gmAll: true);
        var (account, character) = await realm.NewPlayerAsync("hero", "Hero", Warrior);
        var session = new Session(realm, realm.Enter(account, character));
        session.Pump(() => session.Client.State == ClientState.InWorld && session.Client.Inventory.All.Any());
        return (realm, session);
    }

    [Fact]
    public async Task New_characters_start_equipped_and_gear_changes_stats_and_looks()
    {
        var (realm, s) = await StartAsync();
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;

        Assert.Equal(WornShortsword, c.Inventory.Equipped(EquipSlot.MainHand).ItemId);
        Assert.Equal(RecruitsTunic, c.Inventory.Equipped(EquipSlot.Chest).ItemId);
        Assert.Equal(4, s.Count(Hearthbread));
        Assert.Equal(c.Data.GetItem(WornShortsword).DisplayId, c.Self.Values.MainHandDisplay);
        s.Pump(() => c.Stats.Stats[(int)StatType.Armor] > 0);
        int armor = c.Stats.Stats[(int)StatType.Armor];
        int maxDamage = c.Stats.MeleeMaxDamage;

        // Right-click the tunic: off it goes, into the backpack, and the armor with it.
        c.AutoEquip(ItemPos.Equip(EquipSlot.Chest));
        s.Pump(() => c.Inventory.Equipped(EquipSlot.Chest).IsEmpty && c.Stats.Stats[(int)StatType.Armor] == armor - 10);
        Assert.Equal(1, s.Count(RecruitsTunic));

        // Drag the sword into the bags: fists again, and nothing in hand for others to see.
        c.MoveItem(ItemPos.Equip(EquipSlot.MainHand), new ItemPos(ItemPos.Backpack, 10));
        s.Pump(() => c.Self.Values.MainHandDisplay == 0 && c.Stats.MeleeMaxDamage < maxDamage);
        c.AutoEquip(new ItemPos(ItemPos.Backpack, 10));
        s.Pump(() => c.Inventory.Equipped(EquipSlot.MainHand).ItemId == WornShortsword && c.Stats.MeleeMaxDamage == maxDamage);

        // A bag adds room.
        s.Command(".additem " + SmallLinenPouch);
        c.AutoEquip(s.Find(SmallLinenPouch));
        s.Pump(() => c.Inventory.ContainerSize(ItemPos.FirstBag) == 6);
        s.ExpectError(ActionError.CantEquipThere, () => c.AutoEquip(s.Find(Hearthbread)));
    }

    [Fact]
    public async Task Killing_a_creature_lets_its_tapper_loot_money_and_items()
    {
        var (realm, s) = await StartAsync();
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;

        var frog = s.SpawnAndKill(MireFrog, "Mire Frog");
        s.Pump(() => c.IsLootable(frog.Guid));
        c.OpenLoot(frog.Guid);
        s.Pump(() => c.Loot.Corpse == frog.Guid);
        Assert.InRange(c.Loot.Money, 1, 4);
        Assert.DoesNotContain(c.Loot.Items, i => i.ItemId == MireFrogLeg); // only for players on the quest
        long money = c.Loot.Money;
        int items = c.Loot.Items.Count;

        c.TakeLoot(LootTake.AllSlots);
        s.Pump(() => !c.Loot.IsOpen && !c.IsLootable(frog.Guid));
        Assert.Equal(money, c.Inventory.Money);
        Assert.Equal(items, s.Received.Count(r => r.Source == ReceiveSource.Loot));

        // Nothing left on the corpse, and the living have no loot.
        s.ExpectError(ActionError.LootNotYours, () => c.OpenLoot(frog.Guid));
        s.ExpectError(ActionError.CantInteract, () => c.OpenLoot(s.Find("Quartermaster Holt").Guid));
    }

    [Fact]
    public async Task Quest_items_drop_only_while_a_quest_needs_them()
    {
        var (realm, s) = await StartAsync();
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;
        var tamsin = s.Find("Old Tamsin");

        c.Interact(tamsin.Guid);
        s.Pump(() => s.Gossips == 1);
        Assert.Contains(c.Gossip.Quests, q => q.QuestId == FrogLegQuest && q.Status == QuestGiverStatus.Available);
        c.AcceptQuest(tamsin.Guid, FrogLegQuest);
        s.Pump(() => c.Quests.Find(FrogLegQuest) is not null);

        for (int kill = 0; kill < 40 && s.Count(MireFrogLeg) < 5; kill++)
        {
            var frog = s.SpawnAndKill(MireFrog, "Mire Frog");
            s.Pump(() => c.IsLootable(frog.Guid));
            c.OpenLoot(frog.Guid);
            s.Pump(() => c.Loot.Corpse == frog.Guid);
            c.TakeLoot(LootTake.AllSlots);
            s.Pump(() => !c.Loot.IsOpen);
        }
        Assert.Equal(5, s.Count(MireFrogLeg));
        s.Pump(() => c.Quests.Find(FrogLegQuest)!.IsComplete);
        Assert.Contains(s.Progress, p => p.Quest == FrogLegQuest && p.Count == 5);
        s.Pump(() => c.GetQuestGiverStatus(tamsin.Guid) == QuestGiverStatus.Reward);

        // Enough legs: no more drop.
        var last = s.SpawnAndKill(MireFrog, "Mire Frog");
        var corpse = (Creature)realm.Game.World.Instances.Single().Find(last.Guid)!;
        Assert.DoesNotContain(corpse.Loot?.Items ?? new(), i => i.Item.Id == MireFrogLeg);

        c.TurnInQuest(tamsin.Guid, FrogLegQuest);
        s.Pump(() => s.Completed.Contains(FrogLegQuest) && s.Count(Hearthbread) == 4 + 5);
        Assert.Equal(0, s.Count(MireFrogLeg)); // handed in
        Assert.True(c.Quests.IsCompleted(FrogLegQuest));
    }

    [Fact]
    public async Task Kill_quest_with_a_reward_choice()
    {
        var (realm, s) = await StartAsync();
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;
        var tamsin = s.Find("Old Tamsin");
        s.Pump(() => c.GetQuestGiverStatus(tamsin.Guid) == QuestGiverStatus.Available);

        c.AcceptQuest(tamsin.Guid, FrogQuest);
        s.Pump(() => c.Quests.Find(FrogQuest) is { State: QuestLogState.Active });
        s.ExpectError(ActionError.QuestNotAvailable, () => c.AcceptQuest(tamsin.Guid, FrogQuest));
        s.ExpectError(ActionError.QuestNotComplete, () => c.TurnInQuest(tamsin.Guid, FrogQuest, 0));

        for (int i = 0; i < 6; i++) s.SpawnAndKill(MireFrog, "Mire Frog");
        s.Pump(() => c.Quests.Find(FrogQuest)!.IsComplete);
        Assert.Equal(Enumerable.Range(1, 6), s.Progress.Where(p => p.Quest == FrogQuest).Select(p => p.Count));
        s.Pump(() => c.GetQuestGiverStatus(tamsin.Guid) == QuestGiverStatus.Reward);

        s.ExpectError(ActionError.ChooseReward, () => c.TurnInQuest(tamsin.Guid, FrogQuest));
        int xpBefore = c.Stats.Xp;
        c.TurnInQuest(tamsin.Guid, FrogQuest, 1);
        s.Pump(() => s.Completed.Contains(FrogQuest) && s.Count(FrogskinBoots) == 1 && c.Inventory.Money == 25);
        Assert.Null(c.Quests.Find(FrogQuest));
        s.Pump(() => c.Stats.Xp > xpBefore || c.Self.Values.Level > 1);

        c.Interact(tamsin.Guid);
        s.Pump(() => s.Gossips == 1);
        Assert.DoesNotContain(c.Gossip.Quests, q => q.QuestId == FrogQuest);
    }

    [Fact]
    public async Task Vendors_buy_and_sell()
    {
        var (realm, s) = await StartAsync();
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;
        var holt = s.Find("Quartermaster Holt");

        c.Interact(holt.Guid);
        s.Pump(() => s.Vendors == 1); // nothing to say: straight to his wares
        Assert.Equal(holt.Guid, c.Vendor.Vendor);
        Assert.Contains(Hearthbread, c.Vendor.ItemIds);
        Assert.Equal(0, s.Gossips);

        s.ExpectError(ActionError.NotEnoughMoney, () => c.BuyItem(holt.Guid, Hearthbread, 5));
        s.Command(".money 1000");
        c.BuyItem(holt.Guid, Hearthbread, 5);
        s.Pump(() => s.Count(Hearthbread) == 9 && c.Inventory.Money == 1000 - 5 * 25);
        s.ExpectError(ActionError.ItemNotFound, () => c.BuyItem(holt.Guid, WornShortsword)); // he does not sell it

        s.Command($".additem {SlimyFrogSkin} 3");
        c.SellItem(holt.Guid, s.Find(SlimyFrogSkin));
        s.Pump(() => s.Count(SlimyFrogSkin) == 0 && c.Inventory.Money == 875 + 3 * 4);
        s.Command($".additem {MireFrogLeg} 1");
        s.ExpectError(ActionError.CantSellItem, () => c.SellItem(holt.Guid, s.Find(MireFrogLeg)));
        s.ExpectError(ActionError.ItemNotFound, () => c.SellItem(holt.Guid, ItemPos.Equip(EquipSlot.MainHand)));

        s.Command(".tele 40 40");
        s.ExpectError(ActionError.TooFarAway, () => c.BuyItem(holt.Guid, Hearthbread));
    }

    [Fact]
    public async Task Talk_and_explore_objectives()
    {
        var (realm, s) = await StartAsync();
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;
        var edda = s.Find("Captain Edda Brand");
        var holt = s.Find("Quartermaster Holt");

        c.Interact(edda.Guid);
        s.Pump(() => s.Gossips == 1);
        Assert.Contains("Mirewatch Post", c.Gossip.Text);
        Assert.DoesNotContain(c.Gossip.Quests, q => q.QuestId == ScoutQuest); // needs "Wolves at Dusk" first
        c.AcceptQuest(edda.Guid, TalkQuest);
        s.Pump(() => c.Quests.Find(TalkQuest) is not null);
        c.Interact(holt.Guid);
        s.Pump(() => c.Quests.Find(TalkQuest)!.IsComplete);
        c.TurnInQuest(edda.Guid, TalkQuest);
        s.Pump(() => s.Count(MinorHealingDraught) == 2);

        // Explore: walk (teleport) into the bandit camp.
        s.Command(".quest add " + ScoutQuest);
        s.Pump(() => c.Quests.Find(ScoutQuest) is not null);
        s.Command(".tele 95 75");
        s.Pump(() => c.Quests.Find(ScoutQuest)!.IsComplete);
        s.Command(".tele 0 1");
        c.TurnInQuest(edda.Guid, ScoutQuest);
        s.Pump(() => s.Completed.Contains(ScoutQuest));

        // Abandoning gives the quest back to its giver.
        s.Command(".level 3");
        c.Interact(edda.Guid);
        s.Pump(() => s.Gossips == 2);
        uint any = c.Gossip.Quests.First(q => q.Status == QuestGiverStatus.Available).QuestId;
        c.AcceptQuest(edda.Guid, any);
        s.Pump(() => c.Quests.Find(any) is not null);
        c.AbandonQuest(any);
        s.Pump(() => c.Quests.Find(any) is null && !c.Quests.IsCompleted(any));
    }

    [Fact]
    public async Task Eating_takes_one_bread_and_is_not_allowed_in_combat()
    {
        var (realm, s) = await StartAsync(extra: At(TrainingDummy, 0, 7));
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;

        c.UseItem(s.Find(Hearthbread), c.LocalGuid);
        s.Pump(() => s.Count(Hearthbread) == 3 && c.Self.HasAura(500));

        var dummy = s.Find("Training Dummy");
        c.SetTarget(dummy.Guid);
        s.Command(".damage 1");
        s.Pump(() => c.Self.Values.Has(UnitFlags.InCombat) && !c.Self.HasAura(500)); // fighting ends the meal
        s.Realm.PumpFor(new[] { c }, 1100); // the meal's global cooldown
        c.UseItem(s.Find(Hearthbread), c.LocalGuid);
        s.Pump(() => s.CastErrors.Contains(CastResult.InCombat));
        Assert.Equal(3, s.Count(Hearthbread));

        s.Command($".additem {SlimyFrogSkin}");
        s.ExpectError(ActionError.NotUsable, () => c.UseItem(s.Find(SlimyFrogSkin), c.LocalGuid));
        s.ExpectError(ActionError.ItemNotFound, () => c.UseItem(ItemPos.Equip(EquipSlot.MainHand), c.LocalGuid));
    }

    [Fact]
    public async Task Items_money_and_quests_survive_relogging()
    {
        var database = new InMemoryGameDatabase();
        var realm = await TestRealm.StartAsync(database, TestRealm.CombatContent(Post), gmAll: true);
        await using var _ = realm;
        var (account, character) = await realm.NewPlayerAsync("keeper", "Keeper", Warrior);
        using (var s = new Session(realm, realm.Enter(account, character)))
        {
            s.Pump(() => s.Client.State == ClientState.InWorld && s.Client.Inventory.All.Any());
            var tamsin = s.Find("Old Tamsin");
            s.Client.AcceptQuest(tamsin.Guid, FrogQuest);
            s.Pump(() => s.Client.Quests.Find(FrogQuest) is not null);
            s.SpawnAndKill(MireFrog, "Mire Frog");
            s.SpawnAndKill(MireFrog, "Mire Frog");
            s.Pump(() => s.Client.Quests.Find(FrogQuest)!.Progress[0] == 2);
            s.Command(".money 777");
            s.Command(".additem " + SmallLinenPouch);
            s.Client.AutoEquip(s.Find(SmallLinenPouch));
            s.Pump(() => s.Client.Inventory.ContainerSize(ItemPos.FirstBag) == 6);
            s.Client.MoveItem(s.Find(Hearthbread), new ItemPos(ItemPos.FirstBag, 3));
            s.Pump(() => s.Client.Inventory.Get(new ItemPos(ItemPos.FirstBag, 3)).ItemId == Hearthbread);
            s.Client.Disconnect();
            realm.Pump(Array.Empty<GameClient>(), () => realm.Game.World.PlayerCount == 0);
        }
        await WorldIntegrationTests.WaitUntilAsync(() => realm.Host.Saver.SavedCount >= 1);

        using var again = new Session(realm, realm.Enter(account, character));
        var c = again.Client;
        again.Pump(() => c.State == ClientState.InWorld && c.Inventory.All.Any() && c.Quests.Active.Count == 1);
        Assert.Equal(777, c.Inventory.Money);
        Assert.Equal(2, c.Quests.Find(FrogQuest)!.Progress[0]);
        Assert.Equal(6, c.Inventory.ContainerSize(ItemPos.FirstBag));
        Assert.Equal(new ClientItem { ItemId = Hearthbread, Count = 4 }, c.Inventory.Get(new ItemPos(ItemPos.FirstBag, 3)));
        Assert.Equal(4, again.Count(Hearthbread)); // the starting kit came only once
        Assert.Equal(WornShortsword, c.Inventory.Equipped(EquipSlot.MainHand).ItemId);
    }
}
