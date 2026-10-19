using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using LiteNetLib;
using Madlands.Content;
using Madlands.Server.Auth;
using Madlands.Server.Commands;
using Madlands.Server.Core;
using Madlands.Server.Combat;
using Madlands.Server.Diagnostics;
using Madlands.Server.Items;
using Madlands.Server.Net;
using Madlands.Server.Persistence;
using Madlands.Server.Quests;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Game;
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
    private readonly SetTarget _setTarget = new();
    private readonly CastSpell _castSpell = new();
    private readonly ChatInput _chatInput = new();
    private readonly CancelAura _cancelAura = new();
    private readonly MoveItem _moveItem = new();
    private readonly UseItem _useItem = new();
    private readonly DestroyItem _destroyItem = new();
    private readonly AutoEquip _autoEquip = new();
    private readonly Interact _interact = new();
    private readonly LootOpen _lootOpen = new();
    private readonly LootTake _lootTake = new();
    private readonly VendorBuy _vendorBuy = new();
    private readonly VendorSell _vendorSell = new();
    private readonly QuestAccept _questAccept = new();
    private readonly QuestTurnIn _questTurnIn = new();
    private readonly QuestAbandon _questAbandon = new();
    private readonly ChatHandler _chat;
    /// <summary>Client display data, encoded once at startup and sent to everyone before Welcome.</summary>
    private readonly byte[] _gameData;

    private long _tick;

    public GameServer(ServerOptions options, ContentDatabase content, IGameDatabase database, CharacterSaver saver)
    {
        _options = options;
        _database = database;
        _saver = saver;
        World = new WorldManager(content, options.RandomSeed);
        if (World.GetInstance(options.StartMapId, WorldManager.OpenWorldInstance) is null)
            throw new ArgumentException($"Start map {options.StartMapId} does not exist in content.");
        _gameData = BuildGameData(content);
        _chat = new ChatHandler(this);

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
    public ServerOptions Options => _options;
    /// <summary>Players in the world, on all maps (loop thread only).</summary>
    public IEnumerable<Player> OnlinePlayers => _sessions.Values.Where(s => s.Player is not null).Select(s => s.Player!);
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

    private CharacterSnapshot Snapshot(Player player)
    {
        var p = player.Movement.Position;
        long now = NowMs;
        var auras = player.Auras.Where(a => AuraSystem.Persists(a) && a.RemainingMs(now) > 0)
            .Select(a => new AuraRecord(a.Spell.Id, a.CasterLevel, a.Stacks, a.RemainingMs(now))).ToArray();
        var items = player.Inventory.All().Select(e => new ItemRecord(e.Pos.Container, e.Pos.Slot, e.Stack.Item.Id, e.Stack.Count)).ToArray();
        var quests = player.Quests.Active.Select(q => new QuestRecord(q.Quest.Id, false, q.Counts.ToArray()))
            .Concat(player.Quests.Rewarded.Select(id => new QuestRecord(id, true, Array.Empty<int>()))).ToArray();
        return new CharacterSnapshot(player.CharacterId, player.Map?.MapId ?? 0, p.X, p.Y, p.Z, player.Movement.Yaw, player.Level,
            player.Xp, player.Health, player.Power, player.Inventory.Money, player.StarterKitGiven, items, quests, (int)player.RestedXp, auras);
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

                case Opcode.SetTarget or Opcode.CastSpell or Opcode.CancelCast or Opcode.StopAttack or Opcode.ReleaseSpirit or Opcode.CancelAura
                    or Opcode.MoveItem or Opcode.UseItem or Opcode.DestroyItem or Opcode.AutoEquip or Opcode.Interact
                    or Opcode.LootOpen or Opcode.LootTake or Opcode.LootClose or Opcode.VendorBuy or Opcode.VendorSell
                    or Opcode.QuestAccept or Opcode.QuestTurnIn or Opcode.QuestAbandon
                    when session.State == SessionState.InWorld:
                    if (!session.ActionLimiter.TryConsume(now))
                    {
                        AddViolation(session, "action rate");
                        break;
                    }
                    HandleAction(session.Player!, opcode, ref reader, now);
                    break;

                case Opcode.ChatInput when session.State == SessionState.InWorld:
                {
                    _chatInput.Read(ref reader);
                    // GM commands have their own, roomier budget (scripted test setups send many in a row).
                    bool gmCommand = session.GmLevel > 0 && _chatInput.Text?.StartsWith('.') == true;
                    if (!(gmCommand ? session.GmCommandLimiter : session.ChatLimiter).TryConsume(now))
                    {
                        AddViolation(session, "chat rate");
                        break;
                    }
                    _chat.Handle(session.Player!, _chatInput.Text, now);
                    break;
                }

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

    /// <summary>Targeting and spell requests. The systems validate everything; the client only asks.</summary>
    private void HandleAction(Player player, Opcode opcode, ref PacketReader reader, long now)
    {
        var map = player.Map;
        if (map is null) return;
        switch (opcode)
        {
            case Opcode.SetTarget:
            {
                _setTarget.Read(ref reader);
                var target = map.FindUnit(_setTarget.Target);
                player.TargetGuid = target?.Guid ?? EntityGuid.Empty;
                // Switching targets while swinging keeps swinging at the new target, if it can be attacked.
                if (player.AttackTarget is not null && player.AttackTarget != target)
                {
                    if (target is not null && map.CanAttack(player, target)) player.AttackTarget = target;
                    else map.Combat.StopAutoAttack(player);
                }
                break;
            }
            case Opcode.CastSpell:
                _castSpell.Read(ref reader);
                map.Spells.TryCast(player, _castSpell.SpellId, _castSpell.Target, _castSpell.CastId, now);
                break;
            case Opcode.CancelCast:
                map.Spells.Interrupt(player, CastResult.Cancelled, now);
                break;
            case Opcode.StopAttack:
                map.Combat.StopAutoAttack(player);
                break;
            case Opcode.ReleaseSpirit:
                if (player.IsDead) map.Combat.Resurrect(player, now);
                break;
            case Opcode.CancelAura:
                _cancelAura.Read(ref reader);
                if (map.Auras.Find(player, _cancelAura.SpellId) is { IsHarmful: false } aura) map.Auras.Remove(player, aura, now);
                break;
            case Opcode.MoveItem:
                _moveItem.Read(ref reader);
                map.Items.Move(player, _moveItem.From, _moveItem.To, _moveItem.Count);
                break;
            case Opcode.UseItem:
                _useItem.Read(ref reader);
                map.Items.Use(player, _useItem.Pos, _useItem.Target, _useItem.CastId, now);
                break;
            case Opcode.DestroyItem:
                _destroyItem.Read(ref reader);
                map.Items.Destroy(player, _destroyItem.Pos);
                break;
            case Opcode.AutoEquip:
                _autoEquip.Read(ref reader);
                map.Items.AutoEquip(player, _autoEquip.Pos);
                break;
            case Opcode.Interact:
                _interact.Read(ref reader);
                map.Quests.Interact(player, _interact.Npc, _interact.Option);
                break;
            case Opcode.LootOpen:
                _lootOpen.Read(ref reader);
                map.Loot.Open(player, _lootOpen.Corpse);
                break;
            case Opcode.LootTake:
                _lootTake.Read(ref reader);
                map.Loot.Take(player, _lootTake.Slot);
                break;
            case Opcode.LootClose:
                map.Loot.Close(player, notify: false);
                break;
            case Opcode.VendorBuy:
                _vendorBuy.Read(ref reader);
                map.Items.Buy(player, _vendorBuy.Vendor, _vendorBuy.ItemId, _vendorBuy.Count);
                break;
            case Opcode.VendorSell:
                _vendorSell.Read(ref reader);
                map.Items.Sell(player, _vendorSell.Vendor, _vendorSell.Pos);
                break;
            case Opcode.QuestAccept:
                _questAccept.Read(ref reader);
                map.Quests.Accept(player, _questAccept.Npc, _questAccept.QuestId);
                break;
            case Opcode.QuestTurnIn:
                _questTurnIn.Read(ref reader);
                map.Quests.TurnIn(player, _questTurnIn.Npc, _questTurnIn.QuestId, _questTurnIn.Choice, now);
                break;
            case Opcode.QuestAbandon:
                _questAbandon.Read(ref reader);
                map.Quests.Abandon(player, _questAbandon.QuestId);
                break;
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
                    int gmLevel = character is null ? 0 : await _database.GetGmLevelAsync(accountId.Value);
                    var data = character is null ? null : await _database.LoadCharacterDataAsync(characterId);
                    result = character is null
                        ? LoginResult.Fail(RejectReason.CharacterNotFound, "That character does not exist.")
                        : new LoginResult(accountId.Value, character, RejectReason.Unknown, "", gmLevel, data);
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

    private sealed record LoginResult(long AccountId, CharacterRecord? Character, RejectReason Reason, string Message, int GmLevel = 0,
        CharacterData? Data = null)
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

        var content = World.Content;
        if (!content.Classes.TryGetValue(character.Class, out var cls) || !content.Races.TryGetValue(character.Race, out var race))
        {
            Kick(session, RejectReason.ServerError, "Your character's race or class no longer exists.");
            return;
        }

        var map = World.GetInstance(character.MapId, WorldManager.OpenWorldInstance)
                  ?? World.GetInstance(_options.StartMapId, WorldManager.OpenWorldInstance)!;
        // Logged out dead: come back at the graveyard (the map's spawn point) with half health.
        bool wasDead = character.Health is <= 0;
        var spawn = wasDead ? MovementState.Idle(map.Map.SpawnPosition, map.Map.SpawnYaw) : SpawnStateFor(map, character);

        long now = NowMs;
        var player = new Player(character.Id, character.Name, session, new MovementValidator(map.HeightMap), cls, race)
        {
            Movement = spawn,
            Level = (byte)Math.Clamp((int)character.Level, 1, content.Levels.MaxLevel),
            Xp = Math.Max(0, character.Xp),
            NextSaveAtMs = now + SaveIntervalMs,
            GmLevel = _options.GmAll ? Math.Max(1, result.GmLevel) : result.GmLevel,
            StarterKitGiven = character.StarterKit,
            RestedXp = Math.Max(0, character.RestedXp),
        };
        // Rest built up while logged out: at full speed if that was in a village, slower elsewhere.
        if (character.LastSavedUtc is { } saved)
        {
            bool village = map.MapId == character.MapId && map.Map.Definition.IsRestArea(character.X, character.Z);
            double away = (DateTime.UtcNow - saved).TotalSeconds;
            CombatSystem.AddRest(player, content.Levels, Experience.RestGained(content.Levels.Rest, content.Levels.XpToNext(player.Level), away, village));
        }
        var data = result.Data ?? CharacterData.Empty;
        RestoreItemsAndQuests(player, content, character, data);
        if (!player.StarterKitGiven) map.Items.GiveStartingItems(player);
        player.Inventory.ClearChanges();
        // Long buffs kept over logging out (their time stood still), unless the character logged out dead.
        if (!wasDead)
            foreach (var buff in data.SavedAuras)
                if (content.Spells.TryGetValue(buff.SpellId, out var spell))
                    AuraSystem.Restore(player, spell, buff.CasterLevel, buff.Stacks, buff.RemainingMs, now);
        player.RecalculateStats();
        player.UpdateKnownSpells();
        player.Health = wasDead ? player.MaxHealth / 2 : character.Health ?? player.MaxHealth;
        player.Power = character.Power ?? (player.PowerType == PowerType.Mana ? player.MaxPower : 0);

        session.AccountId = result.AccountId;
        session.GmLevel = player.GmLevel;
        session.Player = player;
        session.State = SessionState.InWorld;
        _onlineCharacters[character.Id] = session;

        session.Peer.Send(_gameData, DeliveryMethod.ReliableOrdered);
        _welcome.PlayerGuid = player.Guid;
        _welcome.Name = player.Name;
        _welcome.ServerTimeMs = now;
        _welcome.MapId = map.MapId;
        _welcome.InstanceId = map.InstanceId;
        _welcome.DisplayId = player.DisplayId;
        _welcome.Spawn = spawn;
        _welcome.Self.CopyFrom(player.Values);
        session.Send(_writer, _welcome, DeliveryMethod.ReliableOrdered);
        player.Dirty = UnitFieldMask.None;

        map.AddPlayer(player, now);
        map.Spells.SendSpellbook(player);
        map.SendPlayerStats(player);
        map.Items.SendAll(player);
        map.Quests.SendLog(player);
        map.SendOwnAuras(player, now);
        Log.Information("{Player} entered map {MapId} from {Address}", player, map.MapId, session.Peer);
    }

    /// <summary>
    /// Puts saved items back where they were (equipment first: it sizes the bags) and rebuilds the quest log.
    /// Items whose slot no longer exists move to a free slot; content that no longer exists is dropped.
    /// </summary>
    private static void RestoreItemsAndQuests(Player player, ContentDatabase content, CharacterRecord character, CharacterData data)
    {
        var inventory = player.Inventory;
        inventory.AddMoney(character.Money);
        var homeless = new List<(ItemDefinition Item, int Count)>();
        foreach (var record in data.Items.OrderBy(i => i.Container).ThenBy(i => i.Slot))
        {
            if (!content.Items.TryGetValue(record.ItemId, out var item) || record.Count <= 0)
            {
                Log.Warning("{Player}: dropped unknown item {ItemId} x{Count}", player, record.ItemId, record.Count);
                continue;
            }
            var pos = new ItemPos(record.Container, record.Slot);
            int count = Math.Min(record.Count, item.StackSize);
            bool fits = !pos.IsEquipment || ItemRules.Fits(item.Slot, (EquipSlot)pos.Slot);
            if (!fits || !inventory.Restore(pos, new ItemStack(item, count))) count = 0;
            if (record.Count > count) homeless.Add((item, record.Count - count));
        }
        foreach (var (item, count) in homeless)
        {
            int added = inventory.Add(item, count);
            if (added < count) Log.Warning("{Player}: no room for {Count} x {Item}", player, count - added, item);
        }

        foreach (var record in data.Quests)
        {
            if (!content.Quests.TryGetValue(record.QuestId, out var quest)) continue;
            if (record.Rewarded)
            {
                player.Quests.MarkRewarded(quest.Id);
                continue;
            }
            var progress = player.Quests.Add(quest);
            for (int i = 0; i < progress.Counts.Length && i < record.Progress.Length; i++)
                progress.Counts[i] = Math.Clamp(record.Progress[i], 0, quest.Objectives[i].Count);
        }
    }

    /// <summary>Spell, faction, class, experience, item and quest data for the client (see <see cref="GameData"/>).</summary>
    private static byte[] BuildGameData(ContentDatabase content)
    {
        var learnLevels = new Dictionary<uint, int>();
        foreach (var cls in content.Classes.Values)
            foreach (var cs in cls.Spells)
                learnLevels[cs.Spell] = Math.Min(learnLevels.GetValueOrDefault(cs.Spell, int.MaxValue), cs.Level);

        var msg = new GameData();
        foreach (var s in content.Spells.Values.OrderBy(s => s.Id))
        {
            msg.Spells.Add(new SpellInfo
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                Icon = s.Icon,
                School = s.School,
                Target = s.Target,
                Flags = s.AllFlags,
                MinRange = s.RangeMin,
                MaxRange = s.RangeMax,
                CastTimeMs = s.CastTimeMs,
                CooldownMs = s.CooldownMs,
                GcdMs = s.GcdMs,
                PowerType = s.PowerType,
                Cost = s.Cost,
                AuraDurationMs = s.Aura?.DurationMs ?? 0,
                ProjectileSpeed = s.ProjectileSpeed,
                LearnLevel = (byte)Math.Min(learnLevels.GetValueOrDefault(s.Id, 0), 255),
            });
        }
        msg.Factions.AddRange(content.FactionTable.All.OrderBy(f => f.Id));
        foreach (var c in content.Classes.Values.OrderBy(c => c.Id))
            msg.Classes.Add(new ClassInfo { Id = (byte)c.Id, Name = c.Name, PowerType = c.PowerType });
        msg.XpToNextLevel.AddRange(content.Levels.XpToNextLevel);

        foreach (var i in content.Items.Values.OrderBy(i => i.Id))
        {
            var info = new ItemInfo
            {
                Id = i.Id,
                Name = i.Name,
                Description = i.Description,
                Icon = i.Icon,
                DisplayId = i.DisplayId,
                Quality = i.Quality,
                Class = i.Class,
                InventoryType = i.Slot,
                RequiredLevel = (byte)Math.Clamp(i.RequiredLevel, 0, 255),
                AllowedClasses = i.Classes.Select(c => (byte)c).ToArray(),
                StackSize = (ushort)i.StackSize,
                BuyPrice = (uint)i.BuyPrice,
                SellPrice = (uint)i.SellPrice,
                MinDamage = i.Weapon?.MinDamage ?? 0,
                MaxDamage = i.Weapon?.MaxDamage ?? 0,
                SpeedMs = i.Weapon?.SpeedMs ?? 0,
                UseSpell = i.UseSpell,
                BagSlots = (byte)i.BagSlots,
            };
            for (int s = 0; s < UnitStats.Count; s++)
                if (i.Stats[(StatType)s] != 0) info.Stats.Add(new ItemStat { Stat = (StatType)s, Value = i.Stats[(StatType)s] });
            msg.Items.Add(info);
        }

        foreach (var q in content.Quests.Values.OrderBy(q => q.Id))
        {
            var info = new QuestInfo
            {
                Id = q.Id,
                Title = q.Title,
                Level = (byte)q.Level,
                Details = q.Details,
                Objectives = q.Summary,
                Progress = q.Progress,
                Completion = q.Completion,
                EnderName = content.Creatures.GetValueOrDefault(q.Ender)?.Name ?? "",
                RewardXp = q.Rewards.Xp,
                RewardMoney = (uint)q.Rewards.Money,
            };
            foreach (var o in q.Objectives)
            {
                info.ObjectiveList.Add(new QuestObjectiveInfo
                {
                    Type = o.Type,
                    Target = o.Type == ObjectiveType.Collect ? o.Item : (uint)Math.Max(0, o.Creature),
                    Count = (ushort)o.Count,
                    Text = ObjectiveText(content, o),
                    Areas = QuestAreas.For(content, o),
                });
            }
            info.RewardItems.AddRange(q.Rewards.Items.Select(r => new QuestItemReward { Item = r.Item, Count = (ushort)r.Count }));
            info.RewardChoices.AddRange(q.Rewards.Choices.Select(r => new QuestItemReward { Item = r.Item, Count = (ushort)r.Count }));
            msg.Quests.Add(info);
        }

        var writer = new PacketWriter(4096);
        Packet.Encode(writer, msg);
        return writer.ToArray();
    }

    /// <summary>The quest log line of an objective: its own text, or one made from the target's name.</summary>
    public static string ObjectiveText(ContentDatabase content, QuestObjectiveDefinition o)
    {
        if (o.Text.Length > 0) return o.Text;
        return o.Type switch
        {
            ObjectiveType.Kill => (content.Creatures.GetValueOrDefault(o.Creature)?.Name ?? "Creature") + " slain",
            ObjectiveType.Collect => content.Items.GetValueOrDefault(o.Item)?.Name ?? "Item",
            ObjectiveType.Talk => "Speak with " + (content.Creatures.GetValueOrDefault(o.Creature)?.Name ?? "someone"),
            _ => "Explore",
        };
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
