using System.Diagnostics;
using Madlands.Bots;
using Madlands.ClientCore;
using Madlands.Content;

// Headless load/integration test clients:
//   dotnet run --project server/Madlands.Bots -- --count 200
string host = "127.0.0.1";
int port = 7777;
int count = 50;
int connectRate = 25; // per second
float radius = 200f;
float seconds = 0f;   // 0 = run until Ctrl+C
ushort mapId = 1;
string? contentPath = null;

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
    switch (args[i])
    {
        case "--host": host = Next(); break;
        case "--port": port = int.Parse(Next()); break;
        case "--count": count = int.Parse(Next()); break;
        case "--connect-rate": connectRate = int.Parse(Next()); break;
        case "--radius": radius = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
        case "--seconds": seconds = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
        case "--map": mapId = ushort.Parse(Next()); break;
        case "--content": contentPath = Next(); break;
        default:
            Console.WriteLine("Madlands.Bots [--host 127.0.0.1] [--port 7777] [--count 50] [--connect-rate 25] [--radius 200] [--seconds 0] [--map 1] [--content <path>]");
            return 1;
    }
}

// Bots need the same height map as the server, otherwise their moves would be rejected.
var content = ContentLoader.Load(ContentPaths.Resolve(contentPath));
if (!content.Maps.TryGetValue(mapId, out var map))
{
    Console.WriteLine($"Map {mapId} not found in content.");
    return 1;
}

Console.WriteLine($"Starting {count} bots against {host}:{port} (wander radius {radius} m)");
var bots = new List<Bot>(count);
for (int i = 0; i < count; i++) bots.Add(new Bot($"Bot{i + 1:000}", map.HeightMap, radius, seed: 1000 + i));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
if (seconds > 0) cts.CancelAfter(TimeSpan.FromSeconds(seconds));

var clock = Stopwatch.StartNew();
int connected = 0;
double last = 0;
double nextReport = 5;
const double FrameSeconds = 0.02; // 50 Hz, like a client running at a modest frame rate

while (!cts.IsCancellationRequested)
{
    double now = clock.Elapsed.TotalSeconds;
    float dt = (float)Math.Min(now - last, 0.1);
    last = now;

    int shouldBeConnected = Math.Min(count, (int)(now * connectRate) + 1);
    while (connected < shouldBeConnected) bots[connected++].Connect(host, port);

    for (int i = 0; i < connected; i++) bots[i].Update(dt);

    if (now >= nextReport)
    {
        nextReport = now + 5;
        var inWorld = bots.Take(connected).Where(b => b.Client.State == ClientState.InWorld).ToList();
        double avgRtt = inWorld.Count > 0 ? inWorld.Average(b => b.Client.RoundTripMs) : 0;
        double avgSeen = inWorld.Count > 0 ? inWorld.Average(b => b.Client.World.Count) : 0;
        int corrections = bots.Sum(b => b.Corrections);
        var errors = bots.Where(b => b.LastError is not null).Select(b => b.LastError).Distinct().Take(3).ToList();
        Console.WriteLine($"[{now,6:0}s] in world {inWorld.Count}/{count} | rtt avg {avgRtt:0} ms | sees avg {avgSeen:0.0} entities | corrections {corrections}"
            + (errors.Count > 0 ? " | errors: " + string.Join("; ", errors) : ""));
    }

    double sleep = FrameSeconds - (clock.Elapsed.TotalSeconds - now);
    if (sleep > 0) Thread.Sleep(TimeSpan.FromSeconds(sleep));
}

Console.WriteLine("Disconnecting bots...");
foreach (var bot in bots.Take(connected)) bot.Disconnect();
Thread.Sleep(200);
foreach (var bot in bots.Take(connected))
{
    bot.Client.Update();
    bot.Client.Dispose();
}
return 0;
