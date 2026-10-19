using Madlands.ClientCore;
using Madlands.Content;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;

namespace Madlands.Server.Tests;

/// <summary>
/// Combat end to end: a real realm with the repository's classes, spells and creatures on a flat
/// map, real clients over UDP. The map spawn is (0, 0) facing +Z, so creatures at positive z are in front.
/// </summary>
public class CombatIntegrationTests
{
    private const int MireFrog = 1;
    private const int DuskWolf = 2;
    private const int TrainingDummy = 5;
    private const byte Warrior = 1;
    private const byte Shardmage = 2;

    private sealed class Session : IDisposable
    {
        public Session(TestRealm realm, GameClient client)
        {
            Realm = realm;
            Client = client;
            client.CombatEventReceived += e => Events.Add((e.Source, e.Target, e.SpellId, e.Amount, e.Flags));
            client.SpellFailed += f => Failures.Add(f.Result);
            client.ChatReceived += c => Chat.Add(c.Text);
            client.ExperienceReceived += (amount, bonus, _) =>
            {
                Xp += amount;
                RestedBonus += bonus;
            };
        }

        public TestRealm Realm { get; }
        public GameClient Client { get; }
        public List<(EntityGuid Source, EntityGuid Target, uint Spell, int Amount, CombatFlags Flags)> Events { get; } = new();
        public List<CastResult> Failures { get; } = new();
        public List<string> Chat { get; } = new();
        public int Xp;
        public int RestedBonus;

        public void Pump(Func<bool> until, int timeoutMs = 8000) => Realm.Pump(new[] { Client }, until, timeoutMs);
        public void PumpFor(int ms) => Realm.PumpFor(new[] { Client }, ms);

        public RemoteEntity Find(string name)
        {
            RemoteEntity? found = null;
            Pump(() => (found = Client.World.Entities.FirstOrDefault(e => e.Name == name)) is not null);
            return found!;
        }

        /// <summary>Runs a GM command and waits for its reply.</summary>
        public void Command(string text)
        {
            int before = Chat.Count;
            Client.SendChat(text);
            Pump(() => Chat.Count > before);
        }

        public void Dispose() => Client.Dispose();
    }

    private static async Task<(TestRealm Realm, Session Session)> StartAsync(byte classId, bool gm, params SpawnDefinition[] spawns)
    {
        var realm = await TestRealm.StartAsync(content: TestRealm.CombatContent(spawns), gmAll: gm);
        var (account, character) = await realm.NewPlayerAsync("fighter", "Fighter", classId);
        var session = new Session(realm, realm.Enter(account, character));
        session.Pump(() => session.Client.State == ClientState.InWorld && session.Client.KnownSpells.Count > 0);
        return (realm, session);
    }

    private static SpawnDefinition At(int creature, float x, float z) => new() { Creature = creature, X = x, Z = z, YawDegrees = 180 };

    [Fact]
    public async Task Auto_attack_kills_a_creature_and_grants_experience()
    {
        var (realm, s) = await StartAsync(Warrior, gm: true, At(MireFrog, 0, 4));
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;
        Assert.NotEmpty(c.Data.Spells);
        Assert.Equal(PowerType.Rage, c.Self.Values.PowerType);
        Assert.Equal(0, c.Self.Values.Power);

        s.Command(".level 5");
        s.Pump(() => c.Self.Values.Level == 5);
        var frog = s.Find("Mire Frog");
        Assert.Equal(Reaction.Neutral, c.GetReaction(frog.Guid));

        c.SetTarget(frog.Guid);
        c.CastSpell(1, frog.Guid); // Attack
        s.Pump(() => c.Self.Values.Has(UnitFlags.AutoAttacking));
        s.Pump(() => frog.Unit!.Values.IsDead, 20000);

        Assert.Contains(s.Events, e => e.Source == c.LocalGuid && e.Target == frog.Guid && e.Spell == 0 && e.Amount > 0);
        Assert.Contains(s.Events, e => e.Target == frog.Guid && e.Flags.HasFlag(CombatFlags.KillingBlow));
        s.Pump(() => s.Xp > 0 && c.Stats.Xp > 0);
        Assert.Equal(s.Xp, c.Stats.Xp);
        s.Pump(() => !c.Self.Values.Has(UnitFlags.AutoAttacking));
        Assert.True(c.Self.Values.Power > 0 || s.Events.Any(e => e.Source == c.LocalGuid && e.Spell == 100), "melee hits generate rage");
    }

