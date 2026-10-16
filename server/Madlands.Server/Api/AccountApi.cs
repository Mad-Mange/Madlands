using Madlands.Content;
using Madlands.Server.Auth;
using Madlands.Server.Net;
using Madlands.Server.Persistence;
using Madlands.Shared;
using Madlands.Shared.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace Madlands.Server.Api;

/// <summary>
/// HTTP account API: register, login, character list/create/delete, logout. Bodies are Shared
/// messages (opcode + binary payload), answered with the expected response type or an
/// <see cref="ApiResult"/> on error. Runs on Kestrel threads and only touches the database and
/// immutable content, never game state (except the thread-safe "is this character online" check).
/// In production this sits behind a TLS-terminating reverse proxy (ADR 0007).
/// </summary>
public sealed class AccountApi
{
    public const int MaxCharactersPerAccount = 10;
    private const int MaxRequestBytes = 4096;
    private const string BinaryContentType = "application/octet-stream";

    private static readonly ILogger Log = Serilog.Log.ForContext<AccountApi>();

    private readonly ServerOptions _options;
    private readonly ContentDatabase _content;
    private readonly IGameDatabase _db;
    private readonly PasswordHasher _hasher;
    private readonly GameServer _game;
    private readonly KeyedRateLimiter _perIp;
    private readonly KeyedRateLimiter _failedLogins = new(perMinute: 5, burst: 5);

    public AccountApi(ServerOptions options, ContentDatabase content, IGameDatabase db, PasswordHasher hasher, GameServer game)
    {
        _options = options;
        _content = content;
        _db = db;
        _hasher = hasher;
        _game = game;
        _perIp = new KeyedRateLimiter(options.ApiRateLimitPerMinute, burst: Math.Max(10, options.ApiRateLimitPerMinute / 4));
    }

    /// <summary>Per-request context handed to every handler. AccountId is 0 for unauthenticated endpoints.</summary>
    private readonly record struct ApiCall(HttpContext Http, long AccountId)
    {
        public CancellationToken Cancellation => Http.RequestAborted;
    }

