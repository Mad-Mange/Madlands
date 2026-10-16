using System.Diagnostics;
using Madlands.Bots;
using Madlands.ClientCore;
using Madlands.Content;

// Headless load/integration test clients. Each bot logs in through the account API (registering
// account "botNNN" and character "BotXXX" on first run), then enters the world:
//   dotnet run --project server/Madlands.Bots -- --count 200
// For large runs start the server with --api-rate-limit 0 (all bots share one IP).
string host = "127.0.0.1";
int httpPort = AccountClient.DefaultHttpPort;
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
        case "--http-port": httpPort = int.Parse(Next()); break;
        case "--count": count = int.Parse(Next()); break;
        case "--connect-rate": connectRate = int.Parse(Next()); break;
        case "--radius": radius = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
        case "--seconds": seconds = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
        case "--map": mapId = ushort.Parse(Next()); break;
        case "--content": contentPath = Next(); break;
        default:
            Console.WriteLine("Madlands.Bots [--host 127.0.0.1] [--http-port 7780] [--count 50] [--connect-rate 25] [--radius 200] [--seconds 0] [--map 1] [--content <path>]");
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

string baseUrl = $"http://{host}:{httpPort}";
Console.WriteLine($"Starting {count} bots against {baseUrl} (wander radius {radius} m)");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var bots = new List<Bot>(count);
for (int i = 0; i < count; i++) bots.Add(new Bot(i, map.HeightMap, radius));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
if (seconds > 0) cts.CancelAfter(TimeSpan.FromSeconds(seconds));

var clock = Stopwatch.StartNew();
int started = 0;
double last = 0;
double nextReport = 5;
const double FrameSeconds = 0.02; // 50 Hz, like a client running at a modest frame rate

while (!cts.IsCancellationRequested)
{
    double now = clock.Elapsed.TotalSeconds;
    float dt = (float)Math.Min(now - last, 0.1);
    last = now;

    int shouldBeStarted = Math.Min(count, (int)(now * connectRate) + 1);
    while (started < shouldBeStarted) bots[started++].Start(new AccountClient(baseUrl, http));

    for (int i = 0; i < started; i++) bots[i].Update(dt);

    if (now >= nextReport)
    {
        nextReport = now + 5;
        var inWorld = bots.Take(started).Where(b => b.Client.State == ClientState.InWorld).ToList();
        double avgRtt = inWorld.Count > 0 ? inWorld.Average(b => b.Client.Clock.LastRttMs) : 0;
        double avgSeen = inWorld.Count > 0 ? inWorld.Average(b => b.Client.World.Count) : 0;
        int loggingIn = bots.Count(b => b.LoggingIn);
        int corrections = bots.Sum(b => b.Corrections);
        var errors = bots.Where(b => b.LastError is not null).Select(b => b.LastError).Distinct().Take(3).ToList();
        Console.WriteLine($"[{now,6:0}s] in world {inWorld.Count}/{count} (logging in {loggingIn}) | ping avg {avgRtt:0} ms | sees avg {avgSeen:0.0} entities | corrections {corrections}"
            + (errors.Count > 0 ? " | errors: " + string.Join("; ", errors) : ""));
    }

    double sleep = FrameSeconds - (clock.Elapsed.TotalSeconds - now);
    if (sleep > 0) Thread.Sleep(TimeSpan.FromSeconds(sleep));
}

Console.WriteLine("Disconnecting bots...");
foreach (var bot in bots.Take(started)) bot.Disconnect();
Thread.Sleep(200);
foreach (var bot in bots.Take(started))
{
    bot.Client.Update();
    bot.Client.Dispose();
}
return 0;
