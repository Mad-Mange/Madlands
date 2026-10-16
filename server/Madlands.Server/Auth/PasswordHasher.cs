using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Madlands.Server.Auth;

/// <summary>
/// Argon2id password hashing stored as PHC strings
/// (<c>$argon2id$v=19$m=19456,t=2,p=1$salt$hash</c>), so parameters can be raised later without
/// breaking existing hashes. Defaults follow the OWASP minimum. Hashing is CPU and memory heavy,
/// so it runs on the thread pool behind a concurrency gate.
/// </summary>
public sealed class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private readonly int _memoryKiB;
    private readonly int _iterations;
    private readonly SemaphoreSlim _gate = new(Math.Max(2, Environment.ProcessorCount / 2));
    private readonly Lazy<string> _dummyHash;

    /// <param name="memoryKiB">Argon2 memory cost; tests pass a small value for speed.</param>
    public PasswordHasher(int memoryKiB = 19456, int iterations = 2)
    {
        _memoryKiB = memoryKiB;
        _iterations = iterations;
        _dummyHash = new Lazy<string>(() => Hash("not-a-real-password", RandomNumberGenerator.GetBytes(SaltBytes), _memoryKiB, _iterations));
    }

    public async Task<string> HashAsync(string password)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() => Hash(password, RandomNumberGenerator.GetBytes(SaltBytes), _memoryKiB, _iterations));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Verifies a password. Pass null for an unknown account: a dummy hash is checked so that
    /// "no such user" takes as long as "wrong password" (no username enumeration by timing).
    /// </summary>
    public async Task<bool> VerifyAsync(string password, string? phc)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() => phc is not null & Verify(password, phc ?? _dummyHash.Value));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Hash(string password, byte[] salt, int memoryKiB, int iterations)
    {
        var hash = Compute(password, salt, memoryKiB, iterations, parallelism: 1);
        return string.Create(CultureInfo.InvariantCulture,
            $"$argon2id$v=19$m={memoryKiB},t={iterations},p=1${B64(salt)}${B64(hash)}");
    }

    private static bool Verify(string password, string phc)
    {
        // ["", "argon2id", "v=19", "m=..,t=..,p=..", salt, hash]
        var parts = phc.Split('$');
        if (parts.Length != 6 || parts[1] != "argon2id") return false;
        int m = 0, t = 0, p = 0;
        foreach (var kv in parts[3].Split(','))
        {
            var pair = kv.Split('=');
            if (pair.Length != 2 || !int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return false;
            switch (pair[0])
            {
                case "m": m = value; break;
                case "t": t = value; break;
                case "p": p = value; break;
            }
        }
        if (m <= 0 || t <= 0 || p <= 0) return false;
        var salt = FromB64(parts[4]);
        var expected = FromB64(parts[5]);
        var actual = Compute(password, salt, m, t, p);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Compute(string password, byte[] salt, int memoryKiB, int iterations, int parallelism)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(HashBytes);
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static byte[] FromB64(string s) => Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
}
