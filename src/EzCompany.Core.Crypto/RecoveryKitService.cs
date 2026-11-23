using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EzCompany.Core.Crypto;

public sealed record RecoveryKitPayload(
    Guid CompanyId,
    string CompanyName,
    string TaxCode,
    string RootPrivateKeyBase64,
    string RootPublicKeyBase64,
    DateTimeOffset CreatedAtUtc,
    string ExportedByAdminName);

public sealed record RecoveryKitFileEnvelope(
    int Version,
    string Kdf,
    int MemoryKb,
    int Iterations,
    int Parallelism,
    string SaltBase64,
    string NonceBase64,
    string TagBase64,
    string CiphertextBase64);

/// <summary>
/// Exports and imports password-protected Master Recovery Kits (<c>.recovery</c>)
/// sealed using Argon2id KDF + AES-256-GCM. Used for disaster recovery and Secondary Director setup (Approach A).
/// </summary>
public sealed class RecoveryKitService
{
    private static readonly byte[] AadBytes = Encoding.UTF8.GetBytes("EzCompany.RecoveryKit.v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Argon2PasswordHasher _argon2;
    private readonly AesGcmService _aesGcm;
    private readonly Ed25519Service _ed25519;

    public RecoveryKitService(Argon2PasswordHasher argon2, AesGcmService aesGcm, Ed25519Service ed25519)
    {
        _argon2 = argon2;
        _aesGcm = aesGcm;
        _ed25519 = ed25519;
    }

    public byte[] ExportBytes(RecoveryKitPayload payload, string recoveryPassword)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(recoveryPassword) || recoveryPassword.Length < 8)
        {
            throw new ArgumentException("Recovery password must be at least 8 characters.", nameof(recoveryPassword));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(Argon2PasswordHasher.SaltLength);
        byte[] derivedKey = _argon2.DeriveKey(
            recoveryPassword,
            salt,
            AesGcmService.KeySizeBytes,
            Argon2PasswordHasher.DefaultMemoryKb,
            Argon2PasswordHasher.DefaultIterations,
            Argon2PasswordHasher.DefaultParallelism);

        try
        {
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            AesGcmEnvelope sealedData = _aesGcm.Encrypt(derivedKey, plaintext, AadBytes);
            CryptographicOperations.ZeroMemory(plaintext);

            var fileModel = new RecoveryKitFileEnvelope(
                Version: 1,
                Kdf: "argon2id",
                MemoryKb: Argon2PasswordHasher.DefaultMemoryKb,
                Iterations: Argon2PasswordHasher.DefaultIterations,
                Parallelism: Argon2PasswordHasher.DefaultParallelism,
                SaltBase64: Convert.ToBase64String(salt),
                NonceBase64: Convert.ToBase64String(sealedData.Nonce),
                TagBase64: Convert.ToBase64String(sealedData.Tag),
                CiphertextBase64: Convert.ToBase64String(sealedData.Ciphertext));

            return JsonSerializer.SerializeToUtf8Bytes(fileModel, JsonOptions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    public async Task ExportToFileAsync(
        string filePath,
        RecoveryKitPayload payload,
        string recoveryPassword,
        CancellationToken ct = default)
    {
        byte[] bytes = ExportBytes(payload, recoveryPassword);
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllBytesAsync(filePath, bytes, ct).ConfigureAwait(false);
    }

    public RecoveryKitPayload ImportBytes(byte[] fileBytes, string recoveryPassword)
    {
        if (fileBytes is null || fileBytes.Length == 0)
        {
            throw new CryptographicException("Recovery kit file is empty.");
        }

        if (string.IsNullOrEmpty(recoveryPassword))
        {
            throw new ArgumentException("Recovery password is required.", nameof(recoveryPassword));
        }

        RecoveryKitFileEnvelope? envelope = JsonSerializer.Deserialize<RecoveryKitFileEnvelope>(fileBytes);
        if (envelope is null || envelope.Version != 1 || !string.Equals(envelope.Kdf, "argon2id", StringComparison.Ordinal))
        {
            throw new CryptographicException("Unsupported or corrupted recovery kit format.");
        }

        byte[] salt = Convert.FromBase64String(envelope.SaltBase64);
        byte[] nonce = Convert.FromBase64String(envelope.NonceBase64);
        byte[] tag = Convert.FromBase64String(envelope.TagBase64);
        byte[] ciphertext = Convert.FromBase64String(envelope.CiphertextBase64);

        byte[] derivedKey = _argon2.DeriveKey(
            recoveryPassword,
            salt,
            AesGcmService.KeySizeBytes,
            envelope.MemoryKb,
            envelope.Iterations,
            envelope.Parallelism);

        try
        {
            byte[] plaintext = _aesGcm.Decrypt(derivedKey, nonce, ciphertext, tag, AadBytes);
            try
            {
                RecoveryKitPayload? payload = JsonSerializer.Deserialize<RecoveryKitPayload>(plaintext);
                if (payload is null)
                {
                    throw new CryptographicException("Failed to decode recovery kit payload.");
                }

                byte[] rootPriv = Convert.FromBase64String(payload.RootPrivateKeyBase64);
                byte[] rootPub = Convert.FromBase64String(payload.RootPublicKeyBase64);
                byte[] derivedPub = _ed25519.GetPublicKey(rootPriv);
                if (!CryptographicOperations.FixedTimeEquals(derivedPub, rootPub))
                {
                    throw new CryptographicException("Recovery kit keypair consistency verification failed.");
                }

                return payload;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    public async Task<RecoveryKitPayload> ImportFromFileAsync(
        string filePath,
        string recoveryPassword,
        CancellationToken ct = default)
    {
        byte[] bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        return ImportBytes(bytes, recoveryPassword);
    }
}
