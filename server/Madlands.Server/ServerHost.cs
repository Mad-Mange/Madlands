using System.Net;
using Madlands.Content;
using Madlands.Server.Api;
using Madlands.Server.Auth;
using Madlands.Server.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Madlands.Server;

/// <summary>
/// Composes one realm process: the UDP game server, the HTTP account API and the write-behind
/// saver around one database. Production (Program.cs) and the integration tests both use this,
/// so tests exercise exactly the production wiring.
/// </summary>
public sealed class ServerHost : IAsyncDisposable
{
    /// <summary>How often expired login sessions are deleted.</summary>
    public static readonly TimeSpan SessionCleanupInterval = TimeSpan.FromHours(1);

    private readonly ServerOptions _options;
    private readonly AccountApi _api;
    private readonly CancellationTokenSource _cleanupStop = new();
    private WebApplication? _web;
    private Task? _cleanup;
    private bool _stopped;

    public ServerHost(ServerOptions options, ContentDatabase content, IGameDatabase database, PasswordHasher? hasher = null)
    {
        _options = options;
        Database = database;
        Saver = new CharacterSaver(database);
        Game = new GameServer(options, content, database, Saver);
        _api = new AccountApi(options, content, database, hasher ?? new PasswordHasher(), Game);
    }

    public GameServer Game { get; }
    public CharacterSaver Saver { get; }
    public IGameDatabase Database { get; }
    /// <summary>e.g. http://127.0.0.1:7780 (actual port when HttpPort is 0).</summary>
    public string HttpBaseUrl { get; private set; } = "";

    public async Task StartAsync()
    {
        Game.Start();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(kestrel =>
        {
            if (_options.ApiAddress is not { } address) kestrel.ListenAnyIP(_options.HttpPort);
            else kestrel.Listen(IPAddress.Parse(address), _options.HttpPort);
        });
        _web = builder.Build();
        // Behind a reverse proxy on this machine every request comes from loopback: take the client's
        // address from X-Forwarded-For (only loopback proxies are trusted), so per-IP limits still work.
        if (_options.TrustProxy)
            _web.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
        _api.Map(_web);
        await _web.StartAsync();
        HttpBaseUrl = _web.Urls.First();
        Log.Information("Account API on {Url} (rate limit {Limit}/min per IP{Proxy})", HttpBaseUrl,
            _options.ApiRateLimitPerMinute > 0 ? _options.ApiRateLimitPerMinute : "off", _options.TrustProxy ? ", behind a proxy" : "");
        _cleanup = Task.Run(() => CleanUpSessionsAsync(_cleanupStop.Token));
    }

    /// <summary>Deletes expired login sessions now (at startup) and then every <see cref="SessionCleanupInterval"/>.</summary>
    private async Task CleanUpSessionsAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                int deleted = await Database.DeleteExpiredSessionsAsync(DateTime.UtcNow, stop);
                if (deleted > 0) Log.Information("Deleted {Count} expired login sessions", deleted);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Deleting expired sessions failed");
            }
            try
            {
                await Task.Delay(SessionCleanupInterval, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Saves everyone online, stops networking and waits until all saves are written.</summary>
    public async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;
        Game.SaveAllOnline();
        Game.Dispose();
        _cleanupStop.Cancel();
        if (_cleanup is not null) await _cleanup;
        await Saver.FlushAndStopAsync();
        if (_web is not null) await _web.StopAsync();
        Log.Information("Server stopped ({Saved} saves written, {Failed} failed)", Saver.SavedCount, Saver.FailedCount);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_web is not null) await _web.DisposeAsync();
    }
}
