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
        private readonly InventoryUpdate _inventoryUpdate = new InventoryUpdate();
        private readonly MoneyUpdate _moneyUpdate = new MoneyUpdate();
        private readonly ItemReceived _itemReceived = new ItemReceived();
        private readonly ActionFailed _actionFailed = new ActionFailed();
        private readonly LootWindow _lootWindow = new LootWindow();
        private readonly LootableUpdate _lootableMsg = new LootableUpdate();
        private readonly GossipMenu _gossip = new GossipMenu();
        private readonly VendorInventory _vendor = new VendorInventory();
        private readonly QuestLogUpdate _questLog = new QuestLogUpdate();
        private readonly QuestGiverStatusUpdate _questStatus = new QuestGiverStatusUpdate();
        private readonly QuestCompleted _questCompleted = new QuestCompleted();
        private readonly MoveItem _moveItem = new MoveItem();
        private readonly UseItem _useItem = new UseItem();
        private readonly DestroyItem _destroyItem = new DestroyItem();
        private readonly AutoEquip _autoEquip = new AutoEquip();
        private readonly Interact _interact = new Interact();
        private readonly LootOpen _lootOpen = new LootOpen();
        private readonly LootTake _lootTake = new LootTake();
        private readonly VendorBuy _vendorBuy = new VendorBuy();
        private readonly VendorSell _vendorSell = new VendorSell();
        private readonly QuestAccept _questAccept = new QuestAccept();
        private readonly QuestTurnIn _questTurnIn = new QuestTurnIn();
        private readonly QuestAbandon _questAbandon = new QuestAbandon();
        private readonly HashSet<EntityGuid> _lootableCorpses = new HashSet<EntityGuid>();
        private readonly Dictionary<EntityGuid, QuestGiverStatus> _questGivers = new Dictionary<EntityGuid, QuestGiverStatus>();
        private readonly List<(uint Quest, int Objective, int Count)> _progressScratch = new List<(uint, int, int)>();
        private readonly List<(uint Quest, QuestLogState State)> _questStateScratch = new List<(uint, QuestLogState)>();

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
            Inventory = new ClientInventory(Data);
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
        /// <summary>The local player's model (from the Welcome).</summary>
        public uint LocalDisplayId { get; private set; }
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
        /// <summary>Equipment, bags and money (Phase 4).</summary>
        public ClientInventory Inventory { get; }
        public ClientQuestLog Quests { get; } = new ClientQuestLog();
        /// <summary>The open loot window (Corpse is Empty when closed).</summary>
        public ClientLoot Loot { get; } = new ClientLoot();
        /// <summary>The last NPC dialog the server sent (shown until the player walks away or closes it).</summary>
        public GossipMenu Gossip => _gossip;
        /// <summary>The last vendor's wares.</summary>
        public VendorInventory Vendor => _vendor;
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
        /// <summary>Amount (the rested bonus included), the rested bonus, and the creature it came from (Empty for other sources).</summary>
        public event Action<int, int, EntityGuid> ExperienceReceived;
        public event Action SpellbookChanged;
        public event Action StatsChanged;
        public event Action<EntityGuid> TargetChanged;
        public event Action InventoryChanged;
        /// <summary>Change and where it came from (0 at login).</summary>
        public event Action<long, ReceiveSource> MoneyChanged;
        public event Action<ItemReceived> ItemReceived;
        /// <summary>An item, loot, vendor or quest request was refused (error and its detail value).</summary>
        public event Action<ActionError, int> ActionFailed;
        public event Action LootChanged;
        /// <summary>A corpse became lootable or stopped being lootable for us.</summary>
        public event Action<EntityGuid> LootableChanged;
        public event Action<GossipMenu> GossipOpened;
        public event Action<VendorInventory> VendorOpened;
        public event Action QuestLogChanged;
        /// <summary>Quest id, objective index and the new count (chat: "Mire Frog slain: 3/6").</summary>
        public event Action<uint, int, int> QuestProgressed;
        /// <summary>A quest was taken (Active), finished its objectives (Complete), or left the log (Removed).</summary>
        public event Action<uint, QuestLogState> QuestStateChanged;
        public event Action<QuestCompleted> QuestCompleted;
        /// <summary>The "!"/"?" mark of an NPC changed.</summary>
        public event Action<EntityGuid> QuestGiverStatusChanged;

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
            byte castId = NextCastId();
            if (Data.TryGetSpell(spellId, out var spell)) PredictGcd(spell, castId);
            _castSpell.SpellId = spellId;
            _castSpell.Target = target;
            _castSpell.CastId = castId;
            Send(_castSpell, DeliveryMethod.ReliableOrdered);
            return castId;
        }

        private byte NextCastId()
        {
            if (++_lastCastId == 0) _lastCastId = 1;
            return _lastCastId;
        }

        /// <summary>Starts the global cooldown right away; undone if the server refuses the cast.</summary>
        private void PredictGcd(SpellInfo spell, byte castId)
        {
            if (spell.GcdMs <= 0 || spell.IsAutoAttack) return;
            _gcdBeforeRequest = _gcdEndsAt;
            _gcdEndsAt = Math.Max(_gcdEndsAt, LocalTimeMs + spell.GcdMs);
            _gcdDuration = spell.GcdMs;
            _gcdCastId = castId;
            _gcdConfirmed = false;
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

        // ------------------------------------------------------------------ item, loot, vendor and quest requests

        /// <summary>Moves, swaps, stacks or (with <paramref name="count"/>) splits; into or out of equipment equips or unequips.</summary>
        public void MoveItem(ItemPos from, ItemPos to, int count = 0)
        {
            _moveItem.From = from;
            _moveItem.To = to;
            _moveItem.Count = (ushort)Math.Max(0, Math.Min(count, ushort.MaxValue));
            Send(_moveItem, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>Uses a consumable; the replies are those of a cast with the returned id.</summary>
        public byte UseItem(ItemPos pos, EntityGuid target)
        {
            byte castId = NextCastId();
            var item = Data.GetItem(Inventory.Get(pos).ItemId);
            if (item != null && Data.TryGetSpell(item.UseSpell, out var spell)) PredictGcd(spell, castId);
            _useItem.Pos = pos;
            _useItem.Target = target;
            _useItem.CastId = castId;
            Send(_useItem, DeliveryMethod.ReliableOrdered);
            return castId;
        }

        public void DestroyItem(ItemPos pos)
        {
            _destroyItem.Pos = pos;
            Send(_destroyItem, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>Equips a bag item where it fits, or puts an equipped item into the bags.</summary>
        public void AutoEquip(ItemPos pos)
        {
            _autoEquip.Pos = pos;
            Send(_autoEquip, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>Talks to an NPC (or asks for its wares).</summary>
        public void Interact(EntityGuid npc, InteractOption option = InteractOption.Talk)
        {
            _interact.Npc = npc;
            _interact.Option = option;
            Send(_interact, DeliveryMethod.ReliableOrdered);
        }

        public void OpenLoot(EntityGuid corpse)
        {
            _lootOpen.Corpse = corpse;
            Send(_lootOpen, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>Takes a loot slot, the money (<see cref="Shared.Net.LootTake.MoneySlot"/>) or everything (<see cref="Shared.Net.LootTake.AllSlots"/>).</summary>
        public void TakeLoot(byte slot)
        {
            _lootTake.Slot = slot;
            Send(_lootTake, DeliveryMethod.ReliableOrdered);
        }

        public void CloseLoot()
        {
            if (!Loot.IsOpen) return;
            ResetLoot();
            Send(new LootClose(), DeliveryMethod.ReliableOrdered);
            LootChanged?.Invoke();
        }

        public void BuyItem(EntityGuid vendor, uint itemId, int count = 1)
        {
            _vendorBuy.Vendor = vendor;
            _vendorBuy.ItemId = itemId;
            _vendorBuy.Count = (ushort)Math.Max(1, Math.Min(count, ushort.MaxValue));
            Send(_vendorBuy, DeliveryMethod.ReliableOrdered);
        }

        public void SellItem(EntityGuid vendor, ItemPos pos)
        {
            _vendorSell.Vendor = vendor;
            _vendorSell.Pos = pos;
            Send(_vendorSell, DeliveryMethod.ReliableOrdered);
        }

        public void AcceptQuest(EntityGuid npc, uint questId)
        {
            _questAccept.Npc = npc;
            _questAccept.QuestId = questId;
            Send(_questAccept, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>Turns in a finished quest; <paramref name="choice"/> indexes its reward choices (255 = none).</summary>
        public void TurnInQuest(EntityGuid npc, uint questId, byte choice = QuestTurnIn.NoChoice)
        {
            _questTurnIn.Npc = npc;
            _questTurnIn.QuestId = questId;
            _questTurnIn.Choice = choice;
            Send(_questTurnIn, DeliveryMethod.ReliableOrdered);
        }

        public void AbandonQuest(uint questId)
        {
            _questAbandon.QuestId = questId;
            Send(_questAbandon, DeliveryMethod.ReliableOrdered);
        }

        public bool IsLootable(EntityGuid corpse) => _lootableCorpses.Contains(corpse);

        public QuestGiverStatus GetQuestGiverStatus(EntityGuid npc) => _questGivers.TryGetValue(npc, out var s) ? s : QuestGiverStatus.None;

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
            Inventory.Clear();
            Quests.Clear();
            ResetLoot();
            _lootableCorpses.Clear();
            _questGivers.Clear();
            _gossip.Npc = EntityGuid.Empty;
            _vendor.Vendor = EntityGuid.Empty;
        }

        private void ResetLoot()
        {
            Loot.Corpse = EntityGuid.Empty;
            Loot.Money = 0;
            Loot.Items.Clear();
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
                    case Opcode.InventoryUpdate: HandleInventory(ref r); break;
                    case Opcode.MoneyUpdate: HandleMoney(ref r); break;
                    case Opcode.ItemReceived: HandleItemReceived(ref r); break;
                    case Opcode.ActionFailed: HandleActionFailed(ref r); break;
                    case Opcode.LootWindow: HandleLootWindow(ref r); break;
                    case Opcode.LootableUpdate: HandleLootable(ref r); break;
                    case Opcode.GossipMenu: HandleGossip(ref r); break;
                    case Opcode.VendorInventory: HandleVendor(ref r); break;
                    case Opcode.QuestLogUpdate: HandleQuestLog(ref r); break;
                    case Opcode.QuestGiverStatusUpdate: HandleQuestStatus(ref r); break;
                    case Opcode.QuestCompleted: HandleQuestCompleted(ref r); break;
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
            LocalDisplayId = _welcome.DisplayId;
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
            // Out of sight: its marks are resent when it comes back into view.
            _lootableCorpses.Remove(_despawn.Guid);
            _questGivers.Remove(_despawn.Guid);
            var entity = World.Remove(_despawn.Guid);
            if (entity != null) EntityDespawned?.Invoke(entity);
            // A target that left our view (or vanished, e.g. a corpse) is no longer selectable.
            if (_despawn.Guid == Target)
            {
                Target = EntityGuid.Empty;
                TargetChanged?.Invoke(Target);
            }
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
            ExperienceReceived?.Invoke(_xp.Amount, _xp.RestedBonus, _xp.Victim);
        }

        private void HandleInventory(ref PacketReader r)
        {
            _inventoryUpdate.Read(ref r);
            Inventory.Apply(_inventoryUpdate);
            InventoryChanged?.Invoke();
        }

        private void HandleMoney(ref PacketReader r)
        {
            _moneyUpdate.Read(ref r);
            Inventory.Money = _moneyUpdate.Money;
            MoneyChanged?.Invoke(_moneyUpdate.Delta, _moneyUpdate.Source);
        }

        private void HandleItemReceived(ref PacketReader r)
        {
            _itemReceived.Read(ref r);
            ItemReceived?.Invoke(_itemReceived);
        }

        private void HandleActionFailed(ref PacketReader r)
        {
            _actionFailed.Read(ref r);
            ActionFailed?.Invoke(_actionFailed.Error, _actionFailed.Value);
        }

        private void HandleLootWindow(ref PacketReader r)
        {
            _lootWindow.Read(ref r);
            ResetLoot();
            if (_lootWindow.Open)
            {
                Loot.Corpse = _lootWindow.Corpse;
                Loot.Money = _lootWindow.Money;
                Loot.Items.AddRange(_lootWindow.Entries);
            }
            LootChanged?.Invoke();
        }

        private void HandleLootable(ref PacketReader r)
        {
            _lootableMsg.Read(ref r);
            if (_lootableMsg.Lootable) _lootableCorpses.Add(_lootableMsg.Corpse);
            else _lootableCorpses.Remove(_lootableMsg.Corpse);
            LootableChanged?.Invoke(_lootableMsg.Corpse);
        }

        private void HandleGossip(ref PacketReader r)
        {
            _gossip.Read(ref r);
            GossipOpened?.Invoke(_gossip);
        }

        private void HandleVendor(ref PacketReader r)
        {
            _vendor.Read(ref r);
            VendorOpened?.Invoke(_vendor);
        }

        private void HandleQuestLog(ref PacketReader r)
        {
            _questLog.Read(ref r);
            _progressScratch.Clear();
            _questStateScratch.Clear();
            Quests.Apply(_questLog, _progressScratch, _questStateScratch);
            QuestLogChanged?.Invoke();
            foreach (var (quest, objective, count) in _progressScratch) QuestProgressed?.Invoke(quest, objective, count);
            foreach (var (quest, state) in _questStateScratch) QuestStateChanged?.Invoke(quest, state);
        }

        private void HandleQuestStatus(ref PacketReader r)
        {
            _questStatus.Read(ref r);
            foreach (var e in _questStatus.Entries)
            {
                _questGivers[e.Npc] = e.Status;
                QuestGiverStatusChanged?.Invoke(e.Npc);
            }
        }

        private void HandleQuestCompleted(ref PacketReader r)
        {
            _questCompleted.Read(ref r);
            Quests.MarkCompleted(_questCompleted.QuestId);
            QuestCompleted?.Invoke(_questCompleted);
        }
    }
}
