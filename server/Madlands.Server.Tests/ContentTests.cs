using Madlands.Content;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Terrain;

namespace Madlands.Server.Tests;

public class ContentLoaderTests
{
    /// <summary>A small but complete content folder; tests then break one thing at a time.</summary>
    private static DirectoryInfo WriteMinimalContent()
    {
        var root = Directory.CreateTempSubdirectory("madlands-content-");
        var map = Directory.CreateDirectory(Path.Combine(root.FullName, "maps", "0001"));
        File.WriteAllText(Path.Combine(map.FullName, "map.json"),
            """{ "id": 1, "name": "Good", "spawns": "spawns.json", "spawn": { "x": 5, "z": 5 } }""");
        HeightMap.CreateFlat(0, 0, 1, 11, 11, 3f).Save(Path.Combine(map.FullName, "height.bin"));
        File.WriteAllText(Path.Combine(map.FullName, "spawns.json"), """{ "spawns": [ { "creature": 1, "x": 2, "z": 2, "wanderRadius": 3 } ] }""");
        File.WriteAllText(Path.Combine(root.FullName, "races.json"), """{ "races": [ { "id": 1, "name": "Human", "startMap": 1, "faction": 1 } ] }""");
        File.WriteAllText(Path.Combine(root.FullName, "classes.json"),
            """{ "classes": [ { "id": 1, "name": "Warrior", "powerType": "rage", "baseStats": { "stamina": 20 }, "spells": [ { "spell": 1, "level": 1 } ] } ] }""");
        File.WriteAllText(Path.Combine(root.FullName, "factions.json"),
            """{ "factions": [ { "id": 1, "name": "Players", "hostile": [2] }, { "id": 2, "name": "Monsters" } ] }""");
        File.WriteAllText(Path.Combine(root.FullName, "spells.json"),
            """{ "spells": [ { "id": 1, "name": "Attack", "flags": ["autoAttack"], "gcdMs": 0 } ] }""");
        File.WriteAllText(Path.Combine(root.FullName, "creatures.json"),
            """{ "creatures": [ { "id": 1, "name": "Wolf", "displayId": 102, "faction": 2, "aggroRadius": 10, "loot": 1 } ] }""");
        File.WriteAllText(Path.Combine(root.FullName, "levels.json"), """{ "maxLevel": 3, "xpToNextLevel": [100, 200] }""");
        File.WriteAllText(Path.Combine(root.FullName, "items.json"),
            """{ "items": [ { "id": 1, "name": "Pelt", "class": "junk", "sellPrice": 1, "stackSize": 5 } ] }""");
        File.WriteAllText(Path.Combine(root.FullName, "loot.json"),
            """{ "lootTables": [ { "id": 1, "name": "Wolf", "money": { "min": 1, "max": 3 }, "items": [ { "item": 1, "chance": 0.5 } ] } ] }""");
        File.WriteAllText(Path.Combine(root.FullName, "quests.json"), """
            { "quests": [ { "id": 1, "title": "Hunt", "giver": 1, "ender": 1, "details": "d", "summary": "s", "completion": "c",
                            "objectives": [ { "type": "kill", "creature": 1, "count": 2 } ], "rewards": { "xp": 10, "items": [ { "item": 1 } ] } } ] }
            """);
        return root;
    }

