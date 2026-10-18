using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Madlands.Client.Editor
{
    /// <summary>
    /// Import rules for Assets/Madlands/Audio (art/audio/README.md): effects are mono and decompressed
    /// on load, ambience stays compressed in memory, music streams. Keeps <see cref="SoundCatalog"/>
    /// (Resources/SoundCatalog.asset) in sync.
    /// </summary>
    public sealed class SoundImporter : AssetPostprocessor
    {
        public const string AudioRoot = "Assets/Madlands/Audio/";
        public const string CatalogPath = "Assets/Madlands/Resources/SoundCatalog.asset";

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioRoot, System.StringComparison.Ordinal)) return;
            var importer = (AudioImporter)assetImporter;
            bool music = assetPath.Contains("/Music/");
            bool ambience = assetPath.Contains("/Ambience/");
            importer.forceToMono = !music && !ambience;
            importer.loadInBackground = music || ambience;
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = music ? 0.55f : 0.7f;
            settings.loadType = music ? AudioClipLoadType.Streaming
                : ambience ? AudioClipLoadType.CompressedInMemory
                : AudioClipLoadType.DecompressOnLoad;
            settings.preloadAudioData = !music;
            importer.defaultSampleSettings = settings;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool IsAudio(string p) => p.StartsWith(AudioRoot, System.StringComparison.Ordinal);
            if (imported.Any(IsAudio) || deleted.Any(IsAudio) || moved.Any(IsAudio) || movedFrom.Any(IsAudio))
                RefreshCatalog();
        }

        [MenuItem("Madlands/Refresh Sound Catalog")]
        public static void RefreshCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SoundCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SoundCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.Clips.Clear();
            foreach (var path in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot.TrimEnd('/') })
                         .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, System.StringComparer.Ordinal))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) catalog.Clips.Add(clip);
            }
            catalog.Invalidate();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            Debug.Log($"[SoundImporter] Sound catalog: {catalog.Clips.Count} clips");
        }
    }
}
