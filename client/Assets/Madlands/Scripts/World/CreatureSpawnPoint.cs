using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Marks where a creature spawns. Madlands > Export Map writes all markers of the scene to
    /// content/maps/&lt;id&gt;/spawns.json, which the server reads; the scene is the source of truth.
    /// Markers are tagged EditorOnly, so they are not part of player builds.
    /// </summary>
    public sealed class CreatureSpawnPoint : MonoBehaviour
    {
        [Tooltip("Creature id from content/creatures.json.")]
        public int creatureId = 1;
        [Tooltip("Wanders randomly within this radius (meters); 0 = stands still.")]
        public float wanderRadius;
        [Tooltip("Seconds until it comes back after dying; 0 = the creature's default.")]
        public int respawnSeconds;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.9f);
            var p = transform.position;
            Gizmos.DrawWireSphere(p + Vector3.up * 0.6f, 0.6f);
            Gizmos.DrawLine(p + Vector3.up * 0.6f, p + Vector3.up * 0.6f + transform.forward * 1.2f);
            if (wanderRadius <= 0f) return;
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.5f);
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Gizmos.DrawLine(p + new Vector3(Mathf.Sin(a0) * wanderRadius, 0.2f, Mathf.Cos(a0) * wanderRadius),
                                p + new Vector3(Mathf.Sin(a1) * wanderRadius, 0.2f, Mathf.Cos(a1) * wanderRadius));
            }
        }
    }
}
