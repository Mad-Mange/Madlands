using LiteNetLib;
using Madlands.Content;
using Madlands.Navigation;
using Madlands.Server.Combat;
using Madlands.Server.Items;
using Madlands.Server.Quests;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;
using Madlands.Shared.Terrain;
using Serilog;

namespace Madlands.Server.World;

/// <summary>
/// One running copy of a map: the open world (instance 1), and later dungeon instances or
/// overflow "layers" of the same map. An instance owns its entities and shares no mutable state
/// with other instances, so instances can later run on their own threads or processes.
/// Game rules live in its systems: <see cref="Combat"/>, <see cref="Spells"/>, <see cref="Auras"/>,
/// <see cref="Items"/>, <see cref="Loot"/> and <see cref="Quests"/>.
/// </summary>
public sealed class MapInstance
{
    public const float ViewRadius = 150f;
    public const float DespawnHysteresis = 20f;
    /// <summary>Changed states are repeated to observers this long, covering lost unreliable packets.</summary>
    public const long RecentChangeWindowMs = 1000;
    /// <summary>Visibility and movement broadcasts run every N ticks (20 Hz / 2 = 10 Hz).</summary>
    public const int BroadcastEveryTicks = 2;
    /// <summary>
    /// Movement of entities within this range goes out every broadcast (10 Hz), up to <see cref="MidMoveRange"/> every 2nd
    /// (5 Hz), further away every 4th (2.5 Hz). States are dead-reckoned in between, so far-away movement only costs smoothness.
    /// </summary>
    public const float NearMoveRange = 50f;
    public const float MidMoveRange = 100f;
    /// <summary>Slack on <see cref="GameConstants.InteractRange"/> for a target that moved while the request was in flight.</summary>
    public const float InteractLeeway = 1f;

    private static readonly ILogger Log = Serilog.Log.ForContext<MapInstance>();

    private readonly WorldManager _world;
    private readonly AoiGrid _grid = new();
    private readonly Dictionary<EntityGuid, Entity> _entities = new();
    private readonly List<Player> _players = new();
    private readonly List<Creature> _creatures = new();
    private readonly List<Entity> _queryScratch = new();
    private readonly List<EntityGuid> _removeScratch = new();
    private readonly List<Creature> _despawnScratch = new();
    private readonly List<(SpawnDefinition Spawn, long AtMs)> _respawns = new();
    private readonly PacketWriter _writer = new(1500);
    private readonly EntitySpawn _spawnMsg = new();
    private readonly EntityDespawn _despawnMsg = new();
    private readonly EntityMoves _movesMsg = new();
    private readonly MoveCorrection _correctionMsg = new();
    private readonly UnitUpdates _unitUpdatesMsg = new();
    private readonly AuraUpdate _auraMsg = new();
    private readonly SpellStart _spellStartMsg = new();
    private readonly PlayerStats _statsMsg = new();
    private readonly ChatMessage _chatMsg = new();
    private long _lastTickMs = -1;
    private uint _broadcast;

    public MapInstance(LoadedMap map, uint instanceId, WorldManager world, NavMesh navMesh, CollisionWorld collision, int? randomSeed = null)
    {
        Map = map;
        InstanceId = instanceId;
        _world = world;
        Nav = navMesh.CreateQuery();
        Collision = collision;
        Random = randomSeed is { } seed ? new Random(seed) : new Random();
        Combat = new CombatSystem(this);
        Spells = new SpellSystem(this);
        Auras = new AuraSystem(this);
        Items = new ItemSystem(this);
        Loot = new LootSystem(this);
        Quests = new QuestSystem(this);
        foreach (var spawn in map.Spawns) SpawnFromDefinition(spawn, 0);
    }

