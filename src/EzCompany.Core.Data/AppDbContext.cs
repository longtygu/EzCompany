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
    }
}