    [Fact]
    public void Loads_complete_content()
    {
        var root = WriteMinimalContent();
        try
        {
            var db = ContentLoader.Load(root.FullName);
            Assert.Equal(new Vec3(5, 3, 5), db.Maps[1].SpawnPosition);
            Assert.Equal("Human", db.Races[1].Name);
            Assert.Equal(PowerType.Rage, db.Classes[1].PowerType);
            Assert.True(db.Spells[1].Has(SpellFlags.AutoAttack));
            Assert.Equal(1, db.Maps[1].Spawns.Single().Creature);
            Assert.Equal(Reaction.Hostile, db.FactionTable.GetReaction(1, 2));
            Assert.Equal(Reaction.Hostile, db.FactionTable.GetReaction(2, 1));
            Assert.Equal(200, db.Levels.XpToNext(2));
            Assert.Equal(0, db.Levels.XpToNext(3));
            Assert.Equal(0, db.Maps[1].Collision.TriangleCount);
            Assert.Equal("Pelt", db.Items[1].Name);
            Assert.Equal(1u, db.LootTables[1].Items.Single().Item);
            Assert.Equal(db.Quests[1], db.QuestsGivenBy(1).Single());
            Assert.Equal(db.Quests[1], db.QuestsEndedBy(1).Single());
            Assert.Equal(NpcFlags.QuestGiver, db.NpcFlagsOf(db.Creatures[1]));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Reports_all_errors_at_once()
    {
        var root = WriteMinimalContent();
        try
        {
            var bad = Directory.CreateDirectory(Path.Combine(root.FullName, "maps", "0002"));
            File.WriteAllText(Path.Combine(bad.FullName, "map.json"), """{ "id": 1, "name": "", "spawn": { "x": 0, "z": 0 } }""");
            File.WriteAllText(Path.Combine(root.FullName, "races.json"), """{ "races": [ { "id": 1, "name": "Human", "startMap": 9 }, { "id": 1, "name": "Dupe", "startMap": 1, "faction": 7, "displayId": 0 } ] }""");
            File.Delete(Path.Combine(root.FullName, "classes.json"));
            File.WriteAllText(Path.Combine(root.FullName, "spells.json"), """
                { "spells": [
                  { "id": 1, "name": "Attack", "target": "self", "flags": ["autoAttack"] },
                  { "id": 2, "name": "Broken", "effects": [ { "type": "applyAura" } ] },
                  { "id": 3, "name": "Costly", "cost": 10, "effects": [ { "type": "schoolDamage", "min": 5, "max": 1 } ] }
                ] }
                """);
            File.WriteAllText(Path.Combine(root.FullName, "creatures.json"),
                """{ "creatures": [ { "id": 1, "name": "Wolf", "displayId": 102, "faction": 99, "spells": [ { "spell": 404 } ] } ] }""");
            File.WriteAllText(Path.Combine(root.FullName, "maps", "0001", "spawns.json"), """{ "spawns": [ { "creature": 5, "x": 2, "z": 2 } ] }""");
            File.WriteAllText(Path.Combine(root.FullName, "levels.json"), """{ "maxLevel": 3, "xpToNextLevel": [100] }""");

            var ex = Assert.Throws<ContentException>(() => ContentLoader.Load(root.FullName));
            Assert.Contains(ex.Errors, e => e.Contains("already used"));
            Assert.Contains(ex.Errors, e => e.Contains("name is required"));
            Assert.Contains(ex.Errors, e => e.Contains("height map"));
            Assert.Contains(ex.Errors, e => e.Contains("map 9, which does not exist"));
            Assert.Contains(ex.Errors, e => e.Contains("id 1 is used twice"));
            Assert.Contains(ex.Errors, e => e.Contains("faction 7, which does not exist"));
            Assert.Contains(ex.Errors, e => e.Contains("needs a displayId"));
            Assert.Contains(ex.Errors, e => e.Contains("classes.json"));
            Assert.Contains(ex.Errors, e => e.Contains("autoAttack spell targets an enemy"));
            Assert.Contains(ex.Errors, e => e.Contains("applyAura needs an aura"));
            Assert.Contains(ex.Errors, e => e.Contains("a cost needs a powerType"));
            Assert.Contains(ex.Errors, e => e.Contains("0 <= min <= max"));
            Assert.Contains(ex.Errors, e => e.Contains("faction 99, which does not exist"));
            Assert.Contains(ex.Errors, e => e.Contains("spell 404, which does not exist"));
            Assert.Contains(ex.Errors, e => e.Contains("creature 5, which does not exist"));
            Assert.Contains(ex.Errors, e => e.Contains("xpToNextLevel needs 2 entries"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Reports_item_loot_and_quest_errors()
    {
        var root = WriteMinimalContent();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "items.json"), """
                { "items": [
                  { "id": 1, "name": "Pelt", "class": "junk", "stackSize": 5 },
                  { "id": 2, "name": "Sword", "class": "weapon", "slot": "mainHand", "stackSize": 3 },
                  { "id": 3, "name": "Shield", "class": "armor", "slot": "offHand", "weapon": { "minDamage": 1, "maxDamage": 2 } },
                  { "id": 4, "name": "Sack", "class": "container", "slot": "bag", "bagSlots": 99 },
                  { "id": 5, "name": "Relic", "class": "quest", "sellPrice": 10 },
                  { "id": 6, "name": "Potion", "class": "consumable", "useSpell": 77 }
                ] }
                """);
            File.WriteAllText(Path.Combine(root.FullName, "loot.json"), """
                { "lootTables": [ { "id": 1, "name": "Wolf", "money": { "min": 5, "max": 1 },
                                    "items": [ { "item": 9, "chance": 0.5 }, { "item": 1, "chance": 2, "max": 9 } ],
                                    "groups": [ { "chance": 0.1, "items": [5] } ] } ] }
                """);
            File.WriteAllText(Path.Combine(root.FullName, "creatures.json"),
                """{ "creatures": [ { "id": 1, "name": "Wolf", "displayId": 102, "faction": 2, "loot": 4, "vendor": [1, 404], "equipment": { "mainHand": 3 } } ] }""");
            File.WriteAllText(Path.Combine(root.FullName, "quests.json"), """
                { "quests": [
                  { "id": 1, "title": "Loop A", "giver": 1, "ender": 1, "requires": [2], "details": "d", "summary": "s", "completion": "c",
                    "objectives": [ { "type": "collect", "item": 2, "count": 1 }, { "type": "explore", "map": 1, "x": 500, "z": 0, "radius": 5 } ] },
                  { "id": 2, "title": "Loop B", "giver": 8, "ender": 1, "requires": [1], "level": 2, "minLevel": 3, "details": "d", "summary": "s",
                    "objectives": [ { "type": "talk", "creature": 1, "count": 2 } ], "rewards": { "items": [ { "item": 1, "count": 50 } ] } }
                ] }
                """);

            var ex = Assert.Throws<ContentException>(() => ContentLoader.Load(root.FullName));
            Assert.Contains(ex.Errors, e => e.Contains("equippable items do not stack"));
            Assert.Contains(ex.Errors, e => e.Contains("only mainHand and twoHand items have a weapon section"));
            Assert.Contains(ex.Errors, e => e.Contains("bags need bagSlots"));
            Assert.Contains(ex.Errors, e => e.Contains("quest items cannot be sold"));
            Assert.Contains(ex.Errors, e => e.Contains("uses spell 77, which does not exist"));
            Assert.Contains(ex.Errors, e => e.Contains("money needs 0 <= min <= max"));
            Assert.Contains(ex.Errors, e => e.Contains("drops item 9, which does not exist"));
            Assert.Contains(ex.Errors, e => e.Contains("chance must be in (0, 1]"));
            Assert.Contains(ex.Errors, e => e.Contains("quest item 5 cannot be in a group"));
            Assert.Contains(ex.Errors, e => e.Contains("loot table 4, which does not exist"));
            Assert.Contains(ex.Errors, e => e.Contains("sells item 1, which has no buyPrice"));
            Assert.Contains(ex.Errors, e => e.Contains("sells item 404, which does not exist"));
            Assert.Contains(ex.Errors, e => e.Contains("holds item 3 in the wrong hand"));
            Assert.Contains(ex.Errors, e => e.Contains("item 2 drops nowhere"));
            Assert.Contains(ex.Errors, e => e.Contains("outside map 1"));
            Assert.Contains(ex.Errors, e => e.Contains("explore objectives need a text"));
            Assert.Contains(ex.Errors, e => e.Contains("giver 8 does not exist"));
            Assert.Contains(ex.Errors, e => e.Contains("needs 1 <= minLevel <= level"));
            Assert.Contains(ex.Errors, e => e.Contains("talk objectives have count 1"));
            Assert.Contains(ex.Errors, e => e.Contains("count must be in 1..5"));
            Assert.Contains(ex.Errors, e => e.Contains("requires itself through a chain"));
            Assert.Contains(ex.Errors, e => e.Contains("needs details, summary and completion"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Repository_content_is_valid()
    {
        var root = ContentPaths.FindContentRoot(AppContext.BaseDirectory);
        Assert.NotNull(root);
        var db = ContentLoader.Load(root!);
        Assert.True(db.Maps.ContainsKey(1));
        Assert.NotEmpty(db.Races);
        // Every race has its own character model.
        Assert.Equal(db.Races.Count, db.Races.Values.Select(r => r.DisplayId).Distinct().Count());
        Assert.Equal(3, db.Classes.Count);
        // Every class can fight from level 1: it knows the auto-attack and at least one ability.
        foreach (var cls in db.Classes.Values)
        {
            var level1 = cls.Spells.Where(s => s.Level == 1).Select(s => db.Spells[s.Spell]).ToList();
            Assert.Contains(level1, s => s.Has(SpellFlags.AutoAttack));
            Assert.Contains(level1, s => !s.Has(SpellFlags.AutoAttack));
            // ...and starts with a weapon in hand.
            Assert.Contains(cls.StartingItems, i => db.Items[i.Item].Slot is InventoryType.MainHand or InventoryType.TwoHand);
        }
        // Every quest can be found: its giver stands somewhere in the world.
        var spawned = db.Maps.Values.SelectMany(m => m.Spawns).Select(s => s.Creature).ToHashSet();
        foreach (var quest in db.Quests.Values)
        {
            Assert.Contains(quest.Giver, spawned);
            Assert.Contains(quest.Ender, spawned);
        }
    }
}

public class FactionTableTests
{
    [Fact]
    public void Reactions_follow_the_lists_in_both_directions()
    {
        var table = new FactionTable(new[]
        {
            new FactionInfo { Id = 1, Name = "Players", Hostile = new ushort[] { 2 } },
            new FactionInfo { Id = 2, Name = "Monsters" },
            new FactionInfo { Id = 3, Name = "Wildlife" },
            new FactionInfo { Id = 4, Name = "Guards", Friendly = new ushort[] { 1 } },
        });
        Assert.Equal(Reaction.Friendly, table.GetReaction(1, 1));
        Assert.Equal(Reaction.Hostile, table.GetReaction(1, 2));
        Assert.Equal(Reaction.Hostile, table.GetReaction(2, 1));
        Assert.Equal(Reaction.Neutral, table.GetReaction(1, 3));
        Assert.Equal(Reaction.Friendly, table.GetReaction(1, 4));
        Assert.Equal(Reaction.Friendly, table.GetReaction(4, 1));
        Assert.Equal(Reaction.Neutral, table.GetReaction(2, 99));
    }
}
