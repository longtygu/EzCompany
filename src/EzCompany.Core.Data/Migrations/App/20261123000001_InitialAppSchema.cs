using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EzCompany.Core.Data.Migrations.App;

[DbContext(typeof(AppDbContext))]
[Migration("20261123000001_InitialAppSchema")]
public partial class InitialAppSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CompanyProfiles",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                TaxCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RootPublicKeyBase64 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                CompanyEncryptionPublicKeyBase64 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                IsDirectorNode = table.Column<bool>(type: "INTEGER", nullable: false),
                IsGenesisCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                AppSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_CompanyProfiles", x => x.CompanyId));

        migrationBuilder.CreateTable(
            name: "LocalUsers",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                Username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                FullName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                PasswordHash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Role = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                LastLoginUtc = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_LocalUsers", x => x.UserId));

        migrationBuilder.CreateIndex(
            name: "IX_LocalUsers_Username",
            table: "LocalUsers",
            column: "Username",
            unique: true);

        migrationBuilder.CreateTable(
            name: "PairedDevices",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                MachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                DevicePublicKeyBase64 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                Username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                FullName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Role = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                CurrentPolicyVersion = table.Column<long>(type: "INTEGER", nullable: false),
                CurrentPolicyJson = table.Column<string>(type: "TEXT", nullable: false),
                PendingPolicyJson = table.Column<string>(type: "TEXT", nullable: true),
                LastKnownIp = table.Column<string>(type: "TEXT", nullable: true),
                LastKnownPort = table.Column<int>(type: "INTEGER", nullable: false),
                IsOnline = table.Column<bool>(type: "INTEGER", nullable: false),
                AppVersion = table.Column<string>(type: "TEXT", nullable: false),
                SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                DbSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                VaultSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                FreeDiskPercent = table.Column<double>(type: "REAL", nullable: false),
                LastSeenUtc = table.Column<long>(type: "INTEGER", nullable: true),
                LastBackupUtc = table.Column<long>(type: "INTEGER", nullable: true),
                LastTelemetrySyncUtc = table.Column<long>(type: "INTEGER", nullable: true),
                PairedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                PairedByAdminName = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PairedDevices", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_PairedDevices_MachineId",
            table: "PairedDevices",
            column: "MachineId",
            unique: true);

        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                MachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                Username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ModuleId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Action = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                TargetEntity = table.Column<string>(type: "TEXT", nullable: true),
                DetailsJson = table.Column<string>(type: "TEXT", nullable: true),
                Severity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AuditLogs", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_TimestampUtc",
            table: "AuditLogs",
            column: "TimestampUtc");

        migrationBuilder.CreateTable(
            name: "AccountingInvoices",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                InvoiceSeries = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                IssueDate = table.Column<long>(type: "INTEGER", nullable: false),
                SellerTaxCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                SellerName = table.Column<string>(type: "TEXT", nullable: false),
                BuyerTaxCodeOrCccd = table.Column<string>(type: "TEXT", nullable: false),
                BuyerName = table.Column<string>(type: "TEXT", nullable: false),
                SubTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                VatAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                TotalAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                AmountInWords = table.Column<string>(type: "TEXT", nullable: false),
                HasMathMismatch = table.Column<bool>(type: "INTEGER", nullable: false),
                MathDifference = table.Column<decimal>(type: "TEXT", nullable: false),
                IsDuplicate = table.Column<bool>(type: "INTEGER", nullable: false),
                ValidationNotes = table.Column<string>(type: "TEXT", nullable: false),
                VaultRelativePath = table.Column<string>(type: "TEXT", nullable: false),
                FileSha256 = table.Column<string>(type: "TEXT", nullable: false),
                ImportedByUsername = table.Column<string>(type: "TEXT", nullable: false),
                ImportedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AccountingInvoices", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_AccountingInvoices_SellerTaxCode_InvoiceSeries_InvoiceNumber",
            table: "AccountingInvoices",
            columns: ["SellerTaxCode", "InvoiceSeries", "InvoiceNumber"]);

        migrationBuilder.CreateTable(
            name: "LegalContractReviews",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                OriginalFileName = table.Column<string>(type: "TEXT", nullable: false),
                RevisedFileName = table.Column<string>(type: "TEXT", nullable: false),
                OriginalVaultPath = table.Column<string>(type: "TEXT", nullable: false),
                RevisedVaultPath = table.Column<string>(type: "TEXT", nullable: false),
                OriginalSha256 = table.Column<string>(type: "TEXT", nullable: false),
                RevisedSha256 = table.Column<string>(type: "TEXT", nullable: false),
                AddedCount = table.Column<int>(type: "INTEGER", nullable: false),
                DeletedCount = table.Column<int>(type: "INTEGER", nullable: false),
                ModifiedCount = table.Column<int>(type: "INTEGER", nullable: false),
                ReviewerNotes = table.Column<string>(type: "TEXT", nullable: false),
                ReportVaultPath = table.Column<string>(type: "TEXT", nullable: false),
                ReviewedByUsername = table.Column<string>(type: "TEXT", nullable: false),
                ReviewedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_LegalContractReviews", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_LegalContractReviews_ReviewedAtUtc",
            table: "LegalContractReviews",
            column: "ReviewedAtUtc");

        migrationBuilder.CreateTable(
            name: "OfficeDocumentRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Operation = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                SourceFilesJson = table.Column<string>(type: "TEXT", nullable: false),
                OutputVaultPath = table.Column<string>(type: "TEXT", nullable: false),
                OutputSha256 = table.Column<string>(type: "TEXT", nullable: false),
                PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                Notes = table.Column<string>(type: "TEXT", nullable: false),
                CreatedByUsername = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_OfficeDocumentRecords", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_OfficeDocumentRecords_CreatedAtUtc",
            table: "OfficeDocumentRecords",
            column: "CreatedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("CompanyProfiles");
        migrationBuilder.DropTable("LocalUsers");
        migrationBuilder.DropTable("PairedDevices");
        migrationBuilder.DropTable("AuditLogs");
        migrationBuilder.DropTable("AccountingInvoices");
        migrationBuilder.DropTable("LegalContractReviews");
        migrationBuilder.DropTable("OfficeDocumentRecords");
    }
}
