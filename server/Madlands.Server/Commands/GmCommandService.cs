using System.Globalization;
using Madlands.Content;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;
using Serilog;

namespace Madlands.Server.Commands;

/// <summary>What a GM command did: a result code, a short text and structured fields.</summary>
public sealed class GmOutcome
{
    public GmOutcome(GmResultCode code, string message)
    {
        Code = code;
        Message = message;
    }

    public GmResultCode Code { get; }
    public string Message { get; }
    public List<KeyValuePair<string, string>> Data { get; } = new();
    public bool Ok => Code is GmResultCode.Ok or GmResultCode.Partial;

    public static GmOutcome Success(string message) => new(GmResultCode.Ok, message);
    public static GmOutcome Fail(GmResultCode code, string message) => new(code, message);

    public GmOutcome With(string key, object value)
    {
        Data.Add(new KeyValuePair<string, string>(key, Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""));
        return this;
    }
}

/// <summary>
/// Typed GM commands (docs/GMMenu): the developer menu, automation and the chat console all end here or in
/// its shared helpers. Every command is looked up by its stable id in <see cref="GmCommands"/>, needs a GM
/// account (default deny), gets its arguments validated against the shared schema, runs on the game thread
/// and is logged. Gameplay changes go through the existing systems (combat, items, quests, map).
/// </summary>
public sealed class GmCommandService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<GmCommandService>();
    private const float GeometryRadius = 40f;

    private delegate GmOutcome Handler(Player player, GmArgValues args, long now);

    private readonly Dictionary<string, Handler> _handlers = new(StringComparer.Ordinal);
    private readonly GmCommandResult _result = new();
    private readonly GmStatus _status = new();
    private readonly GmCatalog _catalog = new();
    private readonly GmDebugGeometry _geometry = new();
    private readonly List<Unit> _units = new();

    public GmCommandService()
    {
        _handlers[GmCommands.GodMode] = GodMode;
        _handlers[GmCommands.NoCooldown] = NoCooldown;
        _handlers[GmCommands.HealFull] = (p, a, n) => HealFull(p);
        _handlers[GmCommands.RestoreResources] = (p, a, n) => RestoreResources(p);
        _handlers[GmCommands.Revive] = (p, a, n) => ReviveSelf(p, n);
        _handlers[GmCommands.KillTarget] = KillTarget;
        _handlers[GmCommands.SetLevel] = (p, a, n) => SetLevel(p, p, (int)a.Int("level"), n);
        _handlers[GmCommands.AddXp] = AddXp;
        _handlers[GmCommands.AddGold] = AddGold;
        _handlers[GmCommands.AddItem] = AddItem;
        _handlers[GmCommands.SpeedMultiplier] = (p, a, n) => SetSpeed(p, (int)a.Int("percent"));
        _handlers[GmCommands.DamageMultiplier] = DamageMultiplier;
        _handlers[GmCommands.TeleportLocation] = TeleportLocation;
        _handlers[GmCommands.TeleportCoordinates] = TeleportCoordinates;
        _handlers[GmCommands.TeleportTarget] = TeleportTarget;
        _handlers[GmCommands.SpawnEntity] = SpawnEntity;
        _handlers[GmCommands.DespawnDebug] = (p, a, n) => DespawnDebug(p);
        _handlers[GmCommands.QuestStart] = (p, a, n) => QuestAction(p, "add", (uint)a.Int("quest"));
        _handlers[GmCommands.QuestComplete] = (p, a, n) => QuestAction(p, "complete", (uint)a.Int("quest"));
        _handlers[GmCommands.QuestReset] = (p, a, n) => QuestAction(p, "reset", (uint)a.Int("quest"));
        _handlers[GmCommands.WorldRespawn] = WorldRespawn;
        _handlers[GmCommands.InspectTarget] = (p, a, n) => InspectTarget(p, n);
        _handlers[GmCommands.DebugGeometry] = DebugGeometry;
        _handlers[GmCommands.ResetOverrides] = (p, a, n) => ResetOverrides(p);
        foreach (var info in GmCommands.All)
            if (info.Side == GmSide.Server && !_handlers.ContainsKey(info.Id))
                throw new InvalidOperationException($"GM command {info.Id} has no server handler.");
    }

    // ------------------------------------------------------------------ entry points

    /// <summary>A <see cref="GmCommand"/> from the network: runs it and answers with a <see cref="GmCommandResult"/>.</summary>
    public void Handle(Player player, GmCommand request, long now)
    {
        var outcome = Run(player, request.CommandId, request.Args, now);
        if (player.Map is not { } map) return;
        _result.RequestId = request.RequestId;
        _result.CommandId = request.CommandId ?? "";
        _result.Code = outcome.Code;
        _result.Message = Truncate(outcome.Message, 500);
        _result.Data.Clear();
        foreach (var d in outcome.Data.Take(GmCommandResult.MaxData))
            _result.Data.Add(new KeyValuePair<string, string>(Truncate(d.Key, GmCommands.MaxKeyBytes / 2), Truncate(d.Value, 120)));
        map.Send(player, _result);
        if (outcome.Ok && player.IsGm) SendStatus(player);
    }

    /// <summary>Runs a command by id with raw arguments: authorization, validation, the handler, the log line.</summary>
    public GmOutcome Run(Player player, string? commandId, IReadOnlyList<KeyValuePair<string, string>> args, long now)
    {
        var outcome = RunUnlogged(player, commandId, args, now);
        var level = outcome.Code is GmResultCode.Unauthorized ? Serilog.Events.LogEventLevel.Warning : Serilog.Events.LogEventLevel.Information;
        Log.Write(level, "GM command {CommandId} by {Player} (account {Account}, GM level {GmLevel}) args [{Args}] -> {Code}: {Message}",
            Truncate(commandId ?? "", GmCommands.MaxIdBytes), player, player.Session.AccountId, player.GmLevel, SanitizeArgs(args), outcome.Code, outcome.Message);
        return outcome;
    }

    private GmOutcome RunUnlogged(Player player, string? commandId, IReadOnlyList<KeyValuePair<string, string>> args, long now)
    {
        var info = GmCommands.Find(commandId);
        if (info is null) return GmOutcome.Fail(GmResultCode.UnknownCommand, $"Unknown GM command '{Truncate(commandId ?? "", GmCommands.MaxIdBytes)}'.");
        // Default deny: rights come from the account (set-gm) or --gm-all, never from the client.
        if (player.GmLevel < info.MinGmLevel) return GmOutcome.Fail(GmResultCode.Unauthorized, "You are not allowed to use GM commands.");
        if (info.Side != GmSide.Server) return GmOutcome.Fail(GmResultCode.NotAvailable, $"{info.Id} runs in the client, not on the server.");
        if (player.Map is null) return GmOutcome.Fail(GmResultCode.NotAvailable, "You are not in the world.");
        string? error = GmCommands.Parse(info, args, out var values);
        if (error is not null) return GmOutcome.Fail(GmResultCode.InvalidArgument, error);
        try
        {
            return _handlers[info.Id](player, values, now);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GM command {CommandId} by {Player} failed", info.Id, player);
            return GmOutcome.Fail(GmResultCode.ExecutionFailed, $"{info.Id} failed on the server.");
        }
    }

    /// <summary>On entering the world: GMs learn their rights and the catalogs (others get nothing).</summary>
    public void OnEnteredWorld(Player player)
    {
        if (!player.IsGm || player.Map is null) return;
        SendStatus(player);
        SendCatalog(player);
    }

    /// <summary>The player leaves the world: the creatures they spawned go too (overrides end with the Player object).</summary>
    public void OnLeavingWorld(Player player)
    {
        if (player.GmDebugSpawns.Count > 0) DespawnDebug(player);
    }

    public void SendStatus(Player player)
    {
        if (player.Map is not { } map) return;
        PruneDebugSpawns(player);
        _status.GmLevel = (byte)Math.Clamp(player.GmLevel, 0, 255);
        _status.Overrides = (player.GodMode ? GmOverrides.GodMode : GmOverrides.None) | (player.GmNoCooldowns ? GmOverrides.NoCooldowns : GmOverrides.None);
        _status.SpeedPercent = (ushort)Math.Clamp((int)MathF.Round(player.GmSpeed * 100f), 0, ushort.MaxValue);
        _status.DamagePercent = (ushort)Math.Clamp(player.GmDamagePercent, 0, ushort.MaxValue);
        _status.DebugSpawns = (ushort)player.GmDebugSpawns.Count;
        map.Send(player, _status);
    }

    private void SendCatalog(Player player)
    {
        var map = player.Map!;
        _catalog.MapId = map.MapId;
        _catalog.Creatures.Clear();
        foreach (var c in map.Content.Creatures.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            _catalog.Creatures.Add(new GmCreatureEntry
            {
                Id = (uint)c.Id,
                Name = Truncate(c.Name, 60),
                MinLevel = (byte)Math.Clamp(c.MinLevel, 0, 255),
                MaxLevel = (byte)Math.Clamp(c.MaxLevel, 0, 255),
                Reaction = map.Content.FactionTable.GetReaction(player.Faction, (ushort)c.Faction),
            });
        _catalog.Destinations.Clear();
        foreach (var d in Destinations(map).Take(GmCatalog.MaxEntries))
            _catalog.Destinations.Add(new GmDestination { Id = d.Id, Name = Truncate(d.Name, 60), Kind = d.Kind, X = d.Position.X, Z = d.Position.Z });
        map.Send(player, _catalog);
    }

    // ------------------------------------------------------------------ shared helpers (also used by the chat console)

    /// <summary>Full health; mana and energy too (rage builds in combat and is left alone).</summary>
    public static void HealFully(Unit who)
    {
        who.Health = who.MaxHealth;
        if (who.PowerType != PowerType.Rage) who.Power = who.MaxPower;
    }

    /// <summary>Back to life where the player lies, at full health.</summary>
    public static void ReviveInPlace(Player who, long now)
    {
        who.Map!.Combat.Resurrect(who, now, atGraveyard: false);
        who.Health = who.MaxHealth;
    }

    /// <summary>Sets a level through the combat system (experience starts over at the new level).</summary>
    public static void ApplyLevel(Player who, int level, long now)
    {
        who.Xp = 0;
        who.Map!.Combat.SetLevel(who, level, now);
        who.StatsDirty = true;
    }

    public static void ApplySpeed(Player player, float percent)
    {
        player.GmSpeed = percent / 100f;
        player.RecalculateStats();
    }

    /// <summary>God mode: no damage, no costs or cooldowns, creatures drop the player from their threat lists.</summary>
    public static void SetGodMode(Player player, bool enabled)
    {
        player.GodMode = enabled;
        if (enabled) player.Map!.Combat.ClearAttackers(player);
    }

    /// <summary>Spawns creatures in a row in front of the player, on the ground. They do not respawn.</summary>
    public static List<Creature> SpawnInFront(Player player, CreatureDefinition template, int count, long now, bool snapToNavMesh)
    {
        var map = player.Map!;
        var spawned = new List<Creature>();
        var forward = MathUtil.YawToDirection(player.Yaw);
        var side = MathUtil.YawToDirection(player.Yaw + MathUtil.Pi / 2);
        for (int i = 0; i < count; i++)
        {
            var position = player.Position + forward * 6f + side * ((i - (count - 1) / 2f) * 2.5f);
            if (!map.HeightMap.Contains(position.X, position.Z)) continue;
            if (snapToNavMesh && map.Nav.TryGetNearest(position.WithY(map.GroundHeight(position.X, position.Z)), out var walkable))
                position = walkable;
            spawned.Add(map.SpawnCreature(template, position, player.Yaw + MathUtil.Pi, null, now));
        }
        return spawned;
    }

    /// <summary>The quest actions of the console and the menu: "add", "complete" or "reset".</summary>
    public static GmOutcome QuestAction(Player player, string action, uint questId)
    {
        var map = player.Map!;
        if (!map.Content.Quests.TryGetValue(questId, out var quest)) return GmOutcome.Fail(GmResultCode.InvalidArgument, $"No quest {questId}.");
        switch (action)
        {
            case "add":
                if (player.Quests.Find(quest.Id) is not null) return GmOutcome.Fail(GmResultCode.NotAvailable, $"{quest.Title} is already in your log.");
                if (player.Quests.IsFull) return GmOutcome.Fail(GmResultCode.NotAvailable, "Your quest log is full.");
                player.Quests.Unreward(quest.Id);
                player.Quests.Add(quest);
                break;
            case "complete":
                if (player.Quests.Find(quest.Id) is not { } progress) return GmOutcome.Fail(GmResultCode.NotAvailable, $"{quest.Title} is not in your log.");
                for (int i = 0; i < quest.Objectives.Count; i++)
                {
                    var o = quest.Objectives[i];
                    if (o.Type == ObjectiveType.Collect && map.Content.Items.TryGetValue(o.Item, out var item))
                        map.Items.Give(player, item, Math.Max(0, o.Count - player.Inventory.Count(o.Item)), ReceiveSource.Other);
                    else progress.Counts[i] = o.Count;
                }
                map.Items.Flush(player);
                break;
            case "reset":
                player.Quests.Remove(quest.Id);
                player.Quests.Unreward(quest.Id);
                break;
            default:
                return GmOutcome.Fail(GmResultCode.InvalidArgument, $"Unknown quest action '{action}'.");
        }
        map.Quests.SendLog(player);
        map.Quests.RefreshStatuses(player);
        return GmOutcome.Success($"Quest {quest.Id} '{quest.Title}': {action} done.").With("quest", quest.Id).With("title", quest.Title);
    }

    // ------------------------------------------------------------------ player and combat

    private static GmOutcome GodMode(Player player, GmArgValues args, long now)
    {
        SetGodMode(player, args.Bool("enabled"));
        return GmOutcome.Success(player.GodMode ? "God mode on." : "God mode off.").With("enabled", player.GodMode);
    }

    private static GmOutcome NoCooldown(Player player, GmArgValues args, long now)
    {
        player.GmNoCooldowns = args.Bool("enabled");
        if (player.GmNoCooldowns) player.Map!.Spells.ResetCooldowns(player);
        return GmOutcome.Success(player.GmNoCooldowns ? "No cooldowns: spells start no cooldowns (the global cooldown stays)." : "Cooldowns are normal again.")
            .With("enabled", player.GmNoCooldowns);
    }

    private static GmOutcome HealFull(Player player)
    {
        if (player.IsDead) return GmOutcome.Fail(GmResultCode.NotAvailable, "You are dead: revive first.");
        HealFully(player);
        return GmOutcome.Success("Fully healed.").With("health", player.Health).With("power", player.Power);
    }

    private static GmOutcome RestoreResources(Player player)
    {
        if (player.IsDead) return GmOutcome.Fail(GmResultCode.NotAvailable, "You are dead: revive first.");
        if (player.PowerType == PowerType.Rage) return GmOutcome.Fail(GmResultCode.NotAvailable, "Rage is not restored: it builds in combat.");
        player.Power = player.MaxPower;
        return GmOutcome.Success($"{player.PowerType} restored.").With("power", player.Power).With("powerType", player.PowerType);
    }

    private static GmOutcome ReviveSelf(Player player, long now)
    {
        if (player.IsAlive) return GmOutcome.Fail(GmResultCode.NotAvailable, "You are not dead.");
        ReviveInPlace(player, now);
        return GmOutcome.Success("You live again.").With("health", player.Health);
    }

    private static GmOutcome KillTarget(Player player, GmArgValues args, long now)
    {
        var map = player.Map!;
        if (map.FindUnit(player.TargetGuid) is not { } target || target == player || target.IsDead)
            return GmOutcome.Fail(GmResultCode.NoTarget, "Select a living enemy first.");
        if (map.GetReaction(player, target) == Reaction.Friendly)
            return GmOutcome.Fail(GmResultCode.NoTarget, $"{target.Name} is friendly: select an enemy.");
        map.Combat.Kill(target, player, now);
        return GmOutcome.Success($"Killed {target.Name}.").With("target", target.Guid);
    }

    private static GmOutcome SetLevel(Player player, Player who, int level, long now)
    {
        int max = player.Map!.Content.Levels.MaxLevel;
        if (level < 1 || level > max) return GmOutcome.Fail(GmResultCode.InvalidArgument, $"Levels go from 1 to {max}.");
        ApplyLevel(who, level, now);
        return GmOutcome.Success($"{who.Name} is now level {level}.").With("level", who.Level);
    }

    private static GmOutcome AddXp(Player player, GmArgValues args, long now)
    {
        var map = player.Map!;
        if (player.Level >= map.Content.Levels.MaxLevel) return GmOutcome.Fail(GmResultCode.NotAvailable, "You are at the level cap.");
        int amount = (int)args.Int("amount");
        map.Combat.GiveXp(player, amount, EntityGuid.Empty, now);
        return GmOutcome.Success($"Gave you {amount} experience.").With("level", player.Level).With("xp", player.Xp);
    }

    private static GmOutcome AddGold(Player player, GmArgValues args, long now)
    {
        long copper = args.Int("copper");
        player.Map!.Items.GiveMoney(player, copper, ReceiveSource.Other);
        return GmOutcome.Success($"You have {Money.Format(player.Inventory.Money)}.").With("money", player.Inventory.Money);
    }

    private static GmOutcome AddItem(Player player, GmArgValues args, long now)
    {
        var map = player.Map!;
        uint id = (uint)args.Int("item");
        if (!map.Content.Items.TryGetValue(id, out var item)) return GmOutcome.Fail(GmResultCode.InvalidArgument, $"No item {id}.");
        int count = (int)args.Int("count", 1);
        int added = map.Items.Give(player, item, count, ReceiveSource.Other);
        map.Items.Flush(player);
        var outcome = added == count
            ? GmOutcome.Success($"Added {count} x {item.Name}.")
            : added == 0
                ? GmOutcome.Fail(GmResultCode.NotAvailable, $"Your bags are full: no {item.Name} added.")
                : new GmOutcome(GmResultCode.Partial, $"Added {added} of {count} x {item.Name}: your bags are full.");
        return outcome.With("item", id).With("requested", count).With("added", added);
    }

    private static GmOutcome SetSpeed(Player player, int percent)
    {
        ApplySpeed(player, percent);
        return GmOutcome.Success($"Speed {percent}%.").With("percent", percent);
    }

    private static GmOutcome DamageMultiplier(Player player, GmArgValues args, long now)
    {
        player.GmDamagePercent = (int)args.Int("percent");
        return GmOutcome.Success($"Your damage is {player.GmDamagePercent}% of normal.").With("percent", player.GmDamagePercent);
    }

    private static GmOutcome ResetOverrides(Player player)
    {
        player.GodMode = false;
        player.GmNoCooldowns = false;
        player.GmDamagePercent = 100;
        if (player.GmSpeed != 1f) ApplySpeed(player, 100);
        return GmOutcome.Success("God mode, no cooldowns, speed and damage are back to normal.");
    }

    // ------------------------------------------------------------------ teleport

    private sealed record Destination(string Id, string Name, GmDestinationKind Kind, Vec3 Position, float Yaw);

    /// <summary>
    /// Safe places from the map's own data: the start, the rest areas, every NPC's spawn point (a few meters in
    /// front of the NPC) and the first spawn point of every other creature (a little off it).
    /// </summary>
    private static IEnumerable<Destination> Destinations(MapInstance map)
    {
        var definition = map.Map.Definition;
        yield return new Destination("start", definition.Name, GmDestinationKind.Start, map.Map.SpawnPosition, map.Map.SpawnYaw);
        for (int i = 0; i < definition.RestAreas.Count; i++)
        {
            var rest = definition.RestAreas[i];
            yield return new Destination($"rest:{i}", rest.Name, GmDestinationKind.Rest, new Vec3(rest.X, 0f, rest.Z), 0f);
        }
        var seen = new HashSet<int>();
        foreach (var spawn in map.Map.Spawns)
        {
            if (!seen.Add(spawn.Creature) || !map.Content.Creatures.TryGetValue(spawn.Creature, out var template)) continue;
            bool npc = map.Content.NpcFlagsOf(template) != NpcFlags.None || template.Role != NpcRole.None;
            float yaw = spawn.YawDegrees * MathUtil.Deg2Rad;
            var at = new Vec3(spawn.X, 0f, spawn.Z) + MathUtil.YawToDirection(yaw) * (npc ? 3f : 8f);
            yield return new Destination(npc ? $"npc:{template.Id}" : $"creature:{template.Id}", template.Name,
                npc ? GmDestinationKind.Npc : GmDestinationKind.Creature, at, yaw + MathUtil.Pi);
        }
    }

    /// <summary>Teleports onto the ground, onto the navmesh where there is one nearby (not into a wall or a house).</summary>
    private static GmOutcome TeleportSafely(Player player, float x, float z, float yaw, long now, string what)
    {
        var map = player.Map!;
        if (!map.HeightMap.Contains(x, z)) return GmOutcome.Fail(GmResultCode.InvalidArgument, "That is outside the map.");
        var position = new Vec3(x, map.GroundHeight(x, z), z);
        bool onNavMesh = map.Nav.TryGetNearest(position, out var walkable);
        if (onNavMesh) position = walkable;
        if (player.IsDead) return GmOutcome.Fail(GmResultCode.NotAvailable, "You are dead: revive first.");
        map.Teleport(player, position, yaw, now);
        return GmOutcome.Success(string.Format(CultureInfo.InvariantCulture, "Teleported to {0} ({1:0.0}, {2:0.0}).", what, position.X, position.Z))
            .With("x", position.X.ToString("0.00", CultureInfo.InvariantCulture))
            .With("y", position.Y.ToString("0.00", CultureInfo.InvariantCulture))
            .With("z", position.Z.ToString("0.00", CultureInfo.InvariantCulture))
            .With("navmesh", onNavMesh);
    }

    private static GmOutcome TeleportLocation(Player player, GmArgValues args, long now)
    {
        string id = args.Text("location")!;
        var destination = Destinations(player.Map!).FirstOrDefault(d => d.Id == id);
        if (destination is null) return GmOutcome.Fail(GmResultCode.InvalidArgument, $"No destination '{id}' on this map.");
        return TeleportSafely(player, destination.Position.X, destination.Position.Z, destination.Yaw, now, destination.Name).With("location", id);
    }

    private static GmOutcome TeleportCoordinates(Player player, GmArgValues args, long now) =>
        TeleportSafely(player, args.Float("x"), args.Float("z"), player.Yaw, now, "coordinates");

    private static GmOutcome TeleportTarget(Player player, GmArgValues args, long now)
    {
        var map = player.Map!;
        if (map.FindUnit(player.TargetGuid) is not { } target || target == player) return GmOutcome.Fail(GmResultCode.NoTarget, "Select a unit first.");
        // Two and a half meters from the target, on the side the player comes from, facing it.
        var away = player.Position - target.Position;
        away = new Vec3(away.X, 0f, away.Z);
        float length = MathF.Sqrt(away.X * away.X + away.Z * away.Z);
        var offset = length > 0.1f ? away * (2.5f / length) : MathUtil.YawToDirection(target.Yaw) * 2.5f;
        var at = target.Position + offset;
        float yaw = MathF.Atan2(-offset.X, -offset.Z);
        return TeleportSafely(player, at.X, at.Z, yaw, now, target.Name).With("target", target.Guid);
    }

    // ------------------------------------------------------------------ spawning

    private static void PruneDebugSpawns(Player player)
    {
        var map = player.Map;
        player.GmDebugSpawns.RemoveAll(guid => map?.Find(guid) is not Creature);
    }

    private static GmOutcome SpawnEntity(Player player, GmArgValues args, long now)
    {
        var map = player.Map!;
        int id = (int)args.Int("creature");
        if (!map.Content.Creatures.TryGetValue(id, out var template)) return GmOutcome.Fail(GmResultCode.InvalidArgument, $"No creature {id}.");
        int count = (int)args.Int("count", 1);
        PruneDebugSpawns(player);
        if (player.GmDebugSpawns.Count + count > GmCommands.MaxDebugSpawns)
            return GmOutcome.Fail(GmResultCode.InvalidArgument,
                $"At most {GmCommands.MaxDebugSpawns} debug spawns at a time ({player.GmDebugSpawns.Count} now): despawn some first.");
        var spawned = SpawnInFront(player, template, count, now, snapToNavMesh: true);
        foreach (var c in spawned) player.GmDebugSpawns.Add(c.Guid);
        if (spawned.Count == 0) return GmOutcome.Fail(GmResultCode.ExecutionFailed, "No place in front of you is inside the map.");
        var outcome = spawned.Count == count
            ? GmOutcome.Success($"Spawned {count} x {template.Name}.")
            : new GmOutcome(GmResultCode.Partial, $"Spawned {spawned.Count} of {count} x {template.Name}: the rest would be outside the map.");
        return outcome.With("creature", id).With("spawned", spawned.Count).With("debugSpawns", player.GmDebugSpawns.Count);
    }

    /// <summary>Removes the creatures this GM spawned with the menu; ordinary creatures are never touched.</summary>
    private static GmOutcome DespawnDebug(Player player)
    {
        var map = player.Map;
        int removed = 0;
        foreach (var guid in player.GmDebugSpawns)
        {
            if (map?.Find(guid) is not Creature creature || creature.Spawn is not null) continue;
            map.RemoveEntity(creature);
            removed++;
        }
        player.GmDebugSpawns.Clear();
        return GmOutcome.Success(removed == 1 ? "Removed 1 debug spawn." : $"Removed {removed} debug spawns.").With("removed", removed);
    }

    private static GmOutcome WorldRespawn(Player player, GmArgValues args, long now)
    {
        float radius = args.Int("radius", 60);
        int count = player.Map!.RespawnNear(player.Position, radius, now);
        return GmOutcome.Success($"Respawned {count} creature(s) within {radius:0} m.").With("respawned", count);
    }

    // ------------------------------------------------------------------ debugging

    private static GmOutcome InspectTarget(Player player, long now)
    {
        var map = player.Map!;
        if (map.FindUnit(player.TargetGuid) is not { } t) return GmOutcome.Fail(GmResultCode.NoTarget, "Select a unit first.");
        var outcome = GmOutcome.Success($"{t.Name}: level {t.Level}, health {t.Health}/{t.MaxHealth}.")
            .With("name", t.Name).With("guid", t.Guid).With("level", t.Level)
            .With("health", $"{t.Health}/{t.MaxHealth}").With("power", $"{t.Power}/{t.MaxPower} {t.PowerType}")
            .With("reaction", map.GetReaction(player, t)).With("flags", t.Flags).With("speed", $"{t.SpeedPercent}%")
            .With("distance", player.DistanceTo(t).ToString("0.0", CultureInfo.InvariantCulture));
        if (t.Auras.Count > 0) outcome.With("auras", string.Join(", ", t.Auras.Select(a => $"{a.Spell.Name} ({a.RemainingMs(now) / 1000}s)")));
        if (t is Creature c)
        {
            outcome.With("creature", c.Template.Id).With("ai", c.Ai.Current)
                .With("home", string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}", c.Home.X, c.Home.Z))
                .With("spawn", c.Spawn is null ? "debug or command spawn (no respawn)" : "spawn point")
                .With("threat", c.Threat.Count == 0 ? "none" : string.Join(", ", c.Threat.Units.OrderByDescending(u => c.Threat.Get(u)).Take(5)
                    .Select(u => string.Format(CultureInfo.InvariantCulture, "{0} {1:0}", u.Name, c.Threat.Get(u)))));
        }
        return outcome;
    }

    private GmOutcome DebugGeometry(Player player, GmArgValues args, long now)
    {
        var map = player.Map!;
        float radius = args.Int("radius", (long)GeometryRadius);
        string kind = args.Text("kind")!;
        _geometry.Points.Clear();
        _geometry.Center = player.Position;
        _geometry.Radius = radius;
        bool complete;
        switch (kind)
        {
            case GmCommands.GeometryCollision:
                _geometry.Kind = GmGeometryKind.Collision;
                complete = CollisionEdges(map, player.Position, radius, _geometry.Points);
                break;
            case GmCommands.GeometryNavMesh:
                _geometry.Kind = GmGeometryKind.NavMesh;
                complete = map.NavMesh.CollectPolygonEdges(player.Position, radius, _geometry.Points, GmDebugGeometry.MaxSegments);
                break;
            case GmCommands.GeometrySpawnPoints:
                _geometry.Kind = GmGeometryKind.SpawnPoints;
                complete = SpawnMarkers(map, player.Position, radius, _geometry.Points);
                break;
            default:
                return GmOutcome.Fail(GmResultCode.InvalidArgument, $"Unknown geometry '{kind}': use collision, navmesh or spawn_points.");
        }
        _geometry.Truncated = !complete;
        map.Send(player, _geometry);
        return GmOutcome.Success($"{_geometry.SegmentCount} {kind} segments within {radius:0} m{(complete ? "" : " (cut short)")}.")
            .With("kind", kind).With("segments", _geometry.SegmentCount).With("truncated", !complete);
    }

    /// <summary>The edges of the obstacle triangles (collision.bin: what movement and line of sight use) near a point.</summary>
    private static bool CollisionEdges(MapInstance map, Vec3 center, float radius, List<Vec3> points)
    {
        var mesh = map.Map.Collision;
        float r2 = radius * radius;
        var seen = new HashSet<long>();
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int i0 = mesh.Indices[t * 3], i1 = mesh.Indices[t * 3 + 1], i2 = mesh.Indices[t * 3 + 2];
            Vec3 a = mesh.GetVertex(i0), b = mesh.GetVertex(i1), c = mesh.GetVertex(i2);
            if (DistanceSq(a, center) > r2 && DistanceSq(b, center) > r2 && DistanceSq(c, center) > r2) continue;
            if (!AddEdge(seen, points, i0, i1, a, b) || !AddEdge(seen, points, i1, i2, b, c) || !AddEdge(seen, points, i2, i0, c, a)) return false;
        }
        return true;
    }

    private static bool AddEdge(HashSet<long> seen, List<Vec3> points, int i, int j, Vec3 a, Vec3 b)
    {
        long key = i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;
        if (!seen.Add(key)) return true;
        if (points.Count / 2 >= GmDebugGeometry.MaxSegments) return false;
        points.Add(a);
        points.Add(b);
        return true;
    }

    /// <summary>A post and a cross on the ground for every creature spawn point near a point.</summary>
    private static bool SpawnMarkers(MapInstance map, Vec3 center, float radius, List<Vec3> points)
    {
        float r2 = radius * radius;
        foreach (var spawn in map.Map.Spawns)
        {
            var ground = new Vec3(spawn.X, map.GroundHeight(spawn.X, spawn.Z), spawn.Z);
            if (DistanceSq(ground, center) > r2) continue;
            if (points.Count / 2 + 3 > GmDebugGeometry.MaxSegments) return false;
            points.Add(ground);
            points.Add(ground + new Vec3(0f, 3f, 0f));
            points.Add(ground + new Vec3(-0.8f, 0.05f, 0f));
            points.Add(ground + new Vec3(0.8f, 0.05f, 0f));
            points.Add(ground + new Vec3(0f, 0.05f, -0.8f));
            points.Add(ground + new Vec3(0f, 0.05f, 0.8f));
        }
        return true;
    }

    private static float DistanceSq(Vec3 a, Vec3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    // ------------------------------------------------------------------ logging

    private static string SanitizeArgs(IReadOnlyList<KeyValuePair<string, string>> args) =>
        args is null ? "" : string.Join(", ", args.Take(GmCommands.MaxArgs).Select(a => $"{Truncate(Clean(a.Key), 32)}={Truncate(Clean(a.Value), 64)}"));

    private static string Clean(string? text) => text is null ? "" : new string(text.Where(c => !char.IsControl(c)).ToArray());

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
