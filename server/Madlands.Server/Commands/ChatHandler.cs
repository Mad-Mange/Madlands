using System.Globalization;
using System.Text;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Game;
using Serilog;

namespace Madlands.Server.Commands;

/// <summary>
/// Chat input: normal lines are said aloud (heard within 40 m); lines starting with '.' are GM
/// commands, allowed for accounts with a GM level (or everyone with --gm-all in development).
/// </summary>
public sealed class ChatHandler
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ChatHandler>();

    private sealed record Command(string Usage, string Help, Func<Player, string[], long, string?> Run);

    private readonly GameServer _server;
    private readonly SortedDictionary<string, Command> _commands = new(StringComparer.OrdinalIgnoreCase);

    public ChatHandler(GameServer server)
    {
        _server = server;
        Add("help", "[command]", "Lists the commands, or explains one.", Help);
        Add("gps", "", "Shows where you are.", Gps);
        Add("tele", "<x> <z> | spawn | <player>", "Teleports you.", Tele);
        Add("spawn", "<creature id or name> [count]", "Spawns creatures in front of you (they do not respawn).", Spawn);
        Add("level", "<level>", "Sets the level of your target player, or yours.", Level);
        Add("xp", "<amount>", "Gives you experience.", Xp);
        Add("kill", "", "Kills your target.", Kill);
        Add("damage", "<amount>", "Damages your target.", Damage);
        Add("heal", "", "Fully heals your target, or you.", Heal);
        Add("revive", "", "Brings your dead target player, or you, back to life on the spot.", Revive);
        Add("cd", "", "Resets your cooldowns.", Cooldowns);
        Add("god", "", "Toggles god mode: no damage, no costs, no cooldowns, creatures ignore you.", God);
        Add("speed", "<percent>", "Sets your movement speed (100 = normal).", Speed);
        Add("respawn", "[radius]", "Respawns dead creatures near you (default 60 m).", Respawn);
        Add("aura", "<spell id>", "Applies a spell's aura to your target, or you.", Aura);
        Add("cast", "<spell id>", "Casts any spell at your target, known or not.", Cast);
        Add("info", "", "Shows details about your target.", Info);
        Add("who", "", "Lists the players online.", Who);
        Add("additem", "<item id or name> [count]", "Puts items into your bags.", AddItem);
        Add("money", "<copper>", "Gives you money (negative takes it).", Money);
        Add("quest", "add|complete|reset <quest id>", "Adds a quest to your log, completes its objectives, or forgets it.", Quest);
    }

    private void Add(string name, string usage, string help, Func<Player, string[], long, string?> run) =>
        _commands.Add(name, new Command(usage, help, run));

    public void Handle(Player player, string? raw, long now)
    {
        var text = Sanitize(raw);
        if (text.Length == 0 || player.Map is not { } map) return;
        if (!text.StartsWith('.'))
        {
            map.Say(player, text);
            return;
        }

        if (!player.IsGm)
        {
            map.SendSystemMessage(player, "You are not allowed to use commands.");
            return;
        }
        var parts = text[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        if (!_commands.TryGetValue(parts[0], out var command))
        {
            map.SendSystemMessage(player, $"Unknown command .{parts[0]}. Type .help for a list.");
            return;
        }
        Log.Information("GM command by {Player}: {Command}", player, text);
        string? reply;
        try
        {
            reply = command.Run(player, parts[1..], now);
        }
        catch (FormatException)
        {
            reply = $"Usage: .{parts[0]} {command.Usage}";
        }
        if (reply is not null && player.Map is { } after) after.SendSystemMessage(player, reply);
    }

    /// <summary>Trims and drops control characters; the wire already limits the length.</summary>
    private static string Sanitize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw) if (!char.IsControl(c)) sb.Append(c);
        return sb.ToString().Trim();
    }

    // ------------------------------------------------------------------ helpers

    private static float ParseFloat(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static int ParseInt(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static Unit? Target(Player player) => player.Map?.FindUnit(player.TargetGuid);

    private static Unit TargetOrSelf(Player player) => Target(player) ?? player;

    // ------------------------------------------------------------------ commands

    private string? Help(Player player, string[] args, long now)
    {
        if (args.Length > 0)
        {
            var name = args[0].TrimStart('.');
            return _commands.TryGetValue(name, out var c) ? $".{name} {c.Usage}".TrimEnd() + " - " + c.Help : $"Unknown command .{name}.";
        }
        return "Commands: " + string.Join("  ", _commands.Keys.Select(k => "." + k)) + "\nType .help <command> for details, e.g. .help tele";
    }

    private static string? Gps(Player player, string[] args, long now)
    {
        var p = player.Position;
        var map = player.Map!;
        return string.Format(CultureInfo.InvariantCulture, "Map {0} '{1}' instance {2}: x {3:0.0}, y {4:0.0}, z {5:0.0}, facing {6:0} degrees",
            map.MapId, map.Map.Definition.Name, map.InstanceId, p.X, p.Y, p.Z, player.Yaw * MathUtil.Rad2Deg);
    }

    private string? Tele(Player player, string[] args, long now)
    {
        var map = player.Map!;
        if (args.Length == 1 && args[0].Equals("spawn", StringComparison.OrdinalIgnoreCase))
        {
            map.Teleport(player, map.Map.SpawnPosition, map.Map.SpawnYaw, now);
            return "Teleported to the map's spawn point.";
        }
        if (args.Length == 1)
        {
            var other = _server.OnlinePlayers.FirstOrDefault(p => p.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));
            if (other is null || other.Map != map) return $"No player '{args[0]}' on this map.";
            map.Teleport(player, other.Position, other.Yaw, now);
            return $"Teleported to {other.Name}.";
        }
        if (args.Length != 2) throw new FormatException();
        float x = ParseFloat(args[0]), z = ParseFloat(args[1]);
        if (!map.HeightMap.Contains(x, z)) return "That is outside the map.";
        map.Teleport(player, new Vec3(x, map.GroundHeight(x, z), z), player.Yaw, now);
        return string.Format(CultureInfo.InvariantCulture, "Teleported to {0:0.0}, {1:0.0}.", x, z);
    }

    private static string? Spawn(Player player, string[] args, long now)
    {
        if (args.Length is < 1 or > 2) throw new FormatException();
        var map = player.Map!;
        var creatures = map.Content.Creatures.Values;
        var template = int.TryParse(args[0], out int id)
            ? creatures.FirstOrDefault(c => c.Id == id)
            : creatures.FirstOrDefault(c => c.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase));
        if (template is null) return $"No creature '{args[0]}'. Known: " + string.Join(", ", creatures.Select(c => $"{c.Id} {c.Name}"));
        int count = args.Length == 2 ? Math.Clamp(ParseInt(args[1]), 1, 20) : 1;
        var forward = MathUtil.YawToDirection(player.Yaw);
        for (int i = 0; i < count; i++)
        {
            var offset = forward * 6f + MathUtil.YawToDirection(player.Yaw + MathUtil.Pi / 2) * ((i - (count - 1) / 2f) * 2.5f);
            var position = player.Position + offset;
            if (!map.HeightMap.Contains(position.X, position.Z)) continue;
            map.SpawnCreature(template, position, player.Yaw + MathUtil.Pi, null, now);
        }
        return $"Spawned {count} x {template.Name}.";
    }

    private static string? Level(Player player, string[] args, long now)
    {
        if (args.Length != 1) throw new FormatException();
        var who = Target(player) as Player ?? player;
        int level = ParseInt(args[0]);
        int max = player.Map!.Content.Levels.MaxLevel;
        if (level < 1 || level > max) return $"Levels go from 1 to {max}.";
        who.Xp = 0;
        player.Map.Combat.SetLevel(who, level, now);
        who.StatsDirty = true;
        return $"{who.Name} is now level {level}.";
    }

    private static string? Xp(Player player, string[] args, long now)
    {
        if (args.Length != 1) throw new FormatException();
        int amount = Math.Max(0, ParseInt(args[0]));
        player.Map!.Combat.GiveXp(player, amount, EntityGuid.Empty, now);
        return $"Gave you {amount} experience.";
    }

    private static string? Kill(Player player, string[] args, long now)
    {
        if (Target(player) is not { } target || target.IsDead) return "Select a living target first.";
        player.Map!.Combat.Kill(target, player, now);
        return $"Killed {target.Name}.";
    }

    private static string? Damage(Player player, string[] args, long now)
    {
        if (args.Length != 1) throw new FormatException();
        if (Target(player) is not { } target || target.IsDead) return "Select a living target first.";
        int dealt = player.Map!.Combat.DealDamage(player, player.Guid, target, Math.Max(1, ParseInt(args[0])), SpellSchool.Physical, 0, CombatFlags.None, now);
        return $"Dealt {dealt} damage to {target.Name}.";
    }

    private static string? Heal(Player player, string[] args, long now)
    {
        var who = TargetOrSelf(player);
        if (who.IsDead) return $"{who.Name} is dead. Use .revive.";
        who.Health = who.MaxHealth;
        if (who.PowerType != PowerType.Rage) who.Power = who.MaxPower;
        return $"{who.Name} is fully healed.";
    }

    private static string? Revive(Player player, string[] args, long now)
    {
        var who = Target(player) as Player ?? player;
        if (who.IsAlive) return $"{who.Name} is not dead.";
        player.Map!.Combat.Resurrect(who, now, atGraveyard: false);
        who.Health = who.MaxHealth;
        return $"{who.Name} lives again.";
    }

    private static string? Cooldowns(Player player, string[] args, long now)
    {
        player.Map!.Spells.ResetCooldowns(player);
        return "Cooldowns reset.";
    }

    private static string? God(Player player, string[] args, long now)
    {
        player.GodMode = !player.GodMode;
        if (player.GodMode) player.Map!.Combat.ClearAttackers(player);
        return player.GodMode ? "God mode on." : "God mode off.";
    }

    private static string? Speed(Player player, string[] args, long now)
    {
        if (args.Length != 1) throw new FormatException();
        float percent = Math.Clamp(ParseFloat(args[0]), 10f, 1000f);
        player.GmSpeed = percent / 100f;
        player.RecalculateStats();
        return string.Format(CultureInfo.InvariantCulture, "Speed {0:0}%.", percent);
    }

    private static string? Respawn(Player player, string[] args, long now)
    {
        float radius = args.Length > 0 ? Math.Clamp(ParseFloat(args[0]), 1f, 500f) : 60f;
        int count = player.Map!.RespawnNear(player.Position, radius, now);
        return $"Respawned {count} creature(s).";
    }

    private static string? Aura(Player player, string[] args, long now)
    {
        if (args.Length != 1) throw new FormatException();
        var map = player.Map!;
        if (!map.Content.Spells.TryGetValue((uint)ParseInt(args[0]), out var spell) || spell.Aura is null) return "No spell with an aura has that id.";
        var who = TargetOrSelf(player);
        return map.Auras.Apply(player, who, spell, now) is null ? "Could not apply it." : $"Applied {spell.Name} to {who.Name}.";
    }

    private static string? Cast(Player player, string[] args, long now)
    {
        if (args.Length != 1) throw new FormatException();
        var result = player.Map!.Spells.TryCast(player, (uint)ParseInt(args[0]), player.TargetGuid, 0, now, ignoreKnown: true);
        return result == CastResult.Success ? "Casting." : $"Cast failed: {result}.";
    }

    private static string? Info(Player player, string[] args, long now)
    {
        if (Target(player) is not { } t) return "Select a target first.";
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{t.Name} ({t.Guid}) level {t.Level}, health {t.Health}/{t.MaxHealth}, power {t.Power}/{t.MaxPower} {t.PowerType}, ");
        sb.Append(CultureInfo.InvariantCulture, $"faction {t.Faction} ({player.Map!.GetReaction(player, t)} to you), flags {t.Flags}, speed {t.SpeedPercent}%");
        sb.Append(CultureInfo.InvariantCulture, $"\nArmor {t.Stats.Armor}, AP {t.Stats.AttackPower}, weapon {t.Stats.WeaponMin:0.0}-{t.Stats.WeaponMax:0.0} every {t.Stats.AttackSpeedMs} ms");
        if (t.Auras.Count > 0) sb.Append("\nAuras: " + string.Join(", ", t.Auras.Select(a => $"{a.Spell.Name} ({a.RemainingMs(now) / 1000}s)")));
        if (t is Creature c)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\nCreature {c.Template.Id}, AI {c.Ai.Current}, home {c.Home}");
            if (c.Threat.Count > 0) sb.Append("\nThreat: " + string.Join(", ", c.Threat.Units.Select(u => $"{u.Name} {c.Threat.Get(u):0}")));
        }
        return sb.ToString();
    }

    private static string? AddItem(Player player, string[] args, long now)
    {
        if (args.Length is < 1 or > 2) throw new FormatException();
        var map = player.Map!;
        var items = map.Content.Items.Values;
        var item = uint.TryParse(args[0], out uint id)
            ? items.FirstOrDefault(i => i.Id == id)
            : items.FirstOrDefault(i => i.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase));
        if (item is null) return $"No item '{args[0]}'.";
        int count = args.Length == 2 ? Math.Clamp(ParseInt(args[1]), 1, 1000) : 1;
        int added = map.Items.Give(player, item, count, ReceiveSource.Other);
        map.Items.Flush(player);
        return added == count ? $"Added {count} x {item.Name}." : $"Added {added} of {count} x {item.Name}: your bags are full.";
    }

    private static string? Money(Player player, string[] args, long now)
    {
        if (args.Length != 1) throw new FormatException();
        long amount = long.Parse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
        player.Map!.Items.GiveMoney(player, amount, ReceiveSource.Other);
        return $"You have {Shared.Game.Money.Format(player.Inventory.Money)}.";
    }

    private static string? Quest(Player player, string[] args, long now)
    {
        if (args.Length != 2) throw new FormatException();
        var map = player.Map!;
        if (!map.Content.Quests.TryGetValue((uint)ParseInt(args[1]), out var quest)) return $"No quest {args[1]}.";
        switch (args[0].ToLowerInvariant())
        {
            case "add":
                if (player.Quests.Find(quest.Id) is not null) return $"{quest.Title} is already in your log.";
                if (player.Quests.IsFull) return "Your quest log is full.";
                player.Quests.Unreward(quest.Id);
                player.Quests.Add(quest);
                break;
            case "complete":
                if (player.Quests.Find(quest.Id) is not { } progress) return $"{quest.Title} is not in your log.";
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
                throw new FormatException();
        }
        map.Quests.SendLog(player);
        map.Quests.RefreshStatuses(player);
        return $"Quest {quest.Id} '{quest.Title}': {args[0].ToLowerInvariant()} done.";
    }

    private string? Who(Player player, string[] args, long now)
    {
        var players = _server.OnlinePlayers.OrderBy(p => p.Name).ToList();
        return $"{players.Count} online: " + string.Join(", ", players.Select(p => $"{p.Name} ({p.Level} {p.Class.Name}, map {p.Map?.MapId})"));
    }
}
