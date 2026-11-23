using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

public sealed class TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : DbContext(options)
{
    public DbSet<PresenceSessionEntity> PresenceSessions => Set<PresenceSessionEntity>();
    public DbSet<AppUsageRecordEntity> AppUsageRecords => Set<AppUsageRecordEntity>();
    public DbSet<EncryptedSnapshotEntity> EncryptedSnapshots => Set<EncryptedSnapshotEntity>();
    public DbSet<SyncCheckpointEntity> SyncCheckpoints => Set<SyncCheckpointEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetTicksConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableUtcDateTimeOffsetTicksConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PresenceSessionEntity>(e =>
        {
            e.ToTable("PresenceSessions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MachineId, x.DateKey });
            e.HasIndex(x => x.UpdatedAtUtc);
            e.Property(x => x.MachineId).HasMaxLength(128).IsRequired();
            e.Property(x => x.DateKey).HasMaxLength(16).IsRequired();
            e.Property(x => x.State).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<AppUsageRecordEntity>(e =>
        {
            e.ToTable("AppUsageRecords");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MachineId, x.DateKey, x.ProcessName, x.SanitizedWindowTitle });
            e.HasIndex(x => x.LastSeenUtc);
            e.Property(x => x.MachineId).HasMaxLength(128).IsRequired();
            e.Property(x => x.DateKey).HasMaxLength(16).IsRequired();
            e.Property(x => x.ProcessName).HasMaxLength(128).IsRequired();
            e.Property(x => x.SanitizedWindowTitle).HasMaxLength(512).IsRequired();
        });

        modelBuilder.Entity<EncryptedSnapshotEntity>(e =>
        {
            e.ToTable("EncryptedSnapshots");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MachineId, x.CapturedAtUtc });
            e.Property(x => x.MachineId).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<SyncCheckpointEntity>(e =>
        {
            e.ToTable("SyncCheckpoints");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PeerMachineId).IsUnique();
            e.Property(x => x.PeerMachineId).HasMaxLength(128).IsRequired();
        });
    }
}
