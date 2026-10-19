using Madlands.Content;
using Madlands.Navigation;
using Madlands.Server.Combat;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Game;

namespace Madlands.Server.AI;

/// <summary>
/// Classic MMO creature behaviour: idle and wander near home, notice hostile players within the
/// aggro radius (with line of sight), call nearby friends for help, chase and fight the unit with
/// the most threat, and give up ("evade") when dragged too far from home or the target cannot be
/// reached, running back at full health.
/// </summary>
public sealed class CreatureAI
{
    public enum State
    {
        Idle,
        Combat,
        Evading,
        Dead,
    }

    /// <summary>Evade when this far from where the fight started.</summary>
    public const float LeashRange = 60f;
    /// <summary>Give up on a target that cannot be reached for this long.</summary>
    public const long UnreachableTimeoutMs = 6000;
    public const long ThinkIntervalMs = 250;
    public const long AggroScanIntervalMs = 400;
    /// <summary>Re-path when the chased target moved this far from the end of the current path.</summary>
    public const float RepathDistance = 1.5f;
    /// <summary>Melee creatures close in to this distance (a bit inside melee range, so swings keep landing).</summary>
    public const float MeleeApproach = GameConstants.MeleeRange - 1.5f;
    /// <summary>Casters keep this far from their target.</summary>
    public const float CasterRange = 24f;

    private readonly Creature _c;
    private readonly List<Unit> _scratch = new();
    private Unit? _target;
    private Vec3 _combatStart;
    private long _nextThinkMs;
    private long _nextAggroScanMs;
    private long _nextWanderMs;
    private long _unreachableSinceMs = -1;
    private long _evadeStartedMs;

    public CreatureAI(Creature creature)
    {
        _c = creature;
    }

    public State Current { get; private set; } = State.Idle;
    public bool IsFighting => Current == State.Combat;
    public Unit? Target => _target;

    private MapInstance Map => _c.Map!;

    public void Update(long now)
    {
        if (_c.Map is null || now < _nextThinkMs) return;
        _nextThinkMs = now + ThinkIntervalMs;
        switch (Current)
        {
            case State.Idle:
                UpdateIdle(now);
                break;
            case State.Combat:
                UpdateCombat(now);
                break;
            case State.Evading:
                UpdateEvade(now);
                break;
        }
    }

    // ------------------------------------------------------------------ idle

    private void UpdateIdle(long now)
    {
        if (_c.IsDummy) return;
        if (_c.Template.AggroRadius > 0 && now >= _nextAggroScanMs)
        {
            _nextAggroScanMs = now + AggroScanIntervalMs;
            if (FindAggroTarget() is { } victim)
            {
                Map.Combat.AddThreat(_c, victim, 0f, now);
                return;
            }
        }

        float wander = _c.Spawn?.WanderRadius ?? 0f;
        if (wander <= 0f || _c.Mover.IsMoving || _c.IsImmobile) return;
        if (_nextWanderMs == 0) _nextWanderMs = now + Map.Random.Next(2000, 9000);
        if (now < _nextWanderMs) return;
        _nextWanderMs = now + Map.Random.Next(6000, 15000);
        if (Map.Nav.TryRandomPointAround(_c.Home, wander, Map.Random, out var point)) _c.Mover.MoveTo(point, walk: true, now);
    }

    /// <summary>The closest hostile, living, visible player within aggro range. Higher-level players are noticed later.</summary>
    private Player? FindAggroTarget()
    {
        _scratch.Clear();
        Map.QueryUnits(_c.Position, _c.Template.AggroRadius + 5f, _scratch);
        Player? best = null;
        float bestDistance = float.MaxValue;
        foreach (var unit in _scratch)
        {
            if (unit is not Player player || player.IsDead || player.GodMode) continue;
            if (Map.GetReaction(_c, player) != Reaction.Hostile) continue;
            float radius = Math.Clamp(_c.Template.AggroRadius - (player.Level - _c.Level), 5f, 45f);
            float distance = _c.DistanceTo(player);
            if (distance > radius || distance >= bestDistance) continue;
            if (!Map.HasLineOfSight(_c, player)) continue;
            best = player;
            bestDistance = distance;
        }
        return best;
    }

    // ------------------------------------------------------------------ combat

    /// <summary>Something hit us or put us on alert (threat was added).</summary>
    public void OnAttacked(Unit attacker, long now)
    {
        if (Current is State.Dead or State.Evading || _c.IsDummy) return;
        if (Current == State.Idle)
        {
            Current = State.Combat;
            _combatStart = _c.Position;
            _unreachableSinceMs = -1;
            _nextThinkMs = now; // react this tick
            _c.SetFlag(UnitFlags.InCombat, true);
            CallForHelp(attacker, now);
        }
    }

    private void CallForHelp(Unit attacker, long now)
    {
        float radius = _c.Template.CallForHelpRadius;
        if (radius <= 0f) return;
        _scratch.Clear();
        Map.QueryUnits(_c.Position, radius, _scratch);
        foreach (var unit in _scratch)
        {
            if (unit is not Creature friend || friend == _c || friend.IsDead || friend.Ai.Current != State.Idle) continue;
            if (friend.Faction != _c.Faction || !Map.CanAttack(friend, attacker)) continue;
            Map.Combat.AddThreat(friend, attacker, 0f, now);
        }
    }

