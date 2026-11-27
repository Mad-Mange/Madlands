using System;
using System.Collections.Generic;
using System.Globalization;

namespace Madlands.Shared.Game
{
    /// <summary>The outcome of a GM command. Stable: tools and tests compare these.</summary>
    public enum GmResultCode : byte
    {
        Ok = 0,
        /// <summary>Done in part (for example only some items fit into the bags).</summary>
        Partial = 1,
        /// <summary>The account has no GM rights (or too low a GM level) for the command.</summary>
        Unauthorized = 2,
        InvalidArgument = 3,
        /// <summary>The command cannot run in the current state (dead, not in the world, unsupported here).</summary>
        NotAvailable = 4,
        /// <summary>The command needs a selected unit and there is none (or not a fitting one).</summary>
        NoTarget = 5,
        ExecutionFailed = 6,
        UnknownCommand = 7,
        /// <summary>The same command is still waiting for its answer (client side).</summary>
        Busy = 8,
        /// <summary>No answer arrived in time (client side).</summary>
        Timeout = 9,
    }

    public enum GmArgType : byte { Bool, Int, Float, Text }

    public enum GmCategory : byte { Gm, Player, Combat, Teleport, Spawn, Quest, World, Debug, Developer }

    /// <summary>Where a command runs: on the server (authoritative) or in the client (overlays).</summary>
    public enum GmSide : byte { Server, Client }

    /// <summary>Temporary GM overrides of a player; they end with the session and are never saved.</summary>
    [Flags]
    public enum GmOverrides : ushort
    {
        None = 0,
        /// <summary>No damage taken, creatures ignore the player, no spell costs or cooldowns (the .god command).</summary>
        GodMode = 1,
        /// <summary>Spells start no cooldowns (the global cooldown still applies).</summary>
        NoCooldowns = 2,
    }

    public sealed class GmArg
    {
        public GmArg(string name, GmArgType type, double min = 0, double max = 0, bool optional = false)
        {
            Name = name;
            Type = type;
            Min = min;
            Max = max;
            Optional = optional;
        }

        public string Name { get; }
        public GmArgType Type { get; }
        /// <summary>Inclusive bounds for Int and Float; the maximum length for Text.</summary>
        public double Min { get; }
        public double Max { get; }
        public bool Optional { get; }
    }

    public sealed class GmCommandInfo
    {
        public GmCommandInfo(string id, GmCategory category, GmSide side, bool persistent, params GmArg[] args)
        {
            Id = id;
            Category = category;
            Side = side;
            Persistent = persistent;
            Args = args;
        }

        /// <summary>Stable id, e.g. "player.set_level". Never renamed.</summary>
        public string Id { get; }
        public GmCategory Category { get; }
        public GmSide Side { get; }
        /// <summary>The GM level a player needs (all commands need a GM; none needs more yet).</summary>
        public int MinGmLevel => 1;
        /// <summary>Changes saved game state (level, items, money, quests, kills) rather than a temporary override.</summary>
        public bool Persistent { get; }
        public IReadOnlyList<GmArg> Args { get; }
    }

    /// <summary>
    /// The GM commands both sides know: ids, argument types and bounds. The server validates against this
    /// table again; the developer menu, the client's command service and automation read it for discovery.
    /// </summary>
    public static class GmCommands
    {
        public const int MaxArgs = 8;
        public const int MaxIdBytes = 64;
        public const int MaxKeyBytes = 32;
        public const int MaxValueBytes = 64;
        public const int MaxSpawnCount = 10;
        public const int MaxDebugSpawns = 40;

        public const string GodMode = "player.god_mode";
        public const string NoCooldown = "player.no_cooldown";
        public const string HealFull = "player.heal_full";
        public const string RestoreResources = "player.restore_resources";
        public const string Revive = "player.revive";
        public const string SetLevel = "player.set_level";
        public const string AddXp = "player.add_xp";
        public const string AddGold = "player.add_gold";
        public const string AddItem = "player.add_item";
        public const string SpeedMultiplier = "player.speed_multiplier";
        public const string KillTarget = "combat.kill_target";
        public const string DamageMultiplier = "combat.damage_multiplier";
        public const string TeleportLocation = "teleport.location";
        public const string TeleportCoordinates = "teleport.coordinates";
        public const string TeleportTarget = "teleport.target";
        public const string SpawnEntity = "spawn.entity";
        public const string DespawnDebug = "spawn.despawn_debug";
        public const string QuestStart = "quest.start";
        public const string QuestComplete = "quest.complete";
        public const string QuestReset = "quest.reset";
        public const string WorldRespawn = "world.respawn";
        public const string InspectTarget = "debug.inspect_target";
        public const string DebugGeometry = "debug.geometry";
        public const string ResetOverrides = "dev.reset_overrides";
        // Client-side: overlays drawn by this client only.
        public const string ShowFps = "debug.fps";
        public const string ShowPerformance = "debug.performance";
        public const string ShowCollision = "debug.collision";
        public const string ShowNavMesh = "debug.navmesh";
        public const string ShowSpawnPoints = "debug.spawn_points";

