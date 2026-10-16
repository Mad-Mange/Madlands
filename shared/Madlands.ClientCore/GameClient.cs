using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using LiteNetLib;
using Madlands.Shared;
using Madlands.Shared.Game;
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
    /// The client only asks (target, cast, chat); the server decides and reports back.
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
        private readonly GameData _gameData = new GameData();
        private readonly UnitUpdates _unitUpdates = new UnitUpdates();
        private readonly SpellStart _spellStart = new SpellStart();
        private readonly SpellGo _spellGo = new SpellGo();
        private readonly SpellFailed _spellFailed = new SpellFailed();
        private readonly CombatEvent _combatEvent = new CombatEvent();
        private readonly AuraUpdate _auraUpdate = new AuraUpdate();
        private readonly Cooldown _cooldown = new Cooldown();
        private readonly PlayerStats _stats = new PlayerStats();
        private readonly Spellbook _spellbook = new Spellbook();
        private readonly ChatMessage _chat = new ChatMessage();
        private readonly ExperienceGained _xp = new ExperienceGained();
        private readonly SetTarget _setTarget = new SetTarget();
        private readonly CastSpell _castSpell = new CastSpell();
        private readonly ChatInput _chatInput = new ChatInput();
        private readonly CancelAura _cancelAura = new CancelAura();

        private readonly List<uint> _knownSpells = new List<uint>();
        private readonly Dictionary<uint, (double ReadyAt, double Duration)> _cooldowns = new Dictionary<uint, (double, double)>();

        private NetPeer _peer;
        private string _sessionToken;
        private ulong _characterId;
        private long _lastUpdateMs;
        private long _nextPingAtMs;
        private int _pingsSent;
        private ushort _correctionAck;
        private byte _lastCastId;
        // Global cooldown predicted when a cast is requested; undone if the server refuses that cast.
        private double _gcdEndsAt;
        private double _gcdDuration;
        private double _gcdBeforeRequest;
        private byte _gcdCastId;
        private bool _gcdConfirmed;

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
        public string LocalName { get; private set; }
        public ushort MapId { get; private set; }
        public uint InstanceId { get; private set; }
        public ClientWorld World { get; } = new ClientWorld();
        public ClockSync Clock { get; } = new ClockSync();
        /// <summary>Spells, factions and levels from the server (valid once in the world).</summary>
        public ClientGameData Data { get; } = new ClientGameData();
        /// <summary>The local player's own unit state (health, auras, cast).</summary>
        public UnitState Self { get; } = new UnitState();
        /// <summary>The local player's latest private numbers (experience, attributes).</summary>
        public PlayerStats Stats => _stats;
        public IReadOnlyList<uint> KnownSpells => _knownSpells;
        public EntityGuid Target { get; private set; }
        /// <summary>The local player's latest movement (set by <see cref="SendMovement"/> or <see cref="SetLocalMovement"/>).</summary>
        public MovementState LocalMovement { get; private set; }
        public string LastError { get; private set; }
        /// <summary>Why the server turned us away, if it did (e.g. InvalidSession = log in again).</summary>
        public RejectReason? LastRejectReason { get; private set; }
        public int RoundTripMs => _peer != null ? _peer.RoundTripTime : 0;
        public long LocalTimeMs => _stopwatch.ElapsedMilliseconds;
        public double ServerTimeMs => Clock.ServerTimeAt(LocalTimeMs);
        /// <summary>Server time at which remote entities are rendered (interpolation delay included).</summary>
        public double RenderTimeMs => ServerTimeMs - InterpolationBuffer.InterpolationDelayMs;
        public long PacketsReceived { get; private set; }

        /// <summary>Raised once the server accepted us; argument is the spawn state.</summary>
        public event Action<MovementState> EnteredWorld;
        public event Action<RemoteEntity> EntitySpawned;
        public event Action<RemoteEntity> EntityDespawned;
        /// <summary>The server moved us (rejected movement, teleport, resurrection); the local player must snap to this state.</summary>
        public event Action<MovementState> Corrected;
        public event Action<string> Disconnected;
        public event Action GameDataReceived;
        /// <summary>Unit fields changed (guid may be <see cref="LocalGuid"/>).</summary>
        public event Action<EntityGuid, UnitFieldMask> UnitChanged;
        public event Action<EntityGuid> AurasChanged;
        public event Action<EntityGuid, CastInfo> CastStarted;
        public event Action<SpellGo> SpellLaunched;
        public event Action<SpellFailed> SpellFailed;
        public event Action<CombatEvent> CombatEventReceived;
        public event Action<ChatMessage> ChatReceived;
        /// <summary>Amount and the creature it came from (Empty for other sources).</summary>
        public event Action<int, EntityGuid> ExperienceReceived;
        public event Action SpellbookChanged;
        public event Action StatsChanged;
        public event Action<EntityGuid> TargetChanged;

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
            LocalMovement = state;
            if (State != ClientState.InWorld) return;
            _moveUpdate.CorrectionAck = _correctionAck;
            _moveUpdate.ClientTimeMs = LocalTimeMs;
            _moveUpdate.State = state;
            Send(_moveUpdate, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>Keeps <see cref="LocalMovement"/> current between sends (range checks use it).</summary>
        public void SetLocalMovement(in MovementState state) => LocalMovement = state;

        public void Disconnect()
        {
            if (_peer != null) _net.DisconnectPeer(_peer);
            if (_manualMode) _net.ManualUpdate(0);
        }

        public void Dispose()
        {
            _net.Stop();
        }

        // ------------------------------------------------------------------ actions

        /// <summary>Selects a target (Empty clears). Others see it as our target.</summary>
        public void SetTarget(EntityGuid guid)
        {
            if (guid == Target) return;
            Target = guid;
            _setTarget.Target = guid;
            Send(_setTarget, DeliveryMethod.ReliableOrdered);
            TargetChanged?.Invoke(guid);
        }

        /// <summary>Asks to cast a spell; returns the cast id that replies will carry.</summary>
        public byte CastSpell(uint spellId, EntityGuid target)
        {
            if (++_lastCastId == 0) _lastCastId = 1;
            if (Data.TryGetSpell(spellId, out var spell) && spell.GcdMs > 0 && !spell.IsAutoAttack)
            {
                _gcdBeforeRequest = _gcdEndsAt;
                _gcdEndsAt = Math.Max(_gcdEndsAt, LocalTimeMs + spell.GcdMs);
                _gcdDuration = spell.GcdMs;
                _gcdCastId = _lastCastId;
                _gcdConfirmed = false;
            }
            _castSpell.SpellId = spellId;
            _castSpell.Target = target;
            _castSpell.CastId = _lastCastId;
            Send(_castSpell, DeliveryMethod.ReliableOrdered);
            return _lastCastId;
        }

        public void CancelCast() => Send(new CancelCast(), DeliveryMethod.ReliableOrdered);

        public void StopAttack() => Send(new StopAttack(), DeliveryMethod.ReliableOrdered);

        /// <summary>Says a line, or runs a GM command if it starts with '.'.</summary>
        public void SendChat(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            // The server drops oversized strings as malformed packets, so trim to the byte limit.
            while (System.Text.Encoding.UTF8.GetByteCount(text) > ChatInput.MaxTextBytes) text = text.Substring(0, text.Length - 1);
            _chatInput.Text = text;
            Send(_chatInput, DeliveryMethod.ReliableOrdered);
        }

        public void ReleaseSpirit() => Send(new ReleaseSpirit(), DeliveryMethod.ReliableOrdered);

        public void CancelAura(uint spellId)
        {
            _cancelAura.SpellId = spellId;
            Send(_cancelAura, DeliveryMethod.ReliableOrdered);
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Unit state of the local player or a visible unit.</summary>
        public bool TryGetUnit(EntityGuid guid, out UnitState unit)
        {
            if (guid == LocalGuid && !guid.IsEmpty)
            {
                unit = Self;
                return true;
            }
            if (World.TryGet(guid, out var entity) && entity.Unit != null)
            {
                unit = entity.Unit;
                return true;
            }
            unit = null;
            return false;
        }

        /// <summary>Where a unit is drawn now (the local player: its latest movement).</summary>
        public bool TryGetPosition(EntityGuid guid, out Vec3 position)
        {
            if (guid == LocalGuid && !guid.IsEmpty)
            {
                position = LocalMovement.Position;
                return true;
            }
            if (World.TryGet(guid, out var entity))
            {
                position = entity.Movement.Sample(RenderTimeMs).Position;
                return true;
            }
            position = Vec3.Zero;
            return false;
        }

        public Reaction GetReaction(EntityGuid guid)
        {
            if (!TryGetUnit(guid, out var unit)) return Reaction.Neutral;
            return Data.GetReaction(Self.Values.Faction, unit.Values.Faction);
        }

        public bool CanAttack(EntityGuid guid) =>
            guid != LocalGuid && TryGetUnit(guid, out var unit) && !unit.Values.IsDead && GetReaction(guid) != Reaction.Friendly;

        public double GcdRemainingMs => Math.Max(0, _gcdEndsAt - LocalTimeMs);
        public double GcdDurationMs => _gcdDuration;

        /// <summary>Remaining spell cooldown (not counting the global cooldown).</summary>
        public bool TryGetCooldown(uint spellId, out double remainingMs, out double durationMs)
        {
            remainingMs = 0;
            durationMs = 0;
            if (!_cooldowns.TryGetValue(spellId, out var cd)) return false;
            remainingMs = cd.ReadyAt - LocalTimeMs;
            durationMs = cd.Duration;
            return remainingMs > 0;
        }

        /// <summary>
        /// What the client can tell before asking: known, ready, affordable, valid target in range.
        /// Facing and line of sight are left to the server.
        /// </summary>
        public CastResult CheckCast(uint spellId, EntityGuid target)
        {
            if (!Data.TryGetSpell(spellId, out var spell) || !_knownSpells.Contains(spellId)) return CastResult.NotKnown;
            if (Self.Values.IsDead) return CastResult.CasterDead;
            if (Self.Values.Has(UnitFlags.Stunned)) return CastResult.Stunned;
            if (TryGetCooldown(spellId, out _, out _)) return CastResult.OnCooldown;
            if (spell.GcdMs > 0 && GcdRemainingMs > 0) return CastResult.NotReady;
            if (spell.Cost > 0 && (Self.Values.PowerType != spell.PowerType || Self.Values.Power < spell.Cost)) return CastResult.NoPower;
            if (spell.Target == SpellTarget.Enemy)
            {
                if (target.IsEmpty || !TryGetUnit(target, out var unit)) return CastResult.NoTarget;
                if (unit.Values.IsDead) return CastResult.TargetDead;
                if (!CanAttack(target)) return CastResult.InvalidTarget;
            }
            if (spell.Target == SpellTarget.Enemy || (spell.Target == SpellTarget.Friend && !target.IsEmpty && GetReaction(target) == Reaction.Friendly))
            {
                if (TryGetPosition(target, out var p) && target != LocalGuid)
                {
                    float distance = Vec3.Distance(LocalMovement.Position, p);
                    if (distance > spell.MaxRange) return CastResult.OutOfRange;
                    if (distance < spell.MinRange) return CastResult.TooClose;
                }
            }
            return CastResult.Success;
        }

        // ------------------------------------------------------------------ networking

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
            ResetLocalState();

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

        private void ResetLocalState()
        {
            Self.Auras.Clear();
            Self.Cast = null;
            _cooldowns.Clear();
            _knownSpells.Clear();
            _gcdEndsAt = 0;
            Target = EntityGuid.Empty;
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
                    case Opcode.GameData: HandleGameData(ref r); break;
                    case Opcode.UnitUpdates: HandleUnitUpdates(ref r); break;
                    case Opcode.SpellStart: HandleSpellStart(ref r); break;
                    case Opcode.SpellGo: HandleSpellGo(ref r); break;
                    case Opcode.SpellFailed: HandleSpellFailed(ref r); break;
                    case Opcode.CombatEvent: HandleCombatEvent(ref r); break;
                    case Opcode.AuraUpdate: HandleAuraUpdate(ref r); break;
                    case Opcode.Cooldown: HandleCooldown(ref r); break;
                    case Opcode.PlayerStats: HandlePlayerStats(ref r); break;
                    case Opcode.Spellbook: HandleSpellbook(ref r); break;
                    case Opcode.ChatMessage: HandleChat(ref r); break;
                    case Opcode.ExperienceGained: HandleExperience(ref r); break;
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
            LocalName = _welcome.Name;
            MapId = _welcome.MapId;
            InstanceId = _welcome.InstanceId;
            ResetLocalState();
            Self.Values.CopyFrom(_welcome.Self);
            LocalMovement = _welcome.Spawn;
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
            if (entity.Unit != null)
            {
                entity.Unit.Values.CopyFrom(_spawn.Unit);
                entity.Unit.Auras.Clear();
                entity.Unit.Cast = null;
            }
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
            LocalMovement = _correction.State;
            Corrected?.Invoke(_correction.State);
        }

        private void HandleGameData(ref PacketReader r)
        {
            _gameData.Read(ref r);
            Data.Load(_gameData);
            GameDataReceived?.Invoke();
        }

        private void HandleUnitUpdates(ref PacketReader r)
        {
            _unitUpdates.Read(ref r);
            foreach (var e in _unitUpdates.Entries)
            {
                if (!TryGetUnit(e.Guid, out var unit)) continue;
                unit.Values.CopyFrom(e.Values, e.Mask);
                if ((e.Mask & UnitFieldMask.Flags) != 0 && unit.Values.IsDead) unit.Cast = null;
                UnitChanged?.Invoke(e.Guid, e.Mask);
            }
        }

        private void HandleSpellStart(ref PacketReader r)
        {
            _spellStart.Read(ref r);
            if (_spellStart.Caster == LocalGuid && _spellStart.CastId == _gcdCastId) _gcdConfirmed = true;
            if (!TryGetUnit(_spellStart.Caster, out var unit)) return;
            double now = LocalTimeMs;
            unit.Cast = new CastInfo
            {
                SpellId = _spellStart.SpellId,
                Target = _spellStart.Target,
                CastId = _spellStart.CastId,
                StartLocalMs = now,
                EndLocalMs = now + _spellStart.CastTimeMs,
            };
            CastStarted?.Invoke(_spellStart.Caster, unit.Cast);
        }

        private void HandleSpellGo(ref PacketReader r)
        {
            _spellGo.Read(ref r);
            if (_spellGo.Caster == LocalGuid && _spellGo.CastId == _gcdCastId) _gcdConfirmed = true;
            if (TryGetUnit(_spellGo.Caster, out var unit) && unit.Cast != null && unit.Cast.SpellId == _spellGo.SpellId) unit.Cast = null;
            SpellLaunched?.Invoke(_spellGo);
        }

        private void HandleSpellFailed(ref PacketReader r)
        {
            _spellFailed.Read(ref r);
            if (_spellFailed.Caster == LocalGuid && _spellFailed.CastId == _gcdCastId && _spellFailed.CastId != 0 && !_gcdConfirmed)
            {
                // The server refused the request: the predicted global cooldown never started.
                _gcdEndsAt = _gcdBeforeRequest;
                _gcdConfirmed = true;
            }
            if (TryGetUnit(_spellFailed.Caster, out var unit) && unit.Cast != null && unit.Cast.SpellId == _spellFailed.SpellId) unit.Cast = null;
            SpellFailed?.Invoke(_spellFailed);
        }

        private void HandleCombatEvent(ref PacketReader r)
        {
            _combatEvent.Read(ref r);
            CombatEventReceived?.Invoke(_combatEvent);
        }

        private void HandleAuraUpdate(ref PacketReader r)
        {
            _auraUpdate.Read(ref r);
            if (!TryGetUnit(_auraUpdate.Unit, out var unit)) return;
            if (_auraUpdate.Full) unit.Auras.Clear();
            double now = LocalTimeMs;
            foreach (var e in _auraUpdate.Entries)
            {
                unit.SetAura(new ClientAura
                {
                    Slot = e.Slot,
                    SpellId = e.SpellId,
                    Caster = e.Caster,
                    Stacks = e.Stacks,
                    DurationMs = e.DurationMs,
                    ExpiresAtLocalMs = now + e.RemainingMs,
                });
            }
            AurasChanged?.Invoke(_auraUpdate.Unit);
        }

        private void HandleCooldown(ref PacketReader r)
        {
            _cooldown.Read(ref r);
            if (_cooldown.SpellId == 0 && _cooldown.DurationMs == 0)
            {
                _cooldowns.Clear();
                _gcdEndsAt = 0;
                return;
            }
            if (_cooldown.DurationMs <= 0) _cooldowns.Remove(_cooldown.SpellId);
            else _cooldowns[_cooldown.SpellId] = (LocalTimeMs + _cooldown.DurationMs, _cooldown.DurationMs);
        }

        private void HandlePlayerStats(ref PacketReader r)
        {
            _stats.Read(ref r);
            StatsChanged?.Invoke();
        }

        private void HandleSpellbook(ref PacketReader r)
        {
            _spellbook.Read(ref r);
            _knownSpells.Clear();
            _knownSpells.AddRange(_spellbook.SpellIds);
            SpellbookChanged?.Invoke();
        }

        private void HandleChat(ref PacketReader r)
        {
            _chat.Read(ref r);
            ChatReceived?.Invoke(_chat);
        }

        private void HandleExperience(ref PacketReader r)
        {
            _xp.Read(ref r);
            ExperienceReceived?.Invoke(_xp.Amount, _xp.Victim);
        }
    }
}
