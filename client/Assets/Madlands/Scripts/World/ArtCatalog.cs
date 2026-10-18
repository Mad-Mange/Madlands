using System;
using System.Collections.Generic;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Every model and animation clip under Assets/Madlands/Art (built in Blender, see art/README.md),
    /// by name. Kept up to date by the editor (ArtImporter) and loaded from Resources at runtime.
    /// </summary>
    public sealed class ArtCatalog : ScriptableObject
    {
        [Serializable]
        public struct ModelEntry
        {
            public string Name;
            public GameObject Prefab;
        }

        [Serializable]
        public struct ClipEntry
        {
            /// <summary>The model file the clip comes from, e.g. "HumanoidAnimations".</summary>
            public string Set;
            public string Name;
            public AnimationClip Clip;
        }

        public List<ModelEntry> Models = new List<ModelEntry>();
        public List<ClipEntry> Clips = new List<ClipEntry>();

        private static ArtCatalog _instance;
        private static bool _loaded;
        private Dictionary<string, GameObject> _models;
        private Dictionary<string, Dictionary<string, AnimationClip>> _sets;

        public static ArtCatalog Instance
        {
            get
            {
                if (!_loaded)
                {
                    _instance = Resources.Load<ArtCatalog>("ArtCatalog");
                    _loaded = true;
                }
                return _instance;
            }
        }

        public GameObject GetModel(string name)
        {
            if (_models == null)
            {
                _models = new Dictionary<string, GameObject>();
                foreach (var entry in Models)
                    if (entry.Prefab != null) _models[entry.Name] = entry.Prefab;
            }
            return _models.TryGetValue(name, out var prefab) ? prefab : null;
        }

        /// <summary>The clips of one model file by clip name (empty if there are none).</summary>
        public IReadOnlyDictionary<string, AnimationClip> GetClips(string set)
        {
            if (_sets == null)
            {
                _sets = new Dictionary<string, Dictionary<string, AnimationClip>>();
                foreach (var entry in Clips)
                {
                    if (entry.Clip == null) continue;
                    if (!_sets.TryGetValue(entry.Set, out var clips)) _sets[entry.Set] = clips = new Dictionary<string, AnimationClip>();
                    clips[entry.Name] = entry.Clip;
                }
            }
            return _sets.TryGetValue(set, out var found) ? found : Empty;
        }

        private static readonly Dictionary<string, AnimationClip> Empty = new Dictionary<string, AnimationClip>();

        /// <summary>Drops the lookup caches (the editor calls this after rebuilding the lists).</summary>
        public void Invalidate()
        {
            _models = null;
            _sets = null;
        }
    }
}