    [Fact]
    public async Task Auto_attack_starts_out_of_range_and_swings_once_close()
    {
        var (realm, s) = await StartAsync(Warrior, gm: true, At(TrainingDummy, 0, 20));
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;
        var dummy = s.Find("Training Dummy");

        c.CastSpell(1, dummy.Guid);
        s.Pump(() => c.Self.Values.Has(UnitFlags.AutoAttacking));
        s.Pump(() => s.Failures.Contains(CastResult.OutOfRange)); // the swing, not the start, complains
        Assert.DoesNotContain(s.Events, e => e.Source == c.LocalGuid);

        s.Command(".tele 0 17"); // in front of the dummy, still facing it
        s.Pump(() => s.Events.Any(e => e.Source == c.LocalGuid && e.Target == dummy.Guid && e.Spell == 0));
    }

    [Fact]
    public async Task Hostile_creature_aggroes_chases_and_attacks()
    {
        var (realm, s) = await StartAsync(Warrior, gm: true, At(DuskWolf, 0, 12));
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;
        var wolf = s.Find("Dusk Wolf");
        Assert.Equal(Reaction.Hostile, c.GetReaction(wolf.Guid));

        s.Pump(() => s.Events.Any(e => e.Source == wolf.Guid && e.Target == c.LocalGuid), 10000);
        Assert.True(c.Self.Values.Has(UnitFlags.InCombat));
        Assert.Equal(c.LocalGuid, wolf.Unit!.Values.Target);
        Assert.True(Vec3.Distance(wolf.Movement.Newest.Position, c.LocalMovement.Position) < GameConstants.MeleeRange + 1f, "it came to us");

        // God mode: the wolf loses its target, gives up and returns home at full health.
        s.Command(".god");
        s.Pump(() => wolf.Unit.Values.Has(UnitFlags.Evading));
        s.Pump(() => !wolf.Unit.Values.Has(UnitFlags.Evading) && !wolf.Unit.Values.Has(UnitFlags.InCombat), 10000);
        Assert.Equal(wolf.Unit.Values.MaxHealth, wolf.Unit.Values.Health);
    }

    [Fact]
    public async Task Spell_casts_take_time_cost_mana_and_hit()
    {
        var (realm, s) = await StartAsync(Shardmage, gm: true, At(TrainingDummy, 0, 10));
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;
        var dummy = s.Find("Training Dummy");
        int mana = c.Self.Values.Power;
        Assert.True(mana > 0);

        c.SetTarget(dummy.Guid);
        Assert.Equal(CastResult.Success, c.CheckCast(200, dummy.Guid));
        CastInfo? cast = null;
        c.CastStarted += (caster, info) => { if (caster == c.LocalGuid) cast = info; };
        c.CastSpell(200, dummy.Guid); // Shard Bolt, 1.8 s
        s.Pump(() => cast is not null);
        Assert.Equal(200u, cast!.SpellId);
        Assert.True(c.GcdRemainingMs > 0);

        s.Pump(() => s.Events.Any(e => e.Source == c.LocalGuid && e.Spell == 200), 5000);
        var hit = s.Events.First(e => e.Spell == 200);
        Assert.True(hit.Amount > 0 || hit.Flags.HasFlag(CombatFlags.Miss));
        s.Pump(() => c.Self.Values.Power < mana); // 25 spent; regeneration may already have given a little back
        Assert.Null(c.Self.Cast);

        // Moving interrupts the next cast.
        s.PumpFor(1600); // global cooldown
        c.CastSpell(200, dummy.Guid);
        s.Pump(() => c.Self.Cast is not null);
        var moved = c.LocalMovement;
        moved.Position += new Vec3(1, 0, 0);
        c.SendMovement(moved);
        s.Pump(() => s.Failures.Contains(CastResult.Interrupted));
        Assert.Null(c.Self.Cast);
    }

    [Fact]
    public async Task Invalid_casts_are_refused_with_a_reason()
    {
        var (realm, s) = await StartAsync(Shardmage, gm: true, At(TrainingDummy, 0, 60));
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;
        var dummy = s.Find("Training Dummy");

        c.CastSpell(200, EntityGuid.Empty);
        s.Pump(() => s.Failures.Contains(CastResult.NoTarget));
        c.CastSpell(200, dummy.Guid);
        s.Pump(() => s.Failures.Contains(CastResult.OutOfRange));
        c.CastSpell(100, dummy.Guid); // Heroic Strike is a warrior ability
        s.Pump(() => s.Failures.Contains(CastResult.NotKnown));
        c.CastSpell(200, c.LocalGuid); // not an enemy
        s.Pump(() => s.Failures.Contains(CastResult.InvalidTarget));
        // A refused request does not leave a predicted global cooldown behind.
        Assert.Equal(0, c.GcdRemainingMs);
    }

