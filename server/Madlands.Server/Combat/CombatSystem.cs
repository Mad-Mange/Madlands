using Madlands.Content;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;

namespace Madlands.Server.Combat;

public enum MeleeOutcome
{
    Hit,
    Crit,
    Miss,
    Dodge,
    Parry,
}

/// <summary>
/// Damage, healing, death, threat, experience, regeneration and melee auto-attack for one map
/// instance. Runs on the instance's thread like everything else in it.
/// </summary>
public sealed class CombatSystem
{
    /// <summary>A unit leaves combat this long after its last hostile action (unless a creature still hunts it).</summary>
    public const long CombatTimeoutMs = 5000;
    public const long RegenIntervalMs = 2000;
    /// <summary>Mana regenerates fully only this long after the last mana was spent (the "five second rule").</summary>
    public const long ManaRegenDelayMs = 5000;
    /// <summary>Kill credit needs the tapper this close to the kill.</summary>
    public const float XpRange = 100f;
    /// <summary>Melee reach is checked with this much slack, so swings land on a target that just stepped away.</summary>
    public const float MeleeLeeway = 1f;
    public const long AttackErrorIntervalMs = 1000;
    public const long RestCheckIntervalMs = 1000;

    private readonly MapInstance _map;
    private readonly CombatEvent _event = new();
    private readonly SpellFailed _attackError = new();
    private readonly ExperienceGained _xpMsg = new();
    private readonly List<Creature> _creatureScratch = new();

    public CombatSystem(MapInstance map)
    {
        _map = map;
    }

    private Random Random => _map.Random;

    /// <summary>Per-tick update of one unit: auras, casting, swings, combat state, regeneration.</summary>
    public void UpdateUnit(Unit unit, long now)
    {
        _map.Auras.Update(unit, now);
        if (unit is Player player) player.Validator.SetLimits(player.SpeedMultiplier, player.IsImmobile, now);
        if (unit.IsDead) return;
        _map.Spells.UpdateCasting(unit, now);
        UpdateAutoAttack(unit, now);
        UpdateCombatState(unit, now);
        if (now >= unit.NextRegenMs)
        {
            if (unit.NextRegenMs > 0) Regenerate(unit, now);
            unit.NextRegenMs = now + RegenIntervalMs;
        }
    }

    // ------------------------------------------------------------------ auto-attack

    public void StartAutoAttack(Unit attacker, Unit target, long now)
    {
        attacker.AttackTarget = target;
        attacker.SetFlag(UnitFlags.AutoAttacking, true);
        if (attacker.NextSwingMs < now) attacker.NextSwingMs = now;
    }

    public void StopAutoAttack(Unit attacker)
    {
        attacker.AttackTarget = null;
        attacker.SetFlag(UnitFlags.AutoAttacking, false);
    }

    private void UpdateAutoAttack(Unit attacker, long now)
    {
        var target = attacker.AttackTarget;
        if (target is null) return;
        if (target.Map != _map || !_map.CanAttack(attacker, target))
        {
            StopAutoAttack(attacker);
            return;
        }
        // Swings wait while stunned or casting (casting does not reset the timer here).
        if (now < attacker.NextSwingMs || attacker.IsStunned || attacker.Casting is not null) return;

        if (attacker.DistanceTo(target) > GameConstants.MeleeRange + MeleeLeeway)
        {
            AttackError(attacker, CastResult.OutOfRange, now);
            return;
        }
        if (attacker is Player && !attacker.IsInFront(target))
        {
            AttackError(attacker, CastResult.NotFacing, now);
            return;
        }
        attacker.NextSwingMs = now + attacker.Stats.AttackSpeedMs;
        MeleeHit(attacker, target, 0, 100f, 0f, now, autoAttack: true);
    }

    private void AttackError(Unit attacker, CastResult result, long now)
    {
        if (attacker is not Player player || now < player.NextAttackErrorMs) return;
        player.NextAttackErrorMs = now + AttackErrorIntervalMs;
        _attackError.Caster = player.Guid;
        _attackError.SpellId = _map.Spells.AttackSpellId;
        _attackError.CastId = 0;
        _attackError.Result = result;
        _map.Send(player, _attackError);
    }

