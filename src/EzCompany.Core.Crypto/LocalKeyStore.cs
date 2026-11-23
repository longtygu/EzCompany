using EzCompany.Core.Abstractions;

namespace EzCompany.Core.Crypto;

/// <summary>
/// Manages local DPAPI-protected private keys, pinned public keys, <c>policy.lic</c>, and <c>clock.guard</c>.
/// </summary>
public sealed class LocalKeyStore
{
    private const string RootKeyPurpose = "RootEd25519PrivateKey";
    private const string DeviceKeyPurpose = "DeviceEd25519PrivateKey";

    private readonly ILocalStoragePaths _paths;
    private readonly DpapiSecretStore _dpapi;

    public LocalKeyStore(ILocalStoragePaths paths, DpapiSecretStore dpapi)
    {
        _paths = paths;
        _dpapi = dpapi;
    }

    public string RootPrivateKeyPath => Path.Combine(_paths.KeysDirectory, "root.key.dpapi");
    public string RootPublicKeyPath => Path.Combine(_paths.KeysDirectory, "root.pub");
    public string DevicePrivateKeyPath => Path.Combine(_paths.KeysDirectory, "device.key.dpapi");
    public string DevicePublicKeyPath => Path.Combine(_paths.KeysDirectory, "device.pub");
    public string PinnedRootPublicKeyPath => Path.Combine(_paths.KeysDirectory, "pinned_root.pub");
    public string PinnedCompanyEncryptionPublicKeyPath => Path.Combine(_paths.KeysDirectory, "pinned_enc.pub");
    public string PolicyLicensePath => Path.Combine(_paths.KeysDirectory, "policy.lic");
    public string ClockGuardPath => Path.Combine(_paths.KeysDirectory, "clock.guard");

    public bool HasRootPrivateKey => File.Exists(RootPrivateKeyPath);
    public bool HasPinnedRootPublicKey => File.Exists(PinnedRootPublicKeyPath);
    public bool HasDeviceKeyPair => File.Exists(DevicePrivateKeyPath) && File.Exists(DevicePublicKeyPath);
    public bool HasPolicyLicense => File.Exists(PolicyLicensePath);

    public void SaveRootKeyPair(byte[] privateKey32, byte[] publicKey32)
    {
        _dpapi.SaveProtectedFile(RootPrivateKeyPath, privateKey32, RootKeyPurpose);
        File.WriteAllBytes(RootPublicKeyPath, publicKey32);
        SavePinnedRootPublicKey(publicKey32);
    }

    public byte[]? LoadRootPrivateKey() => _dpapi.LoadProtectedFile(RootPrivateKeyPath, RootKeyPurpose);

    public void SaveDeviceKeyPair(byte[] privateKey32, byte[] publicKey32)
    {
        _dpapi.SaveProtectedFile(DevicePrivateKeyPath, privateKey32, DeviceKeyPurpose);
        File.WriteAllBytes(DevicePublicKeyPath, publicKey32);
    }

    public byte[]? LoadDevicePrivateKey() => _dpapi.LoadProtectedFile(DevicePrivateKeyPath, DeviceKeyPurpose);

    public byte[]? LoadDevicePublicKey() =>
        File.Exists(DevicePublicKeyPath) ? File.ReadAllBytes(DevicePublicKeyPath) : null;

    public void SavePinnedRootPublicKey(byte[] rootPublicKey32)
    {
        Directory.CreateDirectory(_paths.KeysDirectory);
        File.WriteAllBytes(PinnedRootPublicKeyPath, rootPublicKey32);
    }

    public byte[]? LoadPinnedRootPublicKey()
    {
        if (File.Exists(PinnedRootPublicKeyPath))
        {
            return File.ReadAllBytes(PinnedRootPublicKeyPath);
        }

        return File.Exists(RootPublicKeyPath) ? File.ReadAllBytes(RootPublicKeyPath) : null;
    }

    public void SavePinnedCompanyEncryptionPublicKey(byte[] companyEncPub32)
    {
        Directory.CreateDirectory(_paths.KeysDirectory);
        File.WriteAllBytes(PinnedCompanyEncryptionPublicKeyPath, companyEncPub32);
    }

    public byte[]? LoadPinnedCompanyEncryptionPublicKey() =>
        File.Exists(PinnedCompanyEncryptionPublicKeyPath)
            ? File.ReadAllBytes(PinnedCompanyEncryptionPublicKeyPath)
            : null;

    public void SavePolicyLicenseJson(string json)
    {
        Directory.CreateDirectory(_paths.KeysDirectory);
        File.WriteAllText(PolicyLicensePath, json);
    }

    public string? LoadPolicyLicenseJson() =>
        File.Exists(PolicyLicensePath) ? File.ReadAllText(PolicyLicensePath) : null;
}
