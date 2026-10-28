using System.Collections.Generic;
using Madlands.Shared.Game;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// A unit casting a spell: the school's bolt (a crystal, a star, a flame) grows and turns between its
    /// hands, flecks in the school's color swirl around both hands and a soft light pulses on the body. A
    /// model without hands holds it in front of its chest. Destroy the object to end it.
    /// </summary>
    public sealed class CastGlow : MonoBehaviour
    {
        /// <summary>Seconds the bolt takes to grow to its full size.</summary>
        private const float GrowSeconds = 1.2f;

        private readonly List<Transform> _hands = new List<Transform>();
        private readonly List<ParticleSystem> _swirls = new List<ParticleSystem>();
        private Transform _bolt;
        private float _boltSize;
        private Light _light;
        private float _start;

        public static CastGlow Create(Transform unit, DisplayModel model, DisplayShape shape, SpellSchool school)
        {
            var color = SchoolColors.Get(school);
            var go = new GameObject("CastGlow");
            go.transform.SetParent(unit, false);
            go.transform.localPosition = new Vector3(0f, shape.Height * 0.6f, Mathf.Max(0.3f, shape.Radius));
            var glow = go.AddComponent<CastGlow>();
            glow._start = Time.time;
            if (model != null)
            {
                foreach (var name in new[] { "LeftHand", "RightHand" })
                {
                    var bone = model.Bone(name);
                    if (bone != null) glow._hands.Add(bone);
                }
            }
            foreach (var hand in glow._hands.Count > 0 ? glow._hands : new List<Transform> { go.transform })
                glow._swirls.Add(Swirl(hand, color));

            // Bigger units hold bigger bolts.
            glow._boltSize = Mathf.Clamp(shape.Height * 0.14f, 0.22f, 0.6f);
            glow._bolt = Effects.Spawn(SpellEffects.BoltOf(school), go.transform, Vector3.zero, 0f, color);

            glow._light = go.AddComponent<Light>();
            glow._light.type = LightType.Point;
            glow._light.color = color;
            glow._light.range = 3f;
            glow._light.shadows = LightShadows.None;
            glow.Update();
            return glow;
        }

        private static ParticleSystem Swirl(Transform hand, Color color)
        {
            var ps = Effects.Particles("FxFleck", hand, Vector3.zero, color, 0.05f, 0.1f);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.gravityModifier = -0.15f;
            var emission = ps.emission;
            emission.rateOverTime = 26f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.14f;
            // Round the hand and in toward it, so the flecks circle it.
            var orbit = ps.velocityOverLifetime;
            orbit.enabled = true;
            orbit.space = ParticleSystemSimulationSpace.Local;
            orbit.x = new ParticleSystem.MinMaxCurve(0f);
            orbit.y = new ParticleSystem.MinMaxCurve(0f);
            orbit.z = new ParticleSystem.MinMaxCurve(0f);
            orbit.orbitalX = new ParticleSystem.MinMaxCurve(0f);
            orbit.orbitalY = new ParticleSystem.MinMaxCurve(5f);
            orbit.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
            orbit.radial = new ParticleSystem.MinMaxCurve(-0.3f);
            ps.Play();
            return ps;
        }

        private void Update()
        {
            float t = Time.time - _start;
            _light.intensity = 0.9f + Mathf.Sin(Time.time * 9f) * 0.25f;
            // Between the hands, a little in front of them, wherever the cast animation holds them.
            if (_hands.Count > 0)
            {
                var between = Vector3.zero;
                foreach (var hand in _hands) between += hand.position;
                between /= _hands.Count;
                _bolt.position = between + transform.parent.forward * 0.12f + Vector3.up * 0.08f;
            }
            float grow = Mathf.SmoothStep(0f, 1f, t / GrowSeconds);
            _bolt.localScale = Vector3.one * _boltSize * grow * (1f + Mathf.Sin(Time.time * 12f) * 0.06f);
            _bolt.rotation = Quaternion.Euler(0f, Time.time * 240f, 0f);
        }

        private void OnDestroy()
        {
            // The swirls hang on the hands, not under this object.
            foreach (var swirl in _swirls)
                if (swirl != null) Destroy(swirl.gameObject);
        }
    }
}
