using Madlands.Shared.Game;

namespace Madlands.Content;

/// <summary>All game content, loaded and cross-validated by <see cref="ContentLoader"/>. Immutable after loading.</summary>
public sealed class ContentDatabase
{
    private static readonly IReadOnlyList<QuestDefinition> NoQuests = Array.Empty<QuestDefinition>();

    private readonly Dictionary<int, List<QuestDefinition>> _questsByGiver = new();
    private readonly Dictionary<int, List<QuestDefinition>> _questsByEnder = new();

    public ContentDatabase(
        IEnumerable<LoadedMap> maps,
        IEnumerable<RaceDefinition>? races = null,
        IEnumerable<ClassDefinition>? classes = null,
        IEnumerable<FactionDefinition>? factions = null,
        IEnumerable<SpellDefinition>? spells = null,
        IEnumerable<CreatureDefinition>? creatures = null,
        LevelTable? levels = null,
        IEnumerable<ItemDefinition>? items = null,
        IEnumerable<LootTableDefinition>? lootTables = null,
        IEnumerable<QuestDefinition>? quests = null,
        IEnumerable<GameObjectDefinition>? objects = null)
    {
        Maps = maps.ToDictionary(m => m.Id);
        Races = (races ?? DefaultRaces()).ToDictionary(r => (byte)r.Id);
        Classes = (classes ?? DefaultClasses()).ToDictionary(c => (byte)c.Id);
        Factions = (factions ?? DefaultFactions()).ToDictionary(f => (ushort)f.Id);
        Spells = (spells ?? Array.Empty<SpellDefinition>()).ToDictionary(s => s.Id);
        Creatures = (creatures ?? Array.Empty<CreatureDefinition>()).ToDictionary(c => c.Id);
        Levels = levels ?? new LevelTable { MaxLevel = 10, XpToNextLevel = { 400, 900, 1400, 2100, 2800, 3600, 4500, 5400, 6500 } };
        Items = (items ?? Array.Empty<ItemDefinition>()).ToDictionary(i => i.Id);
        LootTables = (lootTables ?? Array.Empty<LootTableDefinition>()).ToDictionary(l => l.Id);
        Quests = (quests ?? Array.Empty<QuestDefinition>()).ToDictionary(q => q.Id);
        Objects = (objects ?? Array.Empty<GameObjectDefinition>()).ToDictionary(o => o.Id);
        FactionTable = new FactionTable(Factions.Values.Select(f => new FactionInfo
        {
            Id = (ushort)f.Id,
            Name = f.Name,
            Friendly = f.Friendly.Select(id => (ushort)id).ToArray(),
            Hostile = f.Hostile.Select(id => (ushort)id).ToArray(),
        }));

        foreach (var q in Quests.Values.OrderBy(q => q.Id))
        {
            Add(_questsByGiver, q.Giver, q);
            Add(_questsByEnder, q.Ender, q);
        }
    }

    public IReadOnlyDictionary<ushort, LoadedMap> Maps { get; }
    public IReadOnlyDictionary<byte, RaceDefinition> Races { get; }
    public IReadOnlyDictionary<byte, ClassDefinition> Classes { get; }
    public IReadOnlyDictionary<ushort, FactionDefinition> Factions { get; }
    public IReadOnlyDictionary<uint, SpellDefinition> Spells { get; }
    public IReadOnlyDictionary<int, CreatureDefinition> Creatures { get; }
    public LevelTable Levels { get; }
    public IReadOnlyDictionary<uint, ItemDefinition> Items { get; }
    public IReadOnlyDictionary<int, LootTableDefinition> LootTables { get; }
    public IReadOnlyDictionary<uint, QuestDefinition> Quests { get; }
    /// <summary>Gatherable objects (content/objects.json); their spawns are in each map.</summary>
    public IReadOnlyDictionary<int, GameObjectDefinition> Objects { get; }
    public FactionTable FactionTable { get; }

    /// <summary>Quests the creature offers, by id.</summary>
    public IReadOnlyList<QuestDefinition> QuestsGivenBy(int creatureId) => _questsByGiver.GetValueOrDefault(creatureId) ?? NoQuests;

    /// <summary>Quests the creature takes back, by id.</summary>
    public IReadOnlyList<QuestDefinition> QuestsEndedBy(int creatureId) => _questsByEnder.GetValueOrDefault(creatureId) ?? NoQuests;

    /// <summary>What talking to a creature of this template offers.</summary>
    public NpcFlags NpcFlagsOf(CreatureDefinition creature)
    {
        var flags = NpcFlags.None;
        if (creature.Gossip.Length > 0) flags |= NpcFlags.Gossip;
        if (_questsByGiver.ContainsKey(creature.Id) || _questsByEnder.ContainsKey(creature.Id)) flags |= NpcFlags.QuestGiver;
        if (creature.Vendor.Count > 0) flags |= NpcFlags.Vendor;
        return flags;
    }

    /// <summary>Same content with other maps (tests run the repository's spells and creatures on small flat maps).</summary>
    public ContentDatabase WithMaps(IEnumerable<LoadedMap> maps) =>
        new(maps, Races.Values, Classes.Values, Factions.Values, Spells.Values, Creatures.Values, Levels, Items.Values, LootTables.Values, Quests.Values,
            Objects.Values);

    private static void Add(Dictionary<int, List<QuestDefinition>> index, int creature, QuestDefinition quest)
    {
        if (!index.TryGetValue(creature, out var list)) index[creature] = list = new List<QuestDefinition>();
        list.Add(quest);
    }

    // Used by tests that build content in code.
    private static IEnumerable<RaceDefinition> DefaultRaces() => new[] { new RaceDefinition { Id = 1, Name = "Test Race", StartMap = 1, Faction = 1 } };
    private static IEnumerable<ClassDefinition> DefaultClasses() => new[] { new ClassDefinition { Id = 1, Name = "Test Class" } };
    private static IEnumerable<FactionDefinition> DefaultFactions() => new[] { new FactionDefinition { Id = 1, Name = "Players" } };
}

public sealed class ContentException : Exception
{
    public ContentException(IReadOnlyList<string> errors)
        : base("Content validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "  - " + e)))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

public static class ContentPaths
{
    /// <summary>
    /// Finds the repo's content/ folder by walking up from <paramref name="startDirectory"/>
    /// (so the server and tools work no matter where they are started from).
    /// </summary>
    public static string? FindContentRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "content");
            if (Directory.Exists(Path.Combine(candidate, "maps"))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public static string Resolve(string? explicitPath)
    {
        if (!string.IsNullOrEmpty(explicitPath)) return Path.GetFullPath(explicitPath);
        return FindContentRoot(Environment.CurrentDirectory)
            ?? FindContentRoot(AppContext.BaseDirectory)
            ?? throw new DirectoryNotFoundException("Could not find the content/ folder. Pass --content <path>.");
    }
}
