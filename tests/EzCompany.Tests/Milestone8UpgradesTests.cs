using System.IO;
using System.Security.Cryptography;
using ClosedXML.Excel;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;
using EzCompany.App.Views;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent;
using EzCompany.Core.LanAgent.Collaboration;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Pairing;
using EzCompany.Core.Updater;
using EzCompany.Modules.Accounting;
using EzCompany.Modules.Legal;
using EzCompany.Modules.Office;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EzCompany.Tests;

public sealed class Milestone8UpgradesTests : IAsyncLifetime
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "EzCompanyTests_M8_" + Guid.NewGuid().ToString("N"));
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
    public async Task SafeMigrationService_UpgradesV3DatabaseToV4WithBackup()
    {
        var paths = new LocalStoragePaths("mig_v3_to_v4", _tempRoot);
        var services = new ServiceCollection();
        services.AddSingleton<ILocalStoragePaths>(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddEzCrypto();
        services.AddEzData(paths);
        services.AddSingleton<SafeMigrationService>();

        using ServiceProvider sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using (AppDbContext v3Db = await dbFactory.CreateDbContextAsync())
        {
            await v3Db.GetService<IMigrator>().MigrateAsync("20261126000001_AddChatTimelineHrAndLogo");
            long nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            await v3Db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO CompanyProfiles (CompanyId, CompanyName, TaxCode, RootPublicKeyBase64, CompanyEncryptionPublicKeyBase64, IsDirectorNode, IsGenesisCompleted, AppSchemaVersion, CreatedAtUtc, UpdatedAtUtc)
                  VALUES ({0}, {1}, {2}, {3}, {4}, 1, 1, 3, {5}, {5});",
                Guid.NewGuid(),
                "V3 Upgrade Corp",
                "0309998887",
                Convert.ToBase64String(new byte[32]),
                Convert.ToBase64String(new byte[32]),
                nowTicks);
        }

        SafeMigrationResult result = await sp.GetRequiredService<SafeMigrationService>().MigrateAllSafelyAsync();
        Assert.True(result.Succeeded);
        Assert.NotNull(result.BackupDirectory);

        await using AppDbContext v4Db = await dbFactory.CreateDbContextAsync();
        CompanyProfileEntity upgraded = await v4Db.CompanyProfiles.AsNoTracking().FirstAsync();
        Assert.Equal(GenesisBootstrapService.CurrentAppSchemaVersion, upgraded.AppSchemaVersion);

        var emp = new EmployeeProfileEntity
        {
            Id = Guid.NewGuid(),
            CompanyId = upgraded.CompanyId,
            EmployeeCode = "NV-V4",
            FullName = "Kiểm tra V4",
            RolesCsv = CompanyRoles.Hr,
            BaseSalaryVnd = 12_000_000m,
            AllowanceVnd = 1_500_000m,
            StandardWorkDaysPerMonth = 26.0,
            InsuranceDeductionVnd = 1_000_000m,
            JoinDateUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        v4Db.EmployeeProfiles.Add(emp);
        v4Db.AttendanceRecords.Add(new AttendanceRecordEntity
        {
            Id = Guid.NewGuid(),
            CompanyId = upgraded.CompanyId,
            EmployeeProfileId = emp.Id,
            EmployeeCode = emp.EmployeeCode,
            EmployeeName = emp.FullName,
            MachineId = paths.MachineId,
            DateKey = "2026-11-27",
            WorkedHours = 8.5,
            OvertimeHours = 1.5,
            WorkDayUnits = 1.0,
            IsManualEntry = true,
            Status = "Present",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await v4Db.SaveChangesAsync();
    }

    [Fact]
    public async Task CustomSticker_AddSendAndSyncP2P_PreservesStickerMessageTypeAndBytes()
    {
        (ServiceProvider dirSp, _, string dirEndpoint) = await StartNodeAsync("dir_stk", isDirector: true);
        (ServiceProvider empSp, _, string empEndpoint) = await StartNodeAsync("emp_stk", isDirector: false);

        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Nhân viên Sticker", "empstk", "Pass123!", CompanyRoles.Staff);

        string dirMachine = dirSp.GetRequiredService<ILocalStoragePaths>().MachineId;
        string empMachine = empSp.GetRequiredService<ILocalStoragePaths>().MachineId;

        // Valid minimal 1x1 PNG
        byte[] pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+X2ZkAAAAASUVORK5CYII=");
        string srcStickerPath = Path.Combine(_tempRoot, "thumbs_up.png");
        await File.WriteAllBytesAsync(srcStickerPath, pngBytes);
        string expectedSha = Convert.ToHexString(SHA256.HashData(pngBytes)).ToLowerInvariant();

        var stickerSvc = new CustomStickerService(dirSp.GetRequiredService<ILocalStoragePaths>());
        CustomStickerItem added = await stickerSvc.AddStickerAsync(srcStickerPath, "Tuyệt vời!");
        IReadOnlyList<CustomStickerItem> storedStickers = stickerSvc.GetStickers();
        Assert.Contains(storedStickers, s => s.Id == added.Id && s.Name == "Tuyệt vời!");

        ChatMessageReceivedEvent? empEvt = null;
        empSp.GetRequiredService<IEventBus>().Subscribe<ChatMessageReceivedEvent>(e => empEvt = e);

        var dirChat = dirSp.GetRequiredService<ChatService>();
        ChatConversationEntity dm = await dirChat.GetOrCreateDirectConversationAsync(empMachine, "Nhân viên Sticker");
        ChatMessageEntity stickerMsg = await dirChat.SendMessageAsync(
            dm.Id,
            added.Name,
            added.FullPath,
            null,
            [empMachine],
            messageType: "Sticker");

        ChatMessageEntity textMsg = await dirChat.SendMessageAsync(
            dm.Id,
            "Kiểm tra delta",
            null,
            null,
            [empMachine],
            messageType: "Text");

        int flushed = await dirSp.GetRequiredService<PeerCollabSyncService>()
            .FlushPendingChatAsync(new Dictionary<string, string> { [empMachine] = empEndpoint });
        Assert.Equal(2, flushed);

        Assert.NotNull(empEvt);
        Assert.Equal("Text", empEvt.MessageType);

        IReadOnlyList<ChatMessageEntity> empMsgs = await empSp.GetRequiredService<ChatService>().GetMessagesAsync(dm.Id);
        ChatMessageEntity receivedSticker = Assert.Single(empMsgs, m => m.Id == stickerMsg.Id);
        Assert.Equal("Sticker", receivedSticker.MessageType);
        Assert.Equal(expectedSha, receivedSticker.AttachmentSha256);
        Assert.Equal(dirMachine, receivedSticker.SenderMachineId);
        Assert.NotNull(receivedSticker.AttachmentVaultPath);

        string empStickerFile = Path.Combine(
            empSp.GetRequiredService<ILocalStoragePaths>().VaultDirectory,
            receivedSticker.AttachmentVaultPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(empStickerFile));
        Assert.Equal(pngBytes, await File.ReadAllBytesAsync(empStickerFile));

        // Verify incremental delta query returns only newer message(s)
        IReadOnlyList<ChatMessageEntity> delta = await empSp.GetRequiredService<ChatService>()
            .GetMessagesAfterAsync(dm.Id, stickerMsg.SentAtUtc, [stickerMsg.Id]);
        ChatMessageEntity onlyDelta = Assert.Single(delta);
        Assert.Equal(textMsg.Id, onlyDelta.Id);
    }

    [Fact]
    public async Task HrCustomAttendance_SupportsCustomHoursOvernightAndMonthlyPayrollCalculation()
    {
        (ServiceProvider dirSp, _, _) = await StartNodeAsync("dir_payroll", isDirector: true);
        var hr = dirSp.GetRequiredService<HrAttendanceService>();

        EmployeeProfileEntity emp = await hr.SaveEmployeeAsync(new EmployeeProfileEntity
        {
            EmployeeCode = "NV-100",
            FullName = "Nguyễn Thu Hà",
            Department = "Kế toán",
            PositionTitle = "Kế toán tổng hợp",
            RolesCsv = CompanyRoles.Accounting,
            BaseSalaryVnd = 13_000_000m,
            AllowanceVnd = 1_000_000m,
            StandardWorkDaysPerMonth = 26.0,
            InsuranceDeductionVnd = 1_050_000m
        });

        // 1. Standard day + 2h overtime (08:15 -> 17:30 = 9.25h)
        AttendanceRecordEntity day1 = await hr.SaveCustomAttendanceAsync(
            emp.Id,
            new DateOnly(2026, 11, 2),
            new TimeSpan(8, 15, 0),
            new TimeSpan(17, 30, 0),
            "Present",
            null,
            2.0,
            "Nhập tay HR");
        Assert.Equal(9.25, day1.WorkedHours);
        Assert.Equal(1.0, day1.WorkDayUnits);
        Assert.Equal(2.0, day1.OvertimeHours);
        Assert.True(day1.IsManualEntry);

        // 2. Half day (0.5 work unit)
        AttendanceRecordEntity day2 = await hr.SaveCustomAttendanceAsync(
            emp.Id,
            new DateOnly(2026, 11, 3),
            new TimeSpan(8, 0, 0),
            new TimeSpan(12, 0, 0),
            "HalfDay",
            null,
            0.0,
            "Nghỉ chiều");
        Assert.Equal(4.0, day2.WorkedHours);
        Assert.Equal(0.5, day2.WorkDayUnits);

        // 3. Overnight shift (22:00 -> 06:00 next morning = 8.0h)
        AttendanceRecordEntity day3 = await hr.SaveCustomAttendanceAsync(
            emp.Id,
            new DateOnly(2026, 11, 4),
            new TimeSpan(22, 0, 0),
            new TimeSpan(6, 0, 0),
            "Present",
            1.0,
            0.0,
            "Trực ca đêm");
        Assert.Equal(8.0, day3.WorkedHours);

        var bonusMap = new Dictionary<Guid, decimal> { [emp.Id] = 500_000m };
        IReadOnlyList<MonthlyPayrollRow> payroll = await hr.CalculateMonthlyPayrollAsync(2026, 11, bonusMap);
        MonthlyPayrollRow row = Assert.Single(payroll, r => r.EmployeeProfileId == emp.Id);

        Assert.Equal(2.5, row.ActualWorkDays);
        Assert.Equal(21.25, row.TotalWorkedHours);
        Assert.Equal(2.0, row.OvertimeHours);
        Assert.Equal(1_250_000m, row.ProratedBaseSalaryVnd); // 13,000,000 * 2.5 / 26
        Assert.Equal(187_500m, row.OvertimePayVnd);         // (13,000,000 / 208) * 1.5 * 2
        Assert.Equal(1_887_500m, row.NetSalaryVnd);         // 1,250,000 + 1,000,000 + 187,500 + 500,000 - 1,050,000

        string payrollXlsx = Path.Combine(_tempRoot, "payroll_2026_11.xlsx");
        var exporter = new AttendanceExcelExporter();
        exporter.ExportMonthlyPayroll(payrollXlsx, 2026, 11, "Ez Payroll Corp", payroll);
        Assert.True(File.Exists(payrollXlsx));

        using var wb = new XLWorkbook(payrollXlsx);
        IXLWorksheet sheet = wb.Worksheet("Bảng lương Tháng");
        Assert.Contains("NV-100", sheet.CellsUsed().Select(c => c.GetString()));
    }

    [Fact]
    public void WorkspaceNavigation_BuildsCollapsibleRoleGroupsAndMapsLegacyHrKey()
    {
        var paths = new LocalStoragePaths("nav_test", _tempRoot);
        var services = new ServiceCollection();
        services.AddSingleton<ILocalStoragePaths>(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddEzCrypto();
        services.AddEzData(paths);
        services.AddEzLanAgent(bindLoopbackOnly: true);
        services.AddSingleton<AppearanceSettingsService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<CustomStickerService>();
        services.AddSingleton<ChatNotificationPopupService>();
        services.AddSingleton<AttendanceExcelExporter>();
        services.AddSingleton<IAppModule, AccountingModule>();
        services.AddSingleton<IAppModule, LegalModule>();
        services.AddSingleton<IAppModule, OfficeModule>();

        using ServiceProvider sp = services.BuildServiceProvider();
        Guid companyId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        var session = (SessionContext)sp.GetRequiredService<ISessionContext>();
        session.SetCompanyContext(companyId, "Ez Nav Corp", "0101010101", isDirectorNode: true);
        session.SetAuthenticatedUser(userId, "dir", "Giám đốc", CompanyRoles.Director);

        var perms = (PermissionService)sp.GetRequiredService<IPermissionService>();
        perms.ApplyPolicySnapshot(new PolicySnapshot(
            companyId,
            paths.MachineId,
            "dev_pub",
            "comp_enc",
            userId,
            "dir",
            "Giám đốc",
            CompanyRoles.Director,
            CompanyRoles.GetDefaultModulesForRole(CompanyRoles.Director),
            CompanyRoles.GetDefaultPermissionsForRole(CompanyRoles.Director),
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30),
            "stable",
            "0.4.0",
            "Giám đốc"));

        var vm = new WorkspaceWindowViewModel(
            session,
            perms,
            paths,
            sp.GetServices<IAppModule>(),
            sp,
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ThemeService>(),
            sp.GetRequiredService<CompanyBrandingService>(),
            NullLogger<WorkspaceWindowViewModel>.Instance,
            sp.GetRequiredService<ChatNotificationPopupService>());

        vm.RebuildNavigation();

        Assert.Contains(vm.NavigationGroups, g => g.GroupKey == "group.common");
        Assert.Contains(vm.NavigationGroups, g => g.GroupKey == "group.director");
        Assert.Contains(vm.NavigationGroups, g => g.GroupKey == "group.accounting");
        Assert.Contains(vm.NavigationGroups, g => g.GroupKey == "group.legal");

        NavigationGroupModel hrGroup = Assert.Single(vm.NavigationGroups, g => g.GroupKey == "group.hr");
        Assert.Equal(3, hrGroup.Items.Count);
        Assert.Contains(hrGroup.Items, i => i.Key == "core.hr.profiles");
        Assert.Contains(hrGroup.Items, i => i.Key == "core.hr.attendance");
        Assert.Contains(hrGroup.Items, i => i.Key == "core.hr.payroll");

        hrGroup.IsExpanded = false;
        vm.OpenNavigationItem("core.hr");
        Assert.True(hrGroup.IsExpanded);
        Assert.Equal("core.hr.profiles", vm.SelectedNavItem?.Key);

        // Switch to Accounting role -> Director group hidden, Accounting group visible
        session.SetCompanyContext(companyId, "Ez Nav Corp", "0101010101", isDirectorNode: false);
        session.SetAuthenticatedUser(userId, "acc", "Kế toán", CompanyRoles.Accounting);
        perms.ApplyPolicySnapshot(new PolicySnapshot(
            companyId,
            paths.MachineId,
            "dev_pub",
            "comp_enc",
            userId,
            "acc",
            "Kế toán",
            CompanyRoles.Accounting,
            CompanyRoles.GetDefaultModulesForRole(CompanyRoles.Accounting),
            CompanyRoles.GetDefaultPermissionsForRole(CompanyRoles.Accounting),
            2,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30),
            "stable",
            "0.4.0",
            "Giám đốc"));

        vm.RebuildNavigation();
        Assert.DoesNotContain(vm.NavigationGroups, g => g.GroupKey == "group.director");
        Assert.Contains(vm.NavigationGroups, g => g.GroupKey == "group.accounting");
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
                "Ez Upgrade M8 Corp",
                "0318889991",
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
