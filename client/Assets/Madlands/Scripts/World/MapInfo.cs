using System;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>A named place on a map (a village, a wood), shown on the minimap and when entering it.</summary>
    [Serializable]
    public struct MapArea
    {
        public string name;
        public Vector2 center;
        public float radius;
    }

    /// <summary>
    /// Marks a scene as a Madlands map. Madlands > Export Map writes content/maps/&lt;id&gt;/
    /// (height.bin + map.json) from the scene's terrains and this component, which is how the
    /// server learns about the world geometry. The scene is the source of truth. The export also
    /// renders the map from above for the minimap and world map (<see cref="worldMap"/>).
    /// </summary>
    public sealed class MapInfo : MonoBehaviour
    {
        public int mapId = 1;
        public string mapName = "Hollowmere";
        [Tooltip("Distance between exported height samples (meters).")]
        public float heightSampleSpacing = 1f;
        public Transform spawnPoint;
        [Tooltip("Named places; the first one containing a position names it.")]
        public MapArea[] areas = new MapArea[0];

        [Header("Written by Export Map")]
        [Tooltip("The map seen from above (north = +Z at the top).")]
        public Texture2D worldMap;
        [Tooltip("World X/Z of the image's bottom-left corner.")]
        public Vector2 worldMapMin;
        [Tooltip("World meters the square image covers.")]
        public float worldMapSize;

        /// <summary>The name of the place at a world position, or null outside every area.</summary>
        public string AreaAt(float x, float z)
        {
            foreach (var area in areas)
            {
                float dx = x - area.center.x, dz = z - area.center.y;
                if (dx * dx + dz * dz <= area.radius * area.radius) return area.name;
            }
            return null;
        }

        /// <summary>0..1 image coordinates (origin bottom-left) of a world position.</summary>
        public Vector2 ToMapUv(float x, float z) =>
            worldMapSize > 0f ? new Vector2((x - worldMapMin.x) / worldMapSize, (z - worldMapMin.y) / worldMapSize) : Vector2.zero;
    }
}
