using Madlands.Content;
using Madlands.Server.World;
using Madlands.Shared.Game;

namespace Madlands.Server.Combat;

/// <summary>
/// Applies, ticks and removes auras (ADR 0005). An aura's identity is its spell: harmful auras
/// stack per caster (two warriors can both Rend a wolf), beneficial ones replace each other.
/// </summary>
public sealed class AuraSystem
{
    public const int MaxAurasPerUnit = 40;
    /// <summary>Beneficial auras at least this long are saved with the character (class buffs, elixirs).</summary>
    public const int PersistMinMs = 60_000;

    private readonly MapInstance _map;
    // Separate lists: a periodic tick can kill (RemoveAll) or hit an absorb while Update iterates.
    private readonly List<Aura> _updateScratch = new();
    private readonly List<Aura> _removeAllScratch = new();
    private readonly List<Aura> _spentScratch = new();
    private readonly List<Aura> _breakScratch = new();

    public AuraSystem(MapInstance map)
    {
        _map = map;
    }

    public Aura? Find(Unit unit, uint spellId)
    {
        foreach (var a in unit.Auras) if (a.Spell.Id == spellId) return a;
        return null;
    }

    public Aura? Apply(Unit caster, Unit target, SpellDefinition spell, long now)
    {
        if (spell.Aura is not { } def || target.IsDead || target.Map != _map) return null;
        bool harmful = spell.Has(SpellFlags.HarmfulAura);

        Aura? aura = null;
        foreach (var a in target.Auras)
        {
            if (a.Spell.Id == spell.Id && (!harmful || a.Caster == caster.Guid))
            {
                aura = a;
                break;
            }
        }

        if (aura is null)
        {
            if (target.Auras.Count >= MaxAurasPerUnit) return null;
            aura = new Aura(spell, caster.Guid, caster.Level, FreeSlot(target)) { AppliedMs = now, NextTickMs = now + def.TickMs };
            target.Auras.Add(aura);
        }
        else
        {
            // Refresh: new duration, one more stack; the tick rhythm continues.
            aura.CasterLevel = caster.Level;
            aura.Stacks = Math.Min(aura.Stacks + 1, def.MaxStacks);
        }
        aura.ExpiresMs = now + def.DurationMs;
        for (int i = 0; i < def.Effects.Count; i++)
        {
            var e = def.Effects[i];
            aura.Amounts[i] = e.Amount + e.PerLevel * Math.Max(0, caster.Level - 1);
            if (e.Type == AuraEffectType.Absorb) aura.AbsorbRemaining = aura.Amounts[i];
        }

        if (aura.AffectsStats) target.RecalculateStats();
        if (target.IsStunned) _map.Spells.Interrupt(target, CastResult.Stunned, now);
        if (target.IsImmobile && target is Creature c) c.Mover.Stop(now);
        _map.SendAuraUpdate(target, aura, removed: false, now);

        if (harmful && caster != target)
        {
            _map.Combat.EnterCombat(caster, target, now);
            if (target is Creature creature) _map.Combat.AddThreat(creature, caster, 1f, now);
        }
        return aura;
    }

    /// <summary>Long beneficial auras survive logging out, as in WoW (not eating and drinking).</summary>
    public static bool Persists(Aura aura) => !aura.IsHarmful && !aura.Definition.BreakOnCombat && aura.DurationMs >= PersistMinMs;

    /// <summary>
    /// Puts a saved aura back on a player entering the world, with the time it had left. Sends nothing: the
    /// player is not on a map yet (<see cref="MapInstance.SendOwnAuras"/> follows).
    /// </summary>
    public static void Restore(Unit target, SpellDefinition spell, int casterLevel, int stacks, int remainingMs, long now)
    {
        if (spell.Aura is not { } def || remainingMs <= 0 || target.Auras.Count >= MaxAurasPerUnit) return;
        foreach (var a in target.Auras) if (a.Spell.Id == spell.Id) return;
        var aura = new Aura(spell, target.Guid, casterLevel, FreeSlot(target))
        {
            AppliedMs = now,
            NextTickMs = now + def.TickMs,
            ExpiresMs = now + Math.Min(remainingMs, def.DurationMs),
            Stacks = Math.Clamp(stacks, 1, def.MaxStacks),
        };
        for (int i = 0; i < def.Effects.Count; i++)
            aura.Amounts[i] = def.Effects[i].Amount + def.Effects[i].PerLevel * Math.Max(0, casterLevel - 1);
        target.Auras.Add(aura);
    }

