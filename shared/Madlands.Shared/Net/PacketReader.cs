using System;
using System.Buffers.Binary;
using System.Text;

namespace Madlands.Shared.Net
{
    /// <summary>Thrown when a packet is truncated or contains invalid data. The sender should be dropped.</summary>
    public sealed class PacketException : Exception
    {
        public PacketException(string message) : base(message) { }
    }

    /// <summary>
    /// Zero-allocation little-endian reader over a received buffer. Every read is bounds-checked
    /// and throws <see cref="PacketException"/>, so malformed input can never crash the reader.
    /// </summary>
    public ref struct PacketReader
    {
        public const int DefaultMaxStringBytes = 1024;

        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        public PacketReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
        }

        public int Position => _position;
        public int Remaining => _data.Length - _position;

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || _position + count > _data.Length)
                throw new PacketException("Packet truncated: needed " + count + " bytes at " + _position + ", have " + Remaining + ".");
            var span = _data.Slice(_position, count);
            _position += count;
            return span;
        }

        public byte ReadByte() => Take(1)[0];
        public bool ReadBool() => ReadByte() != 0;
        public ushort ReadUShort() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public short ReadShort() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
        public uint ReadUInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int ReadInt() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public ulong ReadULong() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        public long ReadLong() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt());

        public Vec3 ReadVec3()
        {
            float x = ReadFloat();
            float y = ReadFloat();
            float z = ReadFloat();
            return new Vec3(x, y, z);
        }

        public EntityGuid ReadGuid() => new EntityGuid(ReadULong());

        public string ReadString(int maxBytes = DefaultMaxStringBytes)
        {
            int length = ReadUShort();
            if (length > maxBytes) throw new PacketException("String of " + length + " bytes exceeds limit " + maxBytes + ".");
            return length == 0 ? string.Empty : Encoding.UTF8.GetString(Take(length));
        }
    }
}
