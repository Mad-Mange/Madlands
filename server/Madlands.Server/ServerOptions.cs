namespace Madlands.Server;

public sealed class ServerOptions
{
    public int Port { get; set; } = 7777;
    /// <summary>IPv4 address to listen on; null = all interfaces. Use 127.0.0.1 for local-only testing.</summary>
    public string? Bind { get; set; }
    public string? ContentPath { get; set; }
    public int MaxConnections { get; set; } = 1000;
    public ushort StartMapId { get; set; } = 1;
    public long StatsIntervalMs { get; set; } = 10_000;
    public bool Verbose { get; set; }

    public static ServerOptions Parse(string[] args)
    {
        var o = new ServerOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
            switch (args[i])
            {
                case "--port": o.Port = int.Parse(Next()); break;
                case "--bind": o.Bind = Next(); break;
                case "--content": o.ContentPath = Next(); break;
                case "--max-connections": o.MaxConnections = int.Parse(Next()); break;
                case "--map": o.StartMapId = ushort.Parse(Next()); break;
                case "--stats-interval": o.StatsIntervalMs = long.Parse(Next()) * 1000; break;
                case "--verbose": o.Verbose = true; break;
                case "--help":
                case "-h":
                    Console.WriteLine("Madlands.Server [--port 7777] [--bind 127.0.0.1] [--content <path>] [--map 1] [--max-connections 1000] [--stats-interval 10] [--verbose]");
                    Environment.Exit(0);
                    break;
                default: throw new ArgumentException($"Unknown argument {args[i]}");
            }
        }
        return o;
    }
}