    public void Remove(Unit unit, Aura aura, long now)
    {
        if (!unit.Auras.Remove(aura)) return;
        if (aura.AffectsStats) unit.RecalculateStats();
        _map.SendAuraUpdate(unit, aura, removed: true, now);
    }

    /// <summary>Removes the auras that end when the unit enters combat (eating, drinking).</summary>
    public void BreakOnCombat(Unit unit, long now)
    {
        if (unit.Auras.Count == 0) return;
        _breakScratch.Clear();
        foreach (var aura in unit.Auras)
            if (aura.Definition.BreakOnCombat) _breakScratch.Add(aura);
        foreach (var aura in _breakScratch) Remove(unit, aura, now);
    }

    public void RemoveAll(Unit unit, long now)
    {
        if (unit.Auras.Count == 0) return;
        _removeAllScratch.Clear();
        _removeAllScratch.AddRange(unit.Auras);
        foreach (var aura in _removeAllScratch) Remove(unit, aura, now);
    }

    /// <summary>Runs periodic ticks that are due and removes expired auras.</summary>
    public void Update(Unit unit, long now)
    {
        if (unit.Auras.Count == 0) return;
        _updateScratch.Clear();
        _updateScratch.AddRange(unit.Auras);
        foreach (var aura in _updateScratch)
        {
            if (!unit.Auras.Contains(aura)) continue; // removed by an earlier tick (death)
            while (aura.NextTickMs <= now && aura.NextTickMs <= aura.ExpiresMs && unit.IsAlive)
            {
                Tick(unit, aura, now);
                aura.NextTickMs += aura.Definition.TickMs;
            }
            if (now >= aura.ExpiresMs && unit.Auras.Contains(aura)) Remove(unit, aura, now);
        }
    }

    private void Tick(Unit unit, Aura aura, long now)
    {
        var effects = aura.Definition.Effects;
        for (int i = 0; i < effects.Count; i++)
        {
            int amount = Math.Max(1, (int)MathF.Round(aura.Amounts[i] * aura.Stacks));
            var caster = _map.FindUnit(aura.Caster);
            switch (effects[i].Type)
            {
                case AuraEffectType.PeriodicDamage:
                    if (caster is not null) amount = Math.Max(1, (int)MathF.Round(amount * caster.Stats.DamageDoneMultiplier));
                    _map.Combat.DealDamage(caster, aura.Caster, unit, amount, aura.Spell.School, aura.Spell.Id, CombatFlags.Periodic, now);
                    break;
                case AuraEffectType.PeriodicHeal:
                    _map.Combat.Heal(caster, unit, amount, aura.Spell.Id, aura.Spell.School, CombatFlags.Periodic, now);
                    break;
                case AuraEffectType.PeriodicEnergize:
                    if (unit.PowerType == effects[i].Power) unit.Power += amount;
                    break;
            }
            if (unit.IsDead) return;
        }
    }

    /// <summary>Lets absorb auras soak up damage (oldest first). Returns the amount absorbed.</summary>
    public int Absorb(Unit unit, ref int amount, long now)
    {
        if (amount <= 0 || unit.Auras.Count == 0) return 0;
        int absorbed = 0;
        _spentScratch.Clear();
        foreach (var aura in unit.Auras)
        {
            if (aura.AbsorbRemaining <= 0) continue;
            int take = Math.Min(amount, (int)MathF.Ceiling(aura.AbsorbRemaining));
            aura.AbsorbRemaining -= take;
            amount -= take;
            absorbed += take;
            if (aura.AbsorbRemaining <= 0) _spentScratch.Add(aura);
            if (amount <= 0) break;
        }
        foreach (var spent in _spentScratch) Remove(unit, spent, now);
        return absorbed;
    }

    private static byte FreeSlot(Unit unit)
    {
        for (int slot = 0; slot < 255; slot++)
        {
            bool used = false;
            foreach (var a in unit.Auras)
                if (a.Slot == slot) { used = true; break; }
            if (!used) return (byte)slot;
        }
        return 255;
    }
}
