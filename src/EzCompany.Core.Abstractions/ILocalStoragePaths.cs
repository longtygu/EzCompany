namespace EzCompany.Core.Abstractions;

/// <summary>
/// Resolves isolated per-profile storage directories and deterministic machine identity.
/// Supports <c>--profile=&lt;name&gt;</c> for simulating multiple LAN nodes on a single Windows workstation.
/// </summary>
public interface ILocalStoragePaths
{
    string ProfileName { get; }
    string MachineId { get; }

    string ProfileRoot { get; }
    string DataDirectory { get; }
    string KeysDirectory { get; }
    string VaultDirectory { get; }
    string DirectorCacheDirectory { get; }
    string BackupsDirectory { get; }
    string TempDirectory { get; }
    string LogsDirectory { get; }

    string AppDbPath { get; }
    string TelemetryDbPath { get; }

    void EnsureDirectoriesExist();
}
