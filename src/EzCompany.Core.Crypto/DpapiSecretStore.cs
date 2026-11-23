using System.Security.Cryptography;
using System.Text;

namespace EzCompany.Core.Crypto;

/// <summary>
/// Wraps Windows DPAPI (<see cref="DataProtectionScope.CurrentUser"/>) with purpose-specific entropy.
/// </summary>
public sealed class DpapiSecretStore
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose)
    {
        byte[] entropy = SHA256.HashData(Encoding.UTF8.GetBytes($"EzCompany.DPAPI.{purpose}"));
        return ProtectedData.Protect(plaintext.ToArray(), entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] protectedBytes, string purpose)
    {
        byte[] entropy = SHA256.HashData(Encoding.UTF8.GetBytes($"EzCompany.DPAPI.{purpose}"));
        return ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
    }

    public void SaveProtectedFile(string filePath, ReadOnlySpan<byte> plaintext, string purpose)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        byte[] protectedBytes = Protect(plaintext, purpose);
        File.WriteAllBytes(filePath, protectedBytes);
    }

    public byte[]? LoadProtectedFile(string filePath, string purpose)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        byte[] protectedBytes = File.ReadAllBytes(filePath);
        return Unprotect(protectedBytes, purpose);
    }
}
