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

/// <summary>All game content, loaded and cross-validated. Immutable after loading.</summary>
public sealed class ContentDatabase
{
    public ContentDatabase(IEnumerable<LoadedMap> maps)
    {
        Maps = maps.ToDictionary(m => m.Id);
    }

    public IReadOnlyDictionary<ushort, LoadedMap> Maps { get; }
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
        if (errors.Count > 0) throw new ContentException(errors);
        return new ContentDatabase(maps);
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
