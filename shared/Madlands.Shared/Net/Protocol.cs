namespace Madlands.Shared.Net
{
    public static class ProtocolVersion
    {
        /// <summary>
        /// Bump on ANY wire-format or shared-rule change. Checked in the handshake; a mismatching
        /// client is rejected with <see cref="RejectReason.ProtocolMismatch"/> ("please update").
        /// </summary>
        public const ushort Current = 10;

        /// <summary>LiteNetLib connection key; filters out stray traffic before the handshake.</summary>
        public const string ConnectionKey = "Madlands";

        /// <summary>HTTP header carrying the protocol version on account API requests.</summary>
        public const string HttpHeader = "X-Madlands-Protocol";
    }

    /// <summary>
    /// Every packet starts with a ushort opcode. Client-to-server opcodes live in 0x0100-0x7FFF,
    /// server-to-client in 0x8000-0xFFFF. Never renumber an existing opcode.
    /// 0x01xx / 0x80xx: game connection (UDP). 0x02xx / 0x81xx: account API (HTTP bodies).
    /// </summary>
    public enum Opcode : ushort
    {
        None = 0,

        // Client -> Server (game)
        Hello = 0x0101,
        MoveUpdate = 0x0102,
        Ping = 0x0103,
        SetTarget = 0x0104,
        CastSpell = 0x0105,
        CancelCast = 0x0106,
        StopAttack = 0x0107,
        ChatInput = 0x0108,
        ReleaseSpirit = 0x0109,
        CancelAura = 0x010A,
        MoveItem = 0x010B,
        UseItem = 0x010C,
        DestroyItem = 0x010D,
        AutoEquip = 0x010E,
        Interact = 0x010F,
        LootOpen = 0x0110,
        LootTake = 0x0111,
        LootClose = 0x0112,
        VendorBuy = 0x0113,
        VendorSell = 0x0114,
        QuestAccept = 0x0115,
        QuestTurnIn = 0x0116,
        QuestAbandon = 0x0117,

        // Client -> Server (account API)
        RegisterRequest = 0x0201,
        LoginRequest = 0x0202,
        CharacterListRequest = 0x0203,
        CreateCharacterRequest = 0x0204,
        DeleteCharacterRequest = 0x0205,
        LogoutRequest = 0x0206,

        // Server -> Client (game)
        Welcome = 0x8001,
        Rejected = 0x8002,
        Pong = 0x8003,
        EntitySpawn = 0x8004,
        EntityDespawn = 0x8005,
        EntityMoves = 0x8006,
        MoveCorrection = 0x8007,
        GameData = 0x8008,
        UnitUpdates = 0x8009,
        SpellStart = 0x800A,
        SpellGo = 0x800B,
        SpellFailed = 0x800C,
        CombatEvent = 0x800D,
        AuraUpdate = 0x800E,
        Cooldown = 0x800F,
        PlayerStats = 0x8010,
        Spellbook = 0x8011,
        ChatMessage = 0x8012,
        ExperienceGained = 0x8013,
        InventoryUpdate = 0x8014,
        MoneyUpdate = 0x8015,
        ItemReceived = 0x8016,
        ActionFailed = 0x8017,
        LootWindow = 0x8018,
        LootableUpdate = 0x8019,
        GossipMenu = 0x801A,
        VendorInventory = 0x801B,
        QuestLogUpdate = 0x801C,
        QuestGiverStatusUpdate = 0x801D,
        QuestCompleted = 0x801E,

        // Server -> Client (account API)
        AuthResponse = 0x8101,
        CharacterListResponse = 0x8102,
        CreateCharacterResponse = 0x8103,
        ApiResult = 0x8104,
    }

    public enum RejectReason : byte
    {
        Unknown = 0,
        ProtocolMismatch = 1,
        InvalidName = 2,
        ServerFull = 3,
        Kicked = 4,
        /// <summary>Session token unknown or expired: log in again.</summary>
        InvalidSession = 5,
        CharacterNotFound = 6,
        ServerError = 7,
        /// <summary>The same character (or account) entered the world from another connection.</summary>
        LoggedInElsewhere = 8,
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
