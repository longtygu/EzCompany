using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EzCompany.Core.Data.Migrations.Telemetry;

[DbContext(typeof(TelemetryDbContext))]
[Migration("20261123000001_InitialTelemetrySchema")]
public partial class InitialTelemetrySchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PresenceSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                MachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                Username = table.Column<string>(type: "TEXT", nullable: false),
                DateKey = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                StartedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                EndedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                DurationSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PresenceSessions", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_PresenceSessions_MachineId_DateKey",
            table: "PresenceSessions",
            columns: ["MachineId", "DateKey"]);

        migrationBuilder.CreateIndex(
            name: "IX_PresenceSessions_UpdatedAtUtc",
            table: "PresenceSessions",
            column: "UpdatedAtUtc");

        migrationBuilder.CreateTable(
            name: "AppUsageRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                MachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                Username = table.Column<string>(type: "TEXT", nullable: false),
                DateKey = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                ProcessName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                SanitizedWindowTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                ActiveSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                LastSeenUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AppUsageRecords", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_AppUsageRecords_MachineId_DateKey_ProcessName_SanitizedWindowTitle",
            table: "AppUsageRecords",
            columns: ["MachineId", "DateKey", "ProcessName", "SanitizedWindowTitle"]);

        migrationBuilder.CreateIndex(
            name: "IX_AppUsageRecords_LastSeenUtc",
            table: "AppUsageRecords",
            column: "LastSeenUtc");

        migrationBuilder.CreateTable(
            name: "EncryptedSnapshots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                MachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                Username = table.Column<string>(type: "TEXT", nullable: false),
                CapturedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                Width = table.Column<int>(type: "INTEGER", nullable: false),
                Height = table.Column<int>(type: "INTEGER", nullable: false),
                ThumbnailEncryptedBlob = table.Column<byte[]>(type: "BLOB", nullable: false),
                FullImageEncryptedBlob = table.Column<byte[]>(type: "BLOB", nullable: false),
                ThumbnailSizeBytes = table.Column<int>(type: "INTEGER", nullable: false),
                FullImageSizeBytes = table.Column<int>(type: "INTEGER", nullable: false),
                IsFullDownloadedOnDirector = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_EncryptedSnapshots", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_EncryptedSnapshots_MachineId_CapturedAtUtc",
            table: "EncryptedSnapshots",
            columns: ["MachineId", "CapturedAtUtc"]);

        migrationBuilder.CreateTable(
            name: "SyncCheckpoints",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PeerMachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                LastTelemetryPulledUtc = table.Column<long>(type: "INTEGER", nullable: false),
                LastSnapshotPulledUtc = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_SyncCheckpoints", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_SyncCheckpoints_PeerMachineId",
            table: "SyncCheckpoints",
            column: "PeerMachineId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("PresenceSessions");
        migrationBuilder.DropTable("AppUsageRecords");
        migrationBuilder.DropTable("EncryptedSnapshots");
        migrationBuilder.DropTable("SyncCheckpoints");
    }
}
