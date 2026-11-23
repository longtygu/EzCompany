using System.IO;
using System.Text.RegularExpressions;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Updater;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EzCompany.Tests;

public sealed class Milestone5UpdaterAndCiTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "EzCompanyTests_M5_" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("0.1.0", "0.2.0", false, "0.2.0", "stable", true, UpdateApprovalStatus.Allowed)]
    [InlineData("0.1.0", "0.2.1", false, "0.2.0", "stable", false, UpdateApprovalStatus.BlockedExceedsMaxApproved)]
    [InlineData("0.1.0", "0.2.0-beta.1", false, "0.2.0", "stable", false, UpdateApprovalStatus.BlockedPrereleaseChannel)]
    [InlineData("0.1.0", "0.2.0-beta.1", false, "0.2.0", "beta", true, UpdateApprovalStatus.Allowed)]
    [InlineData("0.1.0", "0.9.0", true, "0.1.0", "stable", true, UpdateApprovalStatus.Allowed)]
    [InlineData("0.2.0", "0.2.0", false, "0.5.0", "stable", false, UpdateApprovalStatus.UpToDate)]
    public void UpdatePolicyGate_EnforcesSemVerMaxApprovedVersionAndDirectorBypass(
        string current,
        string candidate,
        bool isDirector,
        string maxApproved,
        string channel,
        bool expectedAllowed,
        UpdateApprovalStatus expectedStatus)
    {
        var gate = new UpdatePolicyGate();
        UpdateApprovalDecision decision = gate.Evaluate(current, candidate, isDirector, maxApproved, channel);

        Assert.Equal(expectedAllowed, decision.IsAllowed);
        Assert.Equal(expectedStatus, decision.Status);
    }

    [Fact]
    public async Task SafeMigrationService_CreatesVerifiedBackup_AndRollsBackByteForByteOnFailure()
    {
        var paths = new LocalStoragePaths("mig_m5", _tempRoot);
        var services = new ServiceCollection();
        services.AddSingleton<ILocalStoragePaths>(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddEzCrypto();
        services.AddEzData(paths);
        services.AddSingleton<SafeMigrationService>();

        using ServiceProvider sp = services.BuildServiceProvider();
        var migrationSvc = sp.GetRequiredService<SafeMigrationService>();

        // 1. Clean initial migration
        SafeMigrationResult initial = await migrationSvc.MigrateAllSafelyAsync();
        Assert.True(initial.Succeeded);
        Assert.False(initial.RolledBack);

        // Seed pre-migration business data via Genesis Bootstrap
        var bootstrap = sp.GetRequiredService<GenesisBootstrapService>();
        await bootstrap.InitializeGenesisDirectorAsync(
            "PreMigration Co",
            "0309998888",
            "Admin One",
            "admin1",
            "Pass123!",
            "Recovery123!",
            Path.Combine(_tempRoot, "pre_mig.recovery"));

        // 2. Simulate failed migration that mutates app.db before throwing -> must rollback cleanly
        SafeMigrationResult failed = await migrationSvc.MigrateAllSafelyAsync(async (appDb, _) =>
        {
            await appDb.Database.ExecuteSqlRawAsync("DELETE FROM CompanyProfiles;");
            throw new InvalidOperationException("Simulated schema migration crash!");
        });

        Assert.False(failed.Succeeded);
        Assert.True(failed.RolledBack);
        Assert.NotNull(failed.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(failed.BackupDirectory, "manifest.json")));

        // Verify original CompanyProfile row was restored byte-for-byte
        await using AppDbContext verifyDb = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var restoredCompany = await verifyDb.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync();
        Assert.NotNull(restoredCompany);
        Assert.Equal("PreMigration Co", restoredCompany.CompanyName);
        Assert.Equal("0309998888", restoredCompany.TaxCode);
    }

    [Fact]
    public void RepositoryGovernanceAndWorkflows_MeetStrictOpenSourceAndNoPlaceholderRules()
    {
        string repoRoot = FindRepoRoot();

        string license = File.ReadAllText(Path.Combine(repoRoot, "LICENSE"));
        Assert.Contains("Mozilla Public License Version 2.0", license);

        string codeowners = File.ReadAllText(Path.Combine(repoRoot, ".github", "CODEOWNERS"));
        Assert.Contains("/src/EzCompany.Core.Crypto/", codeowners);
        Assert.Contains("/src/EzCompany.Core.LanAgent/", codeowners);
        Assert.Contains("/src/EzCompany.Core.Updater/", codeowners);
        Assert.Contains("/src/EzCompany.Core.Telemetry/", codeowners);

        string ciYaml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "ci-pr.yml"));
        Assert.Contains("contents: read", ciYaml);
        Assert.Contains("windows-latest", ciYaml);

        string releaseYaml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"));
        Assert.Contains("v*.*.*", releaseYaml);
        Assert.Contains("contents: write", releaseYaml);
        Assert.Contains("id-token: write", releaseYaml);
        Assert.Contains("attestations: write", releaseYaml);
        Assert.Contains("vpk pack", releaseYaml);
        Assert.Contains("SHA256SUMS.txt", releaseYaml);
        Assert.Contains("actions/attest-build-provenance", releaseYaml);

        // Ensure zero TODO comments or NotImplementedException exist in production src/
        string srcRoot = Path.Combine(repoRoot, "src");
        foreach (string csFile in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (csFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                csFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            string content = File.ReadAllText(csFile);
        Assert.DoesNotContain("NotImplementedException", content);
        Assert.False(Regex.IsMatch(content, @"//\s*TODO\b", RegexOptions.IgnoreCase), $"Found TODO placeholder in {csFile}");
        }
    }

    [Fact]
    public void RollingFileLoggerProvider_WritesFormattedEntriesAndExceptions_WithoutLockingFile()
    {
        var paths = new LocalStoragePaths("log_test", _tempRoot);
        string logFilePath;
        using (var provider = new RollingFileLoggerProvider(paths, LogLevel.Debug))
        {
            logFilePath = provider.CurrentLogFilePath;
            ILogger logger = provider.CreateLogger("EzCompany.TestCategory");
            logger.LogInformation("Startup test message for {Profile}", paths.ProfileName);
            logger.LogError(new InvalidOperationException("Boom test error"), "Captured error event");
            provider.Flush();

            // Shared read access must allow tailing while the logger is still open
            using var readStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(readStream);
            string liveContent = reader.ReadToEnd();
            Assert.Contains("[INF] [profile:log_test]", liveContent);
            Assert.Contains("Startup test message for log_test", liveContent);
            Assert.Contains("[ERR] [profile:log_test]", liveContent);
            Assert.Contains("InvalidOperationException: Boom test error", liveContent);
        }

        Assert.True(File.Exists(logFilePath));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EzCompany.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate EzCompany.sln from test base directory.");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
