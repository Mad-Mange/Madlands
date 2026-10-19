using System.Collections.Generic;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Movement;

namespace Madlands.ClientCore
{
    /// <summary>An aura as the client knows it. Expiry is in local time (the server removes it when it ends).</summary>
    public struct ClientAura
    {
        public byte Slot;
        public uint SpellId;
        public EntityGuid Caster;
        public byte Stacks;
        public int DurationMs;
        public double ExpiresAtLocalMs;

        public bool IsPermanent => DurationMs <= 0;
        public double RemainingMs(double nowLocalMs) => IsPermanent ? 0 : System.Math.Max(0, ExpiresAtLocalMs - nowLocalMs);
    }

    /// <summary>A cast in progress (cast bars). Times are local.</summary>
    public sealed class CastInfo
    {
        public uint SpellId;
        public EntityGuid Target;
        public byte CastId;
        public double StartLocalMs;
        public double EndLocalMs;

        public float Progress(double nowLocalMs) =>
            EndLocalMs <= StartLocalMs ? 1f : (float)System.Math.Min(1.0, System.Math.Max(0.0, (nowLocalMs - StartLocalMs) / (EndLocalMs - StartLocalMs)));
    }

    /// <summary>Unit state shared by the local player and remote units: values, auras, current cast.</summary>
    public sealed class UnitState
    {
        public readonly UnitValues Values = new UnitValues();
        public readonly List<ClientAura> Auras = new List<ClientAura>();
        public CastInfo Cast;

        internal void SetAura(in ClientAura aura)
        {
            for (int i = 0; i < Auras.Count; i++)
            {
                if (Auras[i].Slot != aura.Slot) continue;
                if (aura.SpellId == 0) Auras.RemoveAt(i);
                else Auras[i] = aura;
                return;
            }
            if (aura.SpellId != 0) Auras.Add(aura);
        }

        public bool HasAura(uint spellId)
        {
            foreach (var a in Auras) if (a.SpellId == spellId) return true;
            return false;
        }
    }

    public sealed class RemoteEntity
    {
        public RemoteEntity(EntityGuid guid, string name, uint displayId)
        {
            Guid = guid;
            Name = name;
            DisplayId = displayId;
            if (guid.IsUnit) Unit = new UnitState();
        }

        public EntityGuid Guid { get; }
        public string Name { get; }
        public uint DisplayId { get; }
        public InterpolationBuffer Movement { get; } = new InterpolationBuffer();
        /// <summary>Health, auras and casting for players and creatures; null for other entities.</summary>
        public UnitState Unit { get; }
        public bool IsUnit => Unit != null;
        /// <summary>Gatherable objects: the item they give (0 for everything else).</summary>
        public uint ObjectItem;
        public bool IsObject => Guid.Type == EntityType.GameObject;

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