    public void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapPost("/register", ctx => Handle<RegisterRequest>(ctx, requiresAuth: false, RegisterAsync));
        api.MapPost("/login", ctx => Handle<LoginRequest>(ctx, requiresAuth: false, LoginAsync));
        api.MapPost("/characters/list", ctx => Handle<CharacterListRequest>(ctx, requiresAuth: true, ListAsync));
        api.MapPost("/characters/create", ctx => Handle<CreateCharacterRequest>(ctx, requiresAuth: true, CreateAsync));
        api.MapPost("/characters/delete", ctx => Handle<DeleteCharacterRequest>(ctx, requiresAuth: true, DeleteAsync));
        api.MapPost("/logout", ctx => Handle<LogoutRequest>(ctx, requiresAuth: true, LogoutAsync));
    }

    // ------------------------------------------------------------------ handlers

    private async Task<ApiResponse> RegisterAsync(RegisterRequest req, ApiCall call)
    {
        var ct = call.Cancellation;
        var username = NameRules.NormalizeUsername(req.Username);
        if (username is null)
            return Error(ApiResultCode.InvalidUsername, $"Account names are {NameRules.UsernameMin}-{NameRules.UsernameMax} characters: a-z, 0-9, '_', '.', '-'.");
        if (!NameRules.IsValidPassword(req.Password))
            return Error(ApiResultCode.InvalidPassword, $"Passwords must be {NameRules.PasswordMin}-{NameRules.PasswordMax} characters.");

        var hash = await _hasher.HashAsync(req.Password);
        var accountId = await _db.CreateAccountAsync(username, hash, ct);
        if (accountId is null) return Error(ApiResultCode.UsernameTaken, "That account name is already taken.");

        Log.Information("Account {Username} ({AccountId}) registered", username, accountId);
        return await StartSessionAsync(accountId.Value, ct);
    }

    private async Task<ApiResponse> LoginAsync(LoginRequest req, ApiCall call)
    {
        var ct = call.Cancellation;
        var username = NameRules.NormalizeUsername(req.Username);
        if (username is null || req.Password is null) return Error(ApiResultCode.InvalidCredentials, "Wrong account name or password.");
        if (!_failedLogins.CanProceed(username)) return Error(ApiResultCode.RateLimited, "Too many failed attempts. Wait a minute and try again.");

        var account = await _db.FindAccountAsync(username, ct);
        bool valid = await _hasher.VerifyAsync(req.Password, account?.PasswordHash);
        if (!valid || account is null)
        {
            _failedLogins.TryConsume(username);
            return Error(ApiResultCode.InvalidCredentials, "Wrong account name or password.");
        }

        await _db.TouchLoginAsync(account.Id, ct);
        return await StartSessionAsync(account.Id, ct);
    }

    private async Task<ApiResponse> StartSessionAsync(long accountId, CancellationToken ct)
    {
        var token = SessionTokens.Create();
        await _db.CreateSessionAsync(SessionTokens.Hash(token), accountId, DateTime.UtcNow + SessionTokens.Lifetime, ct);
        return new AuthResponse
        {
            Result = ApiResultCode.Ok,
            Message = "",
            SessionToken = token,
            RealmName = _options.RealmName,
            RealmHost = _options.PublicHost ?? _options.Bind ?? "127.0.0.1",
            RealmPort = (ushort)_game.Port,
        };
    }

    private async Task<ApiResponse> ListAsync(CharacterListRequest req, ApiCall call)
    {
        var response = new CharacterListResponse { Result = ApiResultCode.Ok, Message = "", MaxCharacters = MaxCharactersPerAccount };
        foreach (var c in await _db.ListCharactersAsync(call.AccountId, call.Cancellation)) response.Characters.Add(ToInfo(c));
        foreach (var r in _content.Races.Values.OrderBy(r => r.Id)) response.Races.Add(new NamedOption((byte)r.Id, r.Name));
        foreach (var c in _content.Classes.Values.OrderBy(c => c.Id)) response.Classes.Add(new NamedOption((byte)c.Id, c.Name));
        return response;
    }

    private async Task<ApiResponse> CreateAsync(CreateCharacterRequest req, ApiCall call)
    {
        long accountId = call.AccountId;
        var name = NameRules.NormalizeCharacterName(req.Name);
        if (name is null)
            return Error(ApiResultCode.InvalidName, $"Names are {NameRules.CharacterNameMin}-{NameRules.CharacterNameMax} letters, no spaces or digits.");
        if (!_content.Races.TryGetValue(req.Race, out var race) || !_content.Classes.ContainsKey(req.Class))
            return Error(ApiResultCode.InvalidRequest, "Unknown race or class.");
        if (!_content.Maps.TryGetValue((ushort)race.StartMap, out var map))
            return Error(ApiResultCode.ServerError, "The starting area is not available.");

        var spawn = map.SpawnPosition;
        var template = new CharacterRecord(0, accountId, name, req.Race, req.Class, 1, map.Id, spawn.X, spawn.Y, spawn.Z, map.SpawnYaw);
        var (outcome, created) = await _db.CreateCharacterAsync(template, MaxCharactersPerAccount, call.Cancellation);
        return outcome switch
        {
            CreateCharacterOutcome.Created => new CreateCharacterResponse { Result = ApiResultCode.Ok, Message = "", Character = ToInfo(created!) },
            CreateCharacterOutcome.NameTaken => Error(ApiResultCode.NameTaken, "That name is already taken."),
            _ => Error(ApiResultCode.TooManyCharacters, $"You can have at most {MaxCharactersPerAccount} characters."),
        };
    }

    private async Task<ApiResponse> DeleteAsync(DeleteCharacterRequest req, ApiCall call)
    {
        if (req.CharacterId > long.MaxValue) return Error(ApiResultCode.NotFound, "Character not found.");
        if (_game.IsCharacterOnline((long)req.CharacterId)) return Error(ApiResultCode.CharacterOnline, "Log that character out first.");
        return await _db.DeleteCharacterAsync(call.AccountId, (long)req.CharacterId, call.Cancellation)
            ? Error(ApiResultCode.Ok, "")
            : Error(ApiResultCode.NotFound, "Character not found.");
    }

    private async Task<ApiResponse> LogoutAsync(LogoutRequest req, ApiCall call)
    {
        if (BearerToken(call.Http) is { } token) await _db.DeleteSessionAsync(SessionTokens.Hash(token), call.Cancellation);
        return Error(ApiResultCode.Ok, "");
    }

    // ------------------------------------------------------------------ plumbing

    private async Task Handle<TRequest>(HttpContext ctx, bool requiresAuth, Func<TRequest, ApiCall, Task<ApiResponse>> handler)
        where TRequest : IMessage, new()
    {
        ApiResponse response;
        try
        {
            response = await Dispatch(ctx, requiresAuth, handler);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Account API {Path} failed", ctx.Request.Path);
            response = Error(ApiResultCode.ServerError, "Internal server error. Try again later.");
        }

        var writer = new PacketWriter(256);
        Packet.Encode(writer, response);
        ctx.Response.ContentType = BinaryContentType;
        await ctx.Response.Body.WriteAsync(writer.Buffer.AsMemory(0, writer.Length));
    }

    private async Task<ApiResponse> Dispatch<TRequest>(HttpContext ctx, bool requiresAuth, Func<TRequest, ApiCall, Task<ApiResponse>> handler)
        where TRequest : IMessage, new()
    {
        string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_perIp.TryConsume(ip)) return Error(ApiResultCode.RateLimited, "Too many requests. Slow down.");

        if (ctx.Request.Headers[ProtocolVersion.HttpHeader] != ProtocolVersion.Current.ToString())
            return Error(ApiResultCode.ProtocolMismatch, $"The server runs protocol v{ProtocolVersion.Current}. Please update your client.");

        var body = await ReadBodyAsync(ctx.Request);
        var request = new TRequest();
        if (body is null || !TryDecode(body, request)) return Error(ApiResultCode.InvalidRequest, "Malformed request.");

        long accountId = 0;
        if (requiresAuth)
        {
            var token = BearerToken(ctx);
            var account = token is null ? null : await _db.FindSessionAccountAsync(SessionTokens.Hash(token), DateTime.UtcNow, ctx.RequestAborted);
            if (account is null) return Error(ApiResultCode.Unauthorized, "Your session has expired. Please log in again.");
            accountId = account.Value;
        }

        return await handler(request, new ApiCall(ctx, accountId));
    }

    private static bool TryDecode<T>(byte[] body, T message) where T : IMessage
    {
        try
        {
            var reader = new PacketReader(body);
            if (Packet.ReadOpcode(ref reader) != message.Opcode) return false;
            message.Read(ref reader);
            return reader.Remaining == 0;
        }
        catch (PacketException)
        {
            return false;
        }
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is > MaxRequestBytes) return null;
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, request.HttpContext.RequestAborted)) > 0)
        {
            if (buffer.Length + read > MaxRequestBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static string? BearerToken(HttpContext ctx)
    {
        string? header = ctx.Request.Headers.Authorization;
        const string prefix = "Bearer ";
        return header is not null && header.StartsWith(prefix, StringComparison.Ordinal) && header.Length > prefix.Length
            ? header[prefix.Length..].Trim()
            : null;
    }

    private static ApiResult Error(ApiResultCode code, string message) => new() { Result = code, Message = message };

    private static CharacterSummary ToInfo(CharacterRecord c) => new()
    {
        Id = (ulong)c.Id,
        Name = c.Name,
        Race = c.Race,
        Class = c.Class,
        Level = c.Level,
        MapId = c.MapId,
    };
}
