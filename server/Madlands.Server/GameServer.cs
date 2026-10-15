using System.Diagnostics;
using System.Net;
using System.Text;
using LiteNetLib;
using Madlands.Content;
using Madlands.Server.Diagnostics;
using Madlands.Server.Net;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;
using Serilog;

namespace Madlands.Server;

/// <summary>
/// Network front end + fixed-rate game loop. Everything runs on the loop thread: LiteNetLib
/// events are dispatched from <see cref="NetManager.PollEvents"/> at the start of each tick.
/// </summary>
public sealed class GameServer : IDisposable
{
    public const int MaxViolations = 50;
    public const long HelloTimeoutMs = 5000;

    private static readonly ILogger Log = Serilog.Log.ForContext<GameServer>();

    private readonly ServerOptions _options;
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;
    private readonly Dictionary<int, ClientSession> _sessions = new();
    private readonly List<ClientSession> _sessionScratch = new();
    private readonly PacketWriter _writer = new(512);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _random = new();

    private readonly Hello _hello = new();
    private readonly MoveUpdate _moveUpdate = new();
    private readonly Ping _ping = new();
    private readonly Pong _pong = new();
    private readonly Welcome _welcome = new();
    private readonly Rejected _rejected = new();

    private long _tick;

    public GameServer(ServerOptions options, ContentDatabase content)
    {
        _options = options;
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

    public void Start()
    {
        var v4 = _options.Bind is null ? IPAddress.Any : IPAddress.Parse(_options.Bind);
        var v6 = IPAddress.IsLoopback(v4) ? IPAddress.IPv6Loopback : IPAddress.IPv6Any;
        if (!_net.Start(v4, v6, _options.Port)) throw new InvalidOperationException($"Could not listen on UDP {v4}:{_options.Port}.");
        Log.Information("Listening on UDP {Address}:{Port} (protocol v{Protocol}, tick {Rate} Hz)", v4, Port, ProtocolVersion.Current, GameConstants.ServerTickRate);
    }

    /// <summary>Runs one simulation step. Public so tests can drive the server deterministically.</summary>
    public void Tick()
    {
        long start = Stopwatch.GetTimestamp();
        long now = NowMs;
        _net.PollEvents();
        DropIdleHandshakes(now);
        World.Tick(now, _tick);
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
            "Tick avg {Avg:0.00} ms, p99 {P99:0.00} ms, max {Max:0.00} ms | sessions {Sessions}, players {Players} | out {Out:0.0} KB/s, in {In:0.0} KB/s | corrections {Corrections}",
            s.AverageMs, s.P99Ms, s.MaxMs, _sessions.Count, World.PlayerCount,
            net.BytesSent / 1024.0 / seconds, net.BytesReceived / 1024.0 / seconds,
            World.Instances.Sum(i => i.CorrectionsSent));
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
        if (session.Player is { } player)
        {
            player.Map?.RemoveEntity(player);
            Log.Information("{Player} left ({Reason})", player, info.Reason);
        }
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
        if (_sessions.Remove(session.Id) && session.Player is { } player) player.Map?.RemoveEntity(player);
        _net.DisconnectPeer(session.Peer, _writer.ToArray());
    }

    private void AddViolation(ClientSession session, string what)
    {
        session.Violations++;
        Log.Debug("Violation by {Session}: {What}", session, what);
        if (session.Violations > MaxViolations) Kick(session, RejectReason.Kicked, "Too many protocol violations.");
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
                    HandleHello(session, now);
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

    private void HandleHello(ClientSession session, long now)
    {
        if (_hello.ProtocolVersion != ProtocolVersion.Current)
        {
            Kick(session, RejectReason.ProtocolMismatch,
                $"Server runs protocol v{ProtocolVersion.Current}, your client v{_hello.ProtocolVersion}. Please update.");
            return;
        }

        var name = SanitizeName(_hello.Name);
        if (name is null)
        {
            Kick(session, RejectReason.InvalidName, "Name must be 2-24 characters: letters, digits, space, '-' or '_'.");
            return;
        }

        var map = World.GetInstance(_options.StartMapId, WorldManager.OpenWorldInstance)!;
        var spawn = map.Map.SpawnPosition;
        // Spread spawns a little so players are not stacked inside each other.
        float angle = (float)(_random.NextDouble() * MathUtil.TwoPi);
        float radius = (float)_random.NextDouble() * 3f;
        float x = spawn.X + MathF.Sin(angle) * radius;
        float z = spawn.Z + MathF.Cos(angle) * radius;
        var spawnState = MovementState.Idle(new Vec3(x, map.HeightMap.GetHeight(x, z), z), map.Map.SpawnYaw);

        var player = new Player(World.NextPlayerGuid(), name, session, new MovementValidator(map.HeightMap))
        {
            Movement = spawnState,
        };
        session.Player = player;
        session.State = SessionState.InWorld;

        _welcome.PlayerGuid = player.Guid;
        _welcome.ServerTimeMs = now;
        _welcome.MapId = map.MapId;
        _welcome.InstanceId = map.InstanceId;
        _welcome.Spawn = spawnState;
        session.Send(_writer, _welcome, DeliveryMethod.ReliableOrdered);

        map.AddPlayer(player, now);
        Log.Information("{Player} entered map {MapId} from {Address}", player, map.MapId, session.Peer);
    }

    internal static string? SanitizeName(string? raw)
    {
        if (raw is null) return null;
        var name = raw.Trim();
        if (name.Length < 2 || name.Length > 24) return null;
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_') sb.Append(c);
            else return null;
        }
        return sb.ToString();
    }
}
