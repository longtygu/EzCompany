using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CompanyProfileEntity> CompanyProfiles => Set<CompanyProfileEntity>();
    public DbSet<LocalUserEntity> LocalUsers => Set<LocalUserEntity>();
    public DbSet<PairedDeviceEntity> PairedDevices => Set<PairedDeviceEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<AccountingInvoiceEntity> AccountingInvoices => Set<AccountingInvoiceEntity>();
    public DbSet<LegalContractReviewEntity> LegalContractReviews => Set<LegalContractReviewEntity>();
    public DbSet<OfficeDocumentRecordEntity> OfficeDocumentRecords => Set<OfficeDocumentRecordEntity>();
    public DbSet<WorkTaskEntity> WorkTasks => Set<WorkTaskEntity>();
    public DbSet<WorkTaskHistoryEntity> WorkTaskHistories => Set<WorkTaskHistoryEntity>();
    public DbSet<PasswordResetRequestEntity> PasswordResetRequests => Set<PasswordResetRequestEntity>();
    public DbSet<ChatConversationEntity> ChatConversations => Set<ChatConversationEntity>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
    public DbSet<TimelinePlanEntity> TimelinePlans => Set<TimelinePlanEntity>();
    public DbSet<EmployeeProfileEntity> EmployeeProfiles => Set<EmployeeProfileEntity>();
    public DbSet<AttendanceRecordEntity> AttendanceRecords => Set<AttendanceRecordEntity>();
    public DbSet<LeaveRequestEntity> LeaveRequests => Set<LeaveRequestEntity>();
    public DbSet<CashbookEntryEntity> CashbookEntries => Set<CashbookEntryEntity>();
    public DbSet<ExpenseRequestEntity> ExpenseRequests => Set<ExpenseRequestEntity>();
    public DbSet<ContractRecordEntity> ContractRecords => Set<ContractRecordEntity>();
    public DbSet<SharedDocumentEntity> SharedDocuments => Set<SharedDocumentEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetTicksConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableUtcDateTimeOffsetTicksConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyProfileEntity>(e =>
        {
            e.ToTable("CompanyProfiles");
            e.HasKey(x => x.CompanyId);
            e.Property(x => x.CompanyName).HasMaxLength(256).IsRequired();
            e.Property(x => x.TaxCode).HasMaxLength(64).IsRequired();
            e.Property(x => x.RootPublicKeyBase64).HasMaxLength(128).IsRequired();
            e.Property(x => x.CompanyEncryptionPublicKeyBase64).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<LocalUserEntity>(e =>
        {
            e.ToTable("LocalUsers");
            e.HasKey(x => x.UserId);
            e.Property(x => x.Username).HasMaxLength(128).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(256).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            e.Property(x => x.Role).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<PairedDeviceEntity>(e =>
        {
            e.ToTable("PairedDevices");
            e.HasKey(x => x.Id);
            e.Property(x => x.MachineId).HasMaxLength(128).IsRequired();
            e.Property(x => x.DevicePublicKeyBase64).HasMaxLength(128).IsRequired();
            e.Property(x => x.Username).HasMaxLength(128).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(256).IsRequired();
            e.Property(x => x.Role).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.MachineId).IsUnique();
        });

        modelBuilder.Entity<AuditLogEntity>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TimestampUtc);
            e.Property(x => x.MachineId).HasMaxLength(128).IsRequired();
            e.Property(x => x.Username).HasMaxLength(128).IsRequired();
            e.Property(x => x.ModuleId).HasMaxLength(128).IsRequired();
            e.Property(x => x.Action).HasMaxLength(256).IsRequired();
            e.Property(x => x.Severity).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<AccountingInvoiceEntity>(e =>
        {
            e.ToTable("AccountingInvoices");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SellerTaxCode, x.InvoiceSeries, x.InvoiceNumber });
            e.Property(x => x.InvoiceSeries).HasMaxLength(64).IsRequired();
            e.Property(x => x.InvoiceNumber).HasMaxLength(64).IsRequired();
            e.Property(x => x.SellerTaxCode).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<LegalContractReviewEntity>(e =>
        {
            e.ToTable("LegalContractReviews");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ReviewedAtUtc);
            e.Property(x => x.Title).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<OfficeDocumentRecordEntity>(e =>
        {
            e.ToTable("OfficeDocumentRecords");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CreatedAtUtc);
            e.Property(x => x.Operation).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<WorkTaskEntity>(e =>
        {
            e.ToTable("WorkTasks");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.AssigneeMachineId);
            e.HasIndex(x => x.AssignerMachineId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.UpdatedAtUtc);
            e.Property(x => x.TaskKind).HasMaxLength(32).IsRequired();
            e.Property(x => x.Title).HasMaxLength(256).IsRequired();
            e.Property(x => x.Priority).HasMaxLength(32).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
            e.Property(x => x.AssignerMachineId).HasMaxLength(128).IsRequired();
            e.Property(x => x.AssignerUsername).HasMaxLength(128).IsRequired();
            e.Property(x => x.AssignerFullName).HasMaxLength(256).IsRequired();
            e.Property(x => x.AssignerRole).HasMaxLength(64).IsRequired();
            e.Property(x => x.AssigneeMachineId).HasMaxLength(128).IsRequired();
            e.Property(x => x.AssigneeUsername).HasMaxLength(128).IsRequired();
            e.Property(x => x.AssigneeFullName).HasMaxLength(256).IsRequired();
            e.Property(x => x.AssigneeRole).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<WorkTaskHistoryEntity>(e =>
        {
            e.ToTable("WorkTaskHistories");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TaskId);
            e.HasIndex(x => x.TimestampUtc);
            e.Property(x => x.Action).HasMaxLength(64).IsRequired();
            e.Property(x => x.ActorMachineId).HasMaxLength(128).IsRequired();
            e.Property(x => x.ActorUsername).HasMaxLength(128).IsRequired();
            e.Property(x => x.ActorFullName).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<PasswordResetRequestEntity>(e =>
        {
            e.ToTable("PasswordResetRequests");
            e.HasKey(x => x.RequestId);
            e.HasIndex(x => x.State);
            e.HasIndex(x => x.RequestedAtUtc);
            e.Property(x => x.MachineId).HasMaxLength(128).IsRequired();
            e.Property(x => x.DevicePublicKeyBase64).HasMaxLength(128).IsRequired();
            e.Property(x => x.Username).HasMaxLength(128).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(256).IsRequired();
            e.Property(x => x.State).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<ChatConversationEntity>(e =>
        {
            e.ToTable("ChatConversations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UpdatedAtUtc);
            e.Property(x => x.ConversationType).HasMaxLength(32).IsRequired();
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<ChatMessageEntity>(e =>
        {
            e.ToTable("ChatMessages");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ConversationId);
            e.HasIndex(x => x.SentAtUtc);
            e.Property(x => x.SenderMachineId).HasMaxLength(128).IsRequired();
            e.Property(x => x.SenderUsername).HasMaxLength(128).IsRequired();
            e.Property(x => x.SenderFullName).HasMaxLength(256).IsRequired();
            e.Property(x => x.MessageType).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<TimelinePlanEntity>(e =>
        {
            e.ToTable("TimelinePlans");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StartDateUtc);
            e.HasIndex(x => x.UpdatedAtUtc);
            e.Property(x => x.Title).HasMaxLength(256).IsRequired();
            e.Property(x => x.Category).HasMaxLength(32).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<EmployeeProfileEntity>(e =>
        {
            e.ToTable("EmployeeProfiles");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EmployeeCode).IsUnique();
            e.HasIndex(x => x.LinkedMachineId);
            e.Property(x => x.EmployeeCode).HasMaxLength(64).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(256).IsRequired();
            e.Property(x => x.RolesCsv).HasMaxLength(256).IsRequired();
            e.Property(x => x.EmploymentStatus).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<AttendanceRecordEntity>(e =>
        {
            e.ToTable("AttendanceRecords");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EmployeeProfileId, x.DateKey });
            e.HasIndex(x => x.DateKey);
            e.Property(x => x.EmployeeCode).HasMaxLength(64).IsRequired();
            e.Property(x => x.EmployeeName).HasMaxLength(256).IsRequired();
            e.Property(x => x.DateKey).HasMaxLength(16).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<LeaveRequestEntity>(e =>
        {
            e.ToTable("LeaveRequests");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.MachineId);
            e.HasIndex(x => x.UpdatedAtUtc);
            e.Property(x => x.EmployeeCode).HasMaxLength(64).IsRequired();
            e.Property(x => x.EmployeeName).HasMaxLength(256).IsRequired();
            e.Property(x => x.MachineId).HasMaxLength(128).IsRequired();
            e.Property(x => x.RequestType).HasMaxLength(32).IsRequired();
            e.Property(x => x.StartDateKey).HasMaxLength(16).IsRequired();
            e.Property(x => x.EndDateKey).HasMaxLength(16).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<CashbookEntryEntity>(e =>
        {
            e.ToTable("CashbookEntries");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EntryDateKey);
            e.HasIndex(x => x.EntryType);
            e.Property(x => x.EntryDateKey).HasMaxLength(16).IsRequired();
            e.Property(x => x.EntryType).HasMaxLength(32).IsRequired();
            e.Property(x => x.Category).HasMaxLength(128).IsRequired();
            e.Property(x => x.PaymentMethod).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<ExpenseRequestEntity>(e =>
        {
            e.ToTable("ExpenseRequests");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.RequesterMachineId);
            e.HasIndex(x => x.UpdatedAtUtc);
            e.Property(x => x.RequesterMachineId).HasMaxLength(128).IsRequired();
            e.Property(x => x.RequesterUsername).HasMaxLength(128).IsRequired();
            e.Property(x => x.RequesterFullName).HasMaxLength(256).IsRequired();
            e.Property(x => x.RequestType).HasMaxLength(32).IsRequired();
            e.Property(x => x.Category).HasMaxLength(128).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<ContractRecordEntity>(e =>
        {
            e.ToTable("ContractRecords");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ExpiryDateKey);
            e.HasIndex(x => x.Status);
            e.Property(x => x.ContractNumber).HasMaxLength(64).IsRequired();
            e.Property(x => x.Title).HasMaxLength(256).IsRequired();
            e.Property(x => x.CounterpartyName).HasMaxLength(256).IsRequired();
            e.Property(x => x.ContractType).HasMaxLength(32).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<SharedDocumentEntity>(e =>
        {
            e.ToTable("SharedDocuments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.UpdatedAtUtc);
            e.Property(x => x.Title).HasMaxLength(256).IsRequired();
            e.Property(x => x.Category).HasMaxLength(64).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(256).IsRequired();
            e.Property(x => x.FileSha256).HasMaxLength(128).IsRequired();
            e.Property(x => x.UploadedByMachineId).HasMaxLength(128).IsRequired();
        });
    }
}
