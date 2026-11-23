using System.IO;
using System.Security.Cryptography;
using System.Text;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent;
using EzCompany.Core.LanAgent.Pairing;
using EzCompany.Core.Telemetry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EzCompany.Tests;

public sealed class Milestone3TelemetryTests : IAsyncLifetime
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "EzCompanyTests_M3_" + Guid.NewGuid().ToString("N"));
    private readonly List<IAsyncDisposable> _disposables = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (IAsyncDisposable d in _disposables)
        {
            await d.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("chrome", "Vietcombank Internet Banking - Chuyển tiền")]
    [InlineData("msedge", "Techcombank OTP Verification")]
    [InlineData("chrome", "MBBank Đăng nhập Ngân hàng")]
    [InlineData("firefox", "Quản lý Mật khẩu Bitwarden")]
    [InlineData("msedge", "Reset Password - InPrivate")]
    [InlineData("Zalo", "Nhắn tin nội bộ")]
    [InlineData("chrome", "Messenger - Facebook")]
    [InlineData("chrome", "Tab ẩn danh - Incognito")]
    public void PrivacySanitizer_MasksSensitiveWindowTitles(string processName, string windowTitle)
    {
        string sanitized = PrivacySanitizer.SanitizeWindowTitle(processName, windowTitle);
        Assert.Equal(PrivacySanitizer.RedactedPlaceholder, sanitized);
    }

    [Fact]
    public void PrivacySanitizer_PreservesNormalWorkTitles()
    {
        string title = "Hop_dong_lao_dong_2026.docx - Microsoft Word";
        Assert.Equal(title, PrivacySanitizer.SanitizeWindowTitle("WINWORD", title));
    }

    [Fact]
    public async Task SnapshotEncryption_EmployeeCannotDecrypt_BothDirectorsDecryptIdentically()
    {
        (ServiceProvider dir1Sp, LanAgentHost dir1Host, string dir1Endpoint) = await StartNodeAsync("dir1_e2e", isDirector: true);
        (ServiceProvider empSp, LanAgentHost empHost, string empEndpoint) = await StartNodeAsync("emp_e2e", isDirector: false);

        await PairAsync(dir1Sp, dir1Endpoint, empSp, "Staff E2E", "staffe2e", "Pass123!");

        // Employee authenticates so session context has UserId/Username
        var empAuth = await empSp.GetRequiredService<GenesisBootstrapService>()
            .AuthenticateOfflineAsync("staffe2e", "Pass123!");
        Assert.True(empAuth.Succeeded);

        // Employee captures and seals snapshot using ONLY CompanyEncryptionPublicKey
        byte[] fakeThumbPlain = Encoding.UTF8.GetBytes("JPEG_THUMB_320_BYTES_" + new string('A', 64));
        byte[] fakeFullPlain = Encoding.UTF8.GetBytes("JPEG_FULL_1280_BYTES_" + new string('B', 256));

        var snapshotSvc = empSp.GetRequiredService<WindowSnapshotService>();
        Guid snapId = await snapshotSvc.EncryptAndStoreSnapshotAsync(
            (byte[])fakeFullPlain.Clone(),
            (byte[])fakeThumbPlain.Clone(),
            1280,
            720);

        // Employee machine must NOT have RootPrivateKey
        Assert.False(empSp.GetRequiredService<LocalKeyStore>().HasRootPrivateKey);

        // Random wrong key must fail to decrypt
        var ecies = empSp.GetRequiredService<EciesCryptoService>();
        byte[] wrongPriv = RandomNumberGenerator.GetBytes(32);
        await using (TelemetryDbContext empTelDb = await empSp.GetRequiredService<IDbContextFactory<TelemetryDbContext>>().CreateDbContextAsync())
        {
            EncryptedSnapshotEntity stored = await empTelDb.EncryptedSnapshots.FirstAsync(x => x.Id == snapId);
            Assert.ThrowsAny<CryptographicException>(() => ecies.Decrypt(wrongPriv, stored.FullImageEncryptedBlob));
        }

        // Director 1 pulls delta -> receives thumbnail ONLY -> decrypts thumbnail
        var dir1Pull = dir1Sp.GetRequiredService<DirectorTelemetryPullService>();
        string empMachineId = empSp.GetRequiredService<ILocalStoragePaths>().MachineId;
        await dir1Pull.PullFromPeerAsync(empMachineId, empEndpoint);

        await using (TelemetryDbContext dir1TelDb = await dir1Sp.GetRequiredService<IDbContextFactory<TelemetryDbContext>>().CreateDbContextAsync())
        {
            EncryptedSnapshotEntity cached = await dir1TelDb.EncryptedSnapshots.FirstAsync(x => x.Id == snapId);
            Assert.NotEmpty(cached.ThumbnailEncryptedBlob);
            Assert.Empty(cached.FullImageEncryptedBlob);
            Assert.False(cached.IsFullDownloadedOnDirector);

            byte[] decryptedThumb1 = dir1Pull.DecryptBlobWithDirectorKey(cached.ThumbnailEncryptedBlob);
            Assert.Equal(fakeThumbPlain, decryptedThumb1);
        }

        // Director 1 clicks snapshot -> pulls full 1280px blob on-demand and decrypts
        byte[] decryptedFull1 = await dir1Pull.GetOrFetchDecryptedFullSnapshotAsync(snapId);
        Assert.Equal(fakeFullPlain, decryptedFull1);

        // Director 2 restored via .recovery must also decrypt the exact same blobs identically
        ServiceProvider dir2Sp = BuildNode("dir2_e2e");
        _disposables.Add(dir2Sp);
        await MigrateAsync(dir2Sp);
        await dir2Sp.GetRequiredService<GenesisBootstrapService>().RestoreSecondaryDirectorAsync(
            Path.Combine(_tempRoot, "dir1_e2e.recovery"),
            "Recovery123!",
            "Director Two",
            "director2",
            "DirPass2!");

        var dir2Pull = dir2Sp.GetRequiredService<DirectorTelemetryPullService>();
        await dir2Pull.PullFromPeerAsync(empMachineId, empEndpoint);
        byte[] decryptedFull2 = await dir2Pull.GetOrFetchDecryptedFullSnapshotAsync(snapId, empEndpoint);
        Assert.Equal(fakeFullPlain, decryptedFull2);

        await dir1Host.StopAsync(CancellationToken.None);
        await empHost.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TelemetryRetentionService_PrunesRecordsOlderThanSevenDays_AndEnforcesByteCap()
    {
        var now = new DateTimeOffset(2026, 11, 23, 15, 0, 0, TimeSpan.Zero);
        var time = new MutableTimeProvider(now);
        ServiceProvider sp = BuildNode("retention_test", time);
        _disposables.Add(sp);
        await MigrateAsync(sp);

        IDbContextFactory<TelemetryDbContext> telFactory = sp.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        Guid oldSnapId = Guid.NewGuid();
        Guid recentSnapId1 = Guid.NewGuid();
        Guid recentSnapId2 = Guid.NewGuid();

        await using (TelemetryDbContext db = await telFactory.CreateDbContextAsync())
        {
            db.EncryptedSnapshots.AddRange(
                new EncryptedSnapshotEntity
                {
                    Id = oldSnapId,
                    MachineId = "M1",
                    UserId = Guid.NewGuid(),
                    Username = "u1",
                    CapturedAtUtc = now.AddDays(-8),
                    Width = 1280,
                    Height = 720,
                    ThumbnailEncryptedBlob = new byte[100],
                    FullImageEncryptedBlob = new byte[5000],
                    ThumbnailSizeBytes = 100,
                    FullImageSizeBytes = 5000
                },
                new EncryptedSnapshotEntity
                {
                    Id = recentSnapId1,
                    MachineId = "M1",
                    UserId = Guid.NewGuid(),
                    Username = "u1",
                    CapturedAtUtc = now.AddDays(-2),
                    Width = 1280,
                    Height = 720,
                    ThumbnailEncryptedBlob = new byte[100],
                    FullImageEncryptedBlob = new byte[50_000],
                    ThumbnailSizeBytes = 100,
                    FullImageSizeBytes = 50_000
                },
                new EncryptedSnapshotEntity
                {
                    Id = recentSnapId2,
                    MachineId = "M1",
                    UserId = Guid.NewGuid(),
                    Username = "u1",
                    CapturedAtUtc = now.AddHours(-1),
                    Width = 1280,
                    Height = 720,
                    ThumbnailEncryptedBlob = new byte[100],
                    FullImageEncryptedBlob = new byte[50_000],
                    ThumbnailSizeBytes = 100,
                    FullImageSizeBytes = 50_000
                });

            db.AppUsageRecords.Add(new AppUsageRecordEntity
            {
                Id = Guid.NewGuid(),
                MachineId = "M1",
                UserId = Guid.NewGuid(),
                Username = "u1",
                DateKey = "2026-11-10",
                ProcessName = "WINWORD",
                SanitizedWindowTitle = "Old Doc",
                ActiveSeconds = 60,
                LastSeenUtc = now.AddDays(-9)
            });

            await db.SaveChangesAsync();
        }

        var retention = sp.GetRequiredService<TelemetryRetentionService>();
        int prunedByAge = await retention.RunCleanupAsync(retentionDays: 7, maxBytes: 200L * 1024 * 1024);
        Assert.Equal(2, prunedByAge); // 1 old snapshot + 1 old app usage

        // Now enforce tight FIFO size cap -> deletes older recentSnapId1 while keeping newest recentSnapId2
        int prunedByCap = await retention.RunCleanupAsync(retentionDays: 7, maxBytes: 75_000);
        Assert.True(prunedByCap >= 1);

        await using (TelemetryDbContext db = await telFactory.CreateDbContextAsync())
        {
            List<Guid> remainingIds = await db.EncryptedSnapshots.Select(x => x.Id).ToListAsync();
            Assert.DoesNotContain(oldSnapId, remainingIds);
            Assert.Contains(recentSnapId2, remainingIds);
        }
    }

    private async Task<(ServiceProvider Sp, LanAgentHost Host, string Endpoint)> StartNodeAsync(string profile, bool isDirector)
    {
        ServiceProvider sp = BuildNode(profile);
        _disposables.Add(sp);
        await MigrateAsync(sp);

        if (isDirector)
        {
            await sp.GetRequiredService<GenesisBootstrapService>().InitializeGenesisDirectorAsync(
                "Sample Co",
                "0301234567",
                "Director One",
                "director1",
                "DirPass1!",
                "Recovery123!",
                Path.Combine(_tempRoot, $"{profile}.recovery"));
        }

        var host = sp.GetRequiredService<LanAgentHost>();
        await host.StartAsync(CancellationToken.None);
        return (sp, host, $"http://127.0.0.1:{host.BoundPort}");
    }

    private static async Task PairAsync(
        ServiceProvider dirSp,
        string dirEndpoint,
        ServiceProvider empSp,
        string fullName,
        string username,
        string password)
    {
        var mgr = dirSp.GetRequiredService<PairingSessionManager>();
        mgr.OpenPairingWindow(TimeSpan.FromMinutes(2));

        var client = empSp.GetRequiredService<LanPairingClient>();
        StartedPairingSession started = await client.InitiatePairingAsync(dirEndpoint, fullName, username);

        _ = await mgr.ApproveRequestAsync(
            started.RequestId,
            CompanyRoles.Staff,
            [ModuleIds.OfficePdfStudio],
            "0.1.0",
            "stable",
            "Director One");

        PairingStatusResponse final = await client.WaitAndCompleteOnboardingAsync(
            dirEndpoint,
            started,
            fullName,
            username,
            password,
            TimeSpan.FromSeconds(10));

        Assert.Equal(PairingRequestState.Approved, final.State);
    }

    private ServiceProvider BuildNode(string profile, TimeProvider? timeProvider = null)
    {
        var paths = new LocalStoragePaths(profile, _tempRoot);
        var services = new ServiceCollection();
        services.AddSingleton<ILocalStoragePaths>(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddLogging();
        services.AddEzCrypto();
        if (timeProvider is not null)
        {
            services.AddSingleton<TimeProvider>(timeProvider);
        }

        services.AddEzData(paths);
        services.AddEzLanAgent(bindLoopbackOnly: true);
        services.AddEzTelemetry();
        return services.BuildServiceProvider();
    }

    private static async Task MigrateAsync(IServiceProvider sp)
    {
        await using AppDbContext appDb = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        await appDb.Database.MigrateAsync();
        await using TelemetryDbContext telDb = await sp.GetRequiredService<IDbContextFactory<TelemetryDbContext>>().CreateDbContextAsync();
        await telDb.Database.MigrateAsync();
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
