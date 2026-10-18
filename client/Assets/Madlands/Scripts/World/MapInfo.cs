using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Marks a scene as a Madlands map. Madlands > Export Map writes content/maps/&lt;id&gt;/
    /// (height.bin + map.json) from the scene's terrains and this component, which is how the
    /// server learns about the world geometry. The scene is the source of truth. The export also
    /// renders the map from above for the minimap and world map (<see cref="worldMap"/>).
    /// </summary>
    public sealed class MapInfo : MonoBehaviour
    {
        public int mapId = 1;
        public string mapName = "Testlands";
        [Tooltip("Distance between exported height samples (meters).")]
        public float heightSampleSpacing = 1f;
        public Transform spawnPoint;

        [Header("Written by Export Map")]
        [Tooltip("The map seen from above (north = +Z at the top).")]
        public Texture2D worldMap;
        [Tooltip("World X/Z of the image's bottom-left corner.")]
        public Vector2 worldMapMin;
        [Tooltip("World meters the square image covers.")]
        public float worldMapSize;

        /// <summary>0..1 image coordinates (origin bottom-left) of a world position.</summary>
        public Vector2 ToMapUv(float x, float z) =>
            worldMapSize > 0f ? new Vector2((x - worldMapMin.x) / worldMapSize, (z - worldMapMin.y) / worldMapSize) : Vector2.zero;
    }
}