    [Fact]
    public async Task Auras_tick_slow_and_expire()
    {
        var (realm, s) = await StartAsync(Warrior, gm: true, At(TrainingDummy, 0, 3));
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;
        var dummy = s.Find("Training Dummy");
        c.SetTarget(dummy.Guid);

        // Hamstring's slow, applied by GM command so no rage or hit roll is needed.
        s.Command(".aura 102");
        s.Pump(() => dummy.Unit!.HasAura(102) && dummy.Unit.Values.SpeedPercent == 50);

        // Rend ticks every 3 s.
        s.Command(".aura 101");
        s.Pump(() => s.Events.Count(e => e.Spell == 101 && e.Flags.HasFlag(CombatFlags.Periodic)) >= 2, 8000);

        // On ourselves: slowed, and the server holds us to it.
        c.SetTarget(c.LocalGuid);
        s.Command(".aura 102");
        s.Pump(() => c.Self.Values.SpeedPercent == 50 && c.Self.HasAura(102));
        s.PumpFor(1200); // past the grace period
        var corrected = false;
        c.Corrected += _ => corrected = true;
        var state = c.LocalMovement;
        for (int i = 0; i < 6 && !corrected; i++)
        {
            s.PumpFor(250);
            state.Position += new Vec3(0, 0, -GameConstants.RunSpeed * 0.25f); // full speed, away from the dummy
            c.SendMovement(state);
        }
        s.Pump(() => corrected);
    }

    [Fact]
    public async Task Death_release_and_level_up()
    {
        var (realm, s) = await StartAsync(Warrior, gm: true);
        await using var _ = realm;
        using var __ = s;
        var c = s.Client;
        Assert.DoesNotContain(103u, c.KnownSpells);

        s.Command(".xp 400");
        s.Pump(() => c.Self.Values.Level == 2 && c.KnownSpells.Contains(103u)); // Battle Shout at level 2
        Assert.Contains(s.Chat, t => t.Contains("level 2"));

        c.SetTarget(c.LocalGuid);
        s.Command(".kill");
        s.Pump(() => c.Self.Values.IsDead);
        Assert.Equal(0, c.Self.Values.Health);

        c.ReleaseSpirit();
        s.Pump(() => !c.Self.Values.IsDead);
        Assert.Equal(c.Self.Values.MaxHealth / 2, c.Self.Values.Health);
    }

    [Fact]
    public async Task Say_is_heard_nearby_and_commands_need_permission()
    {
        await using var realm = await TestRealm.StartAsync(content: TestRealm.CombatContent());
        var (aliceAccount, aliceCharacter) = await realm.NewPlayerAsync("alice", "Alice");
        var (bobAccount, bobCharacter) = await realm.NewPlayerAsync("bob", "Bob");
        using var alice = new Session(realm, realm.Enter(aliceAccount, aliceCharacter));
        using var bob = new Session(realm, realm.Enter(bobAccount, bobCharacter));
        var both = new[] { alice.Client, bob.Client };
        realm.Pump(both, () => bob.Client.World.TryGet(alice.Client.LocalGuid, out _) && alice.Client.World.TryGet(bob.Client.LocalGuid, out _));

        alice.Client.SendChat("Well met!");
        realm.Pump(both, () => bob.Chat.Contains("Well met!") && alice.Chat.Contains("Well met!"));

        alice.Client.SendChat(".level 9");
        realm.Pump(both, () => alice.Chat.Any(t => t.Contains("not allowed")));
        Assert.Equal(1, alice.Client.Self.Values.Level);
        Assert.Equal(Reaction.Friendly, alice.Client.GetReaction(bob.Client.LocalGuid));
    }

    [Fact]
    public async Task Experience_and_health_survive_relogging()
    {
        var database = new Persistence.InMemoryGameDatabase();
        await using var realm = await TestRealm.StartAsync(database, TestRealm.CombatContent(), gmAll: true);
        var (account, character) = await realm.NewPlayerAsync("keeper", "Keeper");
        using (var s = new Session(realm, realm.Enter(account, character)))
        {
            s.Pump(() => s.Client.State == ClientState.InWorld);
            s.Command(".xp 150");
            s.Client.SetTarget(s.Client.LocalGuid);
            s.Command(".damage 20");
            s.Pump(() => s.Client.Stats.Xp == 150 && s.Client.Self.Values.Health < s.Client.Self.Values.MaxHealth);
            s.Client.Disconnect();
            realm.Pump(Array.Empty<GameClient>(), () => realm.Game.World.PlayerCount == 0);
        }
        await WorldIntegrationTests.WaitUntilAsync(() => realm.Host.Saver.SavedCount >= 1);
        var saved = await database.LoadCharacterAsync(1, (long)character.Id);
        Assert.Equal(150, saved!.Xp);
        Assert.True(saved.Health > 0);

        using var again = new Session(realm, realm.Enter(account, character));
        again.Pump(() => again.Client.State == ClientState.InWorld && again.Client.Stats.Xp == 150);
        Assert.Equal(saved.Health, again.Client.Self.Values.Health);
    }

