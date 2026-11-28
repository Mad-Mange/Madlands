using UnityEngine;
using UnityEngine.SceneManagement;

namespace Madlands.Client
{
    /// <summary>Subtle flame movement and drifting embers for the world's nonblocking campfires.</summary>
    public sealed class CampfireAmbience : MonoBehaviour
    {
        private const float VisibleDistance = 60f;
        private Transform _outer;
        private Transform _core;
        private Vector3 _outerScale;
        private Vector3 _coreScale;
        private ParticleSystem _embers;
        private float _phase;
        private float _nextDistanceCheck;
        private bool _near;

        /// <summary>Install once on campfires in the loaded map. Their effects leave with that scene.</summary>
        public static int AttachToScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return 0;
            int attached = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Campfire" || t.Find("FlameOuter") == null || t.Find("FlameCore") == null) continue;
                    if (t.GetComponent<CampfireAmbience>() == null) t.gameObject.AddComponent<CampfireAmbience>();
                    attached++;
                }
            return attached;
        }

        private void Awake()
        {
            _outer = transform.Find("FlameOuter");
            _core = transform.Find("FlameCore");
            if (_outer == null || _core == null) { enabled = false; return; }
            _outerScale = _outer.localScale;
            _coreScale = _core.localScale;
            _phase = transform.position.x * 0.17f + transform.position.z * 0.11f;
            // A self-lit material remains bright in the shade and uses the same bundled shader as spell effects.
            SetFlameMaterial(_outer, new Color(1f, 0.26f, 0.025f), new Color(1f, 0.55f, 0.06f));
            SetFlameMaterial(_core, new Color(1f, 0.78f, 0.25f), new Color(1f, 0.78f, 0.25f));

            _embers = Effects.Particles("FxFleck", transform, Vector3.zero, new Color(1f, 0.45f, 0.08f), 0.035f, 0.065f);
            // The model's imported root may be rotated; place sparks upright in world space.
            _embers.transform.position = transform.position + Vector3.up * (0.35f * transform.lossyScale.magnitude / Mathf.Sqrt(3f));
            _embers.transform.rotation = Quaternion.identity;
            var main = _embers.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startSpeed = 0f;
            main.maxParticles = 12;
            var shape = _embers.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.18f;
            var emission = _embers.emission;
            emission.rateOverTime = 5f;
            var velocity = _embers.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        }

        private static void SetFlameMaterial(Transform flame, Color color, Color highlight)
        {
            foreach (var renderer in flame.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = EntityVisuals.GlowMaterial(materials[i] != null && materials[i].name.Contains("Bright") ? highlight : color);
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private void Update()
        {
            if (Time.time >= _nextDistanceCheck)
            {
                _nextDistanceCheck = Time.time + 0.5f;
                var camera = Camera.main;
                bool near = camera != null && (camera.transform.position - transform.position).sqrMagnitude < VisibleDistance * VisibleDistance;
                if (near != _near)
                {
                    _near = near;
                    if (_near) _embers.Play();
                    else _embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
            if (_near) AnimateFlames(Time.time);
        }

        private void AnimateFlames(float now)
        {
            float t = now + _phase;
            float outer = 1f + 0.055f * Mathf.Sin(t * 6.1f) + 0.025f * Mathf.Sin(t * 10.7f);
            float core = 1f + 0.07f * Mathf.Sin(t * 7.4f + 1.2f);
            _outer.localScale = _outerScale * outer;
            _core.localScale = _coreScale * core;
        }
    }
}
