using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace EzCompany.Core.Data.Migrations.App;

[DbContext(typeof(AppDbContext))]
partial class AppDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.25");

        modelBuilder.Entity("EzCompany.Core.Data.Entities.AccountingInvoiceEntity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("AmountInWords").IsRequired().HasColumnType("TEXT");
            b.Property<string>("BuyerName").IsRequired().HasColumnType("TEXT");
            b.Property<string>("BuyerTaxCodeOrCccd").IsRequired().HasColumnType("TEXT");
            b.Property<string>("FileSha256").IsRequired().HasColumnType("TEXT");
            b.Property<bool>("HasMathMismatch").HasColumnType("INTEGER");
            b.Property<long>("ImportedAtUtc").HasColumnType("INTEGER");
            b.Property<string>("ImportedByUsername").IsRequired().HasColumnType("TEXT");
            b.Property<string>("InvoiceNumber").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("InvoiceSeries").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.Property<bool>("IsDuplicate").HasColumnType("INTEGER");
            b.Property<long>("IssueDate").HasColumnType("INTEGER");
            b.Property<decimal>("MathDifference").HasColumnType("TEXT");
            b.Property<string>("SellerName").IsRequired().HasColumnType("TEXT");
            b.Property<string>("SellerTaxCode").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.Property<decimal>("SubTotal").HasColumnType("TEXT");
            b.Property<decimal>("TotalAmount").HasColumnType("TEXT");
            b.Property<string>("ValidationNotes").IsRequired().HasColumnType("TEXT");
            b.Property<decimal>("VatAmount").HasColumnType("TEXT");
            b.Property<string>("VaultRelativePath").IsRequired().HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("SellerTaxCode", "InvoiceSeries", "InvoiceNumber");
            b.ToTable("AccountingInvoices");
        });

        modelBuilder.Entity("EzCompany.Core.Data.Entities.AuditLogEntity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("Action").IsRequired().HasMaxLength(256).HasColumnType("TEXT");
            b.Property<string>("DetailsJson").HasColumnType("TEXT");
            b.Property<string>("MachineId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("ModuleId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("Severity").IsRequired().HasMaxLength(32).HasColumnType("TEXT");
            b.Property<string>("TargetEntity").HasColumnType("TEXT");
            b.Property<long>("TimestampUtc").HasColumnType("INTEGER");
            b.Property<Guid?>("UserId").HasColumnType("TEXT");
            b.Property<string>("Username").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("TimestampUtc");
            b.ToTable("AuditLogs");
        });

        modelBuilder.Entity("EzCompany.Core.Data.Entities.CompanyProfileEntity", b =>
        {
            b.Property<Guid>("CompanyId").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<int>("AppSchemaVersion").HasColumnType("INTEGER");
            b.Property<string>("CompanyEncryptionPublicKeyBase64").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("CompanyName").IsRequired().HasMaxLength(256).HasColumnType("TEXT");
            b.Property<long>("CreatedAtUtc").HasColumnType("INTEGER");
            b.Property<bool>("IsDirectorNode").HasColumnType("INTEGER");
            b.Property<bool>("IsGenesisCompleted").HasColumnType("INTEGER");
            b.Property<string>("RootPublicKeyBase64").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("TaxCode").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.Property<long>("UpdatedAtUtc").HasColumnType("INTEGER");
            b.HasKey("CompanyId");
            b.ToTable("CompanyProfiles");
        });

        modelBuilder.Entity("EzCompany.Core.Data.Entities.LegalContractReviewEntity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<int>("AddedCount").HasColumnType("INTEGER");
            b.Property<int>("DeletedCount").HasColumnType("INTEGER");
            b.Property<int>("ModifiedCount").HasColumnType("INTEGER");
            b.Property<string>("OriginalFileName").IsRequired().HasColumnType("TEXT");
            b.Property<string>("OriginalSha256").IsRequired().HasColumnType("TEXT");
            b.Property<string>("OriginalVaultPath").IsRequired().HasColumnType("TEXT");
            b.Property<string>("ReportVaultPath").IsRequired().HasColumnType("TEXT");
            b.Property<long>("ReviewedAtUtc").HasColumnType("INTEGER");
            b.Property<string>("ReviewedByUsername").IsRequired().HasColumnType("TEXT");
            b.Property<string>("ReviewerNotes").IsRequired().HasColumnType("TEXT");
            b.Property<string>("RevisedFileName").IsRequired().HasColumnType("TEXT");
            b.Property<string>("RevisedSha256").IsRequired().HasColumnType("TEXT");
            b.Property<string>("RevisedVaultPath").IsRequired().HasColumnType("TEXT");
            b.Property<string>("Title").IsRequired().HasMaxLength(256).HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("ReviewedAtUtc");
            b.ToTable("LegalContractReviews");
        });

        modelBuilder.Entity("EzCompany.Core.Data.Entities.LocalUserEntity", b =>
        {
            b.Property<Guid>("UserId").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<Guid>("CompanyId").HasColumnType("TEXT");
            b.Property<long>("CreatedAtUtc").HasColumnType("INTEGER");
            b.Property<string>("FullName").IsRequired().HasMaxLength(256).HasColumnType("TEXT");
            b.Property<bool>("IsActive").HasColumnType("INTEGER");
            b.Property<long?>("LastLoginUtc").HasColumnType("INTEGER");
            b.Property<string>("PasswordHash").IsRequired().HasMaxLength(512).HasColumnType("TEXT");
            b.Property<string>("Role").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("Username").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.HasKey("UserId");
            b.HasIndex("Username").IsUnique();
            b.ToTable("LocalUsers");
        });

        modelBuilder.Entity("EzCompany.Core.Data.Entities.OfficeDocumentRecordEntity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<long>("CreatedAtUtc").HasColumnType("INTEGER");
            b.Property<string>("CreatedByUsername").IsRequired().HasColumnType("TEXT");
            b.Property<string>("Notes").IsRequired().HasColumnType("TEXT");
            b.Property<string>("Operation").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("OutputSha256").IsRequired().HasColumnType("TEXT");
            b.Property<string>("OutputVaultPath").IsRequired().HasColumnType("TEXT");
            b.Property<int>("PageCount").HasColumnType("INTEGER");
            b.Property<string>("SourceFilesJson").IsRequired().HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("CreatedAtUtc");
            b.ToTable("OfficeDocumentRecords");
        });

        modelBuilder.Entity("EzCompany.Core.Data.Entities.PairedDeviceEntity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("AppVersion").IsRequired().HasColumnType("TEXT");
            b.Property<Guid>("CompanyId").HasColumnType("TEXT");
            b.Property<string>("CurrentPolicyJson").IsRequired().HasColumnType("TEXT");
            b.Property<long>("CurrentPolicyVersion").HasColumnType("INTEGER");
            b.Property<long>("DbSizeBytes").HasColumnType("INTEGER");
            b.Property<string>("DevicePublicKeyBase64").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<double>("FreeDiskPercent").HasColumnType("REAL");
            b.Property<string>("FullName").IsRequired().HasMaxLength(256).HasColumnType("TEXT");
            b.Property<bool>("IsOnline").HasColumnType("INTEGER");
            b.Property<long?>("LastBackupUtc").HasColumnType("INTEGER");
            b.Property<string>("LastKnownIp").HasColumnType("TEXT");
            b.Property<int>("LastKnownPort").HasColumnType("INTEGER");
            b.Property<long?>("LastSeenUtc").HasColumnType("INTEGER");
            b.Property<long?>("LastTelemetrySyncUtc").HasColumnType("INTEGER");
            b.Property<string>("MachineId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<long>("PairedAtUtc").HasColumnType("INTEGER");
            b.Property<string>("PairedByAdminName").IsRequired().HasColumnType("TEXT");
            b.Property<string>("PendingPolicyJson").HasColumnType("TEXT");
            b.Property<string>("Role").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.Property<int>("SchemaVersion").HasColumnType("INTEGER");
            b.Property<Guid>("UserId").HasColumnType("TEXT");
            b.Property<string>("Username").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<long>("VaultSizeBytes").HasColumnType("INTEGER");
            b.HasKey("Id");
            b.HasIndex("MachineId").IsUnique();
            b.ToTable("PairedDevices");
        });

        modelBuilder.Entity("EzCompany.Core.Data.Entities.PasswordResetRequestEntity", b =>
        {
            b.Property<Guid>("RequestId").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<Guid>("CompanyId").HasColumnType("TEXT");
            b.Property<long?>("DecidedAtUtc").HasColumnType("INTEGER");
            b.Property<string>("DecidedByAdminName").HasColumnType("TEXT");
            b.Property<string>("DecisionMessage").HasColumnType("TEXT");
            b.Property<string>("DevicePublicKeyBase64").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<long>("ExpiresAtUtc").HasColumnType("INTEGER");
            b.Property<string>("FullName").IsRequired().HasMaxLength(256).HasColumnType("TEXT");
            b.Property<string>("MachineId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("Reason").IsRequired().HasColumnType("TEXT");
            b.Property<string>("RemoteIpAddress").HasColumnType("TEXT");
            b.Property<long>("RequestedAtUtc").HasColumnType("INTEGER");
            b.Property<string>("RequestedNewUsername").HasColumnType("TEXT");
            b.Property<string>("SignedResetTicketJson").HasColumnType("TEXT");
            b.Property<string>("State").IsRequired().HasMaxLength(32).HasColumnType("TEXT");
            b.Property<string>("Username").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.HasKey("RequestId");
            b.HasIndex("RequestedAtUtc");
            b.HasIndex("State");
            b.ToTable("PasswordResetRequests");
        });

        modelBuilder.Entity("EzCompany.Core.Data.Entities.WorkTaskEntity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("AssigneeFullName").IsRequired().HasMaxLength(256).HasColumnType("TEXT");
            b.Property<string>("AssigneeMachineId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("AssigneeRole").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("AssigneeUsername").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("AssignerFullName").IsRequired().HasMaxLength(256).HasColumnType("TEXT");
            b.Property<string>("AssignerMachineId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("AssignerRole").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("AssignerUsername").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<Guid>("CompanyId").HasColumnType("TEXT");
            b.Property<long?>("CompletedAtUtc").HasColumnType("INTEGER");
            b.Property<long>("CreatedAtUtc").HasColumnType("INTEGER");
            b.Property<string>("Description").IsRequired().HasColumnType("TEXT");
            b.Property<long?>("DueDateUtc").HasColumnType("INTEGER");
            b.Property<long?>("HandedOverAtUtc").HasColumnType("INTEGER");
            b.Property<string>("HandoverFileName").HasColumnType("TEXT");
            b.Property<string>("HandoverFileSha256").HasColumnType("TEXT");
            b.Property<string>("HandoverNotes").IsRequired().HasColumnType("TEXT");
            b.Property<string>("HandoverVaultPath").HasColumnType("TEXT");
            b.Property<string>("PendingSyncAction").HasColumnType("TEXT");
            b.Property<string>("PendingSyncTargetMachineId").HasColumnType("TEXT");
            b.Property<string>("Priority").IsRequired().HasMaxLength(32).HasColumnType("TEXT");
            b.Property<string>("ReviewNotes").IsRequired().HasColumnType("TEXT");
            b.Property<string>("Status").IsRequired().HasMaxLength(32).HasColumnType("TEXT");
            b.Property<string>("TaskKind").IsRequired().HasMaxLength(32).HasColumnType("TEXT");
            b.Property<string>("Title").IsRequired().HasMaxLength(256).HasColumnType("TEXT");
            b.Property<long>("UpdatedAtUtc").HasColumnType("INTEGER");
            b.HasKey("Id");
            b.HasIndex("AssigneeMachineId");
            b.HasIndex("AssignerMachineId");
            b.HasIndex("Status");
            b.HasIndex("UpdatedAtUtc");
            b.ToTable("WorkTasks");
        });

        modelBuilder.Entity("EzCompany.Core.Data.Entities.WorkTaskHistoryEntity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("Action").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("ActorFullName").IsRequired().HasMaxLength(256).HasColumnType("TEXT");
            b.Property<string>("ActorMachineId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("ActorUsername").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("AttachmentFileName").HasColumnType("TEXT");
            b.Property<string>("Note").IsRequired().HasColumnType("TEXT");
            b.Property<Guid>("TaskId").HasColumnType("TEXT");
            b.Property<long>("TimestampUtc").HasColumnType("INTEGER");
            b.HasKey("Id");
            b.HasIndex("TaskId");
            b.HasIndex("TimestampUtc");
            b.ToTable("WorkTaskHistories");
        });
    }
}
