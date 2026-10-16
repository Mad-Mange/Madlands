using System.Globalization;
using System.Text;

namespace Madlands.Shared
{
    /// <summary>
    /// Validation and normalization of account names, passwords and character names. Shared so
    /// the client can give instant feedback; the server always re-validates.
    /// </summary>
    public static class NameRules
    {
        public const int UsernameMin = 3;
        public const int UsernameMax = 32;
        public const int PasswordMin = 8;
        public const int PasswordMax = 128;
        public const int CharacterNameMin = 2;
        public const int CharacterNameMax = 16;

        /// <summary>Lowercase account name of a-z, 0-9, '_', '.', '-'; null if invalid.</summary>
        public static string NormalizeUsername(string raw)
        {
            if (raw == null) return null;
            var name = raw.Trim().ToLowerInvariant();
            if (name.Length < UsernameMin || name.Length > UsernameMax) return null;
            foreach (var c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';
                if (!ok) return null;
            }
            return name;
        }

        public static bool IsValidPassword(string password) =>
            password != null && password.Length >= PasswordMin && password.Length <= PasswordMax;

        /// <summary>
        /// Character names are letters only, stored as "Capitalized" (like WoW): " aLiCe " becomes
        /// "Alice". Returns null if invalid.
        /// </summary>
        public static string NormalizeCharacterName(string raw)
        {
            if (raw == null) return null;
            var name = raw.Trim();
            if (name.Length < CharacterNameMin || name.Length > CharacterNameMax) return null;
            var sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!char.IsLetter(c)) return null;
                sb.Append(i == 0 ? char.ToUpper(c, CultureInfo.InvariantCulture) : char.ToLower(c, CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