    [Fact]
    public async Task Long_buffs_are_kept_over_relogging()
    {
        var database = new Persistence.InMemoryGameDatabase();
        await using var realm = await TestRealm.StartAsync(database, TestRealm.CombatContent(), gmAll: true);
        var (account, character) = await realm.NewPlayerAsync("buffed", "Buffed", Warrior);
        int buffedHealth;
        using (var s = new Session(realm, realm.Enter(account, character)))
        {
            var c = s.Client;
            s.Pump(() => c.State == ClientState.InWorld);
            int before = c.Self.Values.MaxHealth;
            s.Command(".aura 505");   // Elixir of Fortitude: +10 Stamina for 30 minutes
            s.Pump(() => c.Self.Auras.Any(a => a.SpellId == 505) && c.Self.Values.MaxHealth > before);
            buffedHealth = c.Self.Values.MaxHealth;
            Assert.Equal(before + 20, buffedHealth);   // 2 health per point of Stamina
            s.Command(".aura 500");   // eating ends when you log out
            s.Pump(() => c.Self.Auras.Any(a => a.SpellId == 500));
            c.Disconnect();
            realm.Pump(Array.Empty<GameClient>(), () => realm.Game.World.PlayerCount == 0);
        }
        await WorldIntegrationTests.WaitUntilAsync(() => realm.Host.Saver.SavedCount >= 1);
        var saved = Assert.Single((await database.LoadCharacterDataAsync((long)character.Id)).SavedAuras);
        Assert.Equal(505u, saved.SpellId);
        Assert.InRange(saved.RemainingMs, 1_700_000, 1_800_000);

        using var again = new Session(realm, realm.Enter(account, character));
        again.Pump(() => again.Client.State == ClientState.InWorld && again.Client.Self.Auras.Any(a => a.SpellId == 505));
        again.Pump(() => again.Client.Self.Values.MaxHealth == buffedHealth);
        Assert.DoesNotContain(again.Client.Self.Auras, a => a.SpellId == 500);
    }

    [Fact]
    public async Task Rested_experience_doubles_kills_and_is_kept_over_relogging()
    {
        var database = new Persistence.InMemoryGameDatabase();
        var content = TestRealm.CombatContent(At(MireFrog, 0, 6));
        content.Maps[1].Definition.RestAreas.Add(new RestAreaDefinition { Name = "Inn", X = 0, Z = 0, Radius = 15 });
        await using var realm = await TestRealm.StartAsync(database, content, gmAll: true);
        var (account, character) = await realm.NewPlayerAsync("rester", "Rester", Warrior);
        int left;
        using (var s = new Session(realm, realm.Enter(account, character)))
        {
            var c = s.Client;
            s.Pump(() => c.State == ClientState.InWorld && c.Stats.Resting);   // the spawn is in the inn
            s.Command(".rested 500");
            s.Pump(() => c.Stats.RestedXp == 500);

            c.SetTarget(s.Find("Mire Frog").Guid);
            s.Command(".damage 9999");
            s.Pump(() => s.Xp > 0 && c.Stats.RestedXp < 500);
            Assert.True(s.RestedBonus > 0);
            Assert.Equal(s.Xp, 2 * s.RestedBonus);                              // double experience...
            Assert.Equal(500 - s.Xp, c.Stats.RestedXp);                         // ...taken from the pool, bonus included
            left = c.Stats.RestedXp;

            s.Command(".tele 100 100");
            s.Pump(() => !c.Stats.Resting);
            c.Disconnect();
            realm.Pump(Array.Empty<GameClient>(), () => realm.Game.World.PlayerCount == 0);
        }
        await WorldIntegrationTests.WaitUntilAsync(() => realm.Host.Saver.SavedCount >= 1);
        Assert.Equal(left, (await database.LoadCharacterAsync(1, (long)character.Id))!.RestedXp);

        using var again = new Session(realm, realm.Enter(account, character));
        again.Pump(() => again.Client.State == ClientState.InWorld && again.Client.Stats.RestedXp >= left);
        Assert.False(again.Client.Stats.Resting);
    }
}
