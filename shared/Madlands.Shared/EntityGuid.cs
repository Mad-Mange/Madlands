using System;

namespace Madlands.Shared
{
    public enum EntityType : byte
    {
        None = 0,
        Player = 1,
        Creature = 2,
        GameObject = 3,
        Item = 4,
    }

    /// <summary>
    /// 64-bit entity identity: [type:8][counter:56]. The type is encoded in the id so any system
    /// can tell what an id refers to without a lookup. Reserved for later: realm/shard bits taken
    /// from the high end of the counter when ids must be unique across servers.
    /// </summary>
    public readonly struct EntityGuid : IEquatable<EntityGuid>
    {
        public const ulong CounterMask = (1UL << 56) - 1;
        public static readonly EntityGuid Empty = new EntityGuid(0);

        public readonly ulong Raw;

        public EntityGuid(ulong raw)
        {
            Raw = raw;
        }

        public static EntityGuid Create(EntityType type, ulong counter)
        {
            if (counter > CounterMask) throw new ArgumentOutOfRangeException(nameof(counter));
            return new EntityGuid(((ulong)type << 56) | counter);
        }

        public EntityType Type => (EntityType)(Raw >> 56);
        public ulong Counter => Raw & CounterMask;
        public bool IsEmpty => Raw == 0;

        public bool Equals(EntityGuid other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is EntityGuid other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public static bool operator ==(EntityGuid a, EntityGuid b) => a.Raw == b.Raw;
        public static bool operator !=(EntityGuid a, EntityGuid b) => a.Raw != b.Raw;

        public override string ToString() => IsEmpty ? "Empty" : Type + "-" + Counter;
    }
}
