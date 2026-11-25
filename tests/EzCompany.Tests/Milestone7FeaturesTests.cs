using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using ClosedXML.Excel;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent;
using EzCompany.Core.LanAgent.Collaboration;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Pairing;
using EzCompany.Core.LanAgent.Tasks;
using EzCompany.Core.Updater;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EzCompany.Tests;

public sealed class Milestone7FeaturesTests : IAsyncLifetime
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "EzCompanyTests_M7_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void CompanyRoles_SupportsMultiRoleBilingualFormattingAndUnionModules()
    {
        IReadOnlyList<string> parsed = CompanyRoles.ParseRoles("Accounting, Legal, accounting");
        Assert.Equal([CompanyRoles.Accounting, CompanyRoles.Legal], parsed);

        string bilingual = CompanyRoles.FormatRolesBilingual("Accounting,Legal");
        Assert.Equal("Accounting (Kế toán) / Legal (Pháp chế)", bilingual);

        Assert.Equal("Manager (Giám đốc)", CompanyRoles.GetBilingualLabel(CompanyRoles.Director));
        Assert.Equal("HR (Nhân sự)", CompanyRoles.GetBilingualLabel(CompanyRoles.Hr));
        Assert.Equal("Staff (Nhân viên)", CompanyRoles.GetBilingualLabel(CompanyRoles.Staff));

        IReadOnlyList<string> mods = CompanyRoles.GetDefaultModulesForRole("Accounting,Legal");
        Assert.Contains(ModuleIds.AccountingInvoiceXml, mods);
        Assert.Contains(ModuleIds.LegalContractDiff, mods);
        Assert.Contains(ModuleIds.OfficePdfStudio, mods);
    }

    [Fact]
    public async Task SafeMigrationService_UpgradesV2DatabaseToV3WithBackup()
    {
        var paths = new LocalStoragePaths("mig_v2_to_v3", _tempRoot);
        var services = new ServiceCollection();
        services.AddSingleton<ILocalStoragePaths>(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddEzCrypto();
        services.AddEzData(paths);
        services.AddSingleton<SafeMigrationService>();

        using ServiceProvider sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using (AppDbContext v2Db = await dbFactory.CreateDbContextAsync())
        {
            await v2Db.GetService<IMigrator>().MigrateAsync("20261125000001_AddTasksAndAccountRecovery");
            long nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            await v2Db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO CompanyProfiles (CompanyId, CompanyName, TaxCode, RootPublicKeyBase64, CompanyEncryptionPublicKeyBase64, IsDirectorNode, IsGenesisCompleted, AppSchemaVersion, CreatedAtUtc, UpdatedAtUtc)
                  VALUES ({0}, {1}, {2}, {3}, {4}, 1, 1, 2, {5}, {5});",
                Guid.NewGuid(),
                "V2 Upgrade Corp",
                "0308889990",
                Convert.ToBase64String(new byte[32]),
                Convert.ToBase64String(new byte[32]),
                nowTicks);
        }

        SafeMigrationResult result = await sp.GetRequiredService<SafeMigrationService>().MigrateAllSafelyAsync();
        Assert.True(result.Succeeded);
        Assert.NotNull(result.BackupDirectory);

        await using AppDbContext v3Db = await dbFactory.CreateDbContextAsync();
        CompanyProfileEntity upgraded = await v3Db.CompanyProfiles.AsNoTracking().FirstAsync();
        Assert.Equal(GenesisBootstrapService.CurrentAppSchemaVersion, upgraded.AppSchemaVersion);
        Assert.Empty(await v3Db.ChatConversations.AsNoTracking().ToListAsync());
        Assert.Empty(await v3Db.TimelinePlans.AsNoTracking().ToListAsync());
        Assert.Empty(await v3Db.EmployeeProfiles.AsNoTracking().ToListAsync());
        Assert.Empty(await v3Db.AttendanceRecords.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CompanyLogo_PersistsAndSyncsFromDirectorToEmployeeOverLoopbackLan()
    {
        (ServiceProvider dirSp, _, string dirEndpoint) = await StartNodeAsync("dir_logo", isDirector: true);
        (ServiceProvider empSp, _, _) = await StartNodeAsync("emp_logo", isDirector: false);

        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Nhân viên Logo", "emplogo", "Pass123!", CompanyRoles.Staff);

        byte[] fakePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6];
        var dirBranding = dirSp.GetRequiredService<CompanyBrandingService>();
        await dirBranding.SaveCompanyLogoAsync(fakePng);

        var empCollabClient = empSp.GetRequiredService<LanCollaborationClient>();
        bool pulled = await empCollabClient.PullCompanyBrandingFromDirectorAsync(dirEndpoint);
        Assert.True(pulled);

        byte[]? empBytes = await empSp.GetRequiredService<CompanyBrandingService>().GetCompanyLogoBytesAsync();
        Assert.Equal(fakePng, empBytes);
    }

    [Fact]
    public async Task TaskAssignmentWithAttachment_NotifiesAssigneeAndTransfersVerifiedFile()
    {
        (ServiceProvider dirSp, _, string dirEndpoint) = await StartNodeAsync("dir_tattach", isDirector: true);
        (ServiceProvider empSp, _, string empEndpoint) = await StartNodeAsync("emp_tattach", isDirector: false);

        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Kế toán Giao việc", "acc_assign", "Pass123!", "Accounting,Legal");

        TaskAssignedNotificationEvent? receivedNotif = null;
        empSp.GetRequiredService<IEventBus>().Subscribe<TaskAssignedNotificationEvent>(evt => receivedNotif = evt);

        string briefPath = Path.Combine(_tempRoot, "tai_lieu_giao_viec.txt");
        await File.WriteAllTextAsync(briefPath, "Hướng dẫn đối chiếu hóa đơn tháng 11");
        string expectedSha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(briefPath))).ToLowerInvariant();

        string empMachine = empSp.GetRequiredService<ILocalStoragePaths>().MachineId;
        var dirTaskSvc = dirSp.GetRequiredService<WorkTaskService>();
        WorkTaskEntity task = await dirTaskSvc.CreateAssignedTaskAsync(
            empMachine,
            "acc_assign",
            "Kế toán Giao việc",
            "Accounting,Legal",
            "Kiểm tra chứng từ quý IV",
            "Xem tệp hướng dẫn đính kèm",
            WorkTaskPriorities.High,
            DateTimeOffset.UtcNow.AddDays(2),
            briefPath);

        int flushed = await dirSp.GetRequiredService<PeerTaskSyncService>()
            .FlushPendingTasksOnceAsync(new Dictionary<string, string> { [empMachine] = empEndpoint });
        Assert.Equal(1, flushed);

        Assert.NotNull(receivedNotif);
        Assert.Equal(task.Id, receivedNotif.TaskId);

        IReadOnlyList<WorkTaskEntity> empInbox = await empSp.GetRequiredService<WorkTaskService>().GetAssignedToMeTasksAsync();
        WorkTaskEntity delivered = Assert.Single(empInbox, t => t.Id == task.Id);
        Assert.Equal("tai_lieu_giao_viec.txt", delivered.AssignmentFileName);
        Assert.Equal(expectedSha, delivered.AssignmentFileSha256);
        Assert.NotNull(delivered.AssignmentVaultPath);

        string empVaultFile = Path.Combine(
            empSp.GetRequiredService<ILocalStoragePaths>().VaultDirectory,
            delivered.AssignmentVaultPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(empVaultFile));
    }

    [Fact]
    public async Task InternalChat_DirectAndGroup_PinnedTask_AndReverseHistoryRetrieval_WorkP2P()
    {
        (ServiceProvider dirSp, _, string dirEndpoint) = await StartNodeAsync("dir_chat", isDirector: true);
        (ServiceProvider empSp, _, string empEndpoint) = await StartNodeAsync("emp_chat", isDirector: false);

        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Nhân viên Chat", "empchat", "Pass123!", CompanyRoles.Staff);

        string dirMachine = dirSp.GetRequiredService<ILocalStoragePaths>().MachineId;
        string empMachine = empSp.GetRequiredService<ILocalStoragePaths>().MachineId;

        var dirChat = dirSp.GetRequiredService<ChatService>();
        var empChat = empSp.GetRequiredService<ChatService>();

        // Create a task and pin it inside Direct Conversation
        WorkTaskEntity task = await dirSp.GetRequiredService<WorkTaskService>().CreateAssignedTaskAsync(
            empMachine, "empchat", "Nhân viên Chat", CompanyRoles.Staff,
            "Chuẩn bị báo cáo tuần", "Chi tiết tuần 4", WorkTaskPriorities.Normal, null);

        ChatConversationEntity dmDir = await dirChat.GetOrCreateDirectConversationAsync(empMachine, "Nhân viên Chat");
        await dirChat.PinTaskToConversationAsync(dmDir.Id, task.Id, [empMachine]);

        // Send a message with attachment WITHOUT pushing first -> test reverse pull & attachment retrieval!
        string docPath = Path.Combine(_tempRoot, "chat_spec.txt");
        await File.WriteAllTextAsync(docPath, "Nội dung tệp đính kèm trong nhóm chat nội bộ.");
        ChatMessageEntity msgWithFile = await dirChat.SendMessageAsync(
            dmDir.Id,
            "Xem file đính kèm nhé",
            docPath,
            task.Id,
            [empMachine]);

        // Employee reverse-pulls chat history & attachment from Director endpoint
        int pulled = await empSp.GetRequiredService<LanCollaborationClient>()
            .PullChatHistoryFromPeerAsync(dirEndpoint);
        Assert.True(pulled >= 2);

        IReadOnlyList<ChatConversationEntity> empConvs = await empChat.GetConversationsAsync();
        ChatConversationEntity empDm = Assert.Single(empConvs, c => c.Id == dmDir.Id);
        Assert.Equal(task.Id, empDm.PinnedTaskId);
        Assert.Contains("Chuẩn bị báo cáo tuần", empDm.PinnedTaskSummary);

        IReadOnlyList<ChatMessageEntity> empMsgs = await empChat.GetMessagesAsync(dmDir.Id);
        Assert.Contains(empMsgs, m => m.Id == msgWithFile.Id && m.AttachmentFileName == "chat_spec.txt");

        // Also test reverse attachment download endpoint directly
        bool downloaded = await empSp.GetRequiredService<LanCollaborationClient>()
            .ReverseDownloadChatAttachmentAsync(msgWithFile.Id, dirEndpoint);
        Assert.True(downloaded);
    }

    [Fact]
    public async Task TimelinePlan_SharedSyncBetweenDirectorAndEmployee()
    {
        (ServiceProvider dirSp, _, string dirEndpoint) = await StartNodeAsync("dir_tl", isDirector: true);
        (ServiceProvider empSp, _, _) = await StartNodeAsync("emp_tl", isDirector: false);

        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Nhân viên Kế hoạch", "emptl", "Pass123!", CompanyRoles.Staff);

        var empTimeline = empSp.GetRequiredService<TimelinePlanService>();
        TimelinePlanEntity plan = await empTimeline.AddOrUpdatePlanAsync(
            null,
            "Họp rà soát quyết toán",
            "Toàn bộ phòng Kế toán và Giám đốc tham gia",
            TimelineCategories.Meeting,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1));

        int changes = await empSp.GetRequiredService<LanCollaborationClient>()
            .SyncTimelineWithPeerAsync(dirEndpoint);
        Assert.True(changes >= 1);

        IReadOnlyList<TimelinePlanEntity> dirPlans = await dirSp.GetRequiredService<TimelinePlanService>().GetActivePlansAsync();
        Assert.Contains(dirPlans, p => p.Id == plan.Id && p.Title == "Họp rà soát quyết toán");
    }

    [Fact]
    public async Task HrAttendance_ProfilesCheckInOutAndExcelExport()
    {
        (ServiceProvider dirSp, _, _) = await StartNodeAsync("dir_hr", isDirector: true);
        var hr = dirSp.GetRequiredService<HrAttendanceService>();

        EmployeeProfileEntity emp = await hr.SaveEmployeeAsync(new EmployeeProfileEntity
        {
            EmployeeCode = "NV-001",
            FullName = "Phạm Minh Châu",
            Department = "Kế toán - Pháp chế",
            PositionTitle = "Trưởng nhóm",
            RolesCsv = "Accounting,Legal",
            Phone = "0901234567",
            Email = "chau@example.local"
        });
        Assert.Equal("Accounting,Legal", emp.RolesCsv);

        _ = await hr.CheckInTodayAsync();
        AttendanceRecordEntity recOut = await hr.CheckOutTodayAsync();

        DateTime now = DateTime.Now;
        IReadOnlyList<MonthlyAttendanceSummaryRow> summaries = await hr.GetMonthlySummaryAsync(now.Year, now.Month);
        IReadOnlyList<AttendanceRecordEntity> records = await hr.GetAttendanceRecordsAsync(now.Year, now.Month);
        Assert.NotEmpty(summaries);
        Assert.Contains(records, r => r.Id == recOut.Id);

        string xlsxPath = Path.Combine(_tempRoot, "attendance_report.xlsx");
        var exporter = new AttendanceExcelExporter();
        exporter.ExportMonthlyReport(xlsxPath, now.Year, now.Month, "Ez Test Corp", summaries, records);
        Assert.True(File.Exists(xlsxPath));

        using var wb = new XLWorkbook(xlsxPath);
        Assert.Equal(2, wb.Worksheets.Count);
        Assert.Contains("NV-001", wb.Worksheet("Tổng hợp Chấm công").CellsUsed().Select(c => c.GetString()));
    }

    [Fact]
    public async Task AppDiagnosticsAndRepairService_ScansRepairsAndExportsBugReportZip()
    {
        (ServiceProvider dirSp, _, _) = await StartNodeAsync("dir_diag", isDirector: true);
        var paths = dirSp.GetRequiredService<ILocalStoragePaths>();
        using var fileLogger = new RollingFileLoggerProvider(paths);

        var diag = new AppDiagnosticsAndRepairService(
            paths,
            dirSp.GetRequiredService<ISessionContext>(),
            dirSp.GetRequiredService<IPermissionService>(),
            dirSp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            dirSp.GetRequiredService<IDbContextFactory<TelemetryDbContext>>(),
            dirSp.GetRequiredService<Core.LanAgent.Discovery.ILanDiscoveryService>(),
            dirSp.GetRequiredService<PeerTaskSyncService>(),
            dirSp.GetRequiredService<PeerCollabSyncService>(),
            dirSp.GetRequiredService<ClockGuardService>(),
            dirSp.GetRequiredService<IAuditLogger>(),
            fileLogger);

        IReadOnlyList<DiagnosticRepairItem> scan = await diag.RunAutoScanAndRepairAsync(repair: true);
        Assert.Equal(5, scan.Count);
        Assert.Contains(scan, item => item.Category.Contains("SQLite") && item.Details.Contains("ok", StringComparison.OrdinalIgnoreCase));

        string zipPath = await diag.CreateBugReportZipAsync("Test diagnostic bug", "Step 1 -> Step 2");
        Assert.True(File.Exists(zipPath));

        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.Name == "diagnostic-summary.json");
        Assert.Contains(zip.Entries, e => e.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase));
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
                "Ez Collab Corp",
                "0317778889",
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
        await pairingMgr.ApproveRequestAsync(started.RequestId, role, mods, "0.3.0", "stable", "director");

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
