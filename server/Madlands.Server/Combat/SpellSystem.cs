using Madlands.Content;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;

namespace Madlands.Server.Combat;

/// <summary>
/// Casting (ADR 0005): a cast is validated, may take time (interrupted by moving, stuns or death),
/// then pays its cost, starts cooldowns and applies its effects: right away, or when its
/// projectile arrives. Effects are data; each <see cref="SpellEffectType"/> has a handler here.
/// </summary>
public sealed class SpellSystem
{
    /// <summary>Extra range allowed when a cast finishes, so a target that stepped back is still hit.</summary>
    public const float FinishRangeLeeway = 2.5f;
    /// <summary>Moving this far from where the cast started interrupts it.</summary>
    public const float CastMoveTolerance = 0.25f;

    private readonly MapInstance _map;
    private readonly List<PendingHit> _pending = new();
    private readonly List<PendingHit> _dueScratch = new();
    private readonly List<Unit> _targets = new();
    private readonly List<Unit> _queryScratch = new();
    private readonly Dictionary<Unit, MeleeOutcome> _rolls = new();
    private readonly SpellStart _startMsg = new();
    private readonly SpellGo _goMsg = new();
    private readonly SpellFailed _failedMsg = new();
    private readonly Cooldown _cooldownMsg = new();
    private readonly Spellbook _spellbookMsg = new();

    public SpellSystem(MapInstance map)
    {
        _map = map;
        AttackSpellId = map.Content.Spells.Values.FirstOrDefault(s => s.Has(SpellFlags.AutoAttack))?.Id ?? 1;
    }

    /// <summary>The content's auto-attack spell (reported in auto-attack errors).</summary>
    public uint AttackSpellId { get; }

    private CombatSystem Combat => _map.Combat;

    // ------------------------------------------------------------------ casting

    /// <summary>Validates and starts a cast. Failures are reported to a casting player.</summary>
    public CastResult TryCast(Unit caster, uint spellId, EntityGuid targetGuid, byte castId, long now, bool ignoreKnown = false)
    {
        if (!_map.Content.Spells.TryGetValue(spellId, out var spell)) return Fail(caster, spellId, castId, CastResult.NotKnown, false);
        if (caster is Player player && !ignoreKnown && !player.KnownSpells.Contains(spellId))
            return Fail(caster, spellId, castId, CastResult.NotKnown, false);

        var target = ResolveTarget(caster, spell, targetGuid);
        var result = Check(caster, spell, target, now, starting: true);
        if (result != CastResult.Success) return Fail(caster, spellId, castId, result, false);

        if (spell.Has(SpellFlags.AutoAttack))
        {
            Combat.StartAutoAttack(caster, target!, now);
            return CastResult.Success;
        }

        if (spell.GcdMs > 0) caster.GcdEndsMs = now + spell.GcdMs;
        if (spell.CastTimeMs > 0)
        {
            caster.Casting = new SpellCast(spell, target?.Guid ?? EntityGuid.Empty, castId, now, caster.Position);
            if (caster is Creature creature) creature.Mover.Stop(now);
            _startMsg.Caster = caster.Guid;
            _startMsg.Target = target?.Guid ?? EntityGuid.Empty;
            _startMsg.SpellId = spell.Id;
            _startMsg.CastId = castId;
            _startMsg.CastTimeMs = spell.CastTimeMs;
            _map.SendToObservers(caster, _startMsg);
            return CastResult.Success;
        }

        Execute(caster, spell, target, castId, now);
        return CastResult.Success;
    }

    /// <summary>Finishes a cast whose time is up.</summary>
    public void UpdateCasting(Unit caster, long now)
    {
        if (caster.Casting is not { } cast || now < cast.EndMs) return;
        caster.Casting = null;
        var target = ResolveTarget(caster, cast.Spell, cast.Target);
        var result = Check(caster, cast.Spell, target, now, starting: false);
        if (result != CastResult.Success)
        {
            Fail(caster, cast.Spell.Id, cast.CastId, result, toObservers: true);
            return;
        }
        Execute(caster, cast.Spell, target, cast.CastId, now);
    }

    public void Interrupt(Unit caster, CastResult reason, long now)
    {
        if (caster.Casting is not { } cast) return;
        caster.Casting = null;
        Fail(caster, cast.Spell.Id, cast.CastId, reason, toObservers: true);
    }

    /// <summary>Casting takes standing still: moving or jumping interrupts it.</summary>
    public void OnCasterMoved(Unit caster, Vec3 previous, long now)
    {
        if (caster.Casting is not { } cast) return;
        if (Vec3.HorizontalDistance(cast.StartPosition, caster.Position) > CastMoveTolerance || caster.Movement.IsAirborne)
            Interrupt(caster, CastResult.Interrupted, now);
    }

    private Unit? ResolveTarget(Unit caster, SpellDefinition spell, EntityGuid targetGuid)
    {
        switch (spell.Target)
        {
            case SpellTarget.Self:
            case SpellTarget.None:
                return caster;
            case SpellTarget.Friend:
                var friend = _map.FindUnit(targetGuid);
                return friend is not null && friend.IsAlive && _map.IsFriendly(caster, friend) ? friend : caster;
            default:
                return _map.FindUnit(targetGuid);
        }
    }

