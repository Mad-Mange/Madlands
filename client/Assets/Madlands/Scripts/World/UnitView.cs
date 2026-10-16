using Madlands.Shared;
using Madlands.Shared.Game;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// The visible body of a unit (remote or local): placeholder art from its displayId, a trigger
    /// collider for mouse selection, and small reactions (hit punch, melee lunge, cast glow, falling
    /// over when dead). Pure presentation: all state comes from ClientCore.
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        private const float HitPunchSeconds = 0.18f;
        private const float LungeSeconds = 0.25f;

        private Transform _visual;
        private Transform _castGlow;
        private float _hitAt = -10f;
        private float _lungeAt = -10f;
        private bool _dead;
        private float _deadBlend;

        public EntityGuid Guid { get; private set; }
        public DisplayShape Shape { get; private set; }
        public bool IsLocal { get; private set; }
        public Vector3 HeadPosition => transform.position + Vector3.up * (Shape.Height + 0.35f);
        public Vector3 ChestPosition => transform.position + Vector3.up * (Shape.Height * (_dead ? 0.2f : 0.6f));

        public static UnitView Attach(GameObject go, EntityGuid guid, uint displayId, Color playerColor, bool isLocal)
        {
            var view = go.AddComponent<UnitView>();
            view.Guid = guid;
            view.IsLocal = isLocal;
            view._visual = new GameObject("Visual").transform;
            view._visual.SetParent(go.transform, false);
            view.Shape = EntityVisuals.Create(view._visual, displayId, playerColor);

            // Click target for selection. A trigger: never blocks movement or the camera.
            var collider = go.AddComponent<CapsuleCollider>();
            collider.isTrigger = true;
            collider.height = view.Shape.Height;
            collider.radius = Mathf.Max(0.4f, view.Shape.Radius);
            collider.center = Vector3.up * (view.Shape.Height * 0.5f);
            return view;
        }

        public void PlayHit() => _hitAt = Time.time;

        public void PlayLunge() => _lungeAt = Time.time;

        public void SetDead(bool dead) => _dead = dead;

        /// <summary>Shows (school given) or hides a glow above the unit while it casts.</summary>
        public void SetCasting(SpellSchool? school)
        {
            if (school == null)
            {
                if (_castGlow != null) Destroy(_castGlow.gameObject);
                _castGlow = null;
                return;
            }
            if (_castGlow != null) return;
            _castGlow = EntityVisuals.Glow(transform, Vector3.up * (Shape.Height + 0.15f), Vector3.one * 0.3f, SchoolColors.Get(school.Value), PrimitiveType.Sphere);
        }

        private void LateUpdate()
        {
            float hit = Mathf.Clamp01((Time.time - _hitAt) / HitPunchSeconds);
            float punch = hit < 1f ? Mathf.Sin(hit * Mathf.PI) * 0.12f : 0f;
            float lunge = Mathf.Clamp01((Time.time - _lungeAt) / LungeSeconds);
            float forward = lunge < 1f ? Mathf.Sin(lunge * Mathf.PI) * 0.35f : 0f;

            _deadBlend = Mathf.MoveTowards(_deadBlend, _dead ? 1f : 0f, Time.deltaTime * 3f);
            _visual.localScale = new Vector3(1f + punch, 1f - punch * 0.5f, 1f + punch);
            _visual.localPosition = new Vector3(0f, -_deadBlend * Shape.Radius * 0.3f, forward);
            _visual.localRotation = Quaternion.Euler(0f, 0f, _deadBlend * 90f);

            if (_castGlow != null)
            {
                float pulse = 0.25f + Mathf.Sin(Time.time * 10f) * 0.06f;
                _castGlow.localScale = Vector3.one * pulse;
            }
        }
    }
}
