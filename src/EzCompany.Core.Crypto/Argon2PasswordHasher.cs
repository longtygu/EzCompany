using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace EzCompany.Core.Crypto;

/// <summary>
/// Argon2id password hashing and key derivation using <c>Konscious.Security.Cryptography.Argon2</c>.
/// </summary>
public sealed class Argon2PasswordHasher
{
    public const int DefaultMemoryKb = 65536;
    public const int DefaultIterations = 3;
    public const int DefaultParallelism = 2;
    public const int SaltLength = 16;
    public const int HashLength = 32;

    public string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        byte[] hash = DeriveKey(password, salt, HashLength, DefaultMemoryKb, DefaultIterations, DefaultParallelism);
        return $"$argon2id$v=19$m={DefaultMemoryKb},t={DefaultIterations},p={DefaultParallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        string[] parts = encodedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        // Expected parts: argon2id | v=19 | m=...,t=...,p=... | saltB64 | hashB64
        if (parts.Length != 5 || !string.Equals(parts[0], "argon2id", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryParseParameters(parts[2], out int memoryKb, out int iterations, out int parallelism))
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expectedHash = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] computed = DeriveKey(password, salt, expectedHash.Length, memoryKb, iterations, parallelism);
        return CryptographicOperations.FixedTimeEquals(computed, expectedHash);
    }

    public byte[] DeriveKey(
        string password,
        byte[] salt,
        int length = HashLength,
        int memoryKb = DefaultMemoryKb,
        int iterations = DefaultIterations,
        int parallelism = DefaultParallelism)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKb,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };

        return argon2.GetBytes(length);
    }

    private static bool TryParseParameters(string segment, out int memoryKb, out int iterations, out int parallelism)
    {
        memoryKb = 0;
        iterations = 0;
        parallelism = 0;

        foreach (string token in segment.Split(','))
        {
            string[] kv = token.Split('=');
            if (kv.Length != 2 || !int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) || val <= 0)
            {
                return false;
            }

            switch (kv[0])
            {
                case "m":
                    memoryKb = val;
                    break;
                case "t":
                    iterations = val;
                    break;
                case "p":
                    parallelism = val;
                    break;
                default:
                    return false;
            }
        }

        return memoryKb > 0 && iterations > 0 && parallelism > 0;
    }
}
