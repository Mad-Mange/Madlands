using System.Collections.Generic;
using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Movement;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Presentation of entities. Game state lives in <see cref="ClientWorld"/> (plain C#); this only
    /// mirrors it into GameObjects, rendering slightly in the past so movement can be interpolated
    /// between server samples. Also keeps the guid -> <see cref="UnitView"/> lookup the HUD and
    /// effects use (the local player included).
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        private NetworkController _network;
        private readonly List<RemoteEntity> _views = new List<RemoteEntity>();
        private readonly Dictionary<EntityGuid, UnitView> _units = new Dictionary<EntityGuid, UnitView>();
        private bool _ready;

        private void Awake()
        {
            _network = GetComponent<NetworkController>();
        }

        public IEnumerable<RemoteEntity> Remote => _views;

        public bool TryGetView(EntityGuid guid, out UnitView view) => _units.TryGetValue(guid, out view) && view != null;

        public void RegisterLocal(UnitView view) => _units[view.Guid] = view;

        /// <summary>
        /// Views are only created once the map scene is loaded and active (entities can arrive
        /// while it is still loading; they wait in <see cref="ClientWorld"/> until then).
        /// </summary>
        public void SetReady(bool ready)
        {
            _ready = ready;
            if (!ready) return;
            foreach (var entity in _network.Client.World.Entities)
                if (entity.View == null) OnEntitySpawned(entity);
        }

        public void OnEntitySpawned(RemoteEntity entity)
        {
            if (!_ready) return;
            var go = new GameObject("Entity " + entity.Name);
            var view = UnitView.Attach(go, entity.Guid, entity.DisplayId, isLocal: false);
            entity.View = go;
            _units[entity.Guid] = view;
            _views.Add(entity);
            Place(entity, go.transform);
        }

        public void OnEntityDespawned(RemoteEntity entity)
        {
            if (entity.View is GameObject go) Destroy(go);
            entity.View = null;
            _views.Remove(entity);
            _units.Remove(entity.Guid);
        }

        public void Clear()
        {
            foreach (var entity in _views)
                if (entity.View is GameObject go) Destroy(go);
            _views.Clear();
            _units.Clear();
        }

        private void LateUpdate()
        {
            var client = _network.Client;
            foreach (var entity in _views)
            {
                if (!(entity.View is GameObject go)) continue;
                Place(entity, go.transform);
                if (entity.Unit != null && _units.TryGetValue(entity.Guid, out var view)) Animate(view, entity.Unit, client);
            }
            if (_network.LocalPlayer != null && _units.TryGetValue(client.LocalGuid, out var local))
            {
                var own = _network.LocalPlayer.State;
                local.SetMovement(own.Flags, own.Velocity);
                Animate(local, client.Self, client);
            }
        }

        private void Animate(UnitView view, UnitState unit, GameClient client)
        {
            view.SetDead(unit.Values.IsDead);
            view.SetEquipment(unit.Values.MainHandDisplay, unit.Values.OffHandDisplay);
            view.SetLootable(unit.Values.IsDead && client.IsLootable(view.Guid));
            SpellSchool? school = null;
            if (unit.Cast != null && client.Data.TryGetSpell(unit.Cast.SpellId, out var spell)) school = spell.School;
            view.SetCasting(school);
        }

        private void Place(RemoteEntity entity, Transform t)
        {
            var state = entity.Movement.Sample(_network.Client.RenderTimeMs);
            var position = state.Position.ToUnity();
            float ground = TerrainUtil.GroundHeight(position, position.y);
            // Grounded entities follow the local terrain exactly; airborne ones never sink into it.
            position.y = (state.Flags & MovementFlags.Airborne) != 0 ? Mathf.Max(position.y, ground) : ground;
            t.SetPositionAndRotation(position, Conversions.YawToRotation(state.Yaw));
            if (_units.TryGetValue(entity.Guid, out var view)) view.SetMovement(state.Flags, state.Velocity);
        }
    }
}
