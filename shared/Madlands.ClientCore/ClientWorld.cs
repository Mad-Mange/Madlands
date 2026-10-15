using System.Collections.Generic;
using Madlands.Shared;
using Madlands.Shared.Movement;

namespace Madlands.ClientCore
{
    public sealed class RemoteEntity
    {
        public RemoteEntity(EntityGuid guid, string name, uint displayId)
        {
            Guid = guid;
            Name = name;
            DisplayId = displayId;
        }

        public EntityGuid Guid { get; }
        public string Name { get; }
        public uint DisplayId { get; }
        public InterpolationBuffer Movement { get; } = new InterpolationBuffer();

        /// <summary>Presentation-layer handle (e.g. the Unity GameObject); ClientCore never touches it.</summary>
        public object View;
    }

    /// <summary>Everything the client currently knows about other entities. Plain C#, no engine types.</summary>
    public sealed class ClientWorld
    {
        private readonly Dictionary<EntityGuid, RemoteEntity> _entities = new Dictionary<EntityGuid, RemoteEntity>();

        public int Count => _entities.Count;
        public IEnumerable<RemoteEntity> Entities => _entities.Values;

        public bool TryGet(EntityGuid guid, out RemoteEntity entity) => _entities.TryGetValue(guid, out entity);

        internal RemoteEntity Add(EntityGuid guid, string name, uint displayId, double stateTimeMs, in MovementState state)
        {
            if (_entities.TryGetValue(guid, out var existing))
            {
                existing.Movement.Add(stateTimeMs, state);
                return existing;
            }
            var entity = new RemoteEntity(guid, name, displayId);
            entity.Movement.Add(stateTimeMs, state);
            _entities.Add(guid, entity);
            return entity;
        }

        internal RemoteEntity Remove(EntityGuid guid)
        {
            if (!_entities.TryGetValue(guid, out var entity)) return null;
            _entities.Remove(guid);
            return entity;
        }

        internal void Clear() => _entities.Clear();
    }
}
