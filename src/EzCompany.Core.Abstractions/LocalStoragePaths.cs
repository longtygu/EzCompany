using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace EzCompany.Core.Abstractions;

/// <summary>
/// Default profile-aware local storage layout under <c>%LocalAppData%\EzCompany\Profiles\&lt;profile&gt;\</c>.
/// </summary>
public sealed class LocalStoragePaths : ILocalStoragePaths
{
    public LocalStoragePaths(string? profileName = null, string? customBaseRoot = null)
    {
        ProfileName = SanitizeProfileName(profileName);

        string baseRoot = string.IsNullOrWhiteSpace(customBaseRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EzCompany",
                "Profiles")
            : Path.GetFullPath(customBaseRoot);

        ProfileRoot = Path.Combine(baseRoot, ProfileName);
        DataDirectory = Path.Combine(ProfileRoot, "Data");
        KeysDirectory = Path.Combine(ProfileRoot, "Keys");
        VaultDirectory = Path.Combine(ProfileRoot, "Vault");
        DirectorCacheDirectory = Path.Combine(ProfileRoot, "DirectorCache");
        BackupsDirectory = Path.Combine(ProfileRoot, "Backups");
        TempDirectory = Path.Combine(ProfileRoot, "Temp");
        LogsDirectory = Path.Combine(ProfileRoot, "Logs");

        AppDbPath = Path.Combine(DataDirectory, "app.db");
        TelemetryDbPath = Path.Combine(DataDirectory, "telemetry.db");

        MachineId = DeriveMachineId(ProfileName);
        EnsureDirectoriesExist();
    }

    public string ProfileName { get; }
    public string MachineId { get; }

    public string ProfileRoot { get; }
    public string DataDirectory { get; }
    public string KeysDirectory { get; }
    public string VaultDirectory { get; }
    public string DirectorCacheDirectory { get; }
    public string BackupsDirectory { get; }
    public string TempDirectory { get; }
    public string LogsDirectory { get; }

    public string AppDbPath { get; }
    public string TelemetryDbPath { get; }

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(ProfileRoot);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(KeysDirectory);
        Directory.CreateDirectory(VaultDirectory);
        Directory.CreateDirectory(Path.Combine(VaultDirectory, "Accounting", "Invoices"));
        Directory.CreateDirectory(Path.Combine(VaultDirectory, "Legal", "Contracts"));
        Directory.CreateDirectory(Path.Combine(VaultDirectory, "Office"));
        Directory.CreateDirectory(Path.Combine(VaultDirectory, "Tasks"));
        Directory.CreateDirectory(DirectorCacheDirectory);
        Directory.CreateDirectory(Path.Combine(DirectorCacheDirectory, "Snapshots"));
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public static string ParseProfileFromArgs(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return "default";
        }

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase))
            {
                return SanitizeProfileName(arg["--profile=".Length..]);
            }

            if (string.Equals(arg, "--profile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return SanitizeProfileName(args[i + 1]);
            }
        }

        return "default";
    }

    public static string SanitizeProfileName(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return "default";
        }

        var sb = new StringBuilder(profileName.Length);
        foreach (char c in profileName.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.Length > 0 ? sb.ToString() : "default";
    }

    private static string DeriveMachineId(string profileName)
    {
        if (!string.Equals(profileName, "default", StringComparison.OrdinalIgnoreCase))
        {
            string seed = $"ezcompany:profile:{Environment.MachineName}:{profileName.ToLowerInvariant()}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
            return $"EZ-{Convert.ToHexString(hash[..8])}";
        }

        string? windowsGuid = ReadWindowsMachineGuid();
        string nativeSeed = $"ezcompany:host:{windowsGuid ?? Environment.MachineName}:{Environment.MachineName}";
        byte[] nativeHash = SHA256.HashData(Encoding.UTF8.GetBytes(nativeSeed));
        return $"EZ-{Convert.ToHexString(nativeHash[..8])}";
    }

    private static string? ReadWindowsMachineGuid()
    {
        try
        {
            using RegistryKey? baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? cryptoKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return cryptoKey?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }
}
