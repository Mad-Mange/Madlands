using System;
using System.Diagnostics;
using System.Net;
using LiteNetLib;
using Madlands.Shared;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;

namespace Madlands.ClientCore
{
    public enum ClientState
    {
        Disconnected,
        Connecting,
        Handshaking,
        InWorld,
    }

    /// <summary>
    /// Connection to a Madlands server. Single-threaded: call <see cref="Update"/> regularly
    /// (every frame); all events are raised from inside it. In manual mode no background threads
    /// are created, which lets one process run hundreds of bot clients.
    /// </summary>
    public sealed class GameClient : IDisposable
    {
        private const long PingIntervalMs = 1000;
        private const long FastPingIntervalMs = 200;
        private const int FastPingCount = 5;

        private readonly EventBasedNetListener _listener = new EventBasedNetListener();
        private readonly NetManager _net;
        private readonly bool _manualMode;
        private readonly Action<string> _log;
        private readonly PacketWriter _writer = new PacketWriter(256);
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        private readonly Hello _hello = new Hello();
        private readonly MoveUpdate _moveUpdate = new MoveUpdate();
        private readonly Ping _ping = new Ping();
        private readonly Welcome _welcome = new Welcome();
        private readonly Rejected _rejected = new Rejected();
        private readonly Pong _pong = new Pong();
        private readonly EntitySpawn _spawn = new EntitySpawn();
        private readonly EntityDespawn _despawn = new EntityDespawn();
        private readonly EntityMoves _moves = new EntityMoves();
        private readonly MoveCorrection _correction = new MoveCorrection();

        private NetPeer _peer;
        private string _sessionToken;
        private ulong _characterId;
        private long _lastUpdateMs;
        private long _nextPingAtMs;
        private int _pingsSent;
        private ushort _correctionAck;

        public GameClient(bool manualMode = false, Action<string> log = null)
        {
            _manualMode = manualMode;
            _log = log;
            _net = new NetManager(_listener)
            {
                AutoRecycle = true,
                DisconnectTimeout = 10000,
            };
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnNetworkReceive;
        }

        public ClientState State { get; private set; } = ClientState.Disconnected;
        public EntityGuid LocalGuid { get; private set; }
        public ushort MapId { get; private set; }
        public uint InstanceId { get; private set; }
        public ClientWorld World { get; } = new ClientWorld();
        public ClockSync Clock { get; } = new ClockSync();
        public string LastError { get; private set; }
        /// <summary>Why the server turned us away, if it did (e.g. InvalidSession = log in again).</summary>
        public RejectReason? LastRejectReason { get; private set; }
        public int RoundTripMs => _peer != null ? _peer.RoundTripTime : 0;
        public long LocalTimeMs => _stopwatch.ElapsedMilliseconds;
        public double ServerTimeMs => Clock.ServerTimeAt(LocalTimeMs);
        public long PacketsReceived { get; private set; }

        /// <summary>Raised once the server accepted us; argument is the spawn state.</summary>
        public event Action<MovementState> EnteredWorld;
        public event Action<RemoteEntity> EntitySpawned;
        public event Action<RemoteEntity> EntityDespawned;
        /// <summary>The server rejected our movement; the local player must snap to this state.</summary>
        public event Action<MovementState> Corrected;
        public event Action<string> Disconnected;

        /// <summary>Enters the world with a session token from <see cref="AccountClient"/> and a character id.</summary>
        public void Connect(string host, int port, string sessionToken, ulong characterId)
        {
            if (State != ClientState.Disconnected) throw new InvalidOperationException("Already connected or connecting.");
            if (!_net.IsRunning)
            {
                // Talking to a local server: bind to loopback only (no firewall prompt, not reachable from outside).
                bool local = host == "127.0.0.1" || host == "localhost" || host == "::1";
                var v4 = local ? IPAddress.Loopback : IPAddress.Any;
                var v6 = local ? IPAddress.IPv6Loopback : IPAddress.IPv6Any;
                bool started = _manualMode ? _net.StartInManualMode(v4, v6, 0) : _net.Start(v4, v6, 0);
                if (!started) throw new InvalidOperationException("Could not start the network socket.");
            }
            _sessionToken = sessionToken;
            _characterId = characterId;
            LastError = null;
            LastRejectReason = null;
            _correctionAck = 0;
            _pingsSent = 0;
            _lastUpdateMs = LocalTimeMs;
            State = ClientState.Connecting;
            _peer = _net.Connect(host, port, ProtocolVersion.ConnectionKey);
        }

        public void Update()
        {
            long now = LocalTimeMs;
            _net.PollEvents();

            if (State == ClientState.InWorld && now >= _nextPingAtMs)
            {
                _ping.ClientTimeMs = now;
                Send(_ping, DeliveryMethod.Unreliable);
                _pingsSent++;
                _nextPingAtMs = now + (_pingsSent < FastPingCount ? FastPingIntervalMs : PingIntervalMs);
            }

            if (_manualMode) _net.ManualUpdate(now - _lastUpdateMs);
            _lastUpdateMs = now;
        }