    public LoadedMap Map { get; }
    public ushort MapId => Map.Id;
    public uint InstanceId { get; }
    public HeightMap HeightMap => Map.HeightMap;
    public ContentDatabase Content => _world.Content;
    public NavQuery Nav { get; }
    public CollisionWorld Collision { get; }
    public Random Random { get; }
    public CombatSystem Combat { get; }
    public SpellSystem Spells { get; }
    public AuraSystem Auras { get; }
    public ItemSystem Items { get; }
    public LootSystem Loot { get; }
    public QuestSystem Quests { get; }
    /// <summary>Server time of the current (or last) tick.</summary>
    public long NowMs { get; private set; }

    public int EntityCount => _entities.Count;
    /// <summary>Loop thread only.</summary>
    public IEnumerable<Entity> Entities => _entities.Values;
    public IReadOnlyList<Player> Players => _players;
    public IReadOnlyList<Creature> Creatures => _creatures;
    public int PlayerCount => _players.Count;
    public long CorrectionsSent { get; private set; }

    // ------------------------------------------------------------------ entities

    public void AddPlayer(Player player, long nowMs)
    {
        player.Map = this;
        player.MovementTimeMs = nowMs;
        player.LastChangeMs = nowMs;
        player.Validator.Reset(player.Movement, nowMs);
        _entities.Add(player.Guid, player);
        _players.Add(player);
        _grid.Add(player);
    }

    public Creature SpawnCreature(CreatureDefinition template, Vec3 position, float yaw, SpawnDefinition? spawn, long nowMs)
    {
        byte level = (byte)Random.Next(template.MinLevel, template.MaxLevel + 1);
        var ground = position.WithY(HeightMap.GetHeight(position.X, position.Z));
        var creature = new Creature(_world.NextCreatureGuid(), template, spawn, ground, yaw, level)
        {
            Map = this,
            MovementTimeMs = nowMs,
            LastChangeMs = nowMs,
            NpcFlags = Content.NpcFlagsOf(template),
            MainHandDisplay = Content.Items.GetValueOrDefault(template.Equipment.MainHand)?.DisplayId ?? 0,
            OffHandDisplay = Content.Items.GetValueOrDefault(template.Equipment.OffHand)?.DisplayId ?? 0,
        };
        _entities.Add(creature.Guid, creature);
        _creatures.Add(creature);
        _grid.Add(creature);
        return creature;
    }

    public void RemoveEntity(Entity entity)
    {
        if (!_entities.Remove(entity.Guid)) return;
        _grid.Remove(entity);
        if (entity is Unit unit) Combat.OnUnitRemoved(unit);
        if (entity is Creature lootable) Loot.OnCreatureRemoved(lootable);
        if (entity is Player player)
        {
            Loot.Close(player, notify: false);
            _players.Remove(player);
            foreach (var known in player.Known.Values) known.Observers.Remove(player);
            player.Known.Clear();
        }
        if (entity is Creature creature) _creatures.Remove(creature);
        foreach (var observer in entity.Observers)
        {
            if (observer.Known.Remove(entity.Guid)) SendDespawn(observer, entity.Guid);
        }
        entity.Observers.Clear();
        entity.Map = null;
    }

    public Entity? Find(EntityGuid guid) => _entities.GetValueOrDefault(guid);
    public Unit? FindUnit(EntityGuid guid) => guid.IsEmpty ? null : _entities.GetValueOrDefault(guid) as Unit;

    /// <summary>Units whose horizontal distance to <paramref name="center"/> is at most <paramref name="radius"/>.</summary>
    public void QueryUnits(Vec3 center, float radius, List<Unit> results)
    {
        _queryScratch.Clear();
        _grid.Query(center, radius, _queryScratch);
        foreach (var e in _queryScratch)
            if (e is Unit u) results.Add(u);
    }

    /// <summary>Call after changing an entity's position outside of player movement.</summary>
    internal void OnMoved(Entity entity) => _grid.Move(entity);

    public Reaction GetReaction(Unit a, Unit b) => Content.FactionTable.GetReaction(a.Faction, b.Faction);

