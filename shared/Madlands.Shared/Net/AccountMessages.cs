using System.Collections.Generic;

namespace Madlands.Shared.Net
{
    // Account API messages. They travel as HTTP request/response bodies (opcode + payload, same
    // binary format as the game connection), so the API needs no JSON library on the client.
    // Every message has a roundtrip test in Madlands.Server.Tests/ProtocolTests.cs.

    public enum ApiResultCode : byte
    {
        Ok = 0,
        InvalidCredentials = 1,
        UsernameTaken = 2,
        InvalidUsername = 3,
        InvalidPassword = 4,
        NameTaken = 5,
        InvalidName = 6,
        TooManyCharacters = 7,
        NotFound = 8,
        CharacterOnline = 9,
        Unauthorized = 10,
        RateLimited = 11,
        ProtocolMismatch = 12,
        InvalidRequest = 13,
        ServerError = 14,
        /// <summary>Client-side only: the server could not be reached.</summary>
        Unreachable = 15,
    }

    /// <summary>Common shape of every API response: a result code, a human-readable message, then a body.</summary>
    public abstract class ApiResponse : IMessage
    {
        public ApiResultCode Result;
        public string Message;

        public bool Ok => Result == ApiResultCode.Ok;
        public abstract Opcode Opcode { get; }

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Result);
            w.WriteString(Message);
            WriteBody(w);
        }

        public void Read(ref PacketReader r)
        {
            Result = (ApiResultCode)r.ReadByte();
            Message = r.ReadString();
            ReadBody(ref r);
        }

        protected virtual void WriteBody(PacketWriter w) { }
        protected virtual void ReadBody(ref PacketReader r) { }
    }

    /// <summary>A response without a body; also sent instead of the expected type on any error.</summary>
    public sealed class ApiResult : ApiResponse
    {
        public override Opcode Opcode => Opcode.ApiResult;
    }

    public sealed class RegisterRequest : IMessage
    {
        public string Username;
        public string Password;

        public Opcode Opcode => Opcode.RegisterRequest;

        public void Write(PacketWriter w)
        {
            w.WriteString(Username);
            w.WriteString(Password);
        }

        public void Read(ref PacketReader r)
        {
            Username = r.ReadString(NameRules.UsernameMax * 4);
            Password = r.ReadString(NameRules.PasswordMax * 4);
        }
    }

    public sealed class LoginRequest : IMessage
    {
        public string Username;
        public string Password;

        public Opcode Opcode => Opcode.LoginRequest;

        public void Write(PacketWriter w)
        {
            w.WriteString(Username);
            w.WriteString(Password);
        }

        public void Read(ref PacketReader r)
        {
            Username = r.ReadString(NameRules.UsernameMax * 4);
            Password = r.ReadString(NameRules.PasswordMax * 4);
        }
    }

    /// <summary>Answer to register and login: a session token plus where the realm's game server is.</summary>
    public sealed class AuthResponse : ApiResponse
    {
        public string SessionToken;
        public string RealmName;
        public string RealmHost;
        public ushort RealmPort;

        public override Opcode Opcode => Opcode.AuthResponse;

        protected override void WriteBody(PacketWriter w)
        {
            w.WriteString(SessionToken);
            w.WriteString(RealmName);
            w.WriteString(RealmHost);
            w.WriteUShort(RealmPort);
        }

        protected override void ReadBody(ref PacketReader r)
        {
            SessionToken = r.ReadString(Hello.MaxTokenBytes);
            RealmName = r.ReadString();
            RealmHost = r.ReadString();
            RealmPort = r.ReadUShort();
        }
    }

    public struct CharacterSummary
    {
        public ulong Id;
        public string Name;
        public byte Race;
        public byte Class;
        public byte Level;
        public ushort MapId;

        public void Write(PacketWriter w)
        {
            w.WriteULong(Id);
            w.WriteString(Name);
            w.WriteByte(Race);
            w.WriteByte(Class);
            w.WriteByte(Level);
            w.WriteUShort(MapId);
        }

        public static CharacterSummary Read(ref PacketReader r)
        {
            CharacterSummary c;
            c.Id = r.ReadULong();
            c.Name = r.ReadString(Hello.MaxNameBytes);
            c.Race = r.ReadByte();
            c.Class = r.ReadByte();
            c.Level = r.ReadByte();
            c.MapId = r.ReadUShort();
            return c;
        }
    }

    /// <summary>A selectable option (race, class) as defined in the server's content.</summary>
    public struct NamedOption
    {
        public byte Id;
        public string Name;

        public NamedOption(byte id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    public sealed class CharacterListRequest : IMessage
    {
        public Opcode Opcode => Opcode.CharacterListRequest;
        public void Write(PacketWriter w) { }
        public void Read(ref PacketReader r) { }
    }

    /// <summary>The account's characters plus the races and classes available for creation.</summary>
    public sealed class CharacterListResponse : ApiResponse
    {
        public const int MaxEntries = 64;

        public byte MaxCharacters;
        public readonly List<CharacterSummary> Characters = new List<CharacterSummary>();
        public readonly List<NamedOption> Races = new List<NamedOption>();
        public readonly List<NamedOption> Classes = new List<NamedOption>();

        public override Opcode Opcode => Opcode.CharacterListResponse;

        protected override void WriteBody(PacketWriter w)
        {
            w.WriteByte(MaxCharacters);
            w.WriteByte((byte)Characters.Count);
            foreach (var c in Characters) c.Write(w);
            WriteOptions(w, Races);
            WriteOptions(w, Classes);
        }

        protected override void ReadBody(ref PacketReader r)
        {
            Characters.Clear();
            Races.Clear();
            Classes.Clear();
            MaxCharacters = r.ReadByte();
            int count = ReadCount(ref r);
            for (int i = 0; i < count; i++) Characters.Add(CharacterSummary.Read(ref r));
            ReadOptions(ref r, Races);
            ReadOptions(ref r, Classes);
        }

        private static void WriteOptions(PacketWriter w, List<NamedOption> options)
        {
            w.WriteByte((byte)options.Count);
            foreach (var o in options)
            {
                w.WriteByte(o.Id);
                w.WriteString(o.Name);
            }
        }

        private static void ReadOptions(ref PacketReader r, List<NamedOption> options)
        {
            int count = ReadCount(ref r);
            for (int i = 0; i < count; i++) options.Add(new NamedOption(r.ReadByte(), r.ReadString()));
        }

        private static int ReadCount(ref PacketReader r)
        {
            int count = r.ReadByte();
            if (count > MaxEntries) throw new PacketException("List of " + count + " entries exceeds " + MaxEntries + ".");
            return count;
        }
    }

    public sealed class CreateCharacterRequest : IMessage
    {
        public string Name;
        public byte Race;
        public byte Class;

        public Opcode Opcode => Opcode.CreateCharacterRequest;

        public void Write(PacketWriter w)
        {
            w.WriteString(Name);
            w.WriteByte(Race);
            w.WriteByte(Class);
        }

        public void Read(ref PacketReader r)
        {
            Name = r.ReadString(Hello.MaxNameBytes);
            Race = r.ReadByte();
            Class = r.ReadByte();
        }
    }

    public sealed class CreateCharacterResponse : ApiResponse
    {
        public CharacterSummary Character;

        public override Opcode Opcode => Opcode.CreateCharacterResponse;

        protected override void WriteBody(PacketWriter w) => Character.Write(w);
        protected override void ReadBody(ref PacketReader r) => Character = CharacterSummary.Read(ref r);
    }

    public sealed class DeleteCharacterRequest : IMessage
    {
        public ulong CharacterId;

        public Opcode Opcode => Opcode.DeleteCharacterRequest;
        public void Write(PacketWriter w) => w.WriteULong(CharacterId);
        public void Read(ref PacketReader r) => CharacterId = r.ReadULong();
    }

    public sealed class LogoutRequest : IMessage
    {
        public Opcode Opcode => Opcode.LogoutRequest;
        public void Write(PacketWriter w) { }
        public void Read(ref PacketReader r) { }
    }
}