    /// <summary>A weapon strike: rolls miss/dodge/parry/crit, applies armor, deals physical damage.</summary>
    public MeleeOutcome MeleeHit(Unit attacker, Unit target, uint spellId, float percent, float bonus, long now, bool autoAttack = false)
    {
        var outcome = RollMelee(attacker, target);
        if (IsAvoided(outcome))
        {
            SendAvoided(attacker, target, spellId, SpellSchool.Physical, outcome, now);
            return outcome;
        }
        ApplyWeaponDamage(attacker, target, spellId, percent, bonus, outcome == MeleeOutcome.Crit, now, autoAttack);
        return outcome;
    }

    public static bool IsAvoided(MeleeOutcome outcome) => outcome is MeleeOutcome.Miss or MeleeOutcome.Dodge or MeleeOutcome.Parry;

    /// <summary>Reports a miss/dodge/parry; the attempt still starts the fight.</summary>
    public void SendAvoided(Unit attacker, Unit target, uint spellId, SpellSchool school, MeleeOutcome outcome, long now)
    {
        var flags = outcome switch
        {
            MeleeOutcome.Dodge => CombatFlags.Dodge,
            MeleeOutcome.Parry => CombatFlags.Parry,
            _ => CombatFlags.Miss,
        };
        EnterCombat(attacker, target, now);
        if (target is Creature c) AddThreat(c, attacker, 1f, now);
        SendEvent(attacker.Guid, attacker, target, spellId, school, 0, 0, flags);
    }

    /// <summary>Weapon damage (percent of a swing plus a flat bonus) after the hit roll.</summary>
    public void ApplyWeaponDamage(Unit attacker, Unit target, uint spellId, float percent, float bonus, bool crit, long now, bool autoAttack)
    {
        var s = attacker.Stats;
        float damage = RandomRange(s.WeaponMin, s.WeaponMax) + s.AttackPowerBonus;
        damage = damage * percent / 100f + bonus;
        if (crit) damage *= 2f;
        damage *= s.DamageDoneMultiplier;
        damage *= 1f - StatFormulas.ArmorReduction(target.Stats.Armor, attacker.Level);
        DealDamage(attacker, attacker.Guid, target, Math.Max(1, (int)MathF.Round(damage)), SpellSchool.Physical, spellId,
            crit ? CombatFlags.Crit : CombatFlags.None, now, melee: autoAttack);
    }

    public MeleeOutcome RollMelee(Unit attacker, Unit target)
    {
        int diff = target.Level - attacker.Level;
        float miss = Math.Clamp(5f + diff, 1f, 60f);
        bool canDefend = !target.IsStunned && target.IsInFront(attacker);
        float dodge = canDefend ? target.Stats.DodgeChance + Math.Max(0, diff) * 0.5f : 0f;
        float parry = canDefend && target is Creature ? target.Stats.ParryChance : 0f;
        float crit = Math.Max(0f, attacker.Stats.CritChance - diff);

        float roll = Random.NextSingle() * 100f;
        if ((roll -= miss) < 0) return MeleeOutcome.Miss;
        if ((roll -= dodge) < 0) return MeleeOutcome.Dodge;
        if ((roll -= parry) < 0) return MeleeOutcome.Parry;
        if ((roll -= crit) < 0) return MeleeOutcome.Crit;
        return MeleeOutcome.Hit;
    }

    /// <summary>Spell hit roll: 4% miss against an equal level, steeply worse against higher levels.</summary>
    public bool RollSpellHit(Unit caster, Unit target)
    {
        int diff = target.Level - caster.Level;
        float miss = diff <= 2 ? Math.Clamp(4f + diff, 1f, 6f) : Math.Min(99f, 17f + 11f * (diff - 3));
        return Random.NextSingle() * 100f >= miss;
    }

    public bool RollChance(float percent) => Random.NextSingle() * 100f < percent;

    public float RandomRange(float min, float max) => max <= min ? min : min + Random.NextSingle() * (max - min);

    // ------------------------------------------------------------------ damage and healing

