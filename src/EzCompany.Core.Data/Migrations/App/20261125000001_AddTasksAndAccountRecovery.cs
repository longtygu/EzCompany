using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EzCompany.Core.Data.Migrations.App;

[DbContext(typeof(AppDbContext))]
[Migration("20261125000001_AddTasksAndAccountRecovery")]
public partial class AddTasksAndAccountRecovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WorkTasks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                TaskKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: false),
                Priority = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                AssignerMachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                AssignerUsername = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                AssignerFullName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                AssignerRole = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                AssigneeMachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                AssigneeUsername = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                AssigneeFullName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                AssigneeRole = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DueDateUtc = table.Column<long>(type: "INTEGER", nullable: true),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                CompletedAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                HandoverNotes = table.Column<string>(type: "TEXT", nullable: false),
                HandoverFileName = table.Column<string>(type: "TEXT", nullable: true),
                HandoverVaultPath = table.Column<string>(type: "TEXT", nullable: true),
                HandoverFileSha256 = table.Column<string>(type: "TEXT", nullable: true),
                HandedOverAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                ReviewNotes = table.Column<string>(type: "TEXT", nullable: false),
                PendingSyncTargetMachineId = table.Column<string>(type: "TEXT", nullable: true),
                PendingSyncAction = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_WorkTasks", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_WorkTasks_AssigneeMachineId",
            table: "WorkTasks",
            column: "AssigneeMachineId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkTasks_AssignerMachineId",
            table: "WorkTasks",
            column: "AssignerMachineId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkTasks_Status",
            table: "WorkTasks",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_WorkTasks_UpdatedAtUtc",
            table: "WorkTasks",
            column: "UpdatedAtUtc");

        migrationBuilder.CreateTable(
            name: "WorkTaskHistories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                ActorMachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ActorUsername = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ActorFullName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Note = table.Column<string>(type: "TEXT", nullable: false),
                AttachmentFileName = table.Column<string>(type: "TEXT", nullable: true),
                TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_WorkTaskHistories", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_WorkTaskHistories_TaskId",
            table: "WorkTaskHistories",
            column: "TaskId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkTaskHistories_TimestampUtc",
            table: "WorkTaskHistories",
            column: "TimestampUtc");

        migrationBuilder.CreateTable(
            name: "PasswordResetRequests",
            columns: table => new
            {
                RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                MachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                DevicePublicKeyBase64 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                FullName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                RequestedNewUsername = table.Column<string>(type: "TEXT", nullable: true),
                Reason = table.Column<string>(type: "TEXT", nullable: false),
                State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                RequestedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                ExpiresAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                DecidedAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                DecidedByAdminName = table.Column<string>(type: "TEXT", nullable: true),
                DecisionMessage = table.Column<string>(type: "TEXT", nullable: true),
                SignedResetTicketJson = table.Column<string>(type: "TEXT", nullable: true),
                RemoteIpAddress = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_PasswordResetRequests", x => x.RequestId));

        migrationBuilder.CreateIndex(
            name: "IX_PasswordResetRequests_RequestedAtUtc",
            table: "PasswordResetRequests",
            column: "RequestedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_PasswordResetRequests_State",
            table: "PasswordResetRequests",
            column: "State");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("WorkTasks");
        migrationBuilder.DropTable("WorkTaskHistories");
        migrationBuilder.DropTable("PasswordResetRequests");
    }
}