    /// <summary>A may attack B: B is alive and not friendly to A.</summary>
    public bool CanAttack(Unit a, Unit b) => a != b && b.IsAlive && GetReaction(a, b) != Reaction.Friendly;

    public bool IsFriendly(Unit a, Unit b) => GetReaction(a, b) == Reaction.Friendly;

    public bool HasLineOfSight(Unit a, Unit b) => Collision.HasLineOfSight(a.EyePosition, b.EyePosition);

    public float GroundHeight(float x, float z) => HeightMap.GetHeight(x, z);

    /// <summary>
    /// The creature a player wants to talk to (alive, not hostile) or loot (<paramref name="corpse"/>: dead),
    /// if it is within reach. Otherwise null and why.
    /// </summary>
    public Creature? FindInteractable(Player player, EntityGuid guid, out ActionError error, bool corpse = false)
    {
        error = ActionError.None;
        if (Find(guid) is not Creature creature || player.IsDead || creature.IsDead != corpse ||
            (!corpse && GetReaction(player, creature) == Reaction.Hostile))
        {
            error = ActionError.CantInteract;
            return null;
        }
        if (Vec3.Distance(player.Position, creature.Position) > GameConstants.InteractRange + InteractLeeway)
        {
            error = ActionError.TooFarAway;
            return null;
        }
        return creature;
    }

    // ------------------------------------------------------------------ movement

    public void HandleMove(Player player, MoveUpdate update, long nowMs)
    {
        // Updates sent before the client applied our latest correction are meaningless now.
        if (update.CorrectionAck != player.CorrectionId) return;

        switch (player.Validator.Validate(update.State, update.ClientTimeMs, nowMs))
        {
            case MoveVerdict.Accepted:
                var previous = player.Movement.Position;
                if (!update.State.Equals(player.Movement)) player.LastChangeMs = nowMs;
                player.Movement = update.State;
                player.MovementTimeMs = nowMs;
                _grid.Move(player);
                Spells.OnCasterMoved(player, previous, nowMs);
                break;
            case MoveVerdict.Rejected:
                Correct(player, nowMs, player.Validator.LastRejectReason);
                break;
            case MoveVerdict.Stale:
                break;
        }
    }

    private void Correct(Player player, long nowMs, string reason)
    {
        var state = player.Validator.LastAccepted;
        state.Position = state.Position.WithY(Math.Max(state.Position.Y, HeightMap.GetHeight(state.Position.X, state.Position.Z)));
        Teleport(player, state.Position, state.Yaw, nowMs);
        CorrectionsSent++;
        Log.Debug("Corrected {Player}: {Reason}", player, reason);
    }

    /// <summary>Moves a player (server-initiated: corrections, GM teleports, resurrection). The client must accept it.</summary>
    public void Teleport(Player player, Vec3 position, float yaw, long nowMs)
    {
        var state = MovementState.Idle(position, yaw);
        player.CorrectionId++;
        player.Movement = state;
        player.MovementTimeMs = nowMs;
        player.LastChangeMs = nowMs;
        player.Validator.Reset(state, nowMs);
        _grid.Move(player);
        _correctionMsg.CorrectionId = player.CorrectionId;
        _correctionMsg.State = state;
        player.Session.Send(_writer, _correctionMsg, DeliveryMethod.ReliableOrdered);
    }

    // ------------------------------------------------------------------ tick