    /// <summary>Applies damage after absorbs. Returns the health actually removed. <paramref name="melee"/> generates rage for the attacker.</summary>
    public int DealDamage(Unit? attacker, EntityGuid source, Unit victim, int amount, SpellSchool school, uint spellId,
        CombatFlags flags, long now, bool melee = false)
    {
        if (victim.IsDead || victim.Map != _map) return 0;
        if (attacker is not null && attacker != victim) EnterCombat(attacker, victim, now);

        if (victim.HasFlag(UnitFlags.Immune) || victim is Player { GodMode: true })
        {
            var immune = victim.HasFlag(UnitFlags.Evading) ? CombatFlags.Evade : CombatFlags.Immune;
            SendEvent(source, attacker, victim, spellId, school, 0, 0, flags | immune);
            return 0;
        }

        int absorbed = _map.Auras.Absorb(victim, ref amount, now);
        if (absorbed > 0) flags |= CombatFlags.Absorb;
        if (victim is Creature { IsDummy: true }) amount = Math.Min(amount, Math.Max(0, victim.Health - 1));

        int before = victim.Health;
        victim.Health = before - amount;
        int dealt = before - victim.Health;

        if (attacker is not null && melee && attacker.PowerType == PowerType.Rage && amount > 0)
            attacker.Power += Math.Max(1, (int)MathF.Round(7.5f * amount / StatFormulas.RageConversion(attacker.Level)));
        if (victim.PowerType == PowerType.Rage && amount > 0)
            victim.Power += Math.Max(1, (int)MathF.Round(2.5f * amount / StatFormulas.RageConversion(victim.Level)));

        if (victim is Creature creature && attacker is not null && attacker != victim)
        {
            if (creature.Tapper.IsEmpty && attacker is Player) creature.Tapper = attacker.Guid;
            AddThreat(creature, attacker, amount + absorbed, now);
        }

        bool killed = victim.Health <= 0;
        if (killed) flags |= CombatFlags.KillingBlow;
        SendEvent(source, attacker, victim, spellId, school, dealt, absorbed, flags);
        if (killed) Kill(victim, attacker, now);
        return dealt;
    }

    /// <summary>Restores health. Healing someone a creature is fighting draws half the heal as threat, split between those creatures.</summary>
    public int Heal(Unit? healer, Unit target, int amount, uint spellId, SpellSchool school, CombatFlags flags, long now)
    {
        if (target.IsDead || target.Map != _map || amount <= 0) return 0;
        int before = target.Health;
        target.Health = before + amount;
        int effective = target.Health - before;
        SendEvent(healer?.Guid ?? EntityGuid.Empty, healer, target, spellId, school, amount, 0, flags | CombatFlags.Heal);

        if (healer is not null && effective > 0 && target is Player patient && patient.ThreatenedBy.Count > 0)
        {
            _creatureScratch.Clear();
            _creatureScratch.AddRange(patient.ThreatenedBy);
            float share = effective * 0.5f / _creatureScratch.Count;
            foreach (var c in _creatureScratch) AddThreat(c, healer, share, now);
        }
        if (healer is not null && target.IsInCombat) healer.CombatUntilMs = Math.Max(healer.CombatUntilMs, now + CombatTimeoutMs);
        return effective;
    }

    public void AddThreat(Creature creature, Unit attacker, float amount, long now)
    {
        if (creature.IsDead || creature.HasFlag(UnitFlags.Evading) || attacker.IsDead || attacker.Map != _map) return;
        if (!_map.CanAttack(creature, attacker)) return;
        creature.Threat.Add(attacker, amount);
        if (attacker is Player player) player.ThreatenedBy.Add(creature);
        creature.Ai.OnAttacked(attacker, now);
    }

    /// <summary>Both sides are now fighting (eating and drinking stop).</summary>
    public void EnterCombat(Unit a, Unit b, long now)
    {
        _map.Auras.BreakOnCombat(a, now);
        _map.Auras.BreakOnCombat(b, now);
        a.CombatUntilMs = Math.Max(a.CombatUntilMs, now + CombatTimeoutMs);
        b.CombatUntilMs = Math.Max(b.CombatUntilMs, now + CombatTimeoutMs);
        a.SetFlag(UnitFlags.InCombat, true);
        b.SetFlag(UnitFlags.InCombat, true);
        if (b is Creature cb) AddThreat(cb, a, 0f, now);
        if (a is Creature ca) AddThreat(ca, b, 0f, now);
    }

