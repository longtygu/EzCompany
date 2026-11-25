using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EzCompany.Core.Data.Migrations.App;

[DbContext(typeof(AppDbContext))]
[Migration("20261126000001_AddChatTimelineHrAndLogo")]
public partial class AddChatTimelineHrAndLogo : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CompanyLogoBase64",
            table: "CompanyProfiles",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "CompanyLogoUpdatedAtUtc",
            table: "CompanyProfiles",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AssignmentFileName",
            table: "WorkTasks",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AssignmentVaultPath",
            table: "WorkTasks",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AssignmentFileSha256",
            table: "WorkTasks",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ChatConversations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                ConversationType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                MemberMachineIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                MemberNamesJson = table.Column<string>(type: "TEXT", nullable: false),
                PinnedTaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                PinnedTaskSummary = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ChatConversations", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_ChatConversations_UpdatedAtUtc",
            table: "ChatConversations",
            column: "UpdatedAtUtc");

        migrationBuilder.CreateTable(
            name: "ChatMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                SenderMachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                SenderUsername = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                SenderFullName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Content = table.Column<string>(type: "TEXT", nullable: false),
                AttachmentFileName = table.Column<string>(type: "TEXT", nullable: true),
                AttachmentVaultPath = table.Column<string>(type: "TEXT", nullable: true),
                AttachmentSha256 = table.Column<string>(type: "TEXT", nullable: true),
                AttachmentSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                LinkedTaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                SentAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                PendingSyncTargetsJson = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ChatMessages", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_ChatMessages_ConversationId",
            table: "ChatMessages",
            column: "ConversationId");

        migrationBuilder.CreateIndex(
            name: "IX_ChatMessages_SentAtUtc",
            table: "ChatMessages",
            column: "SentAtUtc");

        migrationBuilder.CreateTable(
            name: "TimelinePlans",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: false),
                Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                StartDateUtc = table.Column<long>(type: "INTEGER", nullable: false),
                EndDateUtc = table.Column<long>(type: "INTEGER", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                CreatedByMachineId = table.Column<string>(type: "TEXT", nullable: false),
                CreatedByUsername = table.Column<string>(type: "TEXT", nullable: false),
                CreatedByFullName = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_TimelinePlans", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_TimelinePlans_StartDateUtc",
            table: "TimelinePlans",
            column: "StartDateUtc");

        migrationBuilder.CreateIndex(
            name: "IX_TimelinePlans_UpdatedAtUtc",
            table: "TimelinePlans",
            column: "UpdatedAtUtc");

        migrationBuilder.CreateTable(
            name: "EmployeeProfiles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                EmployeeCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                FullName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Department = table.Column<string>(type: "TEXT", nullable: false),
                PositionTitle = table.Column<string>(type: "TEXT", nullable: false),
                RolesCsv = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                LinkedMachineId = table.Column<string>(type: "TEXT", nullable: true),
                LinkedUsername = table.Column<string>(type: "TEXT", nullable: true),
                Phone = table.Column<string>(type: "TEXT", nullable: false),
                Email = table.Column<string>(type: "TEXT", nullable: false),
                NationalId = table.Column<string>(type: "TEXT", nullable: false),
                JoinDateUtc = table.Column<long>(type: "INTEGER", nullable: false),
                EmploymentStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Notes = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_EmployeeProfiles", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_EmployeeProfiles_EmployeeCode",
            table: "EmployeeProfiles",
            column: "EmployeeCode",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EmployeeProfiles_LinkedMachineId",
            table: "EmployeeProfiles",
            column: "LinkedMachineId");

        migrationBuilder.CreateTable(
            name: "AttendanceRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                EmployeeProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                EmployeeCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                EmployeeName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                MachineId = table.Column<string>(type: "TEXT", nullable: false),
                DateKey = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                CheckInUtc = table.Column<long>(type: "INTEGER", nullable: true),
                CheckOutUtc = table.Column<long>(type: "INTEGER", nullable: true),
                WorkedHours = table.Column<double>(type: "REAL", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Notes = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AttendanceRecords", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_AttendanceRecords_DateKey",
            table: "AttendanceRecords",
            column: "DateKey");

        migrationBuilder.CreateIndex(
            name: "IX_AttendanceRecords_EmployeeProfileId_DateKey",
            table: "AttendanceRecords",
            columns: ["EmployeeProfileId", "DateKey"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ChatConversations");
        migrationBuilder.DropTable("ChatMessages");
        migrationBuilder.DropTable("TimelinePlans");
        migrationBuilder.DropTable("EmployeeProfiles");
        migrationBuilder.DropTable("AttendanceRecords");

        migrationBuilder.DropColumn(name: "CompanyLogoBase64", table: "CompanyProfiles");
        migrationBuilder.DropColumn(name: "CompanyLogoUpdatedAtUtc", table: "CompanyProfiles");
        migrationBuilder.DropColumn(name: "AssignmentFileName", table: "WorkTasks");
        migrationBuilder.DropColumn(name: "AssignmentVaultPath", table: "WorkTasks");
        migrationBuilder.DropColumn(name: "AssignmentFileSha256", table: "WorkTasks");
    }
}