    public void Tick(long nowMs, long tick)
    {
        NowMs = nowMs;
        float dt = _lastTickMs < 0 ? 0f : Math.Clamp((nowMs - _lastTickMs) / 1000f, 0f, 0.25f);
        _lastTickMs = nowMs;

        for (int i = 0; i < _players.Count; i++)
        {
            Combat.UpdateUnit(_players[i], nowMs);
            Quests.UpdateExplore(_players[i], nowMs);
            Combat.UpdateRest(_players[i], nowMs);
        }
        for (int i = 0; i < _creatures.Count; i++)
        {
            var creature = _creatures[i];
            creature.Ai.Update(nowMs);
            creature.Mover.Update(nowMs, dt);
            Combat.UpdateUnit(creature, nowMs);
        }
        Spells.UpdateProjectiles(nowMs);
        UpdateDespawnsAndRespawns(nowMs);

        if (tick % BroadcastEveryTicks != 0) return;
        _broadcast++;
        Loot.Update();
        foreach (var player in _players)
        {
            UpdateVisibility(player, nowMs);
            SendMoves(player, nowMs, _broadcast);
            SendUnitUpdates(player);
            if (player.StatsDirty) SendPlayerStats(player);
        }
        foreach (var player in _players) player.Dirty = UnitFieldMask.None;
        foreach (var creature in _creatures) creature.Dirty = UnitFieldMask.None;
    }

    private void UpdateDespawnsAndRespawns(long nowMs)
    {
        _despawnScratch.Clear();
        foreach (var c in _creatures)
            if (c.IsDead && nowMs >= c.DespawnAtMs) _despawnScratch.Add(c);
        foreach (var c in _despawnScratch)
        {
            RemoveEntity(c);
            if (c.Spawn is { } spawn)
            {
                int seconds = spawn.RespawnSeconds > 0 ? spawn.RespawnSeconds : c.Template.RespawnSeconds;
                _respawns.Add((spawn, nowMs + seconds * 1000L));
            }
        }

        for (int i = _respawns.Count - 1; i >= 0; i--)
        {
            if (nowMs < _respawns[i].AtMs) continue;
            SpawnFromDefinition(_respawns[i].Spawn, nowMs);
            _respawns.RemoveAt(i);
        }
    }

    /// <summary>GM: brings back everything that is dead or waiting to respawn near a point.</summary>
    public int RespawnNear(Vec3 center, float radius, long nowMs)
    {
        int count = 0;
        foreach (var c in _creatures)
        {
            if (c.IsDead && Vec3.HorizontalDistance(c.Home, center) <= radius)
            {
                c.DespawnAtMs = nowMs;
                count++;
            }
        }
        UpdateDespawnsAndRespawns(nowMs);
        for (int i = _respawns.Count - 1; i >= 0; i--)
        {
            var s = _respawns[i].Spawn;
            if (Vec3.HorizontalDistance(new Vec3(s.X, 0, s.Z), center.WithY(0)) > radius) continue;
            SpawnFromDefinition(s, nowMs);
            _respawns.RemoveAt(i);
            count++;
        }
        return count;
    }

    private void SpawnFromDefinition(SpawnDefinition spawn, long nowMs)
    {
        if (!Content.Creatures.TryGetValue(spawn.Creature, out var template)) return;
        SpawnCreature(template, new Vec3(spawn.X, 0, spawn.Z), spawn.YawDegrees * MathUtil.Deg2Rad, spawn, nowMs);
    }

    // ------------------------------------------------------------------ broadcasting

    /// <summary>Sends to every player who sees <paramref name="unit"/>, the unit itself if it is a player, and <paramref name="alsoTo"/>.</summary>
    internal void SendToObservers<T>(Entity unit, T message, bool includeSelf = true, Player? alsoTo = null,
        DeliveryMethod method = DeliveryMethod.ReliableOrdered) where T : IMessage
    {
        Packet.Encode(_writer, message);
        var bytes = _writer.AsSpan();
        foreach (var observer in unit.Observers)
        {
            if (observer == alsoTo) alsoTo = null;
            observer.Session.SendRaw(bytes, method);
        }
        if (includeSelf && unit is Player self)
        {
            if (self == alsoTo) alsoTo = null;
            self.Session.SendRaw(bytes, method);
        }
        alsoTo?.Session.SendRaw(bytes, method);
    }

    internal void Send<T>(Player player, T message, DeliveryMethod method = DeliveryMethod.ReliableOrdered) where T : IMessage =>
        player.Session.Send(_writer, message, method);

