using System.Collections.Generic;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Every clip under Assets/Madlands/Audio by name (art/audio/README.md). Kept up to date by the
    /// editor (SoundImporter) and loaded from Resources. Variants of one sound are numbered
    /// ("step_grass_1" .. "step_grass_5"); <see cref="Pick"/> takes the base name and chooses one.
    /// </summary>
    public sealed class SoundCatalog : ScriptableObject
    {
        public List<AudioClip> Clips = new List<AudioClip>();

        private static SoundCatalog _instance;
        private static bool _loaded;
        private Dictionary<string, AudioClip> _byName;
        private Dictionary<string, List<AudioClip>> _variants;

        public static SoundCatalog Instance
        {
            get
            {
                if (!_loaded)
                {
                    _instance = Resources.Load<SoundCatalog>("SoundCatalog");
                    _loaded = true;
                }
                return _instance;
            }
        }

        /// <summary>
        /// The clip with this exact name, or a random variant of it (name_1, name_2, ...), other than
        /// <paramref name="avoid"/> when there are more; null if none.
        /// </summary>
        public AudioClip Pick(string name, AudioClip avoid = null)
        {
            if (_byName == null) Index();
            if (_byName.TryGetValue(name, out var clip)) return clip;
            if (!_variants.TryGetValue(name, out var list)) return null;
            int skip = avoid != null && list.Count > 1 ? list.IndexOf(avoid) : -1;
            if (skip < 0) return list[Random.Range(0, list.Count)];
            int i = Random.Range(0, list.Count - 1);
            return list[i >= skip ? i + 1 : i];
        }

        private void Index()
        {
            _byName = new Dictionary<string, AudioClip>();
            _variants = new Dictionary<string, List<AudioClip>>();
            foreach (var clip in Clips)
            {
                if (clip == null) continue;
                _byName[clip.name] = clip;
                int underscore = clip.name.LastIndexOf('_');
                if (underscore > 0 && int.TryParse(clip.name.Substring(underscore + 1), out _))
                {
                    string baseName = clip.name.Substring(0, underscore);
                    if (!_variants.TryGetValue(baseName, out var list)) _variants[baseName] = list = new List<AudioClip>();
                    list.Add(clip);
                }
            }
        }

        public void Invalidate()
        {
            _byName = null;
            _variants = null;
        }
    }
}