        public void SendMovement(in MovementState state)
        {
            if (State != ClientState.InWorld) return;
            _moveUpdate.CorrectionAck = _correctionAck;
            _moveUpdate.ClientTimeMs = LocalTimeMs;
            _moveUpdate.State = state;
            Send(_moveUpdate, DeliveryMethod.ReliableOrdered);
        }

        public void Disconnect()
        {
            if (_peer != null) _net.DisconnectPeer(_peer);
            if (_manualMode) _net.ManualUpdate(0);
        }

        public void Dispose()
        {
            _net.Stop();
        }

        private void Send<T>(T message, DeliveryMethod method) where T : IMessage
        {
            if (_peer == null) return;
            Packet.Encode(_writer, message);
            _peer.Send(_writer.AsSpan(), method);
        }

        private void OnPeerConnected(NetPeer peer)
        {
            _peer = peer; // the connected peer (carries live RTT stats), not the pending one from Connect()
            State = ClientState.Handshaking;
            _hello.ProtocolVersion = ProtocolVersion.Current;
            _hello.SessionToken = _sessionToken;
            _hello.CharacterId = _characterId;
            Send(_hello, DeliveryMethod.ReliableOrdered);
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            State = ClientState.Disconnected;
            _peer = null;
            World.Clear();

            // The server sends its Rejected message as disconnect data (see GameServer.Kick).
            if (info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
            {
                try
                {
                    var r = new PacketReader(info.AdditionalData.GetRemainingBytesSpan());
                    if (Packet.ReadOpcode(ref r) == Opcode.Rejected) HandleRejected(ref r);
                }
                catch (PacketException)
                {
                    // Not our format; fall back to the transport reason below.
                }
            }
            string reason = LastError ?? info.Reason.ToString();
            _log?.Invoke("Disconnected: " + reason);
            Disconnected?.Invoke(reason);
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader packet, byte channel, DeliveryMethod method)
        {
            PacketsReceived++;
            try
            {
                var r = new PacketReader(packet.GetRemainingBytesSpan());
                var opcode = Packet.ReadOpcode(ref r);
                switch (opcode)
                {
                    case Opcode.Welcome: HandleWelcome(ref r); break;
                    case Opcode.Rejected: HandleRejected(ref r); break;
                    case Opcode.Pong: HandlePong(ref r); break;
                    case Opcode.EntitySpawn: HandleSpawn(ref r); break;
                    case Opcode.EntityDespawn: HandleDespawn(ref r); break;
                    case Opcode.EntityMoves: HandleMoves(ref r); break;
                    case Opcode.MoveCorrection: HandleCorrection(ref r); break;
                    default: _log?.Invoke("Unknown opcode from server: " + opcode); break;
                }
            }
            catch (PacketException ex)
            {
                _log?.Invoke("Malformed packet from server: " + ex.Message);
            }
        }

        private void HandleWelcome(ref PacketReader r)
        {
            _welcome.Read(ref r);
            LocalGuid = _welcome.PlayerGuid;
            MapId = _welcome.MapId;
            InstanceId = _welcome.InstanceId;
            Clock.Seed(_welcome.ServerTimeMs, LocalTimeMs);
            State = ClientState.InWorld;
            _nextPingAtMs = LocalTimeMs;
            EnteredWorld?.Invoke(_welcome.Spawn);
        }

        private void HandleRejected(ref PacketReader r)
        {
            _rejected.Read(ref r);
            LastRejectReason = _rejected.Reason;
            LastError = _rejected.Reason + ": " + _rejected.Message;
            _log?.Invoke("Rejected by server: " + LastError);
        }

        private void HandlePong(ref PacketReader r)
        {
            _pong.Read(ref r);
            Clock.AddSample(_pong.ClientTimeMs, LocalTimeMs, _pong.ServerTimeMs);
        }

        private void HandleSpawn(ref PacketReader r)
        {
            _spawn.Read(ref r);
            if (_spawn.Guid == LocalGuid) return;
            var entity = World.Add(_spawn.Guid, _spawn.Name, _spawn.DisplayId, _spawn.StateServerTimeMs, _spawn.State);
            EntitySpawned?.Invoke(entity);
        }

        private void HandleDespawn(ref PacketReader r)
        {
            _despawn.Read(ref r);
            var entity = World.Remove(_despawn.Guid);
            if (entity != null) EntityDespawned?.Invoke(entity);
        }

        private void HandleMoves(ref PacketReader r)
        {
            _moves.Read(ref r);
            for (int i = 0; i < _moves.Entries.Count; i++)
            {
                var e = _moves.Entries[i];
                if (World.TryGet(e.Guid, out var entity))
                    entity.Movement.Add(_moves.ServerTimeMs - e.AgeMs, e.State);
            }
        }

        private void HandleCorrection(ref PacketReader r)
        {
            _correction.Read(ref r);
            _correctionAck = _correction.CorrectionId;
            Corrected?.Invoke(_correction.State);
        }
    }
}
