using System.Text.Json;
using System.Text.Json.Serialization;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Terrain;

namespace Madlands.Content;

public static class ContentLoader
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Loads and validates everything under <paramref name="contentRoot"/>. Collects ALL errors before failing.</summary>
    public static ContentDatabase Load(string contentRoot)
    {
        var errors = new List<string>();
        var maps = LoadMaps(Path.Combine(contentRoot, "maps"), errors);
        var races = LoadList<RaceFile, RaceDefinition>(Path.Combine(contentRoot, "races.json"), f => f.Races, errors);
        var classes = LoadList<ClassFile, ClassDefinition>(Path.Combine(contentRoot, "classes.json"), f => f.Classes, errors);
        var factions = LoadList<FactionFile, FactionDefinition>(Path.Combine(contentRoot, "factions.json"), f => f.Factions, errors);
        var spells = LoadList<SpellFile, SpellDefinition>(Path.Combine(contentRoot, "spells.json"), f => f.Spells, errors);
        var creatures = LoadList<CreatureFile, CreatureDefinition>(Path.Combine(contentRoot, "creatures.json"), f => f.Creatures, errors);
        var levels = LoadFile<LevelTable>(Path.Combine(contentRoot, "levels.json"), errors) ?? new LevelTable();
        var items = LoadList<ItemFile, ItemDefinition>(Path.Combine(contentRoot, "items.json"), f => f.Items, errors);
        var loot = LoadList<LootFile, LootTableDefinition>(Path.Combine(contentRoot, "loot.json"), f => f.LootTables, errors);
        var quests = LoadList<QuestFile, QuestDefinition>(Path.Combine(contentRoot, "quests.json"), f => f.Quests, errors);

        var content = new Validator(errors);
        content.Validate(maps, races, classes, factions, spells, creatures, levels);
        content.ValidateItems(items, spells, classes, levels);
        content.ValidateLoot(loot, items, creatures);
        content.ValidateQuests(quests, creatures, items, classes, maps, levels, loot);
        if (errors.Count > 0) throw new ContentException(errors);
        return new ContentDatabase(maps, races, classes, factions, spells, creatures, levels, items, loot, quests);
    }

    private sealed class RaceFile { public List<RaceDefinition> Races { get; set; } = new(); }
    private sealed class ClassFile { public List<ClassDefinition> Classes { get; set; } = new(); }
    private sealed class FactionFile { public List<FactionDefinition> Factions { get; set; } = new(); }
    private sealed class SpellFile { public List<SpellDefinition> Spells { get; set; } = new(); }
    private sealed class CreatureFile { public List<CreatureDefinition> Creatures { get; set; } = new(); }
    private sealed class SpawnFile { public List<SpawnDefinition> Spawns { get; set; } = new(); }
    private sealed class ItemFile { public List<ItemDefinition> Items { get; set; } = new(); }
    private sealed class LootFile { public List<LootTableDefinition> LootTables { get; set; } = new(); }
    private sealed class QuestFile { public List<QuestDefinition> Quests { get; set; } = new(); }

    private static T? LoadFile<T>(string path, List<string> errors) where T : class
    {
        if (!File.Exists(path))
        {
            errors.Add($"Missing file {path}");
            return null;
        }
        try
        {
            var file = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
            if (file is null) errors.Add($"{path}: empty file");
            return file;
        }
        catch (JsonException ex)
        {
            errors.Add($"{path}: invalid JSON ({ex.Message})");
            return null;
        }
    }

    private static List<TItem> LoadList<TFile, TItem>(string path, Func<TFile, List<TItem>> select, List<string> errors) where TFile : class
    {
        var file = LoadFile<TFile>(path, errors);
        if (file is null) return new List<TItem>();
        var items = select(file);
        if (items.Count == 0) errors.Add($"{path}: no entries");
        return items;
    }

    private static List<LoadedMap> LoadMaps(string mapsDir, List<string> errors)
    {
        var result = new List<LoadedMap>();
        if (!Directory.Exists(mapsDir))
        {
            errors.Add($"Missing folder {mapsDir}");
            return result;
        }

        var seenIds = new Dictionary<int, string>();
        foreach (var file in Directory.EnumerateFiles(mapsDir, "map.json", SearchOption.AllDirectories).Order())
        {
            var def = LoadFile<MapDefinition>(file, errors);
            if (def is null) continue;
            if (def.Id <= 0 || def.Id > ushort.MaxValue) errors.Add($"{file}: id {def.Id} must be in 1..{ushort.MaxValue}");
            if (seenIds.TryGetValue(def.Id, out var other)) errors.Add($"{file}: map id {def.Id} already used by {other}");
            seenIds[def.Id] = file;
            if (string.IsNullOrWhiteSpace(def.Name)) errors.Add($"{file}: name is required");

            string dir = Path.GetDirectoryName(file)!;
            var heightPath = Path.Combine(dir, def.Heightmap);
            if (!File.Exists(heightPath))
            {
                errors.Add($"{file}: height map '{def.Heightmap}' not found (export it from Unity: Madlands > Export Map)");
                continue;
            }

            HeightMap heightMap;
            try
            {
                heightMap = HeightMap.Load(heightPath);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                errors.Add($"{heightPath}: {ex.Message}");
                continue;
            }

            const float max = GameConstants.MaxMapExtent;
            if (heightMap.OriginX < -max || heightMap.OriginZ < -max || heightMap.MaxX > max || heightMap.MaxZ > max)
                errors.Add($"{heightPath}: map extends beyond +-{max} m from the origin (float precision limit)");
            if (!heightMap.Contains(def.Spawn.X, def.Spawn.Z))
                errors.Add($"{file}: spawn ({def.Spawn.X}, {def.Spawn.Z}) is outside the height map");
            foreach (var area in def.RestAreas)
                if (area.Radius <= 0f || !heightMap.Contains(area.X, area.Z))
                    errors.Add($"{file}: rest area '{area.Name}' needs a positive radius and a center on the height map");

            CollisionMesh? collision = null;
            if (def.Collision is not null)
            {
                var collisionPath = Path.Combine(dir, def.Collision);
                try
                {
                    collision = CollisionMesh.Load(collisionPath);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
                {
                    errors.Add($"{collisionPath}: {ex.Message}");
                }
            }

            List<SpawnDefinition>? spawns = null;
            if (def.Spawns is not null)
            {
                var spawnPath = Path.Combine(dir, def.Spawns);
                spawns = LoadFile<SpawnFile>(spawnPath, errors)?.Spawns;
                foreach (var s in spawns ?? new List<SpawnDefinition>())
                {
                    if (!heightMap.Contains(s.X, s.Z)) errors.Add($"{spawnPath}: spawn of creature {s.Creature} at ({s.X}, {s.Z}) is outside the map");
                    if (s.WanderRadius < 0 || s.RespawnSeconds < 0) errors.Add($"{spawnPath}: spawn of creature {s.Creature} has a negative radius or respawn time");
                }
            }

            result.Add(new LoadedMap(def, heightMap, collision, spawns));
        }
        return result;
    }

    /// <summary>Cross-checks between files: everything referenced must exist and numbers must make sense.</summary>
    private sealed class Validator
    {
        private readonly List<string> _errors;

        public Validator(List<string> errors)
        {
            _errors = errors;
        }

        private void Error(string message) => _errors.Add(message);

        public void Validate(List<LoadedMap> maps, List<RaceDefinition> races, List<ClassDefinition> classes,
            List<FactionDefinition> factions, List<SpellDefinition> spells, List<CreatureDefinition> creatures, LevelTable levels)
        {
            ValidateIds("races.json", races.Select(r => ((long)r.Id, r.Name)), 255);
            ValidateIds("classes.json", classes.Select(c => ((long)c.Id, c.Name)), 255);
            ValidateIds("factions.json", factions.Select(f => ((long)f.Id, f.Name)), ushort.MaxValue);
            ValidateIds("spells.json", spells.Select(s => ((long)s.Id, s.Name)), uint.MaxValue);
            ValidateIds("creatures.json", creatures.Select(c => ((long)c.Id, c.Name)), int.MaxValue);

            var mapIds = maps.Select(m => (int)m.Id).ToHashSet();
            var factionIds = factions.Select(f => f.Id).ToHashSet();
            var spellsById = spells.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            var creatureIds = creatures.Select(c => c.Id).ToHashSet();

            if (levels.MaxLevel < 1 || levels.MaxLevel > 255) Error($"levels.json: maxLevel {levels.MaxLevel} must be in 1..255");
            if (levels.XpToNextLevel.Count != levels.MaxLevel - 1)
                Error($"levels.json: xpToNextLevel needs {levels.MaxLevel - 1} entries (one per level below the cap), has {levels.XpToNextLevel.Count}");
            if (levels.XpToNextLevel.Any(xp => xp <= 0)) Error("levels.json: every xpToNextLevel entry must be positive");
            if (levels.Rest.PercentPerHour < 0f || levels.Rest.MaxPercent < 0f || levels.Rest.ElsewhereFactor is < 0f or > 1f)
                Error("levels.json: rest needs percentPerHour and maxPercent of at least 0 and an elsewhereFactor in 0..1");

            foreach (var f in factions)
                foreach (var id in f.Friendly.Concat(f.Hostile).Where(id => !factionIds.Contains(id)))
                    Error($"factions.json: faction {f.Id} '{f.Name}' refers to faction {id}, which does not exist");

            foreach (var race in races)
            {
                if (!mapIds.Contains(race.StartMap)) Error($"races.json: race {race.Id} '{race.Name}' starts on map {race.StartMap}, which does not exist");
                if (!factionIds.Contains(race.Faction)) Error($"races.json: race {race.Id} '{race.Name}' has faction {race.Faction}, which does not exist");
                if (race.DisplayId == 0) Error($"races.json: race {race.Id} '{race.Name}' needs a displayId");
            }

            foreach (var c in classes)
            {
                string where = $"classes.json: class {c.Id} '{c.Name}'";
                if (c.BaseHealth + c.BaseStats.Stamina <= 0) Error($"{where} would have no health");
                if (c.PowerType == PowerType.Mana && c.BaseMana + c.BaseStats.Intellect <= 0) Error($"{where} uses mana but has none");
                if (c.Unarmed.MinDamage < 0 || c.Unarmed.MaxDamage < c.Unarmed.MinDamage || c.Unarmed.SpeedMs < 500)
                    Error($"{where}: unarmed needs 0 <= minDamage <= maxDamage and speedMs >= 500");
                foreach (var cs in c.Spells)
                {
                    if (!spellsById.ContainsKey(cs.Spell)) Error($"{where} learns spell {cs.Spell}, which does not exist");
                    if (cs.Level < 1 || cs.Level > levels.MaxLevel) Error($"{where} learns spell {cs.Spell} at level {cs.Level}, outside 1..{levels.MaxLevel}");
                }
            }

            foreach (var s in spells) ValidateSpell(s);

            foreach (var c in creatures)
            {
                string where = $"creatures.json: creature {c.Id} '{c.Name}'";
                if (c.MinLevel < 1 || c.MaxLevel < c.MinLevel || c.MaxLevel > 255) Error($"{where}: needs 1 <= minLevel <= maxLevel <= 255");
                if (!factionIds.Contains(c.Faction)) Error($"{where} has faction {c.Faction}, which does not exist");
                if (c.DisplayId == 0) Error($"{where} needs a displayId");
                if (c.RunSpeed <= 0 || c.WalkSpeed <= 0 || c.RunSpeed > 20) Error($"{where}: speeds must be in (0, 20]");
                if (c.Health <= 0 || c.Damage < 0 || c.Armor < 0 || c.Mana < 0) Error($"{where}: health must be positive, damage/armor/mana not negative");
                if (c.AttackSpeedMs < 500) Error($"{where}: attackSpeedMs must be at least 500");
                if (c.AggroRadius < 0 || c.AggroRadius > 60) Error($"{where}: aggroRadius must be in 0..60");
                if (c.RespawnSeconds < 1 || c.CorpseSeconds < 0) Error($"{where}: respawnSeconds must be at least 1");
                foreach (var cs in c.Spells)
                {
                    if (!spellsById.TryGetValue(cs.Spell, out var spell)) Error($"{where} uses spell {cs.Spell}, which does not exist");
                    else if (spell.PowerType == PowerType.Mana && spell.Cost > 0 && c.Mana <= 0) Error($"{where} uses mana spell {cs.Spell} but has no mana");
                    if (cs.Chance <= 0 || cs.Chance > 1) Error($"{where}: spell {cs.Spell} chance must be in (0, 1]");
                }
            }

            foreach (var map in maps)
                foreach (var spawn in map.Spawns.Where(s => !creatureIds.Contains(s.Creature)))
                    Error($"map {map.Id}: spawn refers to creature {spawn.Creature}, which does not exist");
        }

        public void ValidateItems(List<ItemDefinition> items, List<SpellDefinition> spells, List<ClassDefinition> classes, LevelTable levels)
        {
            ValidateIds("items.json", items.Select(i => ((long)i.Id, i.Name)), uint.MaxValue);
            var spellsById = spells.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            var classIds = classes.Select(c => c.Id).ToHashSet();
            foreach (var item in items)
            {
                string where = $"items.json: item {item.Id} '{item.Name}'";
                if (Bytes(item.Name) > SpellInfo.MaxNameBytes || Bytes(item.Description) > SpellInfo.MaxDescriptionBytes)
                    Error($"{where}: name or description is too long");
                if (item.StackSize < 1 || item.StackSize > 1000) Error($"{where}: stackSize must be in 1..1000");
                if (item.StackSize > 1 && item.IsEquippable) Error($"{where}: equippable items do not stack");
                if (item.BuyPrice < 0 || item.SellPrice < 0) Error($"{where}: prices must not be negative");
                if (item.RequiredLevel < 0 || item.RequiredLevel > levels.MaxLevel) Error($"{where}: requiredLevel must be in 0..{levels.MaxLevel}");
                foreach (var id in item.Classes.Where(id => !classIds.Contains(id))) Error($"{where} is limited to class {id}, which does not exist");
                if (item.Class == ItemClass.Quest && item.SellPrice > 0) Error($"{where}: quest items cannot be sold (sellPrice 0)");

                bool weaponSlot = item.Slot is InventoryType.MainHand or InventoryType.TwoHand;
                if (weaponSlot && item.Weapon is null) Error($"{where}: a {item.Slot} item needs a weapon section");
                if (!weaponSlot && item.Weapon is not null) Error($"{where}: only mainHand and twoHand items have a weapon section");
                if (item.Weapon is { } w && (w.MinDamage < 0 || w.MaxDamage < w.MinDamage || w.SpeedMs < 1000 || w.SpeedMs > 5000))
                    Error($"{where}: weapon needs 0 <= minDamage <= maxDamage and speedMs in 1000..5000");
                if (item.IsBag != (item.Class == ItemClass.Container)) Error($"{where}: containers (class container) and bags (slot bag) go together");
                if (item.IsBag && (item.BagSlots < 1 || item.BagSlots > Shared.GameConstants.MaxBagSize))
                    Error($"{where}: bags need bagSlots in 1..{Shared.GameConstants.MaxBagSize}");
                if (!item.IsBag && item.BagSlots != 0) Error($"{where}: only bags have bagSlots");
                if (item.UseSpell != 0)
                {
                    if (!spellsById.TryGetValue(item.UseSpell, out var spell)) Error($"{where} uses spell {item.UseSpell}, which does not exist");
                    else if (spell.CastTimeMs != 0) Error($"{where}: its use spell {item.UseSpell} must be instant");
                    if (item.IsEquippable) Error($"{where}: equippable items cannot have a use spell (yet)");
                }
            }
        }

        public void ValidateLoot(List<LootTableDefinition> tables, List<ItemDefinition> items, List<CreatureDefinition> creatures)
        {
            ValidateIds("loot.json", tables.Select(t => ((long)t.Id, t.Name)), int.MaxValue);
            var itemsById = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
            var tableIds = tables.Select(t => t.Id).ToHashSet();
            foreach (var t in tables)
            {
                string where = $"loot.json: table {t.Id} '{t.Name}'";
                if (t.Money is { } m && (m.Min < 0 || m.Max < m.Min)) Error($"{where}: money needs 0 <= min <= max");
                if (t.Items.Count + t.Groups.Count > Shared.Net.LootWindow.MaxEntries) Error($"{where}: too many entries");
                foreach (var e in t.Items)
                {
                    if (!itemsById.TryGetValue(e.Item, out var item)) Error($"{where} drops item {e.Item}, which does not exist");
                    if (e.Chance <= 0 || e.Chance > 1) Error($"{where}: item {e.Item} chance must be in (0, 1]");
                    if (e.Min < 1 || e.Max < e.Min || (item is not null && e.Max > item.StackSize)) Error($"{where}: item {e.Item} needs 1 <= min <= max <= its stack size");
                }
                foreach (var g in t.Groups)
                {
                    if (g.Chance <= 0 || g.Chance > 1) Error($"{where}: group chance must be in (0, 1]");
                    if (g.Items.Count == 0) Error($"{where}: a group needs items");
                    foreach (var id in g.Items)
                    {
                        if (!itemsById.TryGetValue(id, out var item)) Error($"{where}: group item {id} does not exist");
                        else if (item.Class == ItemClass.Quest) Error($"{where}: quest item {id} cannot be in a group");
                    }
                }
            }

            foreach (var c in creatures)
            {
                string where = $"creatures.json: creature {c.Id} '{c.Name}'";
                if (c.Loot != 0 && !tableIds.Contains(c.Loot)) Error($"{where} has loot table {c.Loot}, which does not exist");
                foreach (var id in c.Vendor)
                {
                    if (!itemsById.TryGetValue(id, out var item)) Error($"{where} sells item {id}, which does not exist");
                    else if (item.BuyPrice <= 0) Error($"{where} sells item {id}, which has no buyPrice");
                }
                if (c.Vendor.Count > Shared.Net.VendorInventory.MaxItems) Error($"{where} sells too many items");
                if (c.Role != NpcRole.None && c.Vendor.Count == 0) Error($"{where} has the role {c.Role} but sells nothing");
                if (Bytes(c.Gossip) > QuestInfo.MaxTextBytes || Bytes(c.Name) > SpellInfo.MaxNameBytes) Error($"{where}: name or gossip is too long");
                CheckHeld(where, c.Equipment.MainHand, InventoryType.MainHand, InventoryType.TwoHand);
                CheckHeld(where, c.Equipment.OffHand, InventoryType.OffHand, InventoryType.OffHand);
            }

            void CheckHeld(string where, uint id, InventoryType a, InventoryType b)
            {
                if (id == 0) return;
                if (!itemsById.TryGetValue(id, out var item)) Error($"{where} holds item {id}, which does not exist");
                else if (item.Slot != a && item.Slot != b) Error($"{where} holds item {id} in the wrong hand");
            }
        }

        public void ValidateClassItems(List<ClassDefinition> classes, List<ItemDefinition> items)
        {
            var itemsById = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var c in classes)
            {
                foreach (var start in c.StartingItems)
                {
                    if (!itemsById.TryGetValue(start.Item, out var item)) Error($"classes.json: class {c.Id} '{c.Name}' starts with item {start.Item}, which does not exist");
                    else if (start.Count < 1 || start.Count > item.StackSize) Error($"classes.json: class {c.Id} starting item {start.Item} count must be in 1..{item.StackSize}");
                    else if (!item.AllowsClass(c.Id)) Error($"classes.json: class {c.Id} starts with item {start.Item}, which it cannot use");
                }
            }
        }

        public void ValidateQuests(List<QuestDefinition> quests, List<CreatureDefinition> creatures, List<ItemDefinition> items,
            List<ClassDefinition> classes, List<LoadedMap> maps, LevelTable levels, List<LootTableDefinition> loot)
        {
            ValidateClassItems(classes, items);
            ValidateIds("quests.json", quests.Select(q => ((long)q.Id, q.Title)), uint.MaxValue);
            var questsById = quests.GroupBy(q => q.Id).ToDictionary(g => g.Key, g => g.First());
            var creaturesById = creatures.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
            var itemsById = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
            var mapsById = maps.GroupBy(m => (int)m.Id).ToDictionary(g => g.Key, g => g.First());
            var classIds = classes.Select(c => c.Id).ToHashSet();
            // Items a player can get somewhere (collect objectives must be possible).
            var obtainable = loot.SelectMany(t => t.Items.Select(i => i.Item).Concat(t.Groups.SelectMany(g => g.Items)))
                .Concat(creatures.SelectMany(c => c.Vendor)).ToHashSet();

            foreach (var q in quests)
            {
                string where = $"quests.json: quest {q.Id} '{q.Title}'";
                if (Bytes(q.Title) > QuestInfo.MaxTitleBytes) Error($"{where}: title is too long");
                foreach (var text in new[] { q.Details, q.Summary, q.Progress, q.Completion })
                    if (Bytes(text) > QuestInfo.MaxTextBytes) Error($"{where}: a text is longer than {QuestInfo.MaxTextBytes} bytes");
                if (q.Details.Length == 0 || q.Summary.Length == 0 || q.Completion.Length == 0) Error($"{where} needs details, summary and completion texts");
                if (q.Level < 1 || q.Level > levels.MaxLevel || q.MinLevel < 1 || q.MinLevel > q.Level) Error($"{where}: needs 1 <= minLevel <= level <= {levels.MaxLevel}");
                if (!creaturesById.ContainsKey(q.Giver)) Error($"{where}: giver {q.Giver} does not exist");
                if (!creaturesById.ContainsKey(q.Ender)) Error($"{where}: ender {q.Ender} does not exist");
                foreach (var id in q.Requires)
                    if (id == q.Id || !questsById.ContainsKey(id)) Error($"{where} requires quest {id}, which does not exist (or is itself)");
                foreach (var id in q.Classes.Where(id => !classIds.Contains(id))) Error($"{where} is limited to class {id}, which does not exist");
                if (q.Objectives.Count > QuestInfo.MaxObjectives) Error($"{where} has more than {QuestInfo.MaxObjectives} objectives");
                if (q.Objectives.Count == 0 && q.Giver == q.Ender) Error($"{where} has no objectives: it must at least be delivered to another NPC");

                foreach (var o in q.Objectives)
                {
                    if (o.Count < 1 || o.Count > 250) Error($"{where}: objective counts must be in 1..250");
                    if (Bytes(o.Text) > QuestInfo.MaxTitleBytes) Error($"{where}: objective text is too long");
                    switch (o.Type)
                    {
                        case ObjectiveType.Kill:
                            if (!creaturesById.ContainsKey(o.Creature)) Error($"{where}: kill objective creature {o.Creature} does not exist");
                            break;
                        case ObjectiveType.Talk:
                            if (!creaturesById.ContainsKey(o.Creature)) Error($"{where}: talk objective creature {o.Creature} does not exist");
                            if (o.Count != 1) Error($"{where}: talk objectives have count 1");
                            break;
                        case ObjectiveType.Collect:
                            if (!itemsById.TryGetValue(o.Item, out var item)) Error($"{where}: collect objective item {o.Item} does not exist");
                            else if (!obtainable.Contains(o.Item)) Error($"{where}: item {o.Item} drops nowhere and is sold nowhere");
                            break;
                        case ObjectiveType.Explore:
                            if (!mapsById.TryGetValue(o.Map, out var map)) Error($"{where}: explore objective map {o.Map} does not exist");
                            else if (!map.HeightMap.Contains(o.X, o.Z)) Error($"{where}: explore objective ({o.X}, {o.Z}) is outside map {o.Map}");
                            if (o.Radius < 1 || o.Radius > 200) Error($"{where}: explore radius must be in 1..200");
                            if (o.Count != 1) Error($"{where}: explore objectives have count 1");
                            if (o.Text.Length == 0) Error($"{where}: explore objectives need a text");
                            break;
                    }
                }

                var r = q.Rewards;
                if (r.Xp < 0 || r.Money < 0) Error($"{where}: rewards must not be negative");
                if (r.Items.Count > QuestInfo.MaxRewards || r.Choices.Count > QuestInfo.MaxRewards) Error($"{where}: at most {QuestInfo.MaxRewards} reward items and choices");
                foreach (var reward in r.Items.Concat(r.Choices))
                {
                    if (!itemsById.TryGetValue(reward.Item, out var item)) Error($"{where}: reward item {reward.Item} does not exist");
                    else if (reward.Count < 1 || reward.Count > item.StackSize) Error($"{where}: reward item {reward.Item} count must be in 1..{item.StackSize}");
                }
            }

            // Prerequisite chains must not loop.
            var state = new Dictionary<uint, int>();
            foreach (var q in quests) Visit(q.Id);

            void Visit(uint id)
            {
                if (state.TryGetValue(id, out int s))
                {
                    if (s == 1) Error($"quests.json: quest {id} requires itself through a chain of prerequisites");
                    return;
                }
                state[id] = 1;
                if (questsById.TryGetValue(id, out var q))
                    foreach (var next in q.Requires.Where(questsById.ContainsKey)) Visit(next);
                state[id] = 2;
            }
        }

        private void ValidateSpell(SpellDefinition s)
        {
            string where = $"spells.json: spell {s.Id} '{s.Name}'";
            if (s.RangeMin < 0 || s.RangeMax < s.RangeMin || s.RangeMax > 100) Error($"{where}: needs 0 <= rangeMin <= rangeMax <= 100");
            if (s.CastTimeMs < 0 || s.CastTimeMs > 60_000 || s.CooldownMs < 0 || s.GcdMs < 0 || s.GcdMs > 2000)
                Error($"{where}: cast time 0..60000, cooldown >= 0 and gcd 0..2000 ms");
            if (s.Cost < 0 || (s.PowerType == PowerType.None && s.Cost != 0)) Error($"{where}: a cost needs a powerType and must not be negative");
            if (s.ProjectileSpeed < 0) Error($"{where}: projectileSpeed must not be negative");
            if (s.Has(SpellFlags.AutoAttack))
            {
                if (s.Effects.Count > 0 || s.Target != SpellTarget.Enemy) Error($"{where}: an autoAttack spell targets an enemy and has no effects");
                return;
            }
            if (s.Effects.Count == 0) Error($"{where} has no effects");

            foreach (var e in s.Effects)
            {
                if (e.Targets == EffectTargets.Target && s.Target == SpellTarget.None) Error($"{where}: an effect targets 'target' but the spell has none");
                if (e.Targets is EffectTargets.EnemiesNearCaster or EffectTargets.EnemiesNearTarget or EffectTargets.AlliesNearCaster && (e.Radius <= 0 || e.Radius > 50))
                    Error($"{where}: area effects need a radius in (0, 50]");
                if (e.Targets == EffectTargets.EnemiesNearTarget && s.Target == SpellTarget.None) Error($"{where}: enemiesNearTarget needs a spell target");
                switch (e.Type)
                {
                    case SpellEffectType.SchoolDamage or SpellEffectType.Heal or SpellEffectType.Energize:
                        if (e.Min < 0 || e.Max < e.Min || e.PerLevel < 0) Error($"{where}: {e.Type} needs 0 <= min <= max and perLevel >= 0");
                        if (e.Type == SpellEffectType.Energize && e.Power == PowerType.None) Error($"{where}: energize needs a power");
                        break;
                    case SpellEffectType.WeaponDamage:
                        if (e.Percent < 0 || e.Bonus < 0) Error($"{where}: weaponDamage needs percent and bonus >= 0");
                        break;
                    case SpellEffectType.ApplyAura:
                        if (s.Aura is null) Error($"{where}: applyAura needs an aura section");
                        break;
                }
            }

            if (s.Aura is { } aura)
            {
                if (aura.DurationMs <= 0) Error($"{where}: aura durationMs must be positive");
                if (aura.MaxStacks < 1 || aura.MaxStacks > 255) Error($"{where}: aura maxStacks must be in 1..255");
                if (aura.Effects.Count == 0) Error($"{where}: aura has no effects");
                bool periodic = aura.Effects.Any(e => e.Type is AuraEffectType.PeriodicDamage or AuraEffectType.PeriodicHeal or AuraEffectType.PeriodicEnergize);
                if (periodic && (aura.TickMs < 250 || aura.TickMs > aura.DurationMs)) Error($"{where}: periodic auras need 250 <= tickMs <= durationMs");
                foreach (var e in aura.Effects)
                {
                    if (e.Type == AuraEffectType.ModStat && e.Stat is null) Error($"{where}: modStat needs a stat");
                    if (e.Type == AuraEffectType.ModSpeed && (e.Amount <= -100 || e.Amount > 200)) Error($"{where}: modSpeed amount must be in (-100, 200]");
                    if (e.Type is AuraEffectType.PeriodicDamage or AuraEffectType.PeriodicHeal or AuraEffectType.Absorb or AuraEffectType.PeriodicEnergize && e.Amount <= 0)
                        Error($"{where}: {e.Type} needs a positive amount");
                    if (e.Type == AuraEffectType.PeriodicEnergize && e.Power == PowerType.None) Error($"{where}: periodicEnergize needs a power");
                }
                if (!s.Effects.Any(e => e.Type == SpellEffectType.ApplyAura)) Error($"{where}: has an aura but no applyAura effect");
            }
        }

        private static int Bytes(string text) => System.Text.Encoding.UTF8.GetByteCount(text);

        /// <summary>Ids that end up on the wire or in the database: 1..max, unique, named.</summary>
        private void ValidateIds(string file, IEnumerable<(long Id, string Name)> entries, long max)
        {
            var seen = new HashSet<long>();
            foreach (var (id, name) in entries)
            {
                if (id < 1 || id > max) Error($"{file}: id {id} must be in 1..{max}");
                if (!seen.Add(id)) Error($"{file}: id {id} is used twice");
                if (string.IsNullOrWhiteSpace(name)) Error($"{file}: id {id} has no name");
            }
        }
    }
}
