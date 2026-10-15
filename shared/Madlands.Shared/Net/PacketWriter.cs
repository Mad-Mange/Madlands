using System;
using System.Buffers.Binary;
using System.Text;

namespace Madlands.Shared.Net
{
    /// <summary>
    /// Growable little-endian binary writer. Reused across sends (call <see cref="Reset"/>);
    /// not thread-safe.
    /// </summary>
    public sealed class PacketWriter
    {
        private byte[] _buffer;
        private int _length;

        public PacketWriter(int initialCapacity = 256)
        {
            _buffer = new byte[Math.Max(16, initialCapacity)];
        }

        public int Length => _length;
        public byte[] Buffer => _buffer;
        public ReadOnlySpan<byte> AsSpan() => new ReadOnlySpan<byte>(_buffer, 0, _length);
        public byte[] ToArray() => AsSpan().ToArray();

        public void Reset() => _length = 0;

        private Span<byte> Reserve(int count)
        {
            int required = _length + count;
            if (required > _buffer.Length)
            {
                int newSize = _buffer.Length * 2;
                while (newSize < required) newSize *= 2;
                Array.Resize(ref _buffer, newSize);
            }
            var span = new Span<byte>(_buffer, _length, count);
            _length = required;
            return span;
        }

        public void WriteByte(byte value) => Reserve(1)[0] = value;
        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);
        public void WriteUShort(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), value);
        public void WriteShort(short value) => BinaryPrimitives.WriteInt16LittleEndian(Reserve(2), value);
        public void WriteUInt(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);
        public void WriteInt(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
        public void WriteULong(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);
        public void WriteLong(long value) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value);
        public void WriteFloat(float value) => WriteInt(BitConverter.SingleToInt32Bits(value));

        public void WriteVec3(Vec3 v)
        {
            WriteFloat(v.X);
            WriteFloat(v.Y);
            WriteFloat(v.Z);
        }

        public void WriteGuid(EntityGuid guid) => WriteULong(guid.Raw);

        /// <summary>UTF-8 with a ushort byte-length prefix.</summary>
        public void WriteString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteUShort(0);
                return;
            }
            int byteCount = Encoding.UTF8.GetByteCount(value);
            if (byteCount > ushort.MaxValue) throw new ArgumentException("String too long for packet.", nameof(value));
            WriteUShort((ushort)byteCount);
            Encoding.UTF8.GetBytes(value, Reserve(byteCount));
        }

        /// <summary>Overwrites a ushort at an earlier position (e.g. a count written as a placeholder).</summary>
        public void PatchUShort(int position, ushort value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(new Span<byte>(_buffer, position, 2), value);
    }
}
