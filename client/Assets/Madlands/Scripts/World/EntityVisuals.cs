using System.Collections.Generic;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Phase 1 placeholder art: a capsule with a "nose" showing the facing. In Phase 5 this
    /// becomes a lookup from the server's displayId to an Addressable character prefab.
    /// </summary>
    public static class EntityVisuals
    {
        public static readonly Color LocalPlayerColor = new Color(0.25f, 0.55f, 1f);
        public static readonly Color PlayerColor = new Color(0.3f, 0.85f, 0.4f);
        public static readonly Color BotColor = new Color(1f, 0.6f, 0.2f);

        private static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();
        private static Material _template;

        public static GameObject CreatePlaceholder(Transform parent, Color color)
        {
            // A URP material from Resources/ is guaranteed to be in player builds; the primitives'
            // built-in default material is not URP-compatible there.
            if (_template == null) _template = Resources.Load<Material>("EntityPlaceholder");

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(parent, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.transform.localScale = new Vector3(0.8f, 1f, 0.8f);

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            Object.Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(parent, false);
            nose.transform.localPosition = new Vector3(0f, 1.55f, 0.38f);
            nose.transform.localScale = new Vector3(0.25f, 0.15f, 0.3f);

            var material = GetMaterial(_template != null ? _template : body.GetComponent<Renderer>().sharedMaterial, color);
            body.GetComponent<Renderer>().sharedMaterial = material;
            nose.GetComponent<Renderer>().sharedMaterial = GetMaterial(material, color * 0.5f);
            return body;
        }

        private static Material GetMaterial(Material template, Color color)
        {
            if (!Materials.TryGetValue(color, out var material))
            {
                material = new Material(template) { color = color };
                Materials.Add(color, material);
            }
            return material;
        }
    }
}