    private CastResult Check(Unit caster, SpellDefinition spell, Unit? target, long now, bool starting)
    {
        if (caster.IsDead) return CastResult.CasterDead;
        if (caster.IsStunned) return CastResult.Stunned;
        bool god = caster is Player { GodMode: true };
        if (starting)
        {
            if (caster.Casting is not null) return CastResult.AlreadyCasting;
            if (spell.GcdMs > 0 && now < caster.GcdEndsMs) return CastResult.NotReady;
            if (caster.Cooldowns.TryGetValue(spell.Id, out long readyAt) && now < readyAt) return CastResult.OnCooldown;
            if (caster is Player && spell.CastTimeMs > 0 && caster.Movement.IsMoving) return CastResult.Moving;
        }
        if (!god && spell.Cost > 0 && (caster.PowerType != spell.PowerType || caster.Power < spell.Cost)) return CastResult.NoPower;

        if (spell.Target == SpellTarget.Enemy)
        {
            if (target is null) return CastResult.NoTarget;
            if (target.IsDead) return CastResult.TargetDead;
            if (!_map.CanAttack(caster, target)) return CastResult.InvalidTarget;
        }
        if (target is not null && target != caster && spell.Target is SpellTarget.Enemy or SpellTarget.Friend)
        {
            bool melee = spell.RangeMax <= GameConstants.MeleeRange;
            float leeway = melee ? CombatSystem.MeleeLeeway : starting ? 0f : FinishRangeLeeway;
            float distance = caster.DistanceTo(target);
            if (distance > spell.RangeMax + leeway) return CastResult.OutOfRange;
            if (distance < spell.RangeMin) return CastResult.TooClose;
            if (!spell.Has(SpellFlags.IgnoreLineOfSight) && !_map.HasLineOfSight(caster, target)) return CastResult.NotInLineOfSight;
            if (starting && caster is Player && spell.Has(SpellFlags.RequiresFacing) && !caster.IsInFront(target)) return CastResult.NotFacing;
        }
        return CastResult.Success;
    }

    private CastResult Fail(Unit caster, uint spellId, byte castId, CastResult result, bool toObservers)
    {
        _failedMsg.Caster = caster.Guid;
        _failedMsg.SpellId = spellId;
        _failedMsg.CastId = castId;
        _failedMsg.Result = result;
        if (toObservers) _map.SendToObservers(caster, _failedMsg);
        else if (caster is Player player) _map.Send(player, _failedMsg);
        return result;
    }

    private void Execute(Unit caster, SpellDefinition spell, Unit? target, byte castId, long now)
    {
        bool god = caster is Player { GodMode: true };
        if (spell.Cost > 0 && !god)
        {
            caster.Power -= spell.Cost;
            if (spell.PowerType == PowerType.Mana) caster.LastManaSpendMs = now;
        }
        if (spell.CooldownMs > 0 && !god)
        {
            caster.Cooldowns[spell.Id] = now + spell.CooldownMs;
            if (caster is Player player)
            {
                _cooldownMsg.SpellId = spell.Id;
                _cooldownMsg.DurationMs = spell.CooldownMs;
                _map.Send(player, _cooldownMsg);
            }
        }

        _goMsg.Caster = caster.Guid;
        _goMsg.Target = target?.Guid ?? EntityGuid.Empty;
        _goMsg.SpellId = spell.Id;
        _goMsg.CastId = castId;
        _map.SendToObservers(caster, _goMsg);

        if (spell.ProjectileSpeed > 0 && target is not null && target != caster)
        {
            float distance = caster.DistanceTo(target);
            _pending.Add(new PendingHit
            {
                Spell = spell,
                Caster = caster.Guid,
                Target = target.Guid,
                HitAtMs = now + (long)(distance / spell.ProjectileSpeed * 1000f),
            });
            return;
        }
        ApplyEffects(caster, spell, target, now);
    }

