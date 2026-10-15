using System.Collections.Generic;
using Madlands.ClientCore;
using Madlands.Shared.Movement;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Presentation of remote entities. Game state lives in <see cref="ClientWorld"/> (plain C#);
    /// this only mirrors it into GameObjects, rendering slightly in the past so movement can be
    /// interpolated between server samples.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        [SerializeField] private float nameplateDistance = 45f;

        private NetworkController _network;
        private readonly List<RemoteEntity> _views = new List<RemoteEntity>();
        private GUIStyle _nameStyle;

        private void Awake()
        {
            _network = GetComponent<NetworkController>();
        }

        public void OnEntitySpawned(RemoteEntity entity)
        {
            var go = new GameObject("Entity " + entity.Name);
            bool isBot = entity.Name.StartsWith("Bot", System.StringComparison.Ordinal);
            EntityVisuals.CreatePlaceholder(go.transform, isBot ? EntityVisuals.BotColor : EntityVisuals.PlayerColor);
            entity.View = go;
            _views.Add(entity);
            Place(entity, go.transform);
        }

        public void OnEntityDespawned(RemoteEntity entity)
        {
            if (entity.View is GameObject go) Destroy(go);
            entity.View = null;
            _views.Remove(entity);
        }

        public void Clear()
        {
            foreach (var entity in _views)
                if (entity.View is GameObject go) Destroy(go);
            _views.Clear();
        }

        private void LateUpdate()
        {
            foreach (var entity in _views)
                if (entity.View is GameObject go) Place(entity, go.transform);
        }

        private void Place(RemoteEntity entity, Transform t)
        {
            double renderTime = _network.Client.ServerTimeMs - InterpolationBuffer.InterpolationDelayMs;
            var state = entity.Movement.Sample(renderTime);
            var position = state.Position.ToUnity();
            float ground = TerrainUtil.GroundHeight(position, position.y);
            // Grounded entities follow the local terrain exactly; airborne ones never sink into it.
            position.y = (state.Flags & MovementFlags.Airborne) != 0 ? Mathf.Max(position.y, ground) : ground;
            t.SetPositionAndRotation(position, Conversions.YawToRotation(state.Yaw));
        }

        private void OnGUI()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (_nameStyle == null)
            {
                _nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _nameStyle.normal.textColor = Color.white;
            }

            float maxSq = nameplateDistance * nameplateDistance;
            foreach (var entity in _views)
            {
                if (!(entity.View is GameObject go)) continue;
                var head = go.transform.position + Vector3.up * 2.4f;
                if ((head - cam.transform.position).sqrMagnitude > maxSq) continue;
                var screen = cam.WorldToScreenPoint(head);
                if (screen.z <= 0f) continue;
                GUI.Label(new Rect(screen.x - 75f, Screen.height - screen.y - 10f, 150f, 20f), entity.Name, _nameStyle);
            }
        }
    }
}
