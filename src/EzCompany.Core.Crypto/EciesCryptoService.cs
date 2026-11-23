using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace EzCompany.Core.Crypto;

/// <summary>
/// X25519 + HKDF-SHA256 + AES-256-GCM ECIES hybrid encryption.
/// Allows employee nodes to encrypt telemetry snapshots using only the pinned Company Encryption Public Key,
/// while both Director 1 and Director 2 derive the identical X25519 private key from the shared Ed25519 Root Private Key.
/// </summary>
public sealed class EciesCryptoService
{
    private const byte EnvelopeVersion = 1;
    private const int X25519KeySize = 32;
    private const int HeaderSize = 1 + X25519KeySize + AesGcmService.NonceSizeBytes + AesGcmService.TagSizeBytes;

    private static readonly byte[] CompanyKdfSalt = Encoding.UTF8.GetBytes("EzCompany.v1");
    private static readonly byte[] CompanyKdfInfo = Encoding.UTF8.GetBytes("x25519-telemetry-encryption");
    private static readonly byte[] EciesKdfInfo = Encoding.UTF8.GetBytes("EzCompany.ECIES.v1");

    private readonly AesGcmService _aesGcm = new();

    public byte[] DeriveCompanyX25519PrivateKey(byte[] rootEd25519PrivateKey32)
    {
        if (rootEd25519PrivateKey32 is null || rootEd25519PrivateKey32.Length != Ed25519Service.KeySizeBytes)
        {
            throw new ArgumentException("Root Ed25519 private key must be 32 bytes.", nameof(rootEd25519PrivateKey32));
        }

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            rootEd25519PrivateKey32,
            X25519KeySize,
            CompanyKdfSalt,
            CompanyKdfInfo);
    }

    public byte[] GetX25519PublicKey(byte[] x25519PrivateKey32)
    {
        if (x25519PrivateKey32 is null || x25519PrivateKey32.Length != X25519KeySize)
        {
            throw new ArgumentException("X25519 private key must be 32 bytes.", nameof(x25519PrivateKey32));
        }

        var priv = new X25519PrivateKeyParameters(x25519PrivateKey32, 0);
        return priv.GeneratePublicKey().GetEncoded();
    }

    public byte[] DeriveCompanyX25519PublicKey(byte[] rootEd25519PrivateKey32)
    {
        byte[] priv = DeriveCompanyX25519PrivateKey(rootEd25519PrivateKey32);
        try
        {
            return GetX25519PublicKey(priv);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(priv);
        }
    }

    public byte[] Encrypt(byte[] recipientPublicKey32, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> aad = default)
    {
        if (recipientPublicKey32 is null || recipientPublicKey32.Length != X25519KeySize)
        {
            throw new ArgumentException("Recipient X25519 public key must be 32 bytes.", nameof(recipientPublicKey32));
        }

        var generator = new X25519KeyPairGenerator();
        generator.Init(new X25519KeyGenerationParameters(new SecureRandom()));
        var ephemeralPair = generator.GenerateKeyPair();
        var ephemeralPriv = (X25519PrivateKeyParameters)ephemeralPair.Private;
        byte[] ephemeralPubBytes = ((X25519PublicKeyParameters)ephemeralPair.Public).GetEncoded();

        var recipientPub = new X25519PublicKeyParameters(recipientPublicKey32, 0);
        byte[] sharedSecret = new byte[X25519KeySize];
        var agreement = new X25519Agreement();
        agreement.Init(ephemeralPriv);
        agreement.CalculateAgreement(recipientPub, sharedSecret, 0);

        byte[] aesKey = DeriveSessionKey(sharedSecret, ephemeralPubBytes, recipientPublicKey32);
        try
        {
            AesGcmEnvelope sealedData = _aesGcm.Encrypt(aesKey, plaintext, aad);

            byte[] output = new byte[HeaderSize + sealedData.Ciphertext.Length];
            int offset = 0;
            output[offset++] = EnvelopeVersion;
            Buffer.BlockCopy(ephemeralPubBytes, 0, output, offset, X25519KeySize);
            offset += X25519KeySize;
            Buffer.BlockCopy(sealedData.Nonce, 0, output, offset, AesGcmService.NonceSizeBytes);
            offset += AesGcmService.NonceSizeBytes;
            Buffer.BlockCopy(sealedData.Tag, 0, output, offset, AesGcmService.TagSizeBytes);
            offset += AesGcmService.TagSizeBytes;
            Buffer.BlockCopy(sealedData.Ciphertext, 0, output, offset, sealedData.Ciphertext.Length);
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    public byte[] Decrypt(byte[] recipientPrivateKey32, ReadOnlySpan<byte> envelope, ReadOnlySpan<byte> aad = default)
    {
        if (recipientPrivateKey32 is null || recipientPrivateKey32.Length != X25519KeySize)
        {
            throw new ArgumentException("Recipient X25519 private key must be 32 bytes.", nameof(recipientPrivateKey32));
        }

        if (envelope.Length < HeaderSize)
        {
            throw new CryptographicException("Encrypted envelope is truncated.");
        }

        if (envelope[0] != EnvelopeVersion)
        {
            throw new CryptographicException($"Unsupported ECIES envelope version: {envelope[0]}.");
        }

        int offset = 1;
        byte[] ephemeralPubBytes = envelope.Slice(offset, X25519KeySize).ToArray();
        offset += X25519KeySize;
        ReadOnlySpan<byte> nonce = envelope.Slice(offset, AesGcmService.NonceSizeBytes);
        offset += AesGcmService.NonceSizeBytes;
        ReadOnlySpan<byte> tag = envelope.Slice(offset, AesGcmService.TagSizeBytes);
        offset += AesGcmService.TagSizeBytes;
        ReadOnlySpan<byte> ciphertext = envelope.Slice(offset);

        var recipientPriv = new X25519PrivateKeyParameters(recipientPrivateKey32, 0);
        byte[] recipientPubBytes = recipientPriv.GeneratePublicKey().GetEncoded();
        var ephemeralPub = new X25519PublicKeyParameters(ephemeralPubBytes, 0);

        byte[] sharedSecret = new byte[X25519KeySize];
        var agreement = new X25519Agreement();
        agreement.Init(recipientPriv);
        agreement.CalculateAgreement(ephemeralPub, sharedSecret, 0);

        byte[] aesKey = DeriveSessionKey(sharedSecret, ephemeralPubBytes, recipientPubBytes);
        try
        {
            return _aesGcm.Decrypt(aesKey, nonce, ciphertext, tag, aad);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    private static byte[] DeriveSessionKey(byte[] sharedSecret, byte[] ephemeralPub, byte[] recipientPub)
    {
        byte[] salt = new byte[ephemeralPub.Length + recipientPub.Length];
        Buffer.BlockCopy(ephemeralPub, 0, salt, 0, ephemeralPub.Length);
        Buffer.BlockCopy(recipientPub, 0, salt, ephemeralPub.Length, recipientPub.Length);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, AesGcmService.KeySizeBytes, salt, EciesKdfInfo);
    }
}
