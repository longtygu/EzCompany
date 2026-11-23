using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace EzCompany.Core.Crypto;

public sealed record Ed25519KeyPair(byte[] PrivateKey, byte[] PublicKey)
{
    public string PublicKeyBase64 => Convert.ToBase64String(PublicKey);
    public string PublicKeyFingerprint => Convert.ToHexString(SHA256.HashData(PublicKey)[..8]);
}

/// <summary>
/// Ed25519 asymmetric signing and verification built on BouncyCastle.Cryptography.
/// </summary>
public sealed class Ed25519Service
{
    public const int KeySizeBytes = 32;
    public const int SignatureSizeBytes = 64;

    public Ed25519KeyPair GenerateKeyPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        AsymmetricCipherKeyPair pair = generator.GenerateKeyPair();

        var priv = (Ed25519PrivateKeyParameters)pair.Private;
        var pub = (Ed25519PublicKeyParameters)pair.Public;
        return new Ed25519KeyPair(priv.GetEncoded(), pub.GetEncoded());
    }

    public byte[] GetPublicKey(byte[] privateKey)
    {
        ValidateKeyLength(privateKey, nameof(privateKey));
        var priv = new Ed25519PrivateKeyParameters(privateKey, 0);
        return priv.GeneratePublicKey().GetEncoded();
    }

    public byte[] Sign(byte[] privateKey, ReadOnlySpan<byte> data)
    {
        ValidateKeyLength(privateKey, nameof(privateKey));
        var priv = new Ed25519PrivateKeyParameters(privateKey, 0);
        var signer = new Ed25519Signer();
        signer.Init(true, priv);
        byte[] buffer = data.ToArray();
        signer.BlockUpdate(buffer, 0, buffer.Length);
        return signer.GenerateSignature();
    }

    public bool Verify(byte[] publicKey, ReadOnlySpan<byte> data, byte[] signature)
    {
        if (publicKey is null || publicKey.Length != KeySizeBytes)
        {
            return false;
        }

        if (signature is null || signature.Length != SignatureSizeBytes)
        {
            return false;
        }

        try
        {
            var pub = new Ed25519PublicKeyParameters(publicKey, 0);
            var verifier = new Ed25519Signer();
            verifier.Init(false, pub);
            byte[] buffer = data.ToArray();
            verifier.BlockUpdate(buffer, 0, buffer.Length);
            return verifier.VerifySignature(signature);
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateKeyLength(byte[] key, string paramName)
    {
        if (key is null || key.Length != KeySizeBytes)
        {
            throw new ArgumentException($"Expected {KeySizeBytes}-byte Ed25519 key.", paramName);
        }
    }
}