    private void UpdateCombatState(Unit unit, long now)
    {
        bool inCombat = unit switch
        {
            Player p => now < p.CombatUntilMs || p.ThreatenedBy.Count > 0,
            Creature c => c.Ai.IsFighting || now < c.CombatUntilMs,
            _ => false,
        };
        unit.SetFlag(UnitFlags.InCombat, inCombat);
    }

    private void SendEvent(EntityGuid source, Unit? sourceUnit, Unit target, uint spellId, SpellSchool school, int amount, int absorbed, CombatFlags flags)
    {
        _event.Source = source;
        _event.Target = target.Guid;
        _event.SpellId = spellId;
        _event.School = school;
        _event.Amount = amount;
        _event.Absorbed = absorbed;
        _event.Flags = flags;
        _map.SendToObservers(target, _event, includeSelf: true, alsoTo: sourceUnit as Player);
    }

    // ------------------------------------------------------------------ death

    public void Kill(Unit victim, Unit? killer, long now)
    {
        victim.Health = 0;
        victim.SetFlag(UnitFlags.Dead, true);
        victim.SetFlag(UnitFlags.InCombat, false);
        StopAutoAttack(victim);
        _map.Spells.Interrupt(victim, CastResult.CasterDead, now);
        _map.Auras.RemoveAll(victim, now);
        victim.CombatUntilMs = 0;

        switch (victim)
        {
            case Creature creature:
                creature.DiedAtMs = now;
                creature.DespawnAtMs = now + creature.Template.CorpseSeconds * 1000L;
                var tapper = _map.Find(creature.Tapper) as Player;
                if (tapper is not null && Vec3.Distance(tapper.Position, creature.Position) <= XpRange)
                {
                    GiveXp(tapper, Experience.ForKill(tapper.Level, creature.Level, creature.Template.Xp), creature.Guid, now);
                    _map.Quests.OnKill(tapper, creature);
                }
                _map.Loot.OnCreatureKilled(creature, tapper, now);
                ClearThreat(creature);
                creature.Mover.Stop(now);
                creature.Ai.OnDeath(now);
                break;
            case Player player:
                DropFromThreatLists(player);
                break;
        }
    }

    /// <summary>A unit left the map (logout, despawn): nobody may keep fighting it.</summary>
    public void OnUnitRemoved(Unit unit)
    {
        if (unit is Player player) DropFromThreatLists(player);
        if (unit is Creature creature) ClearThreat(creature);
    }

    /// <summary>Every creature forgets this player (GM god mode, death, logout).</summary>
    public void ClearAttackers(Player player) => DropFromThreatLists(player);

    private void DropFromThreatLists(Player player)
    {
        foreach (var creature in player.ThreatenedBy) creature.Threat.Remove(player);
        player.ThreatenedBy.Clear();
    }

    public void ClearThreat(Creature creature)
    {
        foreach (var unit in creature.Threat.Units)
            if (unit is Player p) p.ThreatenedBy.Remove(creature);
        creature.Threat.Clear();
    }

    /// <summary>Brings a dead player back to life at the map's spawn point (the graveyard for now) with half health and mana.</summary>
    public void Resurrect(Player player, long now, bool atGraveyard = true)
    {
        if (player.IsAlive) return;
        player.SetFlag(UnitFlags.Dead, false);
        player.Health = Math.Max(1, player.MaxHealth / 2);
        player.Power = player.PowerType == PowerType.Mana ? player.MaxPower / 2 : 0;
        player.NextRegenMs = now + RegenIntervalMs;
        if (atGraveyard) _map.Teleport(player, _map.Map.SpawnPosition, _map.Map.SpawnYaw, now);
    }

    // ------------------------------------------------------------------ regeneration

