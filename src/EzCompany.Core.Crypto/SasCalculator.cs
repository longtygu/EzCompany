using System.Buffers.Binary;
using System.Security.Cryptography;

namespace EzCompany.Core.Crypto;

/// <summary>
/// Derives a symmetric 6-digit Short Authentication String (<c>XXX-XXX</c>) for MITM-resistant LAN pairing.
/// </summary>
public static class SasCalculator
{
    public static string ComputeSixDigitCode(
        ReadOnlySpan<byte> rootPublicKey,
        ReadOnlySpan<byte> devicePublicKey,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce)
    {
        if (rootPublicKey.IsEmpty || devicePublicKey.IsEmpty || clientNonce.IsEmpty || serverNonce.IsEmpty)
        {
            throw new ArgumentException("All SAS inputs must be non-empty.");
        }

        byte[] buffer = new byte[rootPublicKey.Length + devicePublicKey.Length + clientNonce.Length + serverNonce.Length];
        int offset = 0;
        rootPublicKey.CopyTo(buffer.AsSpan(offset));
        offset += rootPublicKey.Length;
        devicePublicKey.CopyTo(buffer.AsSpan(offset));
        offset += devicePublicKey.Length;
        clientNonce.CopyTo(buffer.AsSpan(offset));
        offset += clientNonce.Length;
        serverNonce.CopyTo(buffer.AsSpan(offset));

        byte[] digest = SHA256.HashData(buffer);
        uint codeValue = BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(0, 4)) % 1_000_000u;
        string digits = codeValue.ToString("D6");
        return $"{digits[..3]}-{digits[3..]}";
    }
}