    public void UpdateProjectiles(long now)
    {
        if (_pending.Count == 0) return;
        _dueScratch.Clear();
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].HitAtMs > now) continue;
            _dueScratch.Add(_pending[i]);
            _pending.RemoveAt(i);
        }
        foreach (var hit in _dueScratch)
        {
            var caster = _map.FindUnit(hit.Caster);
            var target = _map.FindUnit(hit.Target);
            if (caster is null || target is null || target.IsDead) continue;
            ApplyEffects(caster, hit.Spell, target, now);
        }
    }

    // ------------------------------------------------------------------ effects

    /// <summary>Applies every effect to its targets. Hostile targets roll to hit once per spell.</summary>
    public void ApplyEffects(Unit caster, SpellDefinition spell, Unit? primary, long now)
    {
        _rolls.Clear();
        bool weapon = spell.Effects.Any(e => e.Type == SpellEffectType.WeaponDamage);
        foreach (var effect in spell.Effects)
        {
            CollectTargets(caster, spell, effect, primary);
            foreach (var target in _targets)
            {
                if (target.Map != _map) continue;
                if (caster != target && _map.CanAttack(caster, target))
                {
                    if (!_rolls.TryGetValue(target, out var outcome))
                    {
                        outcome = weapon ? Combat.RollMelee(caster, target)
                            : Combat.RollSpellHit(caster, target) ? MeleeOutcome.Hit : MeleeOutcome.Miss;
                        _rolls[target] = outcome;
                        if (CombatSystem.IsAvoided(outcome)) Combat.SendAvoided(caster, target, spell.Id, spell.School, outcome, now);
                    }
                    if (CombatSystem.IsAvoided(outcome)) continue;
                }
                if (target.IsDead) continue;
                ApplyEffect(caster, spell, effect, target, _rolls.GetValueOrDefault(target) == MeleeOutcome.Crit, now);
            }
        }
    }

    private void ApplyEffect(Unit caster, SpellDefinition spell, SpellEffectDefinition effect, Unit target, bool weaponCrit, long now)
    {
        int levelsAbove1 = Math.Max(0, caster.Level - 1);
        var s = caster.Stats;
        switch (effect.Type)
        {
            case SpellEffectType.SchoolDamage:
            {
                bool physical = spell.School == SpellSchool.Physical;
                float amount = Combat.RandomRange(effect.Min, effect.Max) + effect.PerLevel * levelsAbove1
                               + effect.Coefficient * (physical ? s.AttackPower : s.SpellPower);
                amount *= s.DamageDoneMultiplier;
                bool crit = Combat.RollChance(physical ? s.CritChance : s.SpellCritChance);
                if (crit) amount *= physical ? 2f : 1.5f;
                if (physical) amount *= 1f - StatFormulas.ArmorReduction(target.Stats.Armor, caster.Level);
                Combat.DealDamage(caster, caster.Guid, target, Math.Max(1, (int)MathF.Round(amount)), spell.School, spell.Id,
                    crit ? CombatFlags.Crit : CombatFlags.None, now);
                break;
            }
            case SpellEffectType.WeaponDamage:
                Combat.ApplyWeaponDamage(caster, target, spell.Id, effect.Percent, effect.Bonus + effect.BonusPerLevel * levelsAbove1, weaponCrit, now, autoAttack: false);
                break;
            case SpellEffectType.Heal:
            {
                float amount = Combat.RandomRange(effect.Min, effect.Max) + effect.PerLevel * levelsAbove1 + effect.Coefficient * s.SpellPower;
                bool crit = Combat.RollChance(s.SpellCritChance);
                if (crit) amount *= 1.5f;
                Combat.Heal(caster, target, Math.Max(1, (int)MathF.Round(amount)), spell.Id, spell.School, crit ? CombatFlags.Crit : CombatFlags.None, now);
                break;
            }
            case SpellEffectType.ApplyAura:
                _map.Auras.Apply(caster, target, spell, now);
                break;
            case SpellEffectType.Energize:
                if (target.PowerType == effect.Power)
                    target.Power += Math.Max(0, (int)MathF.Round(Combat.RandomRange(effect.Min, effect.Max) + effect.PerLevel * levelsAbove1));
                break;
        }
    }

    private void CollectTargets(Unit caster, SpellDefinition spell, SpellEffectDefinition effect, Unit? primary)
    {
        _targets.Clear();
        switch (effect.Targets)
        {
            case EffectTargets.Target:
                if (primary is not null) _targets.Add(primary);
                break;
            case EffectTargets.Caster:
                _targets.Add(caster);
                break;
            case EffectTargets.EnemiesNearCaster:
                CollectArea(caster, caster, effect.Radius, enemies: true);
                break;
            case EffectTargets.EnemiesNearTarget:
                if (primary is not null) CollectArea(caster, primary, effect.Radius, enemies: true);
                break;
            case EffectTargets.AlliesNearCaster:
                CollectArea(caster, caster, effect.Radius, enemies: false);
                break;
        }
    }

    private void CollectArea(Unit caster, Unit center, float radius, bool enemies)
    {
        _queryScratch.Clear();
        _map.QueryUnits(center.Position, radius, _queryScratch);
        foreach (var unit in _queryScratch)
        {
            if (unit.IsDead || Vec3.Distance(unit.Position, center.Position) > radius) continue;
            bool match = enemies ? _map.CanAttack(caster, unit) : _map.IsFriendly(caster, unit);
            if (match && _map.Collision.HasLineOfSight(center.EyePosition, unit.EyePosition)) _targets.Add(unit);
        }
    }

    // ------------------------------------------------------------------ player data

    public void SendSpellbook(Player player)
    {
        _spellbookMsg.SpellIds.Clear();
        _spellbookMsg.SpellIds.AddRange(player.KnownSpells.OrderBy(id => id));
        _map.Send(player, _spellbookMsg);
    }

    public void ResetCooldowns(Unit unit)
    {
        unit.Cooldowns.Clear();
        unit.GcdEndsMs = 0;
        if (unit is not Player player) return;
        _cooldownMsg.SpellId = 0;
        _cooldownMsg.DurationMs = 0;
        _map.Send(player, _cooldownMsg);
    }
}