    private void UpdateCombat(long now)
    {
        // Drop targets that died, left, or became friendly.
        _scratch.Clear();
        foreach (var unit in _c.Threat.Units)
            if (unit.IsDead || unit.Map != _c.Map || !Map.CanAttack(_c, unit)) _scratch.Add(unit);
        foreach (var unit in _scratch)
        {
            _c.Threat.Remove(unit);
            if (unit is Player p) p.ThreatenedBy.Remove(_c);
        }

        _target = _c.Threat.SelectTarget(_target);
        if (_target is null || Vec3.HorizontalDistance(_c.Position, _combatStart) > LeashRange)
        {
            Evade(now);
            return;
        }
        _c.TargetGuid = _target.Guid;
        if (_c.IsStunned || _c.Casting is not null) return;

        float distance = _c.DistanceTo(_target);
        bool canSee = Map.HasLineOfSight(_c, _target);
        bool caster = _c.Template.Ai == CreatureAi.Caster && TryPickSpell(now, onlyRanged: true) is not null;
        float wanted = caster ? CasterRange : MeleeApproach;

        if (distance > wanted || !canSee)
        {
            ChaseTarget(now);
            if (_unreachableSinceMs >= 0 && now - _unreachableSinceMs > UnreachableTimeoutMs)
            {
                Evade(now);
                return;
            }
        }
        else
        {
            _unreachableSinceMs = -1;
            if (_c.Mover.IsMoving) _c.Mover.Stop(now);
            _c.Mover.Face(_target.Position, now);
        }

        // Melee whenever in reach; use abilities when they come up.
        if (distance <= GameConstants.MeleeRange && _c.AttackTarget != _target) Map.Combat.StartAutoAttack(_c, _target, now);
        else if (distance > GameConstants.MeleeRange + 2f && _c.AttackTarget is not null) Map.Combat.StopAutoAttack(_c);

        if (TryPickSpell(now, onlyRanged: false) is { } spell && Map.Spells.TryCast(_c, spell.Spell, _target.Guid, 0, now) == CastResult.Success)
        {
            if (spell.CooldownMs > 0) _c.Cooldowns[spell.Spell] = Math.Max(_c.Cooldowns.GetValueOrDefault(spell.Spell), now + spell.CooldownMs);
        }
    }

    private void ChaseTarget(long now)
    {
        if (_c.IsImmobile) return;
        var goal = _target!.Position;
        bool repath = !_c.Mover.IsMoving || Vec3.HorizontalDistance(_c.Mover.Destination, goal) > RepathDistance;
        if (!repath) return;
        var result = _c.Mover.MoveTo(goal, walk: false, now);
        if (result == PathResult.Complete) _unreachableSinceMs = -1;
        else if (_unreachableSinceMs < 0) _unreachableSinceMs = now;
    }

    /// <summary>A usable ability: off cooldown, affordable, target in range; rolls each ability's chance.</summary>
    private CreatureSpell? TryPickSpell(long now, bool onlyRanged)
    {
        if (_target is null) return null;
        foreach (var cs in _c.Template.Spells)
        {
            if (!Map.Content.Spells.TryGetValue(cs.Spell, out var spell)) continue;
            if (onlyRanged && spell.RangeMax <= GameConstants.MeleeRange) continue;
            if (_c.Cooldowns.TryGetValue(cs.Spell, out long ready) && now < ready) continue;
            if (cs.HealthBelow > 0 && _c.Health * 100f > _c.MaxHealth * cs.HealthBelow) continue;
            if (spell.Cost > 0 && (_c.PowerType != spell.PowerType || _c.Power < spell.Cost)) continue;
            if (onlyRanged) return cs; // "could cast": decides whether to keep casting distance
            // Spells on the creature itself or around it (a slam, an enrage) are used with the target in melee reach.
            float reach = spell.Target is SpellTarget.Enemy or SpellTarget.Friend ? spell.RangeMax : GameConstants.MeleeRange + CombatSystem.MeleeLeeway;
            if (_c.DistanceTo(_target) > reach) continue;
            if (Map.Random.NextSingle() > cs.Chance)
            {
                // Missed the roll: try again a little later, not every think.
                _c.Cooldowns[cs.Spell] = now + 1500;
                continue;
            }
            return cs;
        }
        return null;
    }

    // ------------------------------------------------------------------ evading and death

    public void Evade(long now)
    {
        Map.Combat.ClearThreat(_c);
        Map.Combat.StopAutoAttack(_c);
        Map.Spells.Interrupt(_c, CastResult.Interrupted, now);
        _target = null;
        _c.TargetGuid = EntityGuid.Empty;
        _c.Tapper = EntityGuid.Empty;
        Current = State.Evading;
        _evadeStartedMs = now;
        _c.SetFlag(UnitFlags.Evading | UnitFlags.Immune, true);
        _c.SetFlag(UnitFlags.InCombat, false);
        _c.CombatUntilMs = 0;
        _c.Mover.MoveTo(_c.Home, walk: false, now);
    }

    private void UpdateEvade(long now)
    {
        bool home = Vec3.HorizontalDistance(_c.Position, _c.Home) < 1f;
        bool stuck = !_c.Mover.IsMoving && !home;
        if (stuck && now - _evadeStartedMs < 10_000 && !_c.IsImmobile)
        {
            _c.Mover.MoveTo(_c.Home, walk: false, now);
            return;
        }
        if (!home && !stuck) return;

        // Arrived (or gave up walking): reset completely.
        if (!home)
        {
            _c.Movement.Position = _c.Home;
            Map.OnMoved(_c);
        }
        _c.Mover.Stop(now);
        _c.Mover.Face(_c.Home + MathUtil.YawToDirection(_c.HomeYaw), now);
        Map.Auras.RemoveAll(_c, now);
        _c.Health = _c.MaxHealth;
        _c.Power = _c.MaxPower;
        _c.SetFlag(UnitFlags.Evading | UnitFlags.Immune, false);
        Current = State.Idle;
        _nextWanderMs = now + Map.Random.Next(3000, 8000);
    }

    public void OnDeath(long now)
    {
        Current = State.Dead;
        _target = null;
        _c.TargetGuid = EntityGuid.Empty;
    }
}
