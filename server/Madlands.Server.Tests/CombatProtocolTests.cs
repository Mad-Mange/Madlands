using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;
using static Madlands.Server.Tests.ProtocolTests;

namespace Madlands.Server.Tests;

/// <summary>Roundtrips of the Phase 3 messages (targeting, spells, auras, combat, chat).</summary>
public class CombatProtocolTests
{
    internal static readonly UnitValues SampleValues = new()
    {
        Level = 7,
        Health = 123,
        MaxHealth = 456,
        PowerType = PowerType.Rage,
        Power = 42,
        MaxPower = 100,
        Faction = 2,
        Flags = UnitFlags.InCombat | UnitFlags.Rooted,
        Target = EntityGuid.Create(EntityType.Creature, 9),
        SpeedPercent = 60,
        ClassId = 3,
        NpcFlags = NpcFlags.QuestGiver | NpcFlags.Vendor,
        MainHandDisplay = 2004,
        OffHandDisplay = 2006,
    };

    private static readonly EntityGuid Player = EntityGuid.Create(EntityType.Player, 5);
    private static readonly EntityGuid Wolf = EntityGuid.Create(EntityType.Creature, 77);

    [Fact]
    public void Client_requests_roundtrip()
    {
        Assert.Equal(Wolf, Roundtrip(new SetTarget { Target = Wolf }).Target);
        var cast = Roundtrip(new CastSpell { SpellId = 200, Target = Wolf, CastId = 9 });
        Assert.Equal((200u, Wolf, (byte)9), (cast.SpellId, cast.Target, cast.CastId));
        Roundtrip(new CancelCast());
        Roundtrip(new StopAttack());
        Roundtrip(new ReleaseSpirit());
        Assert.Equal("hello there", Roundtrip(new ChatInput { Text = "hello there" }).Text);
        Assert.Equal(303u, Roundtrip(new CancelAura { SpellId = 303 }).SpellId);
    }

    [Fact]
    public void GameData_roundtrips()
    {
        var data = new GameData();
        data.Spells.Add(new SpellInfo
        {
            Id = 201, Name = "Frost Shard", Description = "Brr.", Icon = 201, School = SpellSchool.Frost, Target = SpellTarget.Enemy,
            Flags = SpellFlags.RequiresFacing | SpellFlags.HarmfulAura, MinRange = 0, MaxRange = 30, CastTimeMs = 1500, CooldownMs = 0,
            GcdMs = 1500, PowerType = PowerType.Mana, Cost = 20, AuraDurationMs = 5000, ProjectileSpeed = 22, LearnLevel = 2,
        });
        data.Factions.Add(new FactionInfo { Id = 1, Name = "Settlers", Friendly = new ushort[] { 1 }, Hostile = new ushort[] { 2 } });
        data.Classes.Add(new ClassInfo { Id = 2, Name = "Shardmage", PowerType = PowerType.Mana });
        data.XpToNextLevel.AddRange(new[] { 400, 900 });

        var copy = Roundtrip(data);
        var s = copy.Spells.Single();
        Assert.Equal(("Frost Shard", "Brr.", SpellSchool.Frost, 1500, 20, 5000, 22f, (byte)2),
            (s.Name, s.Description, s.School, s.CastTimeMs, s.Cost, s.AuraDurationMs, s.ProjectileSpeed, s.LearnLevel));
        Assert.Equal(SpellFlags.RequiresFacing | SpellFlags.HarmfulAura, s.Flags);
        Assert.Equal(new ushort[] { 2 }, copy.Factions.Single().Hostile);
        Assert.Equal("Shardmage", copy.Classes.Single().Name);
        Assert.Equal(new[] { 400, 900 }, copy.XpToNextLevel);
    }

    [Fact]
    public void UnitUpdates_write_only_masked_fields()
    {
        var msg = new UnitUpdates();
        msg.Entries.Add(new UnitUpdateEntry { Guid = Wolf, Mask = UnitFieldMask.Health | UnitFieldMask.Flags, Values = SampleValues });
        msg.Entries.Add(new UnitUpdateEntry { Guid = Player, Mask = UnitFieldMask.All, Values = SampleValues });
        var copy = Roundtrip(msg);

        Assert.Equal(2, copy.Entries.Count);
        var first = copy.Entries[0];
        Assert.Equal((Wolf, UnitFieldMask.Health | UnitFieldMask.Flags), (first.Guid, first.Mask));
        var target = new UnitValues { Level = 1, MaxHealth = 999 };
        target.CopyFrom(first.Values, first.Mask);
        Assert.Equal((123, UnitFlags.InCombat | UnitFlags.Rooted, (byte)1, 999), (target.Health, target.Flags, target.Level, target.MaxHealth));
        Assert.True(copy.Entries[1].Values.ValueEquals(SampleValues));
    }

