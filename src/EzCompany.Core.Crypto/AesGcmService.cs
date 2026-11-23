using System.Security.Cryptography;

namespace EzCompany.Core.Crypto;

public sealed record AesGcmEnvelope(byte[] Nonce, byte[] Ciphertext, byte[] Tag);

/// <summary>
/// AES-256-GCM authenticated encryption helper using .NET 8 <see cref="AesGcm"/>.
/// </summary>
public sealed class AesGcmService
{
    public const int KeySizeBytes = 32;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;

    public AesGcmEnvelope Encrypt(ReadOnlySpan<byte> key32, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> aad = default)
    {
        if (key32.Length != KeySizeBytes)
        {
            throw new ArgumentException("AES-256-GCM requires a 32-byte key.", nameof(key32));
        }

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(key32, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        return new AesGcmEnvelope(nonce, ciphertext, tag);
    }

    public byte[] Decrypt(
        ReadOnlySpan<byte> key32,
        ReadOnlySpan<byte> nonce12,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag16,
        ReadOnlySpan<byte> aad = default)
    {
        if (key32.Length != KeySizeBytes)
        {
            throw new ArgumentException("AES-256-GCM requires a 32-byte key.", nameof(key32));
        }

        if (nonce12.Length != NonceSizeBytes)
        {
            throw new CryptographicException("Invalid AES-GCM nonce size.");
        }

        if (tag16.Length != TagSizeBytes)
        {
            throw new CryptographicException("Invalid AES-GCM authentication tag size.");
        }

        byte[] plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key32, TagSizeBytes);
        aes.Decrypt(nonce12, ciphertext, tag16, plaintext, aad);
        return plaintext;
    }
}