    public void SendSystemMessage(Player player, string text, ChatChannel channel = ChatChannel.System)
    {
        _chatMsg.Channel = channel;
        _chatMsg.Sender = EntityGuid.Empty;
        _chatMsg.SenderName = "";
        _chatMsg.Text = text;
        Send(player, _chatMsg);
    }

    /// <summary>"Say": everyone within <see cref="GameConstants.SayRange"/> of the speaker.</summary>
    public void Say(Player speaker, string text)
    {
        _chatMsg.Channel = ChatChannel.Say;
        _chatMsg.Sender = speaker.Guid;
        _chatMsg.SenderName = speaker.Name;
        _chatMsg.Text = text;
        Packet.Encode(_writer, _chatMsg);
        var bytes = _writer.AsSpan();
        speaker.Session.SendRaw(bytes, DeliveryMethod.ReliableOrdered);
        foreach (var observer in speaker.Observers)
            if (Vec3.Distance(observer.Position, speaker.Position) <= GameConstants.SayRange)
                observer.Session.SendRaw(bytes, DeliveryMethod.ReliableOrdered);
    }

    internal void SendAuraUpdate(Unit unit, Aura aura, bool removed, long nowMs)
    {
        _auraMsg.Unit = unit.Guid;
        _auraMsg.Full = false;
        _auraMsg.Entries.Clear();
        _auraMsg.Entries.Add(ToEntry(aura, removed, nowMs));
        SendToObservers(unit, _auraMsg);
    }

    private void SendAllAuras(Player observer, Unit unit, long nowMs)
    {
        if (unit.Auras.Count == 0) return;
        _auraMsg.Unit = unit.Guid;
        _auraMsg.Full = true;
        _auraMsg.Entries.Clear();
        foreach (var aura in unit.Auras)
        {
            if (_auraMsg.Entries.Count == AuraUpdate.MaxAuras) break;
            _auraMsg.Entries.Add(ToEntry(aura, false, nowMs));
        }
        Send(observer, _auraMsg);
    }

    private static AuraEntry ToEntry(Aura aura, bool removed, long nowMs) => new()
    {
        Slot = aura.Slot,
        SpellId = removed ? 0 : aura.Spell.Id,
        Caster = aura.Caster,
        Stacks = (byte)Math.Clamp(aura.Stacks, 1, 255),
        DurationMs = aura.DurationMs,
        RemainingMs = aura.RemainingMs(nowMs),
    };

    public void SendPlayerStats(Player player)
    {
        player.StatsDirty = false;
        _statsMsg.Xp = player.Xp;
        _statsMsg.XpToNextLevel = Content.Levels.XpToNext(player.Level);
        _statsMsg.RestedXp = (int)player.RestedXp;
        _statsMsg.Resting = player.Resting;
        StatFormulas.FillPlayerStats(_statsMsg, player);
        Send(player, _statsMsg);
    }

    private void UpdateVisibility(Player player, long nowMs)
    {
        _queryScratch.Clear();
        _grid.Query(player.Position, ViewRadius, _queryScratch);
        foreach (var entity in _queryScratch)
        {
            if (entity == player) continue;
            if (player.Known.TryAdd(entity.Guid, entity))
            {
                entity.Observers.Add(player);
                SendSpawn(player, entity, nowMs);
            }
        }

        _removeScratch.Clear();
        float maxSq = (ViewRadius + DespawnHysteresis) * (ViewRadius + DespawnHysteresis);
        foreach (var (guid, entity) in player.Known)
        {
            if (Vec3.HorizontalDistanceSquared(entity.Position, player.Position) > maxSq) _removeScratch.Add(guid);
        }
        foreach (var guid in _removeScratch)
        {
            if (player.Known.Remove(guid, out var entity)) entity.Observers.Remove(player);
            SendDespawn(player, guid);
        }
    }

    /// <summary>Every how many broadcasts an entity's movement is sent to an observer this far away (squared meters).</summary>
    public static uint MoveSendInterval(float distanceSquared) =>
        distanceSquared <= NearMoveRange * NearMoveRange ? 1u : distanceSquared <= MidMoveRange * MidMoveRange ? 2u : 4u;

