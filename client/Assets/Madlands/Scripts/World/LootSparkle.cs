using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Marks a corpse the local player can loot: a golden halo on the ground that slowly turns and
    /// breathes, sparkles rising from the body, a warm pulsing light and a small gem turning above it.
    /// Everything is built in code from the shared glow material (no textures).
    /// </summary>
    public sealed class LootSparkle : MonoBehaviour
    {
        private static readonly Color Gold = new Color(1f, 0.82f, 0.3f);
        private static Mesh _ring;

        private Transform _halo;
        private Transform _gem;
        private Light _light;
        private float _gemHeight;
        private float _phase;

        /// <summary>Adds the effect to a unit of the given shape; destroy the returned object to remove it.</summary>
        public static LootSparkle Create(Transform unit, DisplayShape shape)
        {
            var go = new GameObject("LootSparkle");
            go.transform.SetParent(unit, false);
            var sparkle = go.AddComponent<LootSparkle>();
            sparkle.Build(shape);
            return sparkle;
        }

        private void Build(DisplayShape shape)
        {
            _phase = Random.value * 10f;
            float radius = Mathf.Max(0.55f, shape.Radius * 1.1f + 0.2f);

            _halo = new GameObject("Halo").transform;
            _halo.SetParent(transform, false);
            _halo.localPosition = Vector3.up * 0.04f;
            var filter = _halo.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = RingMesh();
            var renderer = _halo.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = EntityVisuals.GlowMaterial(Gold);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _halo.localScale = Vector3.one * radius;

            Sparks(radius * 0.7f);

            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = Gold;
            _light.range = radius * 2.5f + 1f;
            _light.shadows = LightShadows.None;
            transform.localPosition = Vector3.zero;

            // The gem floats a little above where the body lies.
            _gemHeight = Mathf.Max(0.9f, shape.Height * 0.45f + 0.35f);
            _gem = EntityVisuals.Glow(transform, Vector3.up * _gemHeight, Vector3.one * 0.16f, Gold);
            _gem.localRotation = Quaternion.Euler(45f, 0f, 45f);
        }

        /// <summary>Small golden flecks rising from the corpse and shrinking away.</summary>
        private void Sparks(float radius)
        {
            var sparks = new GameObject("Sparks");
            sparks.transform.SetParent(transform, false);
            sparks.transform.localPosition = Vector3.up * 0.15f;
            var ps = sparks.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startColor = Gold;
            main.gravityModifier = -0.04f;
            main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 10f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));

            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = EntityVisuals.GlowMaterial(Gold);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        private void Update()
        {
            float t = Time.time + _phase;
            float breathe = 1f + Mathf.Sin(t * 2.2f) * 0.07f;
            _halo.localRotation = Quaternion.Euler(0f, t * 25f, 0f);
            _halo.localScale = new Vector3(breathe, 1f, breathe) * _halo.localScale.y;
            _light.intensity = 1.1f + Mathf.Sin(t * 2.2f) * 0.35f;
            _gem.localPosition = Vector3.up * (_gemHeight + Mathf.Sin(t * 1.6f) * 0.06f);
            _gem.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
        }

        /// <summary>A flat ring of radius 1 (inner 0.9), made once and shared.</summary>
        private static Mesh RingMesh()
        {
            if (_ring != null) return _ring;
            const int segments = 40;
            const float inner = 0.9f;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                vertices[i * 2] = dir * inner;
                vertices[i * 2 + 1] = dir;
                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = i * 2 + 1;
                triangles[t + 2] = next * 2 + 1;
                triangles[t + 3] = i * 2;
                triangles[t + 4] = next * 2 + 1;
                triangles[t + 5] = next * 2;
            }
            _ring = new Mesh { name = "LootHalo", vertices = vertices, triangles = triangles };
            _ring.RecalculateNormals();
            _ring.RecalculateBounds();
            return _ring;
        }
    }
}
