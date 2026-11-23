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
    }
}