        /// <summary>Kinds for <see cref="DebugGeometry"/>.</summary>
        public const string GeometryCollision = "collision", GeometryNavMesh = "navmesh", GeometrySpawnPoints = "spawn_points";

        private static readonly GmCommandInfo[] AllCommands =
        {
            new GmCommandInfo(GodMode, GmCategory.Gm, GmSide.Server, false, Flag()),
            new GmCommandInfo(NoCooldown, GmCategory.Gm, GmSide.Server, false, Flag()),
            new GmCommandInfo(HealFull, GmCategory.Player, GmSide.Server, false),
            new GmCommandInfo(RestoreResources, GmCategory.Player, GmSide.Server, false),
            new GmCommandInfo(Revive, GmCategory.Gm, GmSide.Server, false),
            new GmCommandInfo(KillTarget, GmCategory.Gm, GmSide.Server, true),
            new GmCommandInfo(SetLevel, GmCategory.Gm, GmSide.Server, true, new GmArg("level", GmArgType.Int, 1, 255)),
            new GmCommandInfo(AddXp, GmCategory.Player, GmSide.Server, true, new GmArg("amount", GmArgType.Int, 1, 1000000)),
            new GmCommandInfo(AddGold, GmCategory.Player, GmSide.Server, true, new GmArg("copper", GmArgType.Int, 1, 1000000000)),
            new GmCommandInfo(AddItem, GmCategory.Player, GmSide.Server, true,
                new GmArg("item", GmArgType.Int, 1, int.MaxValue), new GmArg("count", GmArgType.Int, 1, 250, optional: true)),
            new GmCommandInfo(SpeedMultiplier, GmCategory.Player, GmSide.Server, false, new GmArg("percent", GmArgType.Int, 25, 500)),
            new GmCommandInfo(DamageMultiplier, GmCategory.Combat, GmSide.Server, false, new GmArg("percent", GmArgType.Int, 0, 1000)),
            new GmCommandInfo(TeleportLocation, GmCategory.Teleport, GmSide.Server, false, new GmArg("location", GmArgType.Text, 1, MaxValueBytes)),
            new GmCommandInfo(TeleportCoordinates, GmCategory.Teleport, GmSide.Server, false,
                new GmArg("x", GmArgType.Float, -8192, 8192), new GmArg("z", GmArgType.Float, -8192, 8192)),
            new GmCommandInfo(TeleportTarget, GmCategory.Teleport, GmSide.Server, false),
            new GmCommandInfo(SpawnEntity, GmCategory.Spawn, GmSide.Server, false,
                new GmArg("creature", GmArgType.Int, 1, int.MaxValue), new GmArg("count", GmArgType.Int, 1, MaxSpawnCount, optional: true)),
            new GmCommandInfo(DespawnDebug, GmCategory.Spawn, GmSide.Server, false),
            new GmCommandInfo(QuestStart, GmCategory.Quest, GmSide.Server, true, new GmArg("quest", GmArgType.Int, 1, int.MaxValue)),
            new GmCommandInfo(QuestComplete, GmCategory.Quest, GmSide.Server, true, new GmArg("quest", GmArgType.Int, 1, int.MaxValue)),
            new GmCommandInfo(QuestReset, GmCategory.Quest, GmSide.Server, true, new GmArg("quest", GmArgType.Int, 1, int.MaxValue)),
            new GmCommandInfo(WorldRespawn, GmCategory.World, GmSide.Server, false, new GmArg("radius", GmArgType.Int, 1, 500, optional: true)),
            new GmCommandInfo(InspectTarget, GmCategory.Debug, GmSide.Server, false),
            new GmCommandInfo(DebugGeometry, GmCategory.Debug, GmSide.Server, false,
                new GmArg("kind", GmArgType.Text, 1, 16), new GmArg("radius", GmArgType.Int, 10, 80, optional: true)),
            new GmCommandInfo(ResetOverrides, GmCategory.Developer, GmSide.Server, false),
            new GmCommandInfo(ShowFps, GmCategory.Debug, GmSide.Client, false, Flag()),
            new GmCommandInfo(ShowPerformance, GmCategory.Debug, GmSide.Client, false, Flag()),
            new GmCommandInfo(ShowCollision, GmCategory.Debug, GmSide.Client, false, Flag()),
            new GmCommandInfo(ShowNavMesh, GmCategory.Debug, GmSide.Client, false, Flag()),
            new GmCommandInfo(ShowSpawnPoints, GmCategory.Debug, GmSide.Client, false, Flag()),
        };

