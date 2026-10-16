using System.Net;
using Madlands.Content;
using Madlands.Server.Api;
using Madlands.Server.Auth;
using Madlands.Server.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
    private readonly ServerOptions _options;
    private readonly AccountApi _api;
    private WebApplication? _web;
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
            if (_options.Bind is null) kestrel.ListenAnyIP(_options.HttpPort);
            else kestrel.Listen(IPAddress.Parse(_options.Bind), _options.HttpPort);
        });
        _web = builder.Build();
        _api.Map(_web);
        await _web.StartAsync();
        HttpBaseUrl = _web.Urls.First();
        Log.Information("Account API on {Url} (rate limit {Limit}/min per IP)", HttpBaseUrl,
            _options.ApiRateLimitPerMinute > 0 ? _options.ApiRateLimitPerMinute : "off");
    }

    /// <summary>Saves everyone online, stops networking and waits until all saves are written.</summary>
    public async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;
        Game.SaveAllOnline();
        Game.Dispose();
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
