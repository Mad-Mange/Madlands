using Madlands.Server.Combat;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Movement;
using Madlands.Shared.Terrain;

namespace Madlands.Server.Tests;

public class CombatRulesTests
{
    [Fact]
    public void Experience_follows_the_level_difference()
    {
        Assert.Equal(50, Experience.ForKill(1, 1));                       // 45 + 5 x level
        Assert.Equal(55, Experience.ForKill(1, 3));                       // +5% per level above
        Assert.Equal(60, Experience.ForKill(1, 9));                       // capped at +4 levels
        Assert.Equal(42, Experience.ForKill(5, 3));                       // 70 x (1 - 2/5)
        Assert.Equal(0, Experience.ForKill(10, 4));                       // gray: 10 - 5 - 1 = 4
        Assert.Equal(100, Experience.ForKill(1, 1, 2f));                  // multiplier (elites, later)
        Assert.Equal(0, Experience.ForKill(3, 3, 0f));                    // training dummies give nothing
    }

    [Fact]
    public void Rest_builds_up_in_villages_and_doubles_kills_while_it_lasts()
    {
        var rules = new Content.RestRules { PercentPerHour = 2.5f, ElsewhereFactor = 0.25f, MaxPercent = 150f };
        Assert.Equal(225.0, Experience.RestGained(rules, 900, 10 * 3600, inRestArea: true), 6);  // a night in Mirewatch: 25% of the bar
        Assert.Equal(56.25, Experience.RestGained(rules, 900, 10 * 3600, inRestArea: false), 6); // a quarter of that in the wild
        Assert.Equal(0.0, Experience.RestGained(rules, 0, 3600, true));                           // nothing at the level cap
        Assert.Equal(1350.0, Experience.MaxRested(rules, 900), 6);

        double rested = 100;
        Assert.Equal(40, Experience.RestedBonus(40, ref rested));   // double experience...
        Assert.Equal(20.0, rested, 6);                              // ...and the pool shrinks by all of it
        Assert.Equal(10, Experience.RestedBonus(40, ref rested));   // the bonus stops where the pool ends
        Assert.Equal(0.0, rested);
        Assert.Equal(0, Experience.RestedBonus(40, ref rested));
    }

    [Fact]
    public void Armor_and_rage_formulas()
    {
        Assert.Equal(0f, StatFormulas.ArmorReduction(0, 10));
        Assert.Equal(100f / (100 + 400 + 85), StatFormulas.ArmorReduction(100, 1), 4);
        Assert.Equal(0.75f, StatFormulas.ArmorReduction(1_000_000, 1));
        Assert.Equal(7.5f, StatFormulas.RageConversion(1), 1);
    }

    [Fact]
    public void Movement_limits_apply_after_a_grace_period()
    {
        var validator = new MovementValidator(HeightMap.CreateFlat(-100, -100, 1, 201, 201, 0f));
        var start = MovementState.Idle(Vec3.Zero, 0);
        validator.Reset(start, 0);
        Assert.Equal(MoveVerdict.Accepted, validator.Validate(start, 1000, 1000));

        // Rooted at 1000 ms: the client may still be running for a moment.
        validator.SetLimits(1f, immobile: true, 1000);
        Assert.Equal(1f, validator.AllowedMultiplier(1500));
        var step = MovementState.Idle(new Vec3(0, 0, 3), 0);
        Assert.Equal(MoveVerdict.Accepted, validator.Validate(step, 1500, 1500));

        // After the grace period it may not move at all.
        Assert.Equal(0f, validator.AllowedMultiplier(2100));
        var next = MovementState.Idle(new Vec3(0, 0, 5), 0);
        Assert.Equal(MoveVerdict.Rejected, validator.Validate(next, 2100, 2100));

        // Slowed to 50%: half the distance is fine, full speed is not.
        validator.Reset(step, 3000);
        validator.SetLimits(0.5f, immobile: false, 3000);
        Assert.Equal(MoveVerdict.Accepted, validator.Validate(step, 3000, 3000));
        Assert.Equal(MoveVerdict.Accepted, validator.Validate(MovementState.Idle(new Vec3(0, 0, 3 + 3.5f), 0), 4000, 5000));
        Assert.Equal(MoveVerdict.Rejected, validator.Validate(MovementState.Idle(new Vec3(0, 0, 6.5f + 7f), 0), 5000, 6000));
    }
}