        private static readonly Dictionary<string, GmCommandInfo> ById = Index();

        private static GmArg Flag() => new GmArg("enabled", GmArgType.Bool);

        private static Dictionary<string, GmCommandInfo> Index()
        {
            var map = new Dictionary<string, GmCommandInfo>(StringComparer.Ordinal);
            foreach (var c in AllCommands) map.Add(c.Id, c);
            return map;
        }

        public static IReadOnlyList<GmCommandInfo> All => AllCommands;

        public static GmCommandInfo Find(string id) => id != null && ById.TryGetValue(id, out var info) ? info : null;

        /// <summary>
        /// Checks raw key/value arguments against the command's schema. Returns null and the parsed values, or
        /// an error text. Unknown keys, missing arguments, non-finite or out-of-range numbers are errors.
        /// </summary>
        public static string Parse(GmCommandInfo info, IReadOnlyList<KeyValuePair<string, string>> raw, out GmArgValues values)
        {
            values = new GmArgValues();
            int count = raw?.Count ?? 0;
            if (count > MaxArgs) return "Too many arguments.";
            for (int i = 0; i < count; i++)
            {
                var pair = raw[i];
                GmArg spec = null;
                foreach (var a in info.Args)
                    if (a.Name == pair.Key) spec = a;
                if (spec == null) return $"Unknown argument '{pair.Key}'.";
                if (values.Has(spec.Name)) return $"Argument '{spec.Name}' given twice.";
                string error = ParseOne(spec, pair.Value, values);
                if (error != null) return error;
            }
            foreach (var a in info.Args)
                if (!a.Optional && !values.Has(a.Name)) return $"Missing argument '{a.Name}'.";
            return null;
        }

        private static string ParseOne(GmArg spec, string text, GmArgValues values)
        {
            text = text?.Trim() ?? "";
            switch (spec.Type)
            {
                case GmArgType.Bool:
                    if (text == "true" || text == "1") values.Set(spec.Name, 1);
                    else if (text == "false" || text == "0") values.Set(spec.Name, 0);
                    else return $"'{spec.Name}' must be true or false.";
                    return null;
                case GmArgType.Int:
                    if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole))
                        return $"'{spec.Name}' must be a whole number.";
                    if (whole < spec.Min || whole > spec.Max)
                        return string.Format(CultureInfo.InvariantCulture, "'{0}' must be between {1} and {2}.", spec.Name, spec.Min, spec.Max);
                    values.Set(spec.Name, whole);
                    return null;
                case GmArgType.Float:
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsNaN(number) || double.IsInfinity(number))
                        return $"'{spec.Name}' must be a number.";
                    if (number < spec.Min || number > spec.Max)
                        return string.Format(CultureInfo.InvariantCulture, "'{0}' must be between {1} and {2}.", spec.Name, spec.Min, spec.Max);
                    values.Set(spec.Name, number);
                    return null;
                default:
                    if (text.Length < spec.Min || text.Length > spec.Max) return $"'{spec.Name}' has the wrong length.";
                    foreach (char c in text)
                        if (char.IsControl(c)) return $"'{spec.Name}' contains control characters.";
                    values.SetText(spec.Name, text);
                    return null;
            }
        }

        /// <summary>Formats a value for the wire (invariant culture).</summary>
        public static string Format(bool value) => value ? "true" : "false";
        public static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
        public static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Arguments after <see cref="GmCommands.Parse"/>: typed and within bounds.</summary>
    public sealed class GmArgValues
    {
        private readonly Dictionary<string, double> _numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);

        public bool Has(string name) => _numbers.ContainsKey(name) || _texts.ContainsKey(name);
        internal void Set(string name, double value) => _numbers[name] = value;
        internal void SetText(string name, string value) => _texts[name] = value;

        public bool Bool(string name) => _numbers.TryGetValue(name, out var v) && v != 0;
        public long Int(string name, long fallback = 0) => _numbers.TryGetValue(name, out var v) ? (long)v : fallback;
        public float Float(string name, float fallback = 0f) => _numbers.TryGetValue(name, out var v) ? (float)v : fallback;
        public string Text(string name) => _texts.TryGetValue(name, out var v) ? v : null;
    }
}
