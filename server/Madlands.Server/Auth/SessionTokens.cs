using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Madlands.Server.Auth;

/// <summary>
/// Opaque login session tokens: 256 random bits, base64url. The client presents the token to the
/// account API (Authorization: Bearer) and in Hello on the game connection. Only its SHA-256 hash
/// is stored server-side.
/// </summary>
public static class SessionTokens
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
