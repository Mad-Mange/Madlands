using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Plays sounds: one-shots in the world (3D) or flat (interface), a music track and an ambience
    /// loop that cross-fade when they change, with the volumes from <see cref="Preferences"/>. Clips
    /// come from the <see cref="SoundCatalog"/> by name. Created on first use and kept across scenes.
    /// </summary>
    public sealed class SoundSystem : MonoBehaviour
    {
        private const int Voices = 28;
        private const float MusicFadeSeconds = 3f;
        private const float AmbienceFadeSeconds = 2f;
        /// <summary>World sounds farther from the listener than this are not played at all.</summary>
        private const float Audible = 45f;

        private static SoundSystem _instance;
        private static readonly System.Collections.Generic.HashSet<string> Missing = new System.Collections.Generic.HashSet<string>();
        private readonly AudioSource[] _voices = new AudioSource[Voices];
        private int _next;
        private Loop _music;
        private Loop _ambience;

        /// <summary>Two sources that cross-fade from one looping clip to the next.</summary>
        private sealed class Loop
        {
            public AudioSource A, B;
            /// <summary>The source fading in (or playing); the other fades out.</summary>
            public AudioSource Active;
            public string Current;
            public float Seconds;
            public float Level;
        }

        public static SoundSystem Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Sound");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<SoundSystem>();
                }
                return _instance;
            }
        }

        private void Awake()
        {
            for (int i = 0; i < Voices; i++)
            {
                var source = new GameObject("Voice " + i).AddComponent<AudioSource>();
                source.transform.SetParent(transform, false);
                source.playOnAwake = false;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 4f;
                source.maxDistance = Audible;
                source.dopplerLevel = 0f;
                _voices[i] = source;
            }
            _music = NewLoop("Music", MusicFadeSeconds);
            _ambience = NewLoop("Ambience", AmbienceFadeSeconds);
            Preferences.Changed += ApplyVolumes;
            ApplyVolumes();
        }

        private void OnDestroy() => Preferences.Changed -= ApplyVolumes;

        private Loop NewLoop(string name, float seconds)
        {
            AudioSource Make(string n)
            {
                var source = new GameObject(n).AddComponent<AudioSource>();
                source.transform.SetParent(transform, false);
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.volume = 0f;
                return source;
            }
            return new Loop { A = Make(name + " A"), B = Make(name + " B"), Seconds = seconds };
        }

        private void ApplyVolumes()
        {
            AudioListener.volume = Preferences.MasterVolume;
            _music.Level = Preferences.MusicVolume * 0.6f;
            _ambience.Level = Preferences.AmbienceVolume * 0.8f;
        }

        /// <summary>A flat sound (interface, your own jingles).</summary>
        public void Play(string name, float volume = 1f, float pitchJitter = 0f)
        {
            var clip = Find(name);
            if (clip == null) return;
            var voice = NextVoice();
            voice.spatialBlend = 0f;
            Launch(voice, clip, volume, pitchJitter);
        }

        /// <summary>A sound at a place in the world (silent when far from the listener).</summary>
        public void PlayAt(string name, Vector3 position, float volume = 1f, float pitchJitter = 0.06f)
        {
            var listener = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            if ((position - listener).sqrMagnitude > Audible * Audible) return;
            var clip = Find(name);
            if (clip == null) return;
            var voice = NextVoice();
            voice.transform.position = position;
            voice.spatialBlend = 1f;
            Launch(voice, clip, volume, pitchJitter);
        }

        /// <summary>The clip by name; a missing one is reported once (a typo or an unbuilt sound).</summary>
        private static AudioClip Find(string name)
        {
            var clip = SoundCatalog.Instance != null ? SoundCatalog.Instance.Pick(name) : null;
            if (clip == null && Missing.Add(name)) Debug.LogWarning("[Sound] No clip named " + name);
            return clip;
        }

        private void Launch(AudioSource voice, AudioClip clip, float volume, float pitchJitter)
        {
            voice.clip = clip;
            voice.volume = Mathf.Clamp01(volume * Preferences.EffectsVolume);
            voice.pitch = 1f + (pitchJitter > 0f ? Random.Range(-pitchJitter, pitchJitter) : 0f);
            voice.Play();
        }

        /// <summary>A free voice, or the one that started longest ago.</summary>
        private AudioSource NextVoice()
        {
            for (int i = 0; i < Voices; i++)
            {
                var voice = _voices[(_next + i) % Voices];
                if (!voice.isPlaying)
                {
                    _next = (_next + i + 1) % Voices;
                    return voice;
                }
            }
            var oldest = _voices[_next];
            _next = (_next + 1) % Voices;
            return oldest;
        }

        /// <summary>Changes the music (null fades it out).</summary>
        public void SetMusic(string name) => Switch(_music, name);

        /// <summary>Changes the background ambience (null fades it out).</summary>
        public void SetAmbience(string name) => Switch(_ambience, name);

        private static void Switch(Loop loop, string name)
        {
            if (loop.Current == name) return;
            loop.Current = name;
            var clip = name != null ? Find(name) : null;
            var next = loop.Active == loop.A ? loop.B : loop.A;
            next.Stop();
            next.clip = clip;
            next.volume = 0f;
            if (clip != null) next.Play();
            loop.Active = clip != null ? next : null;
        }

        private void Update()
        {
            Fade(_music);
            Fade(_ambience);
        }

        private static void Fade(Loop loop)
        {
            float step = Time.unscaledDeltaTime / loop.Seconds * Mathf.Max(loop.Level, 0.05f);
            foreach (var source in new[] { loop.A, loop.B })
            {
                bool active = source == loop.Active;
                source.volume = Mathf.MoveTowards(source.volume, active ? loop.Level : 0f, step);
                if (!active && source.isPlaying && source.volume <= 0.0001f) source.Stop();
            }
        }
    }
}
