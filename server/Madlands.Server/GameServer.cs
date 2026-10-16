using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using LiteNetLib;
using Madlands.Content;
using Madlands.Server.Auth;
using Madlands.Server.Core;
using Madlands.Server.Diagnostics;
using Madlands.Server.Net;
using Madlands.Server.Persistence;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;
using Serilog;

namespace Madlands.Server;

/// <summary>
/// Network front end + fixed-rate game loop. Everything runs on the loop thread: LiteNetLib
/// events are dispatched from <see cref="NetManager.PollEvents"/> and results of background work
/// (database lookups) from the <see cref="MainThreadQueue"/>, both at the start of each tick.
/// </summary>
public sealed class GameServer : IDisposable
{
    public const int MaxViolations = 50;
    public const long HelloTimeoutMs = 5000;
    public const long SaveIntervalMs = 60_000;

    private static readonly ILogger Log = Serilog.Log.ForContext<GameServer>();

    private readonly ServerOptions _options;
    private readonly IGameDatabase _database;
    private readonly CharacterSaver _saver;
    private readonly MainThreadQueue _mainThread = new();
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;
    private readonly Dictionary<int, ClientSession> _sessions = new();
    private readonly List<ClientSession> _sessionScratch = new();
    /// <summary>characterId -> session, readable from account API threads.</summary>
    private readonly ConcurrentDictionary<long, ClientSession> _onlineCharacters = new();
    private readonly PacketWriter _writer = new(512);
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private readonly Hello _hello = new();
    private readonly MoveUpdate _moveUpdate = new();
    private readonly Ping _ping = new();
    private readonly Pong _pong = new();
    private readonly Welcome _welcome = new();
    private readonly Rejected _rejected = new();

    private long _tick;

    public GameServer(ServerOptions options, ContentDatabase content, IGameDatabase database, CharacterSaver saver)
    {
        _options = options;
        _database = database;
        _saver = saver;
        World = new WorldManager(content);
        if (World.GetInstance(options.StartMapId, WorldManager.OpenWorldInstance) is null)
            throw new ArgumentException($"Start map {options.StartMapId} does not exist in content.");

        _net = new NetManager(_listener)
        {
            AutoRecycle = true,
            EnableStatistics = true,
            DisconnectTimeout = 10000,
        };
        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
    }

    public WorldManager World { get; }
    public TickStats Stats { get; } = new();
    public int Port => _net.LocalPort;
    public long NowMs => _clock.ElapsedMilliseconds;
    public int SessionCount => _sessions.Count;

    /// <summary>Thread-safe; used by the account API to refuse deleting a character that is in the world.</summary>
    public bool IsCharacterOnline(long characterId) => _onlineCharacters.ContainsKey(characterId);

    public void Start()
    {
        var v4 = _options.Bind is null ? IPAddress.Any : IPAddress.Parse(_options.Bind);
        var v6 = IPAddress.IsLoopback(v4) ? IPAddress.IPv6Loopback : IPAddress.IPv6Any;
        if (!_net.Start(v4, v6, _options.Port)) throw new InvalidOperationException($"Could not listen on UDP {v4}:{_options.Port}.");
        Log.Information("Game server on UDP {Address}:{Port} (protocol v{Protocol}, tick {Rate} Hz)", v4, Port, ProtocolVersion.Current, GameConstants.ServerTickRate);
    }

