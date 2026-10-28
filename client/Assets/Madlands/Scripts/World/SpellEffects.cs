using System.Collections.Generic;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Spell visuals made of the effect models (Effects.cs) in the school's color: a bolt (a crystal, a
    /// star, a fireball, a leaf) that flies from caster to target at the spell's projectile speed with a
    /// trail of flecks, a burst of flecks and a flash where spells land, a shock wave for spells cast around
    /// the caster (frost raises crystals from the ground) and a rising ring where heals land. Timing comes
    /// from the server's SpellGo; the damage itself arrives as combat events.
    /// </summary>
    public sealed class SpellEffects : MonoBehaviour
    {
        private const float FlashSeconds = 0.28f;
        private const float WaveSeconds = 0.5f;
        /// <summary>How far a shock wave runs (meters): spells cast around the caster reach about this far.</summary>
        private const float WaveRadius = 8f;
        private const float HealSeconds = 0.9f;

        /// <summary>What a school's spells look like: the bolt's model and length, and the flecks it sheds.</summary>
        private struct Look
        {
            public string Bolt;
            public float Length;
            public string Fleck;
            public float Gravity;
        }

        private sealed class Projectile
        {
            public Transform Body;
            public Transform Model;
            public ParticleSystem Trail;
            public TrailRenderer Ribbon;
            public EntityGuid Target;
            public Vector3 Goal;
            public float Speed;
            public Color Color;
            public Look Look;
        }

        /// <summary>Something that grows, rises and fades over its lifetime, then goes away.</summary>
        private sealed class Fader
        {
            public Transform Body;
            public Light Light;
            public float Start;
            public float Duration;
            public Color Color;
            /// <summary>How opaque it starts.</summary>
            public float Alpha = 1f;
            public Vector3 FromScale;
            public Vector3 ToScale;
            public Vector3 Rise;
            public Vector3 Origin;
            public bool FaceCamera;
            /// <summary>Solids (crystals out of the ground) grow and sink instead of fading.</summary>
            public bool Solid;
        }

        [SerializeField] private NetworkController network;

        private readonly List<Projectile> _projectiles = new List<Projectile>();
        private readonly List<Fader> _faders = new List<Fader>();

        private GameClient Client => network.Client;
        private WorldView World => network.WorldView;

        private static Look LookOf(SpellSchool school)
        {
            switch (school)
            {
                case SpellSchool.Arcane: return new Look { Bolt = "FxShard", Length = 0.55f, Fleck = "FxFleck", Gravity = 0.1f };
                case SpellSchool.Frost: return new Look { Bolt = "FxShard", Length = 0.6f, Fleck = "FxFleck", Gravity = 0.6f };
                case SpellSchool.Holy: return new Look { Bolt = "FxStar", Length = 0.5f, Fleck = "FxStar", Gravity = -0.05f };
                case SpellSchool.Fire: return new Look { Bolt = "FxFlame", Length = 0.65f, Fleck = "FxFleck", Gravity = -0.25f };
                case SpellSchool.Nature: return new Look { Bolt = "FxLeaf", Length = 0.45f, Fleck = "FxLeaf", Gravity = 0.15f };
                case SpellSchool.Shadow: return new Look { Bolt = "FxOrb", Length = 0.38f, Fleck = "FxFleck", Gravity = 0f };
                default: return new Look { Bolt = "FxFleck", Length = 0.3f, Fleck = "FxFleck", Gravity = 1f };
            }
        }

        /// <summary>The model of a school's bolts (a casting unit holds a small one between its hands).</summary>
        public static string BoltOf(SpellSchool school) => LookOf(school).Bolt;

        private void Awake()
        {
            if (network == null) network = GetComponent<NetworkController>();
        }

        private void Start()
        {
            Client.SpellLaunched += OnLaunched;
            Client.CombatEventReceived += OnCombatEvent;
            network.LeftWorld += (_, __) => ClearAll();
        }

        private void OnLaunched(SpellGo go)
        {
            if (!Client.Data.TryGetSpell(go.SpellId, out var spell) || !World.TryGetView(go.Caster, out var caster)) return;
            // Eating and drinking sit down; melee abilities swing when their damage lands (combat events).
            if ((spell.Flags & SpellFlags.NotInCombat) != 0) caster.SitDown();
            else if ((spell.Flags & SpellFlags.AutoAttack) == 0 && !IsMelee(spell)) caster.PlayCastRelease();
            var color = SchoolColors.Get(spell.School);
            if (spell.Target == SpellTarget.None)
            {
                // Thunder Clap, Shard Nova: a wave sweeping out from the caster.
                Nova(caster.transform.position, spell.School, color);
                return;
            }
            if (spell.ProjectileSpeed <= 0f || go.Target == go.Caster || !World.TryGetView(go.Target, out var target)) return;
            Launch(caster, target, spell, color);
        }

        public static bool IsMelee(SpellInfo spell) =>
            spell.School == SpellSchool.Physical && spell.MaxRange > 0 && spell.MaxRange <= GameConstants.MeleeRange;

        private void OnCombatEvent(CombatEvent e)
        {
            if (e.SpellId == 0 || e.Has(CombatFlags.Periodic) || !World.TryGetView(e.Target, out var target)) return;
            if (!Client.Data.TryGetSpell(e.SpellId, out var spell) || spell.ProjectileSpeed > 0f) return; // projectiles burst on arrival
            if (e.Has(CombatFlags.Heal))
            {
                Heal(target, spell.School == SpellSchool.Holy ? SpellSchool.Holy : SpellSchool.Nature);
                return;
            }
            if (spell.School == SpellSchool.Physical) return;
            Impact(target.ChestPosition, LookOf(spell.School), SchoolColors.Get(spell.School));
        }

        // ------------------------------------------------------------------ bolts

        private void Launch(UnitView caster, UnitView target, SpellInfo spell, Color color)
        {
            var look = LookOf(spell.School);
            var body = new GameObject("Bolt").transform;
            body.position = caster.ChestPosition + caster.transform.forward * 0.5f;
            var model = Effects.Spawn(look.Bolt, body, Vector3.zero, look.Length, color);

            var trail = Effects.Particles(look.Fleck, body, Vector3.zero, color, 0.05f, 0.11f);
            var main = trail.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
            main.gravityModifier = look.Gravity;
            var emission = trail.emission;
            emission.rateOverDistance = 14f;
            var shape = trail.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;
            trail.Play();

            // A ribbon of light behind the bolt, on a child so it can stay behind and fade when the bolt lands.
            var ribbon = new GameObject("Ribbon").AddComponent<TrailRenderer>();
            ribbon.transform.SetParent(body, false);
            ribbon.time = 0.2f;
            ribbon.minVertexDistance = 0.08f;
            ribbon.widthCurve = new AnimationCurve(new Keyframe(0f, look.Length * 0.3f), new Keyframe(1f, 0f));
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Effects.Light(color), 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            ribbon.colorGradient = gradient;
            ribbon.sharedMaterial = Effects.FadeMaterial;
            ribbon.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ribbon.receiveShadows = false;

            var glow = body.gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = color;
            glow.range = 3.5f;
            glow.intensity = 1.2f;
            glow.shadows = LightShadows.None;

            _projectiles.Add(new Projectile
            {
                Body = body, Model = model, Trail = trail, Ribbon = ribbon, Target = target.Guid, Goal = target.ChestPosition,
                Speed = spell.ProjectileSpeed, Color = color, Look = look,
            });
        }

        private void Fly(Projectile p, float dt)
        {
            if (World.TryGetView(p.Target, out var target)) p.Goal = target.ChestPosition;
            var position = Vector3.MoveTowards(p.Body.position, p.Goal, p.Speed * dt);
            var direction = p.Goal - p.Body.position;
            if (direction.sqrMagnitude > 1e-4f) p.Body.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            // Bolts point up their model's axis and turn about it as they fly.
            p.Model.localRotation = Quaternion.Euler(0f, Time.time * 600f, 0f);
            p.Body.position = position;
        }

        /// <summary>The bolt is gone; its trail of flecks and its ribbon stay behind and fade out.</summary>
        private static void Land(Projectile p)
        {
            p.Trail.transform.SetParent(null, true);
            p.Trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(p.Trail.gameObject, 0.6f);
            p.Ribbon.transform.SetParent(null, true);
            p.Ribbon.emitting = false;
            Destroy(p.Ribbon.gameObject, p.Ribbon.time + 0.05f);
            Destroy(p.Body.gameObject);
        }

        // ------------------------------------------------------------------ bursts, waves and heals

        /// <summary>Where a spell hits: flecks fly apart, a flash of light.</summary>
        private void Impact(Vector3 position, Look look, Color color)
        {
            var flecks = Effects.Particles(look.Fleck, null, position, color, 0.06f, 0.14f);
            var main = flecks.main;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            main.gravityModifier = look.Gravity;
            var emission = flecks.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });
            var shape = flecks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;
            var drag = flecks.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 3f;
            flecks.Play();
            Destroy(flecks.gameObject, 1f);

            var flash = Effects.SpawnFading("FxBurst", null, position, 0.35f, Effects.Light(color));
            var light = flash.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = 4f;
            light.shadows = LightShadows.None;
            _faders.Add(new Fader
            {
                Body = flash, Light = light, Start = Time.time, Duration = FlashSeconds, Color = Effects.Light(color),
                FromScale = Vector3.one * 0.35f, ToScale = Vector3.one * 1.5f, Origin = position, FaceCamera = true,
            });
        }

        /// <summary>A shock wave running out over the ground with flecks sprayed ahead of it; frost raises crystals.</summary>
        private void Nova(Vector3 center, SpellSchool school, Color color)
        {
            var wave = Effects.SpawnFading("FxWave", null, center + Vector3.up * 0.05f, 1f, color);
            _faders.Add(new Fader
            {
                Body = wave, Start = Time.time, Duration = WaveSeconds, Color = color, Alpha = 0.7f, Origin = center + Vector3.up * 0.05f,
                FromScale = new Vector3(1f, 1.8f, 1f), ToScale = new Vector3(WaveRadius, 0.6f, WaveRadius),
            });

            var look = LookOf(school);
            var spray = Effects.Particles(look.Fleck, null, center + Vector3.up * 0.3f, color, 0.08f, 0.18f);
            var main = spray.main;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 14f);
            main.gravityModifier = school == SpellSchool.Fire ? -0.3f : 0.4f;
            var emission = spray.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 36) });
            var shape = spray.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var drag = spray.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2.5f;
            spray.Play();
            Destroy(spray.gameObject, 1.2f);

            if (school != SpellSchool.Frost) return;
            // Crystals break out of the ground where the wave passes, then sink back.
            for (int i = 0; i < 14; i++)
            {
                float angle = (i + Random.value * 0.6f) * Mathf.PI * 2f / 14f;
                float radius = Random.Range(2.2f, WaveRadius - 0.8f);
                var p = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                p.y = TerrainUtil.GroundHeight(p, center.y) - 0.05f;
                float size = Random.Range(0.7f, 1.3f);
                var spike = Effects.Spawn("FxSpikes", null, p, 0f, color);
                spike.rotation = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
                _faders.Add(new Fader
                {
                    Body = spike, Start = Time.time + WaveSeconds * Mathf.Sqrt(radius / WaveRadius), Duration = 1.1f, Color = color,
                    FromScale = Vector3.zero, ToScale = Vector3.one * size, Origin = p, Solid = true,
                });
            }
        }

        /// <summary>A heal: a ring of light rising around the body, stars (holy) or leaves (nature) drifting up.</summary>
        private void Heal(UnitView target, SpellSchool school)
        {
            var color = SchoolColors.Get(school);
            var feet = target.transform.position + Vector3.up * 0.05f;
            float radius = target.Shape.Radius * 1.3f + 0.3f;
            var ring = Effects.SpawnFading("FxRing", null, feet, radius, color);
            _faders.Add(new Fader
            {
                Body = ring, Start = Time.time, Duration = HealSeconds, Color = color, Origin = feet,
                FromScale = Vector3.one * radius, ToScale = Vector3.one * radius * 0.8f, Rise = Vector3.up * target.Shape.Height,
            });

            var motes = Effects.Particles(school == SpellSchool.Holy ? "FxStar" : "FxLeaf", null, feet, color, 0.08f, 0.15f);
            var main = motes.main;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startSpeed = 0f;
            var emission = motes.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6), new ParticleSystem.Burst(0.2f, 6) });
            var shape = motes.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.8f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var rise = motes.velocityOverLifetime;
            rise.enabled = true;
            rise.space = ParticleSystemSimulationSpace.World;
            rise.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            rise.y = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            rise.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            motes.Play();
            Destroy(motes.gameObject, 2f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];
                Fly(p, dt);
                if ((p.Body.position - p.Goal).sqrMagnitude > 0.04f) continue;
                Impact(p.Goal, p.Look, p.Color);
                Land(p);
                _projectiles.RemoveAt(i);
            }

            var camera = Camera.main;
            for (int i = _faders.Count - 1; i >= 0; i--)
            {
                var f = _faders[i];
                float t = (Time.time - f.Start) / f.Duration;
                if (t < 0f) continue;
                if (t >= 1f)
                {
                    Destroy(f.Body.gameObject);
                    _faders.RemoveAt(i);
                    continue;
                }
                if (f.Solid)
                {
                    // Out of the ground fast, a while standing, then down again.
                    float grow = t < 0.12f ? Mathf.SmoothStep(0f, 1f, t / 0.12f) : t > 0.7f ? 1f - Mathf.SmoothStep(0f, 1f, (t - 0.7f) / 0.3f) : 1f;
                    f.Body.localScale = f.ToScale * grow;
                    continue;
                }
                float ease = 1f - (1f - t) * (1f - t);
                f.Body.localScale = Vector3.LerpUnclamped(f.FromScale, f.ToScale, ease);
                f.Body.position = f.Origin + f.Rise * ease;
                if (f.FaceCamera && camera != null) f.Body.rotation = Quaternion.LookRotation(f.Body.position - camera.transform.position);
                var c = f.Color;
                c.a = f.Alpha * (1f - t * t);
                Effects.SetColor(f.Body, c);
                if (f.Light != null) f.Light.intensity = 2.5f * (1f - t);
            }
        }

        private void ClearAll()
        {
            foreach (var p in _projectiles) if (p.Body != null) Destroy(p.Body.gameObject);
            foreach (var f in _faders) if (f.Body != null) Destroy(f.Body.gameObject);
            _projectiles.Clear();
            _faders.Clear();
        }
    }
}
