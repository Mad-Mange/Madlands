using UnityEngine;
using UnityEngine.InputSystem;

namespace Madlands.Client
{
    /// <summary>
    /// WoW-style third-person camera: hold left mouse to orbit freely, hold right mouse to steer
    /// the character, scroll to zoom. When running without a mouse button held the camera swings
    /// back behind the character. Pulls in when terrain or objects are in the way.
    /// </summary>
    public sealed class CameraController : MonoBehaviour
    {
        [SerializeField] private float sensitivity = 0.15f;
        [SerializeField] private float minDistance = 1.5f;
        [SerializeField] private float maxDistance = 30f;
        [SerializeField] private float pivotHeight = 1.7f;
        [SerializeField] private float followSpeedDegrees = 150f;

        private PlayerController _target;
        private float _yaw;
        private float _pitch = 18f;
        private float _distance = 9f;

        public float YawDegrees => _yaw;
        public bool IsMouseSteering { get; private set; }

        public void SetTarget(PlayerController target)
        {
            _target = target;
            if (target != null) _yaw = target.transform.eulerAngles.y;
        }

        public void AddYaw(float degrees) => _yaw += degrees;

        /// <summary>Overrides the framing (used by command-line screenshot runs).</summary>
        public void Frame(float? pitch, float? distance, float? yawOffset)
        {
            if (pitch.HasValue) _pitch = Mathf.Clamp(pitch.Value, -75f, 80f);
            if (distance.HasValue) _distance = Mathf.Clamp(distance.Value, minDistance, 60f);
            if (yawOffset.HasValue) _yaw += yawOffset.Value;
        }

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            bool lmb = mouse != null && mouse.leftButton.isPressed;
            bool rmb = mouse != null && mouse.rightButton.isPressed;
            bool dragging = _target != null && (lmb || rmb);
            IsMouseSteering = _target != null && rmb;

            Cursor.lockState = dragging ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !dragging;

            if (_target == null) return;

            if (dragging)
            {
                var delta = mouse.delta.ReadValue();
                _yaw += delta.x * sensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity, -75f, 80f);
            }
            else if (_target.IsMovingForward)
            {
                _yaw = Mathf.MoveTowardsAngle(_yaw, _target.transform.eulerAngles.y, followSpeedDegrees * Time.deltaTime);
            }

            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f) _distance = Mathf.Clamp(_distance * (scroll > 0 ? 0.88f : 1.12f), minDistance, maxDistance);
            }

            var pivot = _target.transform.position + Vector3.up * pivotHeight;
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            float distance = _distance;
            if (Physics.SphereCast(pivot, 0.25f, rotation * Vector3.back, out var hit, _distance, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.gameObject != _target.gameObject)
            {
                distance = Mathf.Max(0.5f, hit.distance);
            }

            var position = pivot + rotation * Vector3.back * distance;
            float ground = TerrainUtil.GroundHeight(position, position.y - 10f) + 0.3f;
            if (position.y < ground) position.y = ground;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(pivot - position));
        }
    }
}