    /// <summary>Runs one simulation step. Public so tests can drive the server deterministically.</summary>
    public void Tick()
    {
        long start = Stopwatch.GetTimestamp();
        long now = NowMs;
        _mainThread.Drain();
        _net.PollEvents();
        DropIdleHandshakes(now);
        World.Tick(now, _tick);
        if (_tick % GameConstants.ServerTickRate == 0) SaveDuePlayers(now);
        _tick++;
        Stats.RecordTick(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>Fixed-timestep loop until cancelled.</summary>
    public void Run(CancellationToken cancellation)
    {
        long next = NowMs;
        long nextReport = next + _options.StatsIntervalMs;
        while (!cancellation.IsCancellationRequested)
        {
            Tick();
            next += GameConstants.ServerTickMs;

            long now = NowMs;
            if (now - next > GameConstants.ServerTickMs * 5)
            {
                Log.Warning("Server is {Behind} ms behind schedule; skipping ticks", now - next);
                next = now;
            }
            if (now >= nextReport)
            {
                ReportStats();
                nextReport = now + _options.StatsIntervalMs;
            }

            while (true)
            {
                long remaining = next - NowMs;
                if (remaining <= 0) break;
                if (remaining > 1) Thread.Sleep(1);
                else Thread.Yield();
            }
        }
    }

    /// <summary>Queues a save of every player in the world (shutdown). Call on the loop thread.</summary>
    public void SaveAllOnline()
    {
        foreach (var session in _sessions.Values)
            if (session.Player is { } player) _saver.Enqueue(Snapshot(player));
    }

    public void Dispose()
    {
        _net.Stop();
    }

    private void ReportStats()
    {
        var s = Stats.TakeSnapshot();
        var net = _net.Statistics;
        double seconds = _options.StatsIntervalMs / 1000.0;
        Log.Information(
            "Tick avg {Avg:0.00} ms, p99 {P99:0.00} ms, max {Max:0.00} ms | sessions {Sessions}, players {Players} | out {Out:0.0} KB/s, in {In:0.0} KB/s | corrections {Corrections} | saves {Saves} ({Failed} failed)",
            s.AverageMs, s.P99Ms, s.MaxMs, _sessions.Count, World.PlayerCount,
            net.BytesSent / 1024.0 / seconds, net.BytesReceived / 1024.0 / seconds,
            World.Instances.Sum(i => i.CorrectionsSent), _saver.SavedCount, _saver.FailedCount);
        net.Reset();
    }

    // ------------------------------------------------------------------ connection lifecycle

    private void OnConnectionRequest(ConnectionRequest request)
    {
        if (_sessions.Count >= _options.MaxConnections)
        {
            request.Reject();
            return;
        }
        request.AcceptIfKey(ProtocolVersion.ConnectionKey);
    }

    private void OnPeerConnected(NetPeer peer)
    {
        _sessions[peer.Id] = new ClientSession(peer, NowMs);
        Log.Debug("Peer {Peer} connected", peer);
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (!_sessions.Remove(peer.Id, out var session)) return;
        if (session.Player is { } player) Log.Information("{Player} left ({Reason})", player, info.Reason);
        LeaveWorld(session);
    }

    private void DropIdleHandshakes(long now)
    {
        _sessionScratch.Clear();
        foreach (var s in _sessions.Values)
            if (s.State == SessionState.AwaitingHello && now - s.ConnectedAtMs > HelloTimeoutMs) _sessionScratch.Add(s);
        foreach (var s in _sessionScratch) Kick(s, RejectReason.Kicked, "Handshake timeout.");
    }

    /// <summary>
    /// The rejection is sent as LiteNetLib disconnect data so it is guaranteed to arrive together
    /// with the disconnect (a separate reliable message could be lost when the peer is dropped).
    /// </summary>
    private void Kick(ClientSession session, RejectReason reason, string message)
    {
        Log.Information("Disconnecting {Session}: {Reason} {Message}", session, reason, message);
        _rejected.Reason = reason;
        _rejected.Message = message;
        Packet.Encode(_writer, _rejected);
        // Remove right away so nothing more from this peer is processed, even within this tick.
        if (_sessions.Remove(session.Id)) LeaveWorld(session);
        _net.DisconnectPeer(session.Peer, _writer.ToArray());
    }

    /// <summary>Saves and removes the session's player, if it has one.</summary>
    private void LeaveWorld(ClientSession session)
    {
        if (session.Player is not { } player) return;
        _saver.Enqueue(Snapshot(player));
        player.Map?.RemoveEntity(player);
        // Only unregister if this session still owns the character (a re-login may have taken over).
        _onlineCharacters.TryRemove(new KeyValuePair<long, ClientSession>(player.CharacterId, session));
        session.Player = null;
    }

    private void AddViolation(ClientSession session, string what)
    {
        session.Violations++;
        Log.Debug("Violation by {Session}: {What}", session, what);
        if (session.Violations > MaxViolations) Kick(session, RejectReason.Kicked, "Too many protocol violations.");
    }

    // ------------------------------------------------------------------ persistence

    private void SaveDuePlayers(long now)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.Player is not { } player || now < player.NextSaveAtMs) continue;
            _saver.Enqueue(Snapshot(player));
            player.NextSaveAtMs = now + SaveIntervalMs;
        }
    }

    private static CharacterSnapshot Snapshot(Player player)
    {
        var p = player.Movement.Position;
        return new CharacterSnapshot(player.CharacterId, player.Map?.MapId ?? 0, p.X, p.Y, p.Z, player.Movement.Yaw, player.Level);
    }

    // ------------------------------------------------------------------ messages

    private void OnNetworkReceive(NetPeer peer, NetPacketReader packet, byte channel, DeliveryMethod method)
    {
        if (!_sessions.TryGetValue(peer.Id, out var session)) return;
        long now = NowMs;
        try
        {
            var reader = new PacketReader(packet.GetRemainingBytesSpan());
            var opcode = Packet.ReadOpcode(ref reader);
            switch (opcode)
            {
                case Opcode.Hello when session.State == SessionState.AwaitingHello:
                    _hello.Read(ref reader);
                    HandleHello(session);
                    break;

                case Opcode.MoveUpdate when session.State == SessionState.InWorld:
                    if (!session.MoveLimiter.TryConsume(now))
                    {
                        AddViolation(session, "move rate");
                        break;
                    }
                    _moveUpdate.Read(ref reader);
                    var player = session.Player!;
                    player.Map?.HandleMove(player, _moveUpdate, now);
                    break;

                case Opcode.Ping:
                    if (!session.PingLimiter.TryConsume(now))
                    {
                        AddViolation(session, "ping rate");
                        break;
                    }
                    _ping.Read(ref reader);
                    _pong.ClientTimeMs = _ping.ClientTimeMs;
                    _pong.ServerTimeMs = now;
                    session.Send(_writer, _pong, DeliveryMethod.Unreliable);
                    break;

                default:
                    AddViolation(session, $"unexpected {opcode} in state {session.State}");
                    break;
            }
        }
        catch (PacketException ex)
        {
            Kick(session, RejectReason.Kicked, "Malformed packet: " + ex.Message);
        }
    }

    private void HandleHello(ClientSession session)
    {
        if (_hello.ProtocolVersion != ProtocolVersion.Current)
        {
            Kick(session, RejectReason.ProtocolMismatch,
                $"Server runs protocol v{ProtocolVersion.Current}, your client v{_hello.ProtocolVersion}. Please update.");
            return;
        }
        if (string.IsNullOrEmpty(_hello.SessionToken) || _hello.CharacterId == 0 || _hello.CharacterId > long.MaxValue)
        {
            Kick(session, RejectReason.InvalidSession, "Missing session. Please log in again.");
            return;
        }

        // Token and character are checked against the database off the game thread; the result
        // comes back through the main-thread queue.
        session.State = SessionState.Authenticating;
        var tokenHash = SessionTokens.Hash(_hello.SessionToken);
        long characterId = (long)_hello.CharacterId;
        _ = Task.Run(async () =>
        {
            LoginResult result;
            try
            {
                var accountId = await _database.FindSessionAccountAsync(tokenHash, DateTime.UtcNow);
                if (accountId is null)
                {
                    result = LoginResult.Fail(RejectReason.InvalidSession, "Your session has expired. Please log in again.");
                }
                else
                {
                    var character = await _database.LoadCharacterAsync(accountId.Value, characterId);
                    result = character is null
                        ? LoginResult.Fail(RejectReason.CharacterNotFound, "That character does not exist.")
                        : new LoginResult(accountId.Value, character, RejectReason.Unknown, "");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Loading character {CharacterId} failed", characterId);
                result = LoginResult.Fail(RejectReason.ServerError, "The server could not load your character. Try again.");
            }
            _mainThread.Enqueue(() => CompleteLogin(session, result));
        });
    }

    private sealed record LoginResult(long AccountId, CharacterRecord? Character, RejectReason Reason, string Message)
    {
        public static LoginResult Fail(RejectReason reason, string message) => new(0, null, reason, message);
    }

    private void CompleteLogin(ClientSession session, LoginResult result)
    {
        // The client may have disconnected while we were loading.
        if (!_sessions.TryGetValue(session.Id, out var current) || current != session) return;
        if (result.Character is not { } character)
        {
            Kick(session, result.Reason, result.Message);
            return;
        }

        // One character per account in the world: a new login wins and the old connection is dropped.
        _sessionScratch.Clear();
        foreach (var other in _sessions.Values)
            if (other != session && other.Player is not null && other.AccountId == result.AccountId) _sessionScratch.Add(other);
        foreach (var other in _sessionScratch) Kick(other, RejectReason.LoggedInElsewhere, "You logged in from another location.");

        var map = World.GetInstance(character.MapId, WorldManager.OpenWorldInstance)
                  ?? World.GetInstance(_options.StartMapId, WorldManager.OpenWorldInstance)!;
        var spawn = SpawnStateFor(map, character);

        long now = NowMs;
        var player = new Player(character.Id, character.Name, session, new MovementValidator(map.HeightMap))
        {
            Movement = spawn,
            Level = character.Level,
            NextSaveAtMs = now + SaveIntervalMs,
        };
        session.AccountId = result.AccountId;
        session.Player = player;
        session.State = SessionState.InWorld;
        _onlineCharacters[character.Id] = session;

        _welcome.PlayerGuid = player.Guid;
        _welcome.ServerTimeMs = now;
        _welcome.MapId = map.MapId;
        _welcome.InstanceId = map.InstanceId;
        _welcome.Spawn = spawn;
        session.Send(_writer, _welcome, DeliveryMethod.ReliableOrdered);

        map.AddPlayer(player, now);
        Log.Information("{Player} entered map {MapId} from {Address}", player, map.MapId, session.Peer);
    }

    /// <summary>The saved position if it is still valid on this map, otherwise the map's spawn point.</summary>
    private static MovementState SpawnStateFor(MapInstance map, CharacterRecord c)
    {
        var heightMap = map.HeightMap;
        bool valid = c.MapId == map.MapId && float.IsFinite(c.X) && float.IsFinite(c.Y) && float.IsFinite(c.Z) && float.IsFinite(c.Yaw)
                     && heightMap.Contains(c.X, c.Z);
        if (!valid) return MovementState.Idle(map.Map.SpawnPosition, map.Map.SpawnYaw);

        float ground = heightMap.GetHeight(c.X, c.Z);
        float y = c.Y < ground || c.Y > ground + MovementValidator.MaxAboveGround ? ground : c.Y;
        return MovementState.Idle(new Vec3(c.X, y, c.Z), c.Yaw);
    }
}
