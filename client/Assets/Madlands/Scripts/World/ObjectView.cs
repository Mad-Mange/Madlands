using Madlands.Shared;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// A gatherable object in the world (mushrooms): its prop model by displayId (the server never names
    /// assets), a click target a little bigger than the model, and a shine while a quest needs what it gives.
    /// </summary>
    public sealed class ObjectView : MonoBehaviour
    {
        /// <summary>The Blender prop of each object display (art/blender/props.py).</summary>
        private static readonly System.Collections.Generic.Dictionary<uint, string> Models = new System.Collections.Generic.Dictionary<uint, string>
        {
            { 1, "Mushrooms" },
        };

        private LootSparkle _shine;

        public EntityGuid Guid { get; private set; }

        public static ObjectView Attach(GameObject go, EntityGuid guid, uint displayId)
        {
            var view = go.AddComponent<ObjectView>();
            view.Guid = guid;
            var catalog = ArtCatalog.Instance;
            var prefab = catalog != null && Models.TryGetValue(displayId, out var name) ? catalog.GetModel(name) : null;
            if (prefab != null) Instantiate(prefab, go.transform, false);
            else
            {
                var placeholder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(placeholder.GetComponent<Collider>());
                placeholder.transform.SetParent(go.transform, false);
                placeholder.transform.localScale = Vector3.one * 0.4f;
            }
            var target = go.AddComponent<SphereCollider>();
            target.isTrigger = true;
            target.radius = 0.75f;
            target.center = new Vector3(0f, 0.35f, 0f);
            return view;
        }

        /// <summary>Shines (a small golden halo and sparkles) while the player can gather it for a quest.</summary>
        public void SetShining(bool shining)
        {
            if (shining == (_shine != null)) return;
            if (shining)
            {
                _shine = LootSparkle.Create(transform, new DisplayShape { Radius = 0.4f, Height = 0.3f });
            }
            else
            {
                Destroy(_shine.gameObject);
                _shine = null;
            }
        }
    }
}
