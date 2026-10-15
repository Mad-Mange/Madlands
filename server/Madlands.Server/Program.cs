using System.Runtime.InteropServices;
using Madlands.Content;
using Madlands.Server;
using Serilog;
using Serilog.Events;

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
    Log.Information("Loaded {Maps} map(s)", content.Maps.Count);

    using var server = new GameServer(options, content);
    server.Start();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    // Windows sleeps in ~15.6 ms steps by default, far too coarse for a 50 ms tick.
    if (OperatingSystem.IsWindows()) WinMM.timeBeginPeriod(1);
    try
    {
        server.Run(cts.Token);
    }
    finally
    {
        if (OperatingSystem.IsWindows()) WinMM.timeEndPeriod(1);
    }

    Log.Information("Server stopped");
    return 0;
}
catch (ContentException ex)
{
    Log.Fatal("{Message}", ex.Message);
    return 2;
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