    /// <summary>True if <paramref name="entity"/>'s movement is due this broadcast (entities are spread over the broadcasts).</summary>
    public static bool IsMoveDue(EntityGuid entity, uint interval, uint broadcast) => (broadcast + (uint)entity.Counter) % interval == 0;

    private void SendMoves(Player player, long nowMs, uint broadcast)
    {
        int maxEntries = EntityMoves.MaxEntriesFor(player.Session.MaxUnreliablePacketSize);
        _movesMsg.ServerTimeMs = nowMs;
        _movesMsg.Entries.Clear();
        foreach (var entity in player.Known.Values)
        {
            if (nowMs - entity.LastChangeMs > RecentChangeWindowMs) continue;
            uint interval = MoveSendInterval(Vec3.HorizontalDistanceSquared(entity.Position, player.Position));
            if (interval > 1 && !IsMoveDue(entity.Guid, interval, broadcast)) continue;
            _movesMsg.Entries.Add(new EntityMoveEntry
            {
                Guid = entity.Guid,
                AgeMs = (ushort)Math.Min(nowMs - entity.MovementTimeMs, ushort.MaxValue),
                State = entity.Movement,
            });
            if (_movesMsg.Entries.Count == maxEntries)
            {
                player.Session.Send(_writer, _movesMsg, DeliveryMethod.Unreliable);
                _movesMsg.Entries.Clear();
            }
        }
        if (_movesMsg.Entries.Count > 0) player.Session.Send(_writer, _movesMsg, DeliveryMethod.Unreliable);
    }

    /// <summary>Changed unit fields of the player itself and of every unit it sees, in one batch.</summary>
    private void SendUnitUpdates(Player player)
    {
        _unitUpdatesMsg.Entries.Clear();
        if (player.Dirty != UnitFieldMask.None)
            _unitUpdatesMsg.Entries.Add(new UnitUpdateEntry { Guid = player.Guid, Mask = player.Dirty, Values = player.Values });
        foreach (var entity in player.Known.Values)
        {
            if (entity is Unit unit && unit.Dirty != UnitFieldMask.None)
                _unitUpdatesMsg.Entries.Add(new UnitUpdateEntry { Guid = unit.Guid, Mask = unit.Dirty, Values = unit.Values });
        }
        if (_unitUpdatesMsg.Entries.Count > 0) Send(player, _unitUpdatesMsg);
    }

    private void SendSpawn(Player observer, Entity entity, long nowMs)
    {
        _spawnMsg.Guid = entity.Guid;
        _spawnMsg.Name = entity.Name;
        _spawnMsg.DisplayId = entity.DisplayId;
        _spawnMsg.StateServerTimeMs = entity.MovementTimeMs;
        _spawnMsg.State = entity.Movement;
        if (entity is Unit unit) _spawnMsg.Unit.CopyFrom(unit.Values);
        Send(observer, _spawnMsg);
        if (entity is not Unit u) return;

        SendAllAuras(observer, u, nowMs);
        if (u is Creature creature)
        {
            if ((creature.NpcFlags & NpcFlags.QuestGiver) != 0) Quests.SendStatus(observer, creature);
            if (creature.IsDead) Loot.OnCorpseSeen(observer, creature);
        }
        if (u.Casting is { } cast)
        {
            _spellStartMsg.Caster = u.Guid;
            _spellStartMsg.Target = cast.Target;
            _spellStartMsg.SpellId = cast.Spell.Id;
            _spellStartMsg.CastId = cast.CastId;
            _spellStartMsg.CastTimeMs = (int)Math.Max(0, cast.EndMs - nowMs);
            Send(observer, _spellStartMsg);
        }
    }

    private void SendDespawn(Player observer, EntityGuid guid)
    {
        _despawnMsg.Guid = guid;
        Send(observer, _despawnMsg);
    }
}
