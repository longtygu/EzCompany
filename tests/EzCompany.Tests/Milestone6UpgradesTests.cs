using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Pairing;
using EzCompany.Core.LanAgent.Recovery;
using EzCompany.Core.LanAgent.Tasks;
using EzCompany.Core.Updater;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EzCompany.Tests;

public sealed class Milestone6UpgradesTests : IAsyncLifetime
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "EzCompanyTests_M6_" + Guid.NewGuid().ToString("N"));
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
    [InlineData(2024, 2, 10, 1, 1, 2024, false, "Giáp Thìn")]
    [InlineData(2025, 1, 29, 1, 1, 2025, false, "Ất Tỵ")]
    [InlineData(2026, 2, 17, 1, 1, 2026, false, "Bính Ngọ")]
    [InlineData(2027, 2, 6, 1, 1, 2027, false, "Đinh Mùi")]
    [InlineData(2025, 7, 25, 1, 6, 2025, true, "Ất Tỵ")]
    public void VietnameseLunarCalendar_ConvertsKnownTetAndLeapDatesAccurately(
        int solarYear,
        int solarMonth,
        int solarDay,
        int expectedLunarDay,
        int expectedLunarMonth,
        int expectedLunarYear,
        bool expectedLeap,
        string expectedCanChi)
    {
        VietnameseLunarDate res = VietnameseLunarCalendar.ConvertSolar2Lunar(new DateOnly(solarYear, solarMonth, solarDay));

        Assert.Equal(expectedLunarDay, res.Day);
        Assert.Equal(expectedLunarMonth, res.Month);
        Assert.Equal(expectedLunarYear, res.Year);
        Assert.Equal(expectedLeap, res.IsLeapMonth);
        Assert.Equal(expectedCanChi, res.CanChiYear);
    }

    [Fact]
    public void AppearanceSettingsService_PersistsClampsAndResetsDefaults()
    {
        var paths = new LocalStoragePaths("ui_settings", _tempRoot);
        var svc1 = new AppearanceSettingsService(paths);

        AppearanceSettings applied = svc1.Apply(new AppearanceSettings
        {
            IsDarkMode = false,
            AccentColorHex = "107c10",
            BaseFontSize = 99,
            CardMaxWidth = 100,
            CardCornerRadius = 50,
            CardPadding = 2,
            SidebarWidth = 900,
            WindowOpacity = 0.25,
            BackdropType = "Acrylic"
        });

        Assert.False(applied.IsDarkMode);
        Assert.Equal("#107C10", applied.AccentColorHex);
        Assert.Equal(20.0, applied.BaseFontSize);
        Assert.Equal(640.0, applied.CardMaxWidth);
        Assert.Equal(20.0, applied.CardCornerRadius);
        Assert.Equal(8.0, applied.CardPadding);
        Assert.Equal(340.0, applied.SidebarWidth);
        Assert.Equal(0.70, applied.WindowOpacity);
        Assert.Equal("Acrylic", applied.BackdropType);

        var svc2 = new AppearanceSettingsService(paths);
        Assert.Equal("#107C10", svc2.Current.AccentColorHex);
        Assert.Equal("Acrylic", svc2.Current.BackdropType);
        Assert.Equal(0.70, svc2.Current.WindowOpacity);

        AppearanceSettings reset = svc2.ResetToDefaults();
        Assert.True(reset.IsDarkMode);
        Assert.Equal("#0078D4", reset.AccentColorHex);
        Assert.Equal(1.0, reset.WindowOpacity);
        Assert.Equal("Mica", reset.BackdropType);
    }

    [Fact]
    public async Task AccountCredentialsChange_AndDirectorRecoveryKitReset_WorkEndToEnd()
    {
        (ServiceProvider dirSp, _, _) = await StartNodeAsync("dir_acct", isDirector: true);
        var bootstrap = dirSp.GetRequiredService<GenesisBootstrapService>();
        LocalUserEntity initialUser = (await bootstrap.GetPrimaryLocalUserAsync())!;

        // 1. Change full name, username and password
        LocalUserEntity changed = await bootstrap.ChangeCredentialsAsync(
            initialUser.UserId,
            "AdminPass123!",
            "Giám đốc Cập nhật",
            "director_renamed",
            "NewStrongPass99!");

        Assert.Equal("Giám đốc Cập nhật", changed.FullName);
        Assert.Equal("director_renamed", changed.Username);

        AuthenticationResult oldFail = await bootstrap.AuthenticateOfflineAsync("director_renamed", "AdminPass123!");
        Assert.False(oldFail.Succeeded);

        AuthenticationResult newOk = await bootstrap.AuthenticateOfflineAsync("director_renamed", "NewStrongPass99!");
        Assert.True(newOk.Succeeded);

        // 2. Wrong .recovery kit (different company) must be rejected
        string foreignKitPath = Path.Combine(_tempRoot, "foreign.recovery");
        var ed = dirSp.GetRequiredService<Ed25519Service>();
        var kitSvc = dirSp.GetRequiredService<RecoveryKitService>();
        Ed25519KeyPair foreignKeys = ed.GenerateKeyPair();
        await kitSvc.ExportToFileAsync(
            foreignKitPath,
            new RecoveryKitPayload(
                Guid.NewGuid(),
                "Foreign Corp",
                "9999999999",
                Convert.ToBase64String(foreignKeys.PrivateKey),
                Convert.ToBase64String(foreignKeys.PublicKey),
                DateTimeOffset.UtcNow,
                "Foreign"),
            "ForeignPass123!");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bootstrap.RecoverDirectorAccountWithRecoveryKitAsync(
                foreignKitPath,
                "ForeignPass123!",
                "director_hacked",
                "RecoveredPass123!"));

        // 3. Genuine .recovery kit resets forgotten Director password
        string validKitPath = Path.Combine(_tempRoot, "dir_acct_master.recovery");
        LocalUserEntity recovered = await bootstrap.RecoverDirectorAccountWithRecoveryKitAsync(
            validKitPath,
            "RecoveryPass123!",
            "director_restored",
            "RecoveredPass456!");

        Assert.Equal("director_restored", recovered.Username);
        AuthenticationResult recoveredAuth = await bootstrap.AuthenticateOfflineAsync("director_restored", "RecoveredPass456!");
        Assert.True(recoveredAuth.Succeeded);
    }

    [Fact]
    public async Task EmployeePasswordReset_OverLoopbackLan_RequiresDeviceAuthAndRootSignedTicket()
    {
        (ServiceProvider dirSp, _, string dirEndpoint) = await StartNodeAsync("dir_reset", isDirector: true);
        (ServiceProvider empSp, _, _) = await StartNodeAsync("emp_reset", isDirector: false);

        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Nhân viên Quên MK", "empforgot", "OldPass123!", CompanyRoles.Staff);

        // Unsigned request must be rejected with 401
        using (var http = new HttpClient())
        {
            using HttpResponseMessage unsignedResp = await http.PostAsync(
                $"{dirEndpoint}/api/v1/account-recovery/request",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Unauthorized, unsignedResp.StatusCode);
        }

        // Signed request submitted by Employee
        var empRecoveryClient = empSp.GetRequiredService<LanAccountRecoveryClient>();
        AccountResetInitiateResponseDto initiated = await empRecoveryClient.SubmitResetRequestAsync(
            dirEndpoint,
            "empnewname",
            "Quên mật khẩu máy trạm");

        Assert.Equal("Pending", initiated.State);

        var dirRecoveryMgr = dirSp.GetRequiredService<AccountRecoveryManager>();
        IReadOnlyList<PasswordResetRequestEntity> pending = await dirRecoveryMgr.GetPendingRequestsAsync();
        Assert.Contains(pending, r => r.RequestId == initiated.RequestId && r.RequestedNewUsername == "empnewname");

        // Director approves & issues Root-signed ticket; Employee applies new password locally
        _ = await dirRecoveryMgr.ApproveRequestAsync(initiated.RequestId, "Giám đốc Duyệt");

        (AccountResetStatusResponseDto finalStatus, LocalUserEntity? updatedUser) = await empRecoveryClient
            .WaitAndApplyResetAsync(dirEndpoint, initiated.RequestId, "BrandNewLocalPass99!", TimeSpan.FromSeconds(5));

        Assert.Equal("Approved", finalStatus.State);
        Assert.NotNull(updatedUser);
        Assert.Equal("empnewname", updatedUser.Username);

        var empBootstrap = empSp.GetRequiredService<GenesisBootstrapService>();
        Assert.False((await empBootstrap.AuthenticateOfflineAsync("empnewname", "OldPass123!")).Succeeded);
        Assert.True((await empBootstrap.AuthenticateOfflineAsync("empnewname", "BrandNewLocalPass99!")).Succeeded);
    }

    [Fact]
    public async Task PeerToPeerTaskAssignment_AndHandover_WorksEmployeeToEmployeeAndOfflineQueue()
    {
        (ServiceProvider dirSp, _, string dirEndpoint) = await StartNodeAsync("dir_tasks", isDirector: true);
        (ServiceProvider empASp, _, string empAEndpoint) = await StartNodeAsync("empA_tasks", isDirector: false);
        (ServiceProvider empBSp, _, string empBEndpoint) = await StartNodeAsync("empB_tasks", isDirector: false);

        await PairNodesAsync(dirSp, dirEndpoint, empASp, "Nguyễn Văn A", "emp_a", "PassA123!", CompanyRoles.Accounting);
        await PairNodesAsync(dirSp, dirEndpoint, empBSp, "Trần Thị B", "emp_b", "PassB123!", CompanyRoles.Legal);

        var taskSvcA = empASp.GetRequiredService<WorkTaskService>();
        var taskSvcB = empBSp.GetRequiredService<WorkTaskService>();
        var syncA = empASp.GetRequiredService<PeerTaskSyncService>();
        var syncB = empBSp.GetRequiredService<PeerTaskSyncService>();
        string empAMachine = empASp.GetRequiredService<ILocalStoragePaths>().MachineId;
        string empBMachine = empBSp.GetRequiredService<ILocalStoragePaths>().MachineId;

        // 1. Employee A creates today's self-task & toggles completion -> check progress metrics
        WorkTaskEntity selfTask = await taskSvcA.CreateSelfTaskAsync("Đối chiếu sao kê", "Kiểm tra hôm nay", WorkTaskPriorities.High, null);
        Assert.Single(await taskSvcA.GetTodaySelfTasksAsync());
        await taskSvcA.ToggleSelfTaskCompleteAsync(selfTask.Id);
        WorkTaskProgressSummary summaryAfterSelf = await taskSvcA.GetProgressSummaryAsync();
        Assert.Equal(1, summaryAfterSelf.CompletedCount);
        Assert.Equal(100.0, summaryAfterSelf.CompletionPercent);

        // 2. Employee A assigns a task directly to Employee B (Employee-to-Employee over P2P LAN!)
        WorkTaskEntity assignedTask = await taskSvcA.CreateAssignedTaskAsync(
            empBMachine,
            "emp_b",
            "Trần Thị B",
            CompanyRoles.Legal,
            "Rà soát phụ lục hợp đồng 09",
            "Kiểm tra điều khoản phạt chậm tiến độ",
            WorkTaskPriorities.Urgent,
            DateTimeOffset.UtcNow.AddDays(1));

        int syncedAssign = await syncA.FlushPendingTasksOnceAsync(
            new Dictionary<string, string> { [empBMachine] = empBEndpoint });
        Assert.Equal(1, syncedAssign);

        IReadOnlyList<WorkTaskEntity> bInbox = await taskSvcB.GetAssignedToMeTasksAsync();
        Assert.Contains(bInbox, t => t.Id == assignedTask.Id && t.Title == "Rà soát phụ lục hợp đồng 09");

        // 3. Employee B attaches result file and submits handover -> synced back to Employee A's Vault
        string sampleAttachment = Path.Combine(_tempRoot, "ket_qua_ra_soat.txt");
        await File.WriteAllTextAsync(sampleAttachment, "Đã rà soát xong các điều khoản pháp lý.");
        string expectedSha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sampleAttachment))).ToLowerInvariant();

        await taskSvcB.SubmitHandoverAsync(assignedTask.Id, "Hoàn tất rà soát điều khoản 4 và 5.", sampleAttachment);
        int syncedHandover = await syncB.FlushPendingTasksOnceAsync(
            new Dictionary<string, string> { [empAMachine] = empAEndpoint });
        Assert.Equal(1, syncedHandover);

        IReadOnlyList<WorkTaskEntity> aOutbox = await taskSvcA.GetAssignedByMeTasksAsync();
        WorkTaskEntity aReceivedHandover = Assert.Single(aOutbox, t => t.Id == assignedTask.Id);
        Assert.Equal(WorkTaskStatuses.HandedOver, aReceivedHandover.Status);
        Assert.Equal(expectedSha, aReceivedHandover.HandoverFileSha256);
        Assert.NotNull(aReceivedHandover.HandoverVaultPath);

        string aVaultFile = Path.Combine(
            empASp.GetRequiredService<ILocalStoragePaths>().VaultDirectory,
            aReceivedHandover.HandoverVaultPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(aVaultFile));

        // 4. Employee A confirms completion -> synced back to B & appears in completed history on both sides
        await taskSvcA.ReviewHandoverAsync(assignedTask.Id, approveComplete: true, "Rất tốt, cảm ơn B.");
        int syncedComplete = await syncA.FlushPendingTasksOnceAsync(
            new Dictionary<string, string> { [empBMachine] = empBEndpoint });
        Assert.Equal(1, syncedComplete);

        Assert.Contains(await taskSvcB.GetCompletedTasksHistoryAsync(), t => t.Id == assignedTask.Id);
        Assert.Contains(await taskSvcA.GetCompletedTasksHistoryAsync(), t => t.Id == assignedTask.Id);
        Assert.True((await taskSvcA.GetTaskHistoriesAsync(assignedTask.Id)).Count >= 3);
    }

    [Fact]
    public async Task SafeMigrationService_UpgradesExistingV1DatabaseToV2WithBackup()
    {
        var paths = new LocalStoragePaths("mig_v1_to_v2", _tempRoot);
        var services = new ServiceCollection();
        services.AddSingleton<ILocalStoragePaths>(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddEzCrypto();
        services.AddEzData(paths);
        services.AddSingleton<SafeMigrationService>();

        using ServiceProvider sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();

        // Create database ONLY up to v1 migration
        await using (AppDbContext v1Db = await dbFactory.CreateDbContextAsync())
        {
            await v1Db.GetService<IMigrator>().MigrateAsync("20261123000001_InitialAppSchema");
            v1Db.CompanyProfiles.Add(new CompanyProfileEntity
            {
                CompanyId = Guid.NewGuid(),
                CompanyName = "Legacy V1 Co",
                TaxCode = "0301112223",
                RootPublicKeyBase64 = Convert.ToBase64String(new byte[32]),
                CompanyEncryptionPublicKeyBase64 = Convert.ToBase64String(new byte[32]),
                IsDirectorNode = true,
                IsGenesisCompleted = true,
                AppSchemaVersion = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await v1Db.SaveChangesAsync();
        }

        // Upgrade via SafeMigrationService -> should backup v1, migrate to v2, and bump AppSchemaVersion=2
        SafeMigrationResult result = await sp.GetRequiredService<SafeMigrationService>().MigrateAllSafelyAsync();
        Assert.True(result.Succeeded);
        Assert.NotNull(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory, "manifest.json")));

        await using AppDbContext v2Db = await dbFactory.CreateDbContextAsync();
        CompanyProfileEntity upgraded = await v2Db.CompanyProfiles.AsNoTracking().FirstAsync();
        Assert.Equal(GenesisBootstrapService.CurrentAppSchemaVersion, upgraded.AppSchemaVersion);
        Assert.Empty(await v2Db.WorkTasks.AsNoTracking().ToListAsync());
        Assert.Empty(await v2Db.PasswordResetRequests.AsNoTracking().ToListAsync());
    }

    private async Task<(ServiceProvider Sp, LanAgentHost Host, string Endpoint)> StartNodeAsync(string profile, bool isDirector)
    {
        var paths = new LocalStoragePaths(profile, _tempRoot);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILocalStoragePaths>(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddEzCrypto();
        services.AddEzData(paths);
        services.AddEzLanAgent(bindLoopbackOnly: true);

        ServiceProvider sp = services.BuildServiceProvider();
        _disposables.Add(sp);

        await using (AppDbContext appDb = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            await appDb.Database.MigrateAsync();
        }

        if (isDirector)
        {
            string recoveryPath = Path.Combine(_tempRoot, $"{profile}_master.recovery");
            await sp.GetRequiredService<GenesisBootstrapService>().InitializeGenesisDirectorAsync(
                "Ez Upgrade Corp",
                "0319998888",
                "Giám đốc Gốc",
                "director",
                "AdminPass123!",
                "RecoveryPass123!",
                recoveryPath);
        }

        var host = sp.GetRequiredService<LanAgentHost>();
        await host.StartAsync(CancellationToken.None);
        return (sp, host, $"http://127.0.0.1:{host.BoundPort}");
    }

    private static async Task PairNodesAsync(
        ServiceProvider dirSp,
        string dirEndpoint,
        ServiceProvider empSp,
        string fullName,
        string username,
        string localPassword,
        string role)
    {
        var pairingMgr = dirSp.GetRequiredService<PairingSessionManager>();
        pairingMgr.OpenPairingWindow(TimeSpan.FromMinutes(2));

        var empClient = empSp.GetRequiredService<LanPairingClient>();
        StartedPairingSession started = await empClient.InitiatePairingAsync(dirEndpoint, fullName, username);

        IReadOnlyList<string> mods = CompanyRoles.GetDefaultModulesForRole(role);
        await pairingMgr.ApproveRequestAsync(started.RequestId, role, mods, "0.2.0", "stable", "director");

        PairingStatusResponse final = await empClient.WaitAndCompleteOnboardingAsync(
            dirEndpoint,
            started,
            fullName,
            username,
            localPassword,
            TimeSpan.FromSeconds(10));

        Assert.Equal(PairingRequestState.Approved, final.State);
    }
}