    [Fact]
    public void Unknown_unit_field_bits_are_rejected()
    {
        var w = new PacketWriter();
        w.WriteUShort((ushort)Opcode.UnitUpdates);
        w.WriteUShort(1);
        w.WriteGuid(Wolf);
        w.WriteUShort(0x8000);
        w.WriteInt(0);
        Assert.Throws<PacketException>(() =>
        {
            var r = new PacketReader(w.AsSpan());
            Packet.ReadOpcode(ref r);
            new UnitUpdates().Read(ref r);
        });
    }

    [Fact]
    public void Spell_messages_roundtrip()
    {
        var start = Roundtrip(new SpellStart { Caster = Player, Target = Wolf, SpellId = 200, CastId = 4, CastTimeMs = 1800 });
        Assert.Equal((Player, Wolf, 200u, (byte)4, 1800), (start.Caster, start.Target, start.SpellId, start.CastId, start.CastTimeMs));
        var go = Roundtrip(new SpellGo { Caster = Player, Target = Wolf, SpellId = 200, CastId = 4 });
        Assert.Equal((Player, Wolf, 200u, (byte)4), (go.Caster, go.Target, go.SpellId, go.CastId));
        var failed = Roundtrip(new SpellFailed { Caster = Player, SpellId = 200, CastId = 5, Result = CastResult.OutOfRange });
        Assert.Equal((Player, 200u, (byte)5, CastResult.OutOfRange), (failed.Caster, failed.SpellId, failed.CastId, failed.Result));
        var cd = Roundtrip(new Cooldown { SpellId = 202, DurationMs = 20000 });
        Assert.Equal((202u, 20000), (cd.SpellId, cd.DurationMs));
    }

    [Fact]
    public void CombatEvent_roundtrips()
    {
        var e = Roundtrip(new CombatEvent
        {
            Source = Player, Target = Wolf, SpellId = 100, School = SpellSchool.Physical, Amount = 17, Absorbed = 3,
            Flags = CombatFlags.Crit | CombatFlags.Absorb | CombatFlags.KillingBlow,
        });
        Assert.Equal((Player, Wolf, 100u, 17, 3), (e.Source, e.Target, e.SpellId, e.Amount, e.Absorbed));
        Assert.True(e.Has(CombatFlags.Crit) && e.Has(CombatFlags.KillingBlow) && !e.Has(CombatFlags.Miss));
    }

    [Fact]
    public void AuraUpdate_roundtrips()
    {
        var msg = new AuraUpdate { Unit = Wolf, Full = true };
        msg.Entries.Add(new AuraEntry { Slot = 0, SpellId = 101, Caster = Player, Stacks = 1, DurationMs = 12000, RemainingMs = 11500 });
        msg.Entries.Add(new AuraEntry { Slot = 3, SpellId = 0 });
        var copy = Roundtrip(msg);
        Assert.Equal((Wolf, true, 2), (copy.Unit, copy.Full, copy.Entries.Count));
        var a = copy.Entries[0];
        Assert.Equal(((byte)0, 101u, Player, (byte)1, 12000, 11500), (a.Slot, a.SpellId, a.Caster, a.Stacks, a.DurationMs, a.RemainingMs));
        Assert.Equal(0u, copy.Entries[1].SpellId);
    }

    [Fact]
    public void Player_messages_roundtrip()
    {
        var stats = new PlayerStats { Xp = 250, XpToNextLevel = 900, MeleeMinDamage = 5, MeleeMaxDamage = 9, MeleeSpeedMs = 2000, CritChance = 6.5f, SpellCritChance = 5.5f, DodgeChance = 6f };
        stats.Stats[(int)StatType.Strength] = 25;
        stats.Stats[(int)StatType.AttackPower] = 30;
        var s = Roundtrip(stats);
        Assert.Equal((250, 900, 25, 30, 6.5f), (s.Xp, s.XpToNextLevel, s.Stats[(int)StatType.Strength], s.Stats[(int)StatType.AttackPower], s.CritChance));

        var book = new Spellbook();
        book.SpellIds.AddRange(new uint[] { 1, 100, 103 });
        Assert.Equal(new uint[] { 1, 100, 103 }, Roundtrip(book).SpellIds);

        var chat = Roundtrip(new ChatMessage { Channel = ChatChannel.Say, Sender = Player, SenderName = "Alice", Text = "For Calderra!" });
        Assert.Equal((ChatChannel.Say, Player, "Alice", "For Calderra!"), (chat.Channel, chat.Sender, chat.SenderName, chat.Text));

        var xp = Roundtrip(new ExperienceGained { Amount = 55, Victim = Wolf });
        Assert.Equal((55, Wolf), (xp.Amount, xp.Victim));
    }
}
