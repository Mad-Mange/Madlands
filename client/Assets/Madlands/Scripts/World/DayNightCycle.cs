using Madlands.ClientCore;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>Shared world time, derived from the server clock without another network message.</summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        public const double DayLengthSeconds = 3600.0;
        private GameClient _client;
        private Light _sun;
        private Quaternion _sunRotation;
        private Color _sunColor, _sky, _equator, _ground, _fog;
        private float _sunIntensity;
        private Material _originalSkybox, _skybox;
        private float _skyExposure;
        private bool _initialized;
        private float? _fixedPhase;

        public static float PhaseAt(double serverTimeMs) =>
            (float)(((serverTimeMs / 1000.0 / DayLengthSeconds + 1.0 / 3.0) % 1.0 + 1.0) % 1.0);

        /// <summary>Stops the clock at an hour (0-24; screenshots, -timeofday); null follows the server again.</summary>
        public void FixHour(float? hour)
        {
            _fixedPhase = hour.HasValue ? Mathf.Repeat(hour.Value, 24f) / 24f : (float?)null;
            if (_initialized) ApplyPhase(_fixedPhase ?? PhaseAt(_client != null ? _client.ServerTimeMs : 0));
        }

        public void Initialize(GameClient client)
        {
            _client = client;
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var light in root.GetComponentsInChildren<Light>(true))
                    if (light.type == LightType.Directional && (light == RenderSettings.sun || light.name == "Sun"))
                        _sun = light;
            if (_sun == null) _sun = RenderSettings.sun;
            if (_sun != null)
            {
                _sunRotation = _sun.transform.rotation;
                _sunColor = _sun.color;
                _sunIntensity = _sun.intensity;
            }
            _sky = RenderSettings.ambientSkyColor;
            _equator = RenderSettings.ambientEquatorColor;
            _ground = RenderSettings.ambientGroundColor;
            _fog = RenderSettings.fogColor;
            _originalSkybox = RenderSettings.skybox;
            if (_originalSkybox != null)
            {
                _skybox = new Material(_originalSkybox);
                if (_skybox.HasProperty("_Exposure")) _skyExposure = _skybox.GetFloat("_Exposure");
                RenderSettings.skybox = _skybox;
            }
            _initialized = true;
            ApplyPhase(_fixedPhase ?? PhaseAt(_client != null ? _client.ServerTimeMs : 0));
        }

        private void Update()
        {
            if (_initialized && _client != null) ApplyPhase(_fixedPhase ?? PhaseAt(_client.ServerTimeMs));
        }

        /// <param name="phase">0 is midnight, 0.5 is noon.</param>
        public void ApplyPhase(float phase)
        {
            if (!_initialized) return;
            float elevation = -Mathf.Cos(phase * 2f * Mathf.PI);
            float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.18f, 0.28f, elevation));
            if (_sun != null)
            {
                // The procedural sky goes completely black with a light below the horizon.
                // At night this light supplies soft moonlight from above the horizon instead.
                _sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(18f, Mathf.Lerp(12f, 50f, Mathf.Max(0f, elevation)), day), _sunRotation.eulerAngles.y, 0f);
                _sun.intensity = Mathf.Lerp(_sunIntensity * 0.30f, _sunIntensity, day);
                _sun.color = Color.Lerp(new Color(0.55f, 0.65f, 1f), _sunColor, day);
            }
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.31f, 0.35f, 0.45f), _sky, day);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.27f, 0.29f, 0.36f), _equator, day);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.20f, 0.20f, 0.25f), _ground, day);
            RenderSettings.fogColor = Color.Lerp(new Color(0.22f, 0.27f, 0.36f), _fog, day);
            if (_skybox != null && _skybox.HasProperty("_Exposure"))
                _skybox.SetFloat("_Exposure", _skyExposure * Mathf.Lerp(0.45f, 1f, day));
            Shader.SetGlobalFloat("_MadlandsDaylight", day);
        }

        private void OnDestroy()
        {
            if (!_initialized) return;
            if (_sun != null)
            {
                _sun.transform.rotation = _sunRotation;
                _sun.color = _sunColor;
                _sun.intensity = _sunIntensity;
            }
            // An unloading scene may already have activated another scene's lighting.
            if (RenderSettings.skybox == _skybox)
            {
                RenderSettings.skybox = _originalSkybox;
                RenderSettings.ambientSkyColor = _sky;
                RenderSettings.ambientEquatorColor = _equator;
                RenderSettings.ambientGroundColor = _ground;
                RenderSettings.fogColor = _fog;
            }
            if (_skybox != null) Destroy(_skybox);
            Shader.SetGlobalFloat("_MadlandsDaylight", 1f);
        }
    }
}
