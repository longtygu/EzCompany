using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace EzCompany.Core.Data.Migrations.Telemetry;

[DbContext(typeof(TelemetryDbContext))]
partial class TelemetryDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.25");

        modelBuilder.Entity("EzCompany.Core.Data.Entities.AppUsageRecordEntity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<long>("ActiveSeconds").HasColumnType("INTEGER");
            b.Property<string>("DateKey").IsRequired().HasMaxLength(16).HasColumnType("TEXT");
            b.Property<long>("LastSeenUtc").HasColumnType("INTEGER");
            b.Property<string>("MachineId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("ProcessName").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("SanitizedWindowTitle").IsRequired().HasMaxLength(512).HasColumnType("TEXT");
            b.Property<Guid>("UserId").HasColumnType("TEXT");
            b.Property<string>("Username").IsRequired().HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("LastSeenUtc");
            b.HasIndex("MachineId", "DateKey", "ProcessName", "SanitizedWindowTitle");
            b.ToTable("AppUsageRecords");
        });

        modelBuilder.Entity("EzCompany.Core.Data.Entities.EncryptedSnapshotEntity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<long>("CapturedAtUtc").HasColumnType("INTEGER");
            b.Property<byte[]>("FullImageEncryptedBlob").IsRequired().HasColumnType("BLOB");
            b.Property<int>("FullImageSizeBytes").HasColumnType("INTEGER");
            b.Property<int>("Height").HasColumnType("INTEGER");
            b.Property<bool>("IsFullDownloadedOnDirector").HasColumnType("INTEGER");
            b.Property<string>("MachineId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<byte[]>("ThumbnailEncryptedBlob").IsRequired().HasColumnType("BLOB");
            b.Property<int>("ThumbnailSizeBytes").HasColumnType("INTEGER");
            b.Property<Guid>("UserId").HasColumnType("TEXT");
            b.Property<string>("Username").IsRequired().HasColumnType("TEXT");
            b.Property<int>("Width").HasColumnType("INTEGER");
            b.HasKey("Id");
            b.HasIndex("MachineId", "CapturedAtUtc");
            b.ToTable("EncryptedSnapshots");
        });

        modelBuilder.Entity("EzCompany.Core.Data.Entities.PresenceSessionEntity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("DateKey").IsRequired().HasMaxLength(16).HasColumnType("TEXT");
            b.Property<long>("DurationSeconds").HasColumnType("INTEGER");
            b.Property<long>("EndedAtUtc").HasColumnType("INTEGER");
            b.Property<string>("MachineId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<long>("StartedAtUtc").HasColumnType("INTEGER");
            b.Property<string>("State").IsRequired().HasMaxLength(32).HasColumnType("TEXT");
            b.Property<long>("UpdatedAtUtc").HasColumnType("INTEGER");
            b.Property<Guid>("UserId").HasColumnType("TEXT");
            b.Property<string>("Username").IsRequired().HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("UpdatedAtUtc");
            b.HasIndex("MachineId", "DateKey");
            b.ToTable("PresenceSessions");
        });

        modelBuilder.Entity("EzCompany.Core.Data.Entities.SyncCheckpointEntity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<long>("LastSnapshotPulledUtc").HasColumnType("INTEGER");
            b.Property<long>("LastTelemetryPulledUtc").HasColumnType("INTEGER");
            b.Property<string>("PeerMachineId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            b.Property<long>("UpdatedAtUtc").HasColumnType("INTEGER");
            b.HasKey("Id");
            b.HasIndex("PeerMachineId").IsUnique();
            b.ToTable("SyncCheckpoints");
        });
    }
}
