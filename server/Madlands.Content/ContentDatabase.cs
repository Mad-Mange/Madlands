using System.Text.Json;
using System.Text.Json.Serialization;
using Madlands.Shared;
using Madlands.Shared.Terrain;

namespace Madlands.Content;

/// <summary>content/maps/&lt;id&gt;/map.json</summary>
public sealed class MapDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Height map file relative to the map folder, written by Unity's Madlands > Export Map.</summary>
    public string Heightmap { get; set; } = "height.bin";
    public SpawnPoint Spawn { get; set; } = new();
}

public sealed class SpawnPoint
{
    public float X { get; set; }
    public float Z { get; set; }
    public float YawDegrees { get; set; }
}

public sealed class LoadedMap
{
    public LoadedMap(MapDefinition definition, HeightMap heightMap)
    {
        Definition = definition;
        HeightMap = heightMap;
    }

    public MapDefinition Definition { get; }
    public HeightMap HeightMap { get; }
    public ushort Id => (ushort)Definition.Id;

    public Vec3 SpawnPosition =>
        new(Definition.Spawn.X, HeightMap.GetHeight(Definition.Spawn.X, Definition.Spawn.Z), Definition.Spawn.Z);

    public float SpawnYaw => Definition.Spawn.YawDegrees * MathUtil.Deg2Rad;
}

/// <summary>content/races.json entry. Ids are stable and never reused (they are stored on characters).</summary>
public sealed class RaceDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Map new characters of this race start on (at the map's spawn point).</summary>
    public int StartMap { get; set; } = 1;
}

/// <summary>content/classes.json entry. Ids are stable and never reused.</summary>
public sealed class ClassDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

internal sealed class RaceFile
{
    public List<RaceDefinition> Races { get; set; } = new();
}

internal sealed class ClassFile
{
    public List<ClassDefinition> Classes { get; set; } = new();
}

/// <summary>All game content, loaded and cross-validated. Immutable after loading.</summary>
public sealed class ContentDatabase
{
    public ContentDatabase(IEnumerable<LoadedMap> maps, IEnumerable<RaceDefinition>? races = null, IEnumerable<ClassDefinition>? classes = null)
    {
        Maps = maps.ToDictionary(m => m.Id);
        Races = (races ?? DefaultRaces()).ToDictionary(r => (byte)r.Id);
        Classes = (classes ?? DefaultClasses()).ToDictionary(c => (byte)c.Id);
    }

    public IReadOnlyDictionary<ushort, LoadedMap> Maps { get; }
    public IReadOnlyDictionary<byte, RaceDefinition> Races { get; }
    public IReadOnlyDictionary<byte, ClassDefinition> Classes { get; }

    // Used by tests that build content in code.
    private static IEnumerable<RaceDefinition> DefaultRaces() => new[] { new RaceDefinition { Id = 1, Name = "Test Race", StartMap = 1 } };
    private static IEnumerable<ClassDefinition> DefaultClasses() => new[] { new ClassDefinition { Id = 1, Name = "Test Class" } };
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

public static class ContentLoader
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    /// <summary>Loads and validates everything under <paramref name="contentRoot"/>. Collects ALL errors before failing.</summary>
    public static ContentDatabase Load(string contentRoot)
    {
        var errors = new List<string>();
        var maps = LoadMaps(Path.Combine(contentRoot, "maps"), errors);
        var races = LoadList<RaceFile, RaceDefinition>(Path.Combine(contentRoot, "races.json"), f => f.Races, errors);
        var classes = LoadList<ClassFile, ClassDefinition>(Path.Combine(contentRoot, "classes.json"), f => f.Classes, errors);

        ValidateIds("races.json", races.Select(r => (r.Id, r.Name)), errors);
        ValidateIds("classes.json", classes.Select(c => (c.Id, c.Name)), errors);
        var mapIds = maps.Select(m => (int)m.Id).ToHashSet();
        foreach (var race in races.Where(r => !mapIds.Contains(r.StartMap)))
            errors.Add($"races.json: race {race.Id} '{race.Name}' starts on map {race.StartMap}, which does not exist");

        if (errors.Count > 0) throw new ContentException(errors);
        return new ContentDatabase(maps, races, classes);
    }

    private static List<TItem> LoadList<TFile, TItem>(string path, Func<TFile, List<TItem>> select, List<string> errors) where TFile : class
    {
        if (!File.Exists(path))
        {
            errors.Add($"Missing file {path}");
            return new List<TItem>();
        }
        try
        {
            var file = JsonSerializer.Deserialize<TFile>(File.ReadAllText(path), JsonOptions);
            var items = file is null ? new List<TItem>() : select(file);
            if (items.Count == 0) errors.Add($"{path}: no entries");
            return items;
        }
        catch (JsonException ex)
        {
            errors.Add($"{path}: invalid JSON ({ex.Message})");
            return new List<TItem>();
        }
    }

    /// <summary>Ids that end up in a byte on the wire / in the database: 1..255, unique, named.</summary>
    private static void ValidateIds(string file, IEnumerable<(int Id, string Name)> entries, List<string> errors)
    {
        var seen = new HashSet<int>();
        foreach (var (id, name) in entries)
        {
            if (id < 1 || id > 255) errors.Add($"{file}: id {id} must be in 1..255");
            if (!seen.Add(id)) errors.Add($"{file}: id {id} is used twice");
            if (string.IsNullOrWhiteSpace(name)) errors.Add($"{file}: id {id} has no name");
        }
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
            MapDefinition? def;
            try
            {
                def = JsonSerializer.Deserialize<MapDefinition>(File.ReadAllText(file), JsonOptions);
            }
            catch (JsonException ex)
            {
                errors.Add($"{file}: invalid JSON ({ex.Message})");
                continue;
            }
            if (def is null)
            {
                errors.Add($"{file}: empty file");
                continue;
            }
            if (def.Id <= 0 || def.Id > ushort.MaxValue) errors.Add($"{file}: id {def.Id} must be in 1..{ushort.MaxValue}");
            if (seenIds.TryGetValue(def.Id, out var other)) errors.Add($"{file}: map id {def.Id} already used by {other}");
            seenIds[def.Id] = file;
            if (string.IsNullOrWhiteSpace(def.Name)) errors.Add($"{file}: name is required");

            var heightPath = Path.Combine(Path.GetDirectoryName(file)!, def.Heightmap);
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

            result.Add(new LoadedMap(def, heightMap));
        }
        return result;
    }
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
