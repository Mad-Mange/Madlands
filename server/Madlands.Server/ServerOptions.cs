namespace Madlands.Server;

public sealed class ServerOptions
{
    public const string DevConnectionString = "Host=127.0.0.1;Port=5432;Username=madlands;Password=madlands_dev;Database=madlands";

    /// <summary>UDP port of the game connection.</summary>
    public int Port { get; set; } = 7777;
    /// <summary>TCP port of the HTTP account API.</summary>
    public int HttpPort { get; set; } = 7780;
    /// <summary>IPv4 address to listen on; null = all interfaces. Use 127.0.0.1 for local-only testing.</summary>
    public string? Bind { get; set; }
    /// <summary>Address clients should connect to for the game, sent in the login response. Defaults to Bind or 127.0.0.1.</summary>
    public string? PublicHost { get; set; }
    public string RealmName { get; set; } = "Madlands (dev)";
    public string? ContentPath { get; set; }
    /// <summary>"postgres" (default) or "memory" (no persistence across restarts; no Docker needed).</summary>
    public string Database { get; set; } = "postgres";
    public string ConnectionString { get; set; } = DevConnectionString;
    /// <summary>Account API requests per minute per IP; 0 disables (needed for large local bot runs).</summary>
    public int ApiRateLimitPerMinute { get; set; } = 120;
    public int MaxConnections { get; set; } = 1000;
    public ushort StartMapId { get; set; } = 1;
    public long StatsIntervalMs { get; set; } = 10_000;
    public bool Verbose { get; set; }

    public const string Usage =
        "Madlands.Server [--port 7777] [--http-port 7780] [--bind 127.0.0.1] [--public-host <host>] [--content <path>]\n" +
        "                [--db postgres|memory] [--connection-string <npgsql>] [--api-rate-limit 120]\n" +
        "                [--map 1] [--max-connections 1000] [--stats-interval 10] [--verbose]";

    public static ServerOptions Parse(string[] args)
    {
        var o = new ServerOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
            switch (args[i])
            {
                case "--port": o.Port = int.Parse(Next()); break;
                case "--http-port": o.HttpPort = int.Parse(Next()); break;
                case "--bind": o.Bind = Next(); break;
                case "--public-host": o.PublicHost = Next(); break;
                case "--realm-name": o.RealmName = Next(); break;
                case "--content": o.ContentPath = Next(); break;
                case "--db": o.Database = Next().ToLowerInvariant(); break;
                case "--connection-string": o.ConnectionString = Next(); break;
                case "--api-rate-limit": o.ApiRateLimitPerMinute = int.Parse(Next()); break;
                case "--max-connections": o.MaxConnections = int.Parse(Next()); break;
                case "--map": o.StartMapId = ushort.Parse(Next()); break;
                case "--stats-interval": o.StatsIntervalMs = long.Parse(Next()) * 1000; break;
                case "--verbose": o.Verbose = true; break;
                case "--help":
                case "-h":
                    Console.WriteLine(Usage);
                    Environment.Exit(0);
                    break;
                default: throw new ArgumentException($"Unknown argument {args[i]}\n{Usage}");
            }
        }
        if (o.Database is not ("postgres" or "memory")) throw new ArgumentException("--db must be 'postgres' or 'memory'.");
        return o;
    }
}
