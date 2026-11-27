using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EzCompany.Core.Data.Migrations.App;

[DbContext(typeof(AppDbContext))]
[Migration("20261127000002_AddEnterpriseSuiteV5")]
public partial class AddEnterpriseSuiteV5 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AvatarBase64",
            table: "LocalUsers",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "AvatarUpdatedAtUtc",
            table: "LocalUsers",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AvatarBase64",
            table: "PairedDevices",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "AvatarUpdatedAtUtc",
            table: "PairedDevices",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SubtasksJson",
            table: "WorkTasks",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "RecurrenceRule",
            table: "WorkTasks",
            type: "TEXT",
            nullable: false,
            defaultValue: "None");

        migrationBuilder.AddColumn<Guid>(
            name: "ReplyToMessageId",
            table: "ChatMessages",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReplyPreviewText",
            table: "ChatMessages",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsDeleted",
            table: "AttendanceRecords",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "LeaveRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                EmployeeProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                EmployeeCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                EmployeeName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                MachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                RequestType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                StartDateKey = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                EndDateKey = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                OvertimeHours = table.Column<double>(type: "REAL", nullable: false),
                Reason = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                ReviewedByName = table.Column<string>(type: "TEXT", nullable: false),
                ReviewedAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                ReviewNotes = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_LeaveRequests", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_LeaveRequests_Status", table: "LeaveRequests", column: "Status");
        migrationBuilder.CreateIndex(name: "IX_LeaveRequests_MachineId", table: "LeaveRequests", column: "MachineId");
        migrationBuilder.CreateIndex(name: "IX_LeaveRequests_UpdatedAtUtc", table: "LeaveRequests", column: "UpdatedAtUtc");

        migrationBuilder.CreateTable(
            name: "CashbookEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                EntryDateKey = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                EntryType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Category = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                AmountVnd = table.Column<decimal>(type: "TEXT", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: false),
                ReferenceCode = table.Column<string>(type: "TEXT", nullable: false),
                PaymentMethod = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                LinkedExpenseRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                CreatedByName = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_CashbookEntries", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_CashbookEntries_EntryDateKey", table: "CashbookEntries", column: "EntryDateKey");
        migrationBuilder.CreateIndex(name: "IX_CashbookEntries_EntryType", table: "CashbookEntries", column: "EntryType");

        migrationBuilder.CreateTable(
            name: "ExpenseRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                RequesterMachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                RequesterUsername = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                RequesterFullName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                RequestType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Category = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                AmountVnd = table.Column<decimal>(type: "TEXT", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: false),
                ReceiptFileName = table.Column<string>(type: "TEXT", nullable: true),
                ReceiptVaultPath = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                ApprovedByName = table.Column<string>(type: "TEXT", nullable: false),
                ApprovedAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                ReviewNotes = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ExpenseRequests", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_ExpenseRequests_Status", table: "ExpenseRequests", column: "Status");
        migrationBuilder.CreateIndex(name: "IX_ExpenseRequests_RequesterMachineId", table: "ExpenseRequests", column: "RequesterMachineId");
        migrationBuilder.CreateIndex(name: "IX_ExpenseRequests_UpdatedAtUtc", table: "ExpenseRequests", column: "UpdatedAtUtc");

        migrationBuilder.CreateTable(
            name: "ContractRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                ContractNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                CounterpartyName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                ContractType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                SignedDateKey = table.Column<string>(type: "TEXT", nullable: false),
                StartDateKey = table.Column<string>(type: "TEXT", nullable: false),
                ExpiryDateKey = table.Column<string>(type: "TEXT", nullable: false),
                ValueVnd = table.Column<decimal>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                OwnerName = table.Column<string>(type: "TEXT", nullable: false),
                VaultFilePath = table.Column<string>(type: "TEXT", nullable: true),
                FileName = table.Column<string>(type: "TEXT", nullable: true),
                Notes = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ContractRecords", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_ContractRecords_ExpiryDateKey", table: "ContractRecords", column: "ExpiryDateKey");
        migrationBuilder.CreateIndex(name: "IX_ContractRecords_Status", table: "ContractRecords", column: "Status");

        migrationBuilder.CreateTable(
            name: "SharedDocuments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: false),
                FileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                VaultRelativePath = table.Column<string>(type: "TEXT", nullable: true),
                FileSha256 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                UploadedByMachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                UploadedByName = table.Column<string>(type: "TEXT", nullable: false),
                UploadedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_SharedDocuments", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_SharedDocuments_Category", table: "SharedDocuments", column: "Category");
        migrationBuilder.CreateIndex(name: "IX_SharedDocuments_UpdatedAtUtc", table: "SharedDocuments", column: "UpdatedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LeaveRequests");
        migrationBuilder.DropTable(name: "CashbookEntries");
        migrationBuilder.DropTable(name: "ExpenseRequests");
        migrationBuilder.DropTable(name: "ContractRecords");
        migrationBuilder.DropTable(name: "SharedDocuments");

        migrationBuilder.DropColumn(name: "AvatarBase64", table: "LocalUsers");
        migrationBuilder.DropColumn(name: "AvatarUpdatedAtUtc", table: "LocalUsers");
        migrationBuilder.DropColumn(name: "AvatarBase64", table: "PairedDevices");
        migrationBuilder.DropColumn(name: "AvatarUpdatedAtUtc", table: "PairedDevices");
        migrationBuilder.DropColumn(name: "SubtasksJson", table: "WorkTasks");
        migrationBuilder.DropColumn(name: "RecurrenceRule", table: "WorkTasks");
        migrationBuilder.DropColumn(name: "ReplyToMessageId", table: "ChatMessages");
        migrationBuilder.DropColumn(name: "ReplyPreviewText", table: "ChatMessages");
        migrationBuilder.DropColumn(name: "IsDeleted", table: "AttendanceRecords");
    }
}
