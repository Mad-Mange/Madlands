using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Marks a scene as a Madlands map. Madlands > Export Map writes content/maps/&lt;id&gt;/
    /// (height.bin + map.json) from the scene's terrains and this component, which is how the
    /// server learns about the world geometry. The scene is the source of truth.
    /// </summary>
    public sealed class MapInfo : MonoBehaviour
    {
        public int mapId = 1;
        public string mapName = "Testlands";
        [Tooltip("Distance between exported height samples (meters).")]
        public float heightSampleSpacing = 1f;
        public Transform spawnPoint;
    }
}
