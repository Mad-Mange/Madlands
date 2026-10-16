using System.Collections.Generic;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Placeholder spell visuals: glowing projectiles that fly from caster to target at the spell's
    /// projectile speed, bursts where spells land, and an expanding disc for spells cast around the
    /// caster. Timing comes from the server's SpellGo; the damage itself arrives as combat events.
    /// </summary>
    public sealed class SpellEffects : MonoBehaviour
    {
        private const float BurstSeconds = 0.3f;
        private const float NovaSeconds = 0.45f;

        private sealed class Projectile
        {
            public Transform Body;
            public EntityGuid Target;
            public Vector3 Goal;
            public float Speed;
            public Color Color;
        }

        private sealed class Burst
        {
            public Transform Body;
            public float Start;
            public float Duration;
            public float FromScale;
            public float ToScale;
            public bool Flat;
        }

        [SerializeField] private NetworkController network;

        private readonly List<Projectile> _projectiles = new List<Projectile>();
        private readonly List<Burst> _bursts = new List<Burst>();

        private GameClient Client => network.Client;
        private WorldView World => network.WorldView;

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
            var color = SchoolColors.Get(spell.School);
            if (spell.Target == SpellTarget.None)
            {
                // Thunder Clap, Shard Nova: a disc sweeping out from the caster.
                var disc = EntityVisuals.Glow(null, caster.transform.position + Vector3.up * 0.1f, Vector3.one, color, PrimitiveType.Cylinder);
                _bursts.Add(new Burst { Body = disc, Start = Time.time, Duration = NovaSeconds, FromScale = 1f, ToScale = 16f, Flat = true });
                return;
            }
            if (spell.ProjectileSpeed <= 0f || go.Target == go.Caster || !World.TryGetView(go.Target, out var target)) return;
            var body = EntityVisuals.Glow(null, caster.ChestPosition + caster.transform.forward * 0.5f, Vector3.one * 0.35f, color, PrimitiveType.Sphere);
            _projectiles.Add(new Projectile { Body = body, Target = go.Target, Goal = target.ChestPosition, Speed = spell.ProjectileSpeed, Color = color });
        }

        private void OnCombatEvent(CombatEvent e)
        {
            if (e.SpellId == 0 || e.Has(CombatFlags.Periodic) || !World.TryGetView(e.Target, out var target)) return;
            if (!Client.Data.TryGetSpell(e.SpellId, out var spell) || spell.ProjectileSpeed > 0f) return; // projectiles burst on arrival
            if (spell.School == SpellSchool.Physical && !e.Has(CombatFlags.Heal)) return;
            AddBurst(target.ChestPosition, e.Has(CombatFlags.Heal) ? SchoolColors.Get(SpellSchool.Nature) : SchoolColors.Get(spell.School));
        }

        private void AddBurst(Vector3 position, Color color)
        {
            var body = EntityVisuals.Glow(null, position, Vector3.one * 0.3f, color, PrimitiveType.Sphere);
            _bursts.Add(new Burst { Body = body, Start = Time.time, Duration = BurstSeconds, FromScale = 0.3f, ToScale = 1.4f });
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];
                if (World.TryGetView(p.Target, out var target)) p.Goal = target.ChestPosition;
                var position = Vector3.MoveTowards(p.Body.position, p.Goal, p.Speed * dt);
                p.Body.position = position;
                if ((position - p.Goal).sqrMagnitude > 0.04f) continue;
                AddBurst(p.Goal, p.Color);
                Destroy(p.Body.gameObject);
                _projectiles.RemoveAt(i);
            }

            for (int i = _bursts.Count - 1; i >= 0; i--)
            {
                var b = _bursts[i];
                float t = (Time.time - b.Start) / b.Duration;
                if (t >= 1f)
                {
                    Destroy(b.Body.gameObject);
                    _bursts.RemoveAt(i);
                    continue;
                }
                float scale = Mathf.Lerp(b.FromScale, b.ToScale, Mathf.Sqrt(t));
                b.Body.localScale = b.Flat ? new Vector3(scale, 0.02f, scale) : Vector3.one * scale * (1f - t * 0.5f);
            }
        }

        private void ClearAll()
        {
            foreach (var p in _projectiles) if (p.Body != null) Destroy(p.Body.gameObject);
            foreach (var b in _bursts) if (b.Body != null) Destroy(b.Body.gameObject);
            _projectiles.Clear();
            _bursts.Clear();
        }
    }
}