    private void Regenerate(Unit unit, long now)
    {
        var s = unit.Stats;
        switch (unit)
        {
            case Player p:
                if (!p.IsInCombat) p.Health += Math.Max(1, (int)(p.MaxHealth * 0.03f + s.Spirit * 0.1f));
                if (p.PowerType == PowerType.Mana)
                {
                    bool resting = now - p.LastManaSpendMs >= ManaRegenDelayMs;
                    p.Power += Math.Max(1, (int)(resting ? p.MaxPower * 0.04f + s.Spirit * 0.2f : p.MaxPower * 0.01f));
                }
                else if (p.PowerType == PowerType.Rage && !p.IsInCombat)
                {
                    p.Power -= 3;
                }
                else if (p.PowerType == PowerType.Energy)
                {
                    p.Power += 20;
                }
                break;
            case Creature c:
                if (!c.Ai.IsFighting && now >= c.CombatUntilMs)
                {
                    c.Health += Math.Max(1, c.MaxHealth / 10);
                    c.Power += Math.Max(1, c.MaxPower / 10);
                }
                else if (c.MaxPower > 0)
                {
                    c.Power += Math.Max(1, c.MaxPower / 50);
                }
                break;
        }
    }

    // ------------------------------------------------------------------ experience and levels

    /// <summary>Kills (a <paramref name="victim"/>) get the rested bonus on top; quests and GM grants do not.</summary>
    public void GiveXp(Player player, int amount, EntityGuid victim, long now)
    {
        int maxLevel = _map.Content.Levels.MaxLevel;
        if (amount <= 0 || player.Level >= maxLevel) return;
        int bonus = victim.IsEmpty ? 0 : Experience.RestedBonus(amount, ref player.RestedXp);
        amount += bonus;
        player.Xp += amount;
        _xpMsg.Amount = amount;
        _xpMsg.RestedBonus = bonus;
        _xpMsg.Victim = victim;
        _map.Send(player, _xpMsg);

        while (player.Level < maxLevel)
        {
            int needed = _map.Content.Levels.XpToNext(player.Level);
            if (needed <= 0 || player.Xp < needed) break;
            player.Xp -= needed;
            SetLevel(player, player.Level + 1, now);
        }
        player.StatsDirty = true;
    }

    /// <summary>Once a second: notices entering and leaving rest areas, and builds up rest inside them.</summary>
    public void UpdateRest(Player player, long now)
    {
        if (player.RestCheckedAtMs >= 0 && now - player.RestCheckedAtMs < RestCheckIntervalMs) return;
        double seconds = player.RestCheckedAtMs < 0 ? 0 : (now - player.RestCheckedAtMs) / 1000.0;
        player.RestCheckedAtMs = now;
        bool resting = _map.Map.Definition.IsRestArea(player.Position.X, player.Position.Z);
        if (resting != player.Resting)
        {
            player.Resting = resting;
            player.StatsDirty = true;
        }
        if (resting) AddRest(player, _map.Content.Levels, Experience.RestGained(_map.Content.Levels.Rest, _map.Content.Levels.XpToNext(player.Level), seconds, true));
    }

    /// <summary>Adds to the rested pool up to its cap (there is none at the level cap).</summary>
    public static void AddRest(Player player, LevelTable levels, double amount)
    {
        int before = (int)player.RestedXp;
        int needed = levels.XpToNext(player.Level);
        if (needed <= 0) player.RestedXp = 0;
        else if (amount > 0) player.RestedXp = Math.Max(player.RestedXp, Math.Min(Experience.MaxRested(levels.Rest, needed), player.RestedXp + amount));
        if ((int)player.RestedXp != before) player.StatsDirty = true;
    }

    public void SetLevel(Player player, int level, long now)
    {
        int maxLevel = _map.Content.Levels.MaxLevel;
        level = Math.Clamp(level, 1, maxLevel);
        bool up = level > player.Level;
        player.Level = (byte)level;
        if (level >= maxLevel)
        {
            player.Xp = 0;
            player.RestedXp = 0;
        }
        player.RecalculateStats();
        if (player.IsAlive)
        {
            player.Health = player.MaxHealth;
            if (player.PowerType == PowerType.Mana) player.Power = player.MaxPower;
        }
        if (player.UpdateKnownSpells()) _map.Spells.SendSpellbook(player);
        if (up) _map.SendSystemMessage(player, $"You have reached level {level}!");
        _map.Quests.RefreshStatuses(player);
    }
}
