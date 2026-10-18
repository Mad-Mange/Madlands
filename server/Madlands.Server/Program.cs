using System.Runtime.InteropServices;
using Madlands.Content;
using Madlands.Server;
using Madlands.Server.Persistence;
using Npgsql;
using Serilog;
using Serilog.Events;

// Admin: "set-gm <account> <level>" grants GM commands to an account (0 revokes), then exits.
if (args.Length > 0 && args[0] == "set-gm")
{
    if (args.Length < 3 || !int.TryParse(args[2], out int gmLevel))
    {
        Console.WriteLine("Usage: Madlands.Server set-gm <account> <level> [--connection-string <npgsql>]");
        return 1;
    }
    var adminOptions = ServerOptions.Parse(args[3..]);
    var username = Madlands.Shared.NameRules.NormalizeUsername(args[1]) ?? args[1];
    var adminDb = PostgresGameDatabase.Open(adminOptions.ConnectionString);
    bool updated = await adminDb.SetGmLevelAsync(username, gmLevel);
    Console.WriteLine(updated ? $"Account '{username}' now has GM level {gmLevel}." : $"No account named '{username}'.");
    return updated ? 0 : 2;
}

// Admin: "set-password <account> <password>" replaces an account's password and ends its sessions, then exits.
if (args.Length > 0 && args[0] == "set-password")
{
    if (args.Length < 3 || !Madlands.Shared.NameRules.IsValidPassword(args[2]))
    {
        Console.WriteLine($"Usage: Madlands.Server set-password <account> <password> [--connection-string <npgsql>]" +
                          $" (passwords are {Madlands.Shared.NameRules.PasswordMin}-{Madlands.Shared.NameRules.PasswordMax} characters)");
        return 1;
    }
    var adminOptions = ServerOptions.Parse(args[3..]);
    var username = Madlands.Shared.NameRules.NormalizeUsername(args[1]) ?? args[1];
    var adminDb = PostgresGameDatabase.Open(adminOptions.ConnectionString);
    var hash = await new Madlands.Server.Auth.PasswordHasher().HashAsync(args[2]);
    bool updated = await adminDb.SetPasswordAsync(username, hash);
    Console.WriteLine(updated ? $"Account '{username}' has a new password; its sessions were ended." : $"No account named '{username}'.");
    return updated ? 0 : 2;
}

var options = ServerOptions.Parse(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(options.Verbose ? LogEventLevel.Debug : LogEventLevel.Information)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var contentRoot = ContentPaths.Resolve(options.ContentPath);
    Log.Information("Loading content from {Root}", contentRoot);
    var content = ContentLoader.Load(contentRoot);
    Log.Information("Loaded {Maps} map(s), {Races} race(s), {Classes} class(es)", content.Maps.Count, content.Races.Count, content.Classes.Count);

    IGameDatabase database;
    if (options.Database == "memory")
    {
        Log.Warning("Using the in-memory database: accounts and characters are lost when the server stops");
        database = new InMemoryGameDatabase();
    }
    else
    {
        database = PostgresGameDatabase.Open(options.ConnectionString, message => Log.Information("{Message}", message));
        Log.Information("Connected to PostgreSQL");
    }

    await using var host = new ServerHost(options, content, database);
    await host.StartAsync();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };
    // systemd and Docker stop with SIGTERM: shut down the same way (everyone is saved).
    using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
        System.Runtime.InteropServices.PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cts.Cancel();
        });

    // Windows sleeps in ~15.6 ms steps by default, far too coarse for a 50 ms tick.
    if (OperatingSystem.IsWindows()) WinMM.timeBeginPeriod(1);
    try
    {
        host.Game.Run(cts.Token);
    }
    finally
    {
        if (OperatingSystem.IsWindows()) WinMM.timeEndPeriod(1);
    }

    await host.StopAsync();
    return 0;
}
catch (ContentException ex)
{
    Log.Fatal("{Message}", ex.Message);
    return 2;
}
catch (Exception ex) when (ex is NpgsqlException || ex.InnerException is NpgsqlException || ex is System.Net.Sockets.SocketException)
{
    Log.Fatal("Could not connect to PostgreSQL ({Error}). Start it with 'docker compose -f deploy/docker-compose.yml up -d', or run with '--db memory'.", ex.Message);
    return 3;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Server crashed");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

internal static class WinMM
{
    [DllImport("winmm.dll")]
    internal static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    internal static extern uint timeEndPeriod(uint period);
}
