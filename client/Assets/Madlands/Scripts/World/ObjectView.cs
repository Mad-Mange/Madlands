using Madlands.Shared;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// A thing in the world players right-click: its prop model by displayId (the server never names assets) and a
    /// click target a little bigger than the model. Gathered objects (mushrooms) shine while a quest needs what they
    /// give; chests always shine, and their lid swings open while your loot window shows what is inside.
    /// </summary>
    public sealed class ObjectView : MonoBehaviour
    {
        private struct Display
        {
            /// <summary>The Blender prop (art/blender/nature.py, props.py).</summary>
            public string Model;
            /// <summary>Size of the shine and the click target.</summary>
            public DisplayShape Shape;
            public bool Chest;
            /// <summary>Drawn this much bigger than modeled (chests read better a little larger than life).</summary>
            public float Scale;
        }

        private static readonly System.Collections.Generic.Dictionary<uint, Display> Displays = new System.Collections.Generic.Dictionary<uint, Display>
        {
            { 1, new Display { Model = "Mushrooms", Shape = new DisplayShape { Radius = 0.4f, Height = 0.3f } } },
            { 2, new Display { Model = "Chest", Shape = new DisplayShape { Radius = 0.75f, Height = 0.75f }, Chest = true, Scale = 1.25f } },
            { 3, new Display { Model = "Strongbox", Shape = new DisplayShape { Radius = 0.7f, Height = 0.7f }, Chest = true, Scale = 1.25f } },
        };

        /// <summary>How far an open lid swings back, and how fast (degrees per second).</summary>
        private const float LidOpenDegrees = 105f, LidSpeed = 300f;

        private Display _display;
        private LootSparkle _shine;
        private Transform _lid;
        private Quaternion _lidClosed;
        /// <summary>The lid's hinge in its parent's space, signed so that a positive turn lifts the front.</summary>
        private Vector3 _hinge;
        private float _lidAngle;
        private bool _open;

        public EntityGuid Guid { get; private set; }
        public bool IsChest => _display.Chest;

        public static ObjectView Attach(GameObject go, EntityGuid guid, uint displayId)
        {
            var view = go.AddComponent<ObjectView>();
            view.Guid = guid;
            if (!Displays.TryGetValue(displayId, out view._display))
                view._display = new Display { Shape = new DisplayShape { Radius = 0.4f, Height = 0.3f } };
            var catalog = ArtCatalog.Instance;
            var prefab = catalog != null && view._display.Model != null ? catalog.GetModel(view._display.Model) : null;
            if (prefab != null)
            {
                var model = Instantiate(prefab, go.transform, false);
                if (view._display.Scale > 0f) model.transform.localScale *= view._display.Scale;
                view.FindLid(model.transform);
            }
            else
            {
                var placeholder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(placeholder.GetComponent<Collider>());
                placeholder.transform.SetParent(go.transform, false);
                placeholder.transform.localScale = Vector3.one * 0.4f;
            }
            var shape = view._display.Shape;
            var target = go.AddComponent<SphereCollider>();
            target.isTrigger = true;
            target.radius = Mathf.Max(0.75f, shape.Radius * 1.4f);
            target.center = new Vector3(0f, Mathf.Max(0.35f, shape.Height * 0.6f), 0f);
            return view;
        }

        /// <summary>Shines (a small golden halo and sparkles) while the player can gather or loot it.</summary>
        public void SetShining(bool shining)
        {
            if (shining == (_shine != null)) return;
            if (shining)
            {
                _shine = LootSparkle.Create(transform, _display.Shape);
            }
            else
            {
                Destroy(_shine.gameObject);
                _shine = null;
            }
        }

        /// <summary>A chest's lid opens while its loot window is open (only the looter sees it: the rest see it vanish when emptied).</summary>
        public void SetOpen(bool open)
        {
            if (_lid == null || open == _open) return;
            _open = open;
            if (open && SoundSystem.Instance != null) SoundSystem.Instance.PlayAt("chest_open", transform.position + Vector3.up * 0.4f, 0.8f);
        }

        private void Update()
        {
            if (_lid == null) return;
            float target = _open ? LidOpenDegrees : 0f;
            if (Mathf.Approximately(_lidAngle, target)) return;
            _lidAngle = Mathf.MoveTowards(_lidAngle, target, LidSpeed * Time.deltaTime);
            // Ease out: fast at first, settling against the stop.
            float t = _lidAngle / LidOpenDegrees;
            float eased = LidOpenDegrees * (1f - (1f - t) * (1f - t));
            _lid.localRotation = Quaternion.AngleAxis(eased, _hinge) * _lidClosed;
        }

        /// <summary>
        /// A chest model has a child "Lid" whose origin is the hinge at the back. The hinge runs along the chest's width
        /// (its local x); which way lifts the front is measured once, by turning the lid both ways.
        /// </summary>
        private void FindLid(Transform model)
        {
            if (!_display.Chest) return;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Lid") continue;
                var filter = t.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || t.parent == null) continue;
                _lid = t;
                _lidClosed = t.localRotation;
                var axis = t.parent.InverseTransformDirection(model.right).normalized;
                var center = filter.sharedMesh.bounds.center;
                t.localRotation = Quaternion.AngleAxis(60f, axis) * _lidClosed;
                float up = t.TransformPoint(center).y;
                t.localRotation = Quaternion.AngleAxis(-60f, axis) * _lidClosed;
                float down = t.TransformPoint(center).y;
                t.localRotation = _lidClosed;
                _hinge = up >= down ? axis : -axis;
                return;
            }
        }
    }
}
