using Madlands.Shared;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>Shared and Unity use the same axes and units, so conversion is a plain copy.</summary>
    public static class Conversions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Vec3 ToShared(this Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Quaternion YawToRotation(float yawRadians) => Quaternion.Euler(0f, yawRadians * Mathf.Rad2Deg, 0f);
    }

    public static class TerrainUtil
    {
        /// <summary>Ground height under a position from whichever terrain tile contains it.</summary>
        public static float GroundHeight(Vector3 position, float fallback)
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                var origin = terrain.GetPosition();
                var size = terrain.terrainData.size;
                if (position.x < origin.x || position.z < origin.z || position.x > origin.x + size.x || position.z > origin.z + size.z)
                    continue;
                return terrain.SampleHeight(position) + origin.y;
            }
            return fallback;
        }
    }
}
