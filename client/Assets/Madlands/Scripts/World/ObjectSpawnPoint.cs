using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Marks where a world object grows or stands (mushrooms, chests). Madlands > Export Map writes all markers of the
    /// scene to content/maps/&lt;id&gt;/objects.json, which the server reads; the server spawns the objects and the
    /// client draws them. Markers are tagged EditorOnly, so they are not part of player builds.
    /// </summary>
    public sealed class ObjectSpawnPoint : MonoBehaviour
    {
        [Tooltip("Object id from content/objects.json.")]
        public int objectId = 1;

        [Tooltip("0 = always here. Markers sharing a pool take turns: one object of the pool is out at a time, and once " +
                 "it is gathered or looted it comes back at another marker of the pool.")]
        public int pool;

        private void OnDrawGizmos()
        {
            Gizmos.color = pool > 0 ? new Color(1f, 0.8f, 0.3f, 0.9f) : new Color(0.4f, 1f, 0.5f, 0.9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.3f, 0.35f);
        }
    }
}
