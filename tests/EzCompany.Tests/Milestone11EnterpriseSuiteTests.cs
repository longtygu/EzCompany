using System.IO;
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
using EzCompany.Core.Updater;
using EzCompany.Modules.Accounting;
using EzCompany.Modules.Legal;
using EzCompany.Modules.Office;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EzCompany.Tests;

public sealed class Milestone11EnterpriseSuiteTests : IAsyncLifetime
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "EzCompanyTests_M11_" + Guid.NewGuid().ToString("N"));
    private readonly List<IAsyncDisposable> _disposables = [];

    private static readonly byte[] SamplePng1x1 = CreateSampleRgbaPngBytes();

    private static byte[] CreateSampleRgbaPngBytes()
    {
        const int w = 4;
        const int h = 4;
        byte[] pixels = new byte[w * h * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0xD4;
            pixels[i + 1] = 0x78;
            pixels[i + 2] = 0x00;
            pixels[i + 3] = 0xFF;
        }

        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
            w,
            h,
            96,
            96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            pixels,
            w * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

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
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task SafeMigrationService_UpgradesV4DatabaseToV5WithNewEnterpriseTables()
    {
        var paths = new LocalStoragePaths("mig_v4_to_v5", _tempRoot);
        var services = new ServiceCollection();
        services.AddSingleton<ILocalStoragePaths>(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddEzCrypto();
        services.AddEzData(paths);
        services.AddSingleton<SafeMigrationService>();

        using ServiceProvider sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();

        Guid companyId = Guid.NewGuid();
        await using (AppDbContext v4Db = await dbFactory.CreateDbContextAsync())
        {
            await v4Db.GetService<IMigrator>().MigrateAsync("20261127000001_AddChatStickerAndPayrollFields");
            long nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            await v4Db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO CompanyProfiles (CompanyId, CompanyName, TaxCode, RootPublicKeyBase64, CompanyEncryptionPublicKeyBase64, IsDirectorNode, IsGenesisCompleted, AppSchemaVersion, CreatedAtUtc, UpdatedAtUtc)
                  VALUES ({0}, {1}, {2}, {3}, {4}, 1, 1, 4, {5}, {5});",
                companyId,
                "V4 Upgrade Corp",
                "0301112223",
                Convert.ToBase64String(new byte[32]),
                Convert.ToBase64String(new byte[32]),
                nowTicks);
        }

        SafeMigrationResult result = await sp.GetRequiredService<SafeMigrationService>().MigrateAllSafelyAsync();
        Assert.True(result.Succeeded);
        Assert.NotNull(result.BackupDirectory);

        await using AppDbContext v5Db = await dbFactory.CreateDbContextAsync();
        CompanyProfileEntity upgraded = await v5Db.CompanyProfiles.AsNoTracking().FirstAsync();
        Assert.Equal(5, GenesisBootstrapService.CurrentAppSchemaVersion);
        Assert.Equal(GenesisBootstrapService.CurrentAppSchemaVersion, upgraded.AppSchemaVersion);

        v5Db.LeaveRequests.Add(new LeaveRequestEntity
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            EmployeeProfileId = Guid.NewGuid(),
            EmployeeCode = "NV-01",
            EmployeeName = "Test V5",
            MachineId = paths.MachineId,
            RequestType = LeaveRequestTypes.Leave,
            StartDateKey = "2026-12-01",
            EndDateKey = "2026-12-01",
            Reason = "Nghỉ phép năm",
            Status = LeaveRequestStatuses.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        v5Db.CashbookEntries.Add(new CashbookEntryEntity
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            EntryDateKey = "2026-11-27",
            EntryType = "Income",
            Category = "Doanh thu",
            AmountVnd = 5_000_000m,
            Description = "Thu tiền hợp đồng",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        v5Db.ContractRecords.Add(new ContractRecordEntity
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ContractNumber = "HD-2026-01",
            Title = "HĐ Cung cấp phần mềm",
            CounterpartyName = "Công ty ABC",
            ExpiryDateKey = "2026-12-31",
            Status = "Active",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        v5Db.SharedDocuments.Add(new SharedDocumentEntity
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Title = "Nội quy công ty",
            FileName = "noiquy.pdf",
            FileSha256 = "abc",
            UploadedByMachineId = paths.MachineId,
            UploadedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await v5Db.SaveChangesAsync();

        Assert.Equal(1, await v5Db.LeaveRequests.CountAsync());
        Assert.Equal(1, await v5Db.CashbookEntries.CountAsync());
        Assert.Equal(1, await v5Db.ContractRecords.CountAsync());
        Assert.Equal(1, await v5Db.SharedDocuments.CountAsync());
    }

    [Fact]
    public async Task UserAvatarService_SaveSyncP2PAndClear_WorksAcrossNodes()
    {
        (ServiceProvider dirSp, _, string dirEndpoint) = await StartNodeAsync("dir_ava", isDirector: true);
        (ServiceProvider empSp, _, string empEndpoint) = await StartNodeAsync("emp_ava", isDirector: false);
        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Nhân viên Avatar", "empava", "Pass123!", CompanyRoles.Staff);

        string empMachine = empSp.GetRequiredService<ILocalStoragePaths>().MachineId;
        var empAvatarSvc = empSp.GetRequiredService<UserAvatarService>();
        await empAvatarSvc.SaveMyAvatarAsync(SamplePng1x1);

        byte[]? loadedMine = await empAvatarSvc.GetMyAvatarBytesAsync();
        Assert.Equal(SamplePng1x1, loadedMine);

        int synced = await dirSp.GetRequiredService<LanCollaborationClient>().SyncAvatarsWithPeerAsync(empEndpoint);
        Assert.True(synced >= 1);

        IReadOnlyList<UserAvatarSyncItem> dirAvatars = await dirSp.GetRequiredService<UserAvatarService>().GetAllAvatarsForSyncAsync();
        UserAvatarSyncItem empRow = Assert.Single(dirAvatars, a => a.MachineId == empMachine);
        Assert.Equal(Convert.ToBase64String(SamplePng1x1), empRow.AvatarBase64);

        await empAvatarSvc.ClearMyAvatarAsync();
        Assert.Null(await empAvatarSvc.GetMyAvatarBytesAsync());
    }

    [Fact]
    public async Task ChatUpgrades_ReplySearchAndGroupUpdateSync_WorkP2P()
    {
        (ServiceProvider dirSp, _, string dirEndpoint) = await StartNodeAsync("dir_chat11", isDirector: true);
        (ServiceProvider empSp, _, string empEndpoint) = await StartNodeAsync("emp_chat11", isDirector: false);
        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Nhân viên Chat 11", "empchat11", "Pass123!", CompanyRoles.Staff);

        string empMachine = empSp.GetRequiredService<ILocalStoragePaths>().MachineId;
        var dirChat = dirSp.GetRequiredService<ChatService>();
        var empChat = empSp.GetRequiredService<ChatService>();

        ChatConversationEntity grp = await dirChat.CreateGroupConversationAsync(
            "Nhóm Dự án Alpha",
            [(empMachine, "Nhân viên Chat 11")]);

        ChatMessageEntity first = await dirChat.SendMessageAsync(grp.Id, "Báo cáo tiến độ quý 4 nhé", null, null, [empMachine]);
        ChatMessageEntity reply = await dirChat.SendMessageAsync(
            grp.Id,
            "Đã nhận báo cáo từ mọi người",
            null,
            null,
            [empMachine],
            replyToMessageId: first.Id,
            replyPreviewText: $"{first.SenderFullName}: {first.Content}");

        Assert.Equal(first.Id, reply.ReplyToMessageId);
        Assert.Contains("Báo cáo tiến độ quý 4", reply.ReplyPreviewText);

        ChatMessageEntity renameNotice = await dirChat.UpdateGroupConversationAsync(
            grp.Id,
            "Nhóm Dự án Alpha V2",
            [(empMachine, "Nhân viên Chat 11")]);
        Assert.Equal("GroupUpdated", renameNotice.MessageType);

        int flushed = await dirSp.GetRequiredService<PeerCollabSyncService>()
            .FlushPendingChatAsync(new Dictionary<string, string> { [empMachine] = empEndpoint });
        Assert.True(flushed >= 3);

        IReadOnlyList<ChatConversationEntity> empConvs = await empChat.GetConversationsAsync();
        ChatConversationEntity empGrp = Assert.Single(empConvs, c => c.Id == grp.Id);
        Assert.Equal("Nhóm Dự án Alpha V2", empGrp.Name);

        IReadOnlyList<ChatMessageEntity> found = await empChat.SearchMessagesAsync(grp.Id, "tiến độ");
        Assert.Contains(found, m => m.Id == first.Id);
        Assert.Contains(await empChat.SearchMessagesAsync(grp.Id, "Nhận báo cáo"), m => m.Id == reply.Id && m.ReplyToMessageId == first.Id);
    }

    [Fact]
    public async Task WorkTask_SubtasksRecurrenceAndDeadlineReminder_WorkTogether()
    {
        (ServiceProvider dirSp, _, _) = await StartNodeAsync("dir_task11", isDirector: true);
        var tasks = dirSp.GetRequiredService<WorkTaskService>();

        DateTimeOffset dueSoon = DateTimeOffset.UtcNow.AddMinutes(30);
        WorkTaskEntity created = await tasks.CreateSelfTaskAsync(
            "Kiểm tra chứng từ thuế hàng ngày",
            "Rà soát VAT đầu ra",
            WorkTaskPriorities.High,
            dueSoon,
            recurrenceRule: WorkTaskRecurrenceRules.Daily);

        WorkTaskSubtaskItem[] subtasks =
        [
            new(Guid.NewGuid(), "Xuất bảng kê bán ra", true),
            new(Guid.NewGuid(), "Đối chiếu XML", false)
        ];
        WorkTaskEntity updatedSub = await tasks.UpdateSubtasksAsync(created.Id, subtasks);
        IReadOnlyList<WorkTaskSubtaskItem> parsed = WorkTaskService.ParseSubtasks(updatedSub.SubtasksJson);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("Xuất bảng kê bán ra", parsed[0].Title);
        Assert.True(parsed[0].IsDone);

        // Deadline reminder fires once per calendar day
        var reminderEvents = new List<TaskDeadlineReminderEvent>();
        dirSp.GetRequiredService<IEventBus>().Subscribe<TaskDeadlineReminderEvent>(e => reminderEvents.Add(e));
        var reminderSvc = new TaskDeadlineReminderService(tasks, dirSp.GetRequiredService<IEventBus>());

        int firstCheck = await reminderSvc.CheckAndNotifyAsync(TimeSpan.FromHours(2));
        int secondCheck = await reminderSvc.CheckAndNotifyAsync(TimeSpan.FromHours(2));
        Assert.Equal(1, firstCheck);
        Assert.Equal(0, secondCheck);
        Assert.Single(reminderEvents);

        // Completing Daily task automatically spawns tomorrow's occurrence
        WorkTaskEntity completed = await tasks.ToggleSelfTaskCompleteAsync(created.Id);
        Assert.Equal(WorkTaskStatuses.Completed, completed.Status);

        IReadOnlyList<WorkTaskEntity> all = await tasks.GetTodaySelfTasksAsync();
        Assert.Equal(2, all.Count(t => t.Title == created.Title));
        WorkTaskEntity nextOccurrence = Assert.Single(all, t => t.Id != created.Id && t.Title == created.Title);
        Assert.Equal(WorkTaskStatuses.Pending, nextOccurrence.Status);
        Assert.Equal(WorkTaskRecurrenceRules.Daily, nextOccurrence.RecurrenceRule);
        Assert.True(nextOccurrence.DueDateUtc > created.DueDateUtc);
    }

    [Fact]
    public async Task LeaveRequest_AndAttendanceTombstoneDelete_SyncAndApplyAutomatically()
    {
        (ServiceProvider dirSp, _, string dirEndpoint) = await StartNodeAsync("dir_leave11", isDirector: true);
        (ServiceProvider empSp, _, string empEndpoint) = await StartNodeAsync("emp_leave11", isDirector: false);
        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Lê Thị Phép", "empleave", "Pass123!", CompanyRoles.Staff);

        string empMachine = empSp.GetRequiredService<ILocalStoragePaths>().MachineId;
        var dirHr = dirSp.GetRequiredService<HrAttendanceService>();
        var empHr = empSp.GetRequiredService<HrAttendanceService>();

        await dirHr.SaveEmployeeAsync(new EmployeeProfileEntity
        {
            EmployeeCode = "NV-L01",
            FullName = "Lê Thị Phép",
            LinkedMachineId = empMachine,
            RolesCsv = CompanyRoles.Staff
        });

        EmployeeProfileEntity empSelf = await empHr.EnsureCurrentUserProfileAsync();
        empSelf.EmployeeCode = "NV-L01";
        await empHr.SaveEmployeeAsync(empSelf);

        // 1. Submit Leave & Overtime requests on Employee, sync to Director, approve on Director
        var empLeaves = empSp.GetRequiredService<LeaveRequestService>();
        LeaveRequestEntity leaveReq = await empLeaves.SubmitRequestAsync(
            LeaveRequestTypes.Leave,
            new DateOnly(2026, 11, 10),
            new DateOnly(2026, 11, 10),
            0.0,
            "Nghỉ việc gia đình");

        LeaveRequestEntity otReq = await empLeaves.SubmitRequestAsync(
            LeaveRequestTypes.Overtime,
            new DateOnly(2026, 11, 11),
            new DateOnly(2026, 11, 11),
            2.5,
            "Chạy quyết toán");

        await empSp.GetRequiredService<LanCollaborationClient>().SyncLeaveRequestsWithPeerAsync(dirEndpoint);

        var dirLeaves = dirSp.GetRequiredService<LeaveRequestService>();
        await dirLeaves.DecideRequestAsync(leaveReq.Id, approve: true, "Đồng ý");
        await dirLeaves.DecideRequestAsync(otReq.Id, approve: true, "Đồng ý OT");

        IReadOnlyList<AttendanceRecordEntity> dirAtt = await dirHr.GetAttendanceRecordsAsync(2026, 11);
        Assert.Contains(dirAtt, r => r.DateKey == "2026-11-10" && r.Status == "Leave");
        Assert.Contains(dirAtt, r => r.DateKey == "2026-11-11" && Math.Abs(r.OvertimeHours - 2.5) < 0.01);

        // 2. Employee adds manual attendance, pushes to Director, then soft-deletes -> tombstone propagates to Director
        AttendanceRecordEntity rec = await empHr.SaveCustomAttendanceAsync(
            empSelf.Id,
            new DateOnly(2026, 11, 12),
            new TimeSpan(8, 0, 0),
            new TimeSpan(17, 0, 0),
            "Present",
            1.0,
            0.0,
            "Chấm nhầm");
        await empSp.GetRequiredService<LanCollaborationClient>().PushAttendanceToDirectorsAsync(rec, dirEndpoint);
        Assert.Contains(await dirHr.GetAttendanceRecordsAsync(2026, 11), r => r.DateKey == "2026-11-12");

        AttendanceRecordEntity? deleted = await empHr.DeleteAttendanceRecordAsync(rec.Id);
        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);
        await empSp.GetRequiredService<LanCollaborationClient>().PushAttendanceToDirectorsAsync(deleted, dirEndpoint);
        Assert.DoesNotContain(await dirHr.GetAttendanceRecordsAsync(2026, 11), r => r.DateKey == "2026-11-12");
    }

    [Fact]
    public void PayslipPdfExporter_GeneratesValidPdfDocument()
    {
        string outPdf = Path.Combine(_tempRoot, "PhieuLuong_NV01_2026_11.pdf");
        var exporter = new PayslipPdfExporter();
        var row = new MonthlyPayrollRow(
            Guid.NewGuid(),
            "NV-01",
            "Trần Văn Lương",
            "Kế toán",
            "Kế toán viên",
            15_000_000m,
            1_500_000m,
            26.0,
            25.0,
            200.0,
            4.0,
            1,
            0,
            14_423_077m,
            432_692m,
            500_000m,
            1_050_000m,
            15_805_769m);

        exporter.ExportEmployeePayslip(outPdf, 2026, 11, "Ez Enterprise Corp", row);

        Assert.True(File.Exists(outPdf));
        byte[] bytes = File.ReadAllBytes(outPdf);
        Assert.True(bytes.Length > 500);
        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
    }

    [Fact]
    public async Task AccountingLedger_CashbookSummaryDisbursementAndExcelExport_Work()
    {
        (ServiceProvider dirSp, _, _) = await StartNodeAsync("dir_acc11", isDirector: true);
        var ledger = dirSp.GetRequiredService<AccountingLedgerService>();

        await ledger.SaveCashbookEntryAsync(new CashbookEntryEntity
        {
            EntryDateKey = "2026-11-05",
            EntryType = "Income",
            Category = "Thu tiền khách hàng",
            AmountVnd = 20_000_000m,
            PaymentMethod = "BankTransfer",
            ReferenceCode = "PT-01",
            Description = "Thanh toán đợt 1"
        });

        ExpenseRequestEntity req = await ledger.SubmitExpenseRequestAsync(
            ExpenseRequestTypes.Advance,
            "Công tác phí",
            3_500_000m,
            "Tạm ứng công tác Đà Nẵng");

        ExpenseRequestEntity approved = await ledger.DecideExpenseRequestAsync(req.Id, true, "Giám đốc duyệt");
        Assert.Equal(ExpenseRequestStatuses.Approved, approved.Status);

        (ExpenseRequestEntity disbursed, CashbookEntryEntity linkedEntry) = await ledger.DisburseExpenseRequestAsync(req.Id, "Cash");
        Assert.Equal(ExpenseRequestStatuses.Disbursed, disbursed.Status);
        Assert.Equal(req.Id, linkedEntry.LinkedExpenseRequestId);
        Assert.Equal("Expense", linkedEntry.EntryType);

        IReadOnlyList<CashbookEntryEntity> allEntries = await ledger.GetCashbookEntriesAsync();
        (decimal inc, decimal exp, decimal bal) = AccountingLedgerService.Summarize(allEntries);
        Assert.Equal(20_000_000m, inc);
        Assert.Equal(3_500_000m, exp);
        Assert.Equal(16_500_000m, bal);

        string xlsxPath = Path.Combine(_tempRoot, "Cashbook_M11.xlsx");
        new AccountingLedgerExcelExporter().ExportCashbook(xlsxPath, "Ez Accounting Corp", allEntries);
        Assert.True(File.Exists(xlsxPath));
        using var wb = new XLWorkbook(xlsxPath);
        Assert.Equal("Sổ Quỹ Thu Chi", wb.Worksheets.First().Name);
    }

    [Fact]
    public async Task ContractRegistry_StatusComputationTimelineSyncAndExcelExport_Work()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        Assert.Equal("Terminated", ContractRegistryService.ComputeStatus(today.AddDays(-5), today, "Terminated"));
        Assert.Equal("Expired", ContractRegistryService.ComputeStatus(today.AddDays(-1), today, "Active"));
        Assert.Equal("ExpiringSoon", ContractRegistryService.ComputeStatus(today.AddDays(15), today, "Active"));
        Assert.Equal("Active", ContractRegistryService.ComputeStatus(today.AddDays(60), today, "Active"));

        (ServiceProvider dirSp, _, _) = await StartNodeAsync("dir_legal11", isDirector: true);
        var contracts = dirSp.GetRequiredService<ContractRegistryService>();
        var timeline = dirSp.GetRequiredService<TimelinePlanService>();

        DateOnly expiry = today.AddDays(10);
        ContractRecordEntity saved = await contracts.SaveContractAsync(new ContractRecordEntity
        {
            ContractNumber = "HD-2026-99",
            Title = "Hợp đồng Thuê văn phòng",
            CounterpartyName = "Tòa nhà Bitexco",
            ContractType = "Service",
            SignedDateKey = today.AddMonths(-11).ToString("yyyy-MM-dd"),
            StartDateKey = today.AddMonths(-11).ToString("yyyy-MM-dd"),
            ExpiryDateKey = expiry.ToString("yyyy-MM-dd"),
            ValueVnd = 240_000_000m,
            Status = "Active"
        });
        Assert.Equal("ExpiringSoon", saved.Status);

        TimelinePlanEntity milestone = await contracts.SyncExpiryToTimelineAsync(saved.Id);
        Assert.Equal(TimelineCategories.Deadline, milestone.Category);
        Assert.Contains("HD-2026-99", milestone.Title);
        Assert.Contains(await timeline.GetActivePlansAsync(), p => p.Id == milestone.Id);

        string xlsxPath = Path.Combine(_tempRoot, "Contracts_M11.xlsx");
        IReadOnlyList<ContractRecordEntity> list = await contracts.GetContractsAsync();
        new ContractRegistryExcelExporter().ExportContracts(xlsxPath, "Ez Legal Corp", list);
        Assert.True(File.Exists(xlsxPath));
        using var wb = new XLWorkbook(xlsxPath);
        Assert.Equal("Danh mục Hợp đồng", wb.Worksheets.First().Name);
    }

    [Fact]
    public async Task LocalPdfStudio_ConvertImagesToPdfAndStampImage_ProduceValidPdfs()
    {
        (ServiceProvider dirSp, _, _) = await StartNodeAsync("dir_pdf11", isDirector: true);
        var pdfSvc = new LocalPdfStudioService(
            dirSp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            dirSp.GetRequiredService<ILocalStoragePaths>(),
            dirSp.GetRequiredService<ISessionContext>(),
            dirSp.GetRequiredService<IAuditLogger>());

        string img1 = Path.Combine(_tempRoot, "scan1.png");
        string img2 = Path.Combine(_tempRoot, "scan2.png");
        string seal = Path.Combine(_tempRoot, "seal.png");
        await File.WriteAllBytesAsync(img1, SamplePng1x1);
        await File.WriteAllBytesAsync(img2, SamplePng1x1);
        await File.WriteAllBytesAsync(seal, SamplePng1x1);

        string mergedPdf = Path.Combine(_tempRoot, "from_images.pdf");
        PdfOperationResult imgRes = await pdfSvc.ConvertImagesToPdfAsync([img1, img2], mergedPdf, fitToA4: true);
        Assert.Equal(2, imgRes.PageCount);
        Assert.True(File.Exists(mergedPdf));
        Assert.Equal("%PDF-"u8.ToArray(), File.ReadAllBytes(mergedPdf)[..5]);

        string stampedPdf = Path.Combine(_tempRoot, "stamped.pdf");
        PdfOperationResult stampRes = await pdfSvc.StampImageOnPdfAsync(mergedPdf, seal, stampedPdf, 2, 70, 75, 18);
        Assert.Equal(2, stampRes.PageCount);
        Assert.True(File.Exists(stampedPdf));
        Assert.Equal("%PDF-"u8.ToArray(), File.ReadAllBytes(stampedPdf)[..5]);
    }

    [Fact]
    public async Task SharedDocumentVault_MetadataSyncAndPeerDownloadWithSha256_Work()
    {
        (ServiceProvider dirSp, _, string dirEndpoint) = await StartNodeAsync("dir_docs11", isDirector: true);
        (ServiceProvider empSp, _, _) = await StartNodeAsync("emp_docs11", isDirector: false);
        await PairNodesAsync(dirSp, dirEndpoint, empSp, "Nhân viên Kho", "empdocs", "Pass123!", CompanyRoles.Staff);

        string srcFile = Path.Combine(_tempRoot, "QuyTrinh_2026.txt");
        byte[] content = "Quy trình làm việc chuẩn công ty 2026."u8.ToArray();
        await File.WriteAllBytesAsync(srcFile, content);

        SharedDocumentEntity uploaded = await dirSp.GetRequiredService<SharedDocumentService>()
            .UploadDocumentAsync("Quy trình nội bộ 2026", SharedDocumentCategories.Policy, "Ban hành T11", srcFile);

        int synced = await empSp.GetRequiredService<LanCollaborationClient>().SyncSharedDocumentsWithPeerAsync(dirEndpoint);
        Assert.True(synced >= 1);

        string? downloadedPath = await empSp.GetRequiredService<LanCollaborationClient>()
            .DownloadSharedDocumentFromPeersAsync(uploaded.Id, dirEndpoint);
        Assert.NotNull(downloadedPath);
        Assert.True(File.Exists(downloadedPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(downloadedPath));
    }

    [Fact]
    public void DirectorAutoBackupService_PruneBackupFiles_KeepsNewestRetentionCount()
    {
        string backupDir = Path.Combine(_tempRoot, "backup_prune");
        Directory.CreateDirectory(backupDir);

        string[] names =
        [
            "20261120_080000.db",
            "20261121_080000.db",
            "20261122_080000.db",
            "20261123_080000.db",
            "20261124_080000.db"
        ];
        foreach (string n in names)
        {
            File.WriteAllText(Path.Combine(backupDir, n), "sqlite-placeholder");
        }

        int deleted = DirectorAutoBackupService.PruneBackupFiles(backupDir, retentionCount: 3);
        Assert.Equal(2, deleted);

        string[] remaining = Directory.GetFiles(backupDir, "*.db")
            .Select(Path.GetFileName)
            .OrderBy(n => n)
            .ToArray()!;
        Assert.Equal(["20261122_080000.db", "20261123_080000.db", "20261124_080000.db"], remaining);
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
                "Ez Upgrade M11 Corp",
                "0319997775",
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
        await pairingMgr.ApproveRequestAsync(started.RequestId, role, mods, "0.5.0", "stable", "director");

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
