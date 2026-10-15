namespace Madlands.Shared.Net
{
    public static class ProtocolVersion
    {
        /// <summary>
        /// Bump on ANY wire-format or shared-rule change. Checked in the handshake; a mismatching
        /// client is rejected with <see cref="RejectReason.ProtocolMismatch"/> ("please update").
        /// </summary>
        public const ushort Current = 1;

        /// <summary>LiteNetLib connection key; filters out stray traffic before the handshake.</summary>
        public const string ConnectionKey = "Madlands";
    }

    /// <summary>
    /// Every packet starts with a ushort opcode. Client-to-server opcodes live in 0x0100-0x7FFF,
    /// server-to-client in 0x8000-0xFFFF. Never renumber an existing opcode.
    /// </summary>
    public enum Opcode : ushort
    {
        None = 0,

        // Client -> Server
        Hello = 0x0101,
        MoveUpdate = 0x0102,
        Ping = 0x0103,

        // Server -> Client
        Welcome = 0x8001,
        Rejected = 0x8002,
        Pong = 0x8003,
        EntitySpawn = 0x8004,
        EntityDespawn = 0x8005,
        EntityMoves = 0x8006,
        MoveCorrection = 0x8007,
    }

    public enum RejectReason : byte
    {
        Unknown = 0,
        ProtocolMismatch = 1,
        InvalidName = 2,
        ServerFull = 3,
        Kicked = 4,
    }

    public interface IMessage
    {
        Opcode Opcode { get; }
        void Write(PacketWriter writer);
        void Read(ref PacketReader reader);
    }

    public static class Packet
    {
        /// <summary>Resets the writer and writes opcode + payload: a complete packet.</summary>
        public static void Encode<T>(PacketWriter writer, T message) where T : IMessage
        {
            writer.Reset();
            writer.WriteUShort((ushort)message.Opcode);
            message.Write(writer);
        }

        public static Opcode ReadOpcode(ref PacketReader reader) => (Opcode)reader.ReadUShort();
    }
}
