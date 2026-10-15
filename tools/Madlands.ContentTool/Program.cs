using Madlands.Content;

// Content pipeline entry point. Phase 1: validation only. Later phases add compiling content
// into the binary format the client ships with, and building navmeshes (DotRecast).
//   dotnet run --project tools/Madlands.ContentTool -- validate [--content <path>]
string command = args.Length > 0 ? args[0] : "validate";
string? contentPath = null;
for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--content" && i + 1 < args.Length) contentPath = args[++i];
}

switch (command)
{
    case "validate":
        try
        {
            var root = ContentPaths.Resolve(contentPath);
            var db = ContentLoader.Load(root);
            Console.WriteLine($"Content OK: {root}");
            foreach (var map in db.Maps.Values.OrderBy(m => m.Id))
            {
                var h = map.HeightMap;
                Console.WriteLine($"  map {map.Id} '{map.Definition.Name}': {h.Width}x{h.Depth} samples @ {h.CellSize} m, " +
                                  $"x {h.OriginX}..{h.MaxX}, z {h.OriginZ}..{h.MaxZ}, spawn {map.SpawnPosition}");
            }
            return 0;
        }
        catch (ContentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Commands: validate");
        return 1;
}
