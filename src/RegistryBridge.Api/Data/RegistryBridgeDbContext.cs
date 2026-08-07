using Microsoft.EntityFrameworkCore;
using RegistryBridge.Api.Catalog;
using RegistryBridge.Api.SynchronizationRuns;

namespace RegistryBridge.Api.Data;

public sealed class RegistryBridgeDbContext(DbContextOptions<RegistryBridgeDbContext> options)
    : DbContext(options)
{
    public DbSet<CatalogRevision> CatalogRevisions => Set<CatalogRevision>();

    public DbSet<CatalogState> CatalogStates => Set<CatalogState>();

    public DbSet<SynchronizationRun> SynchronizationRuns => Set<SynchronizationRun>();

    public DbSet<ArtifactOutcomeRecord> ArtifactOutcomes => Set<ArtifactOutcomeRecord>();

    public DbSet<RunLog> RunLogs => Set<RunLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogRevision>(entity =>
        {
            entity.ToTable("catalog_revisions");
            entity.HasKey(revision => revision.Id);
            entity.Property(revision => revision.CreatedAt).IsRequired();
            entity.Property(revision => revision.Definition).HasColumnType("jsonb").IsRequired();
        });

        modelBuilder.Entity<CatalogState>(entity =>
        {
            entity.ToTable("catalog_state");
            entity.HasKey(state => state.Id);
            entity.Property(state => state.Id).ValueGeneratedNever();
            entity.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
            entity.HasOne(state => state.CurrentRevision)
                .WithMany()
                .HasForeignKey(state => state.CurrentRevisionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasData(new CatalogState { Id = CatalogState.SingletonId });
        });

        modelBuilder.Entity<SynchronizationRun>(entity =>
        {
            entity.ToTable("synchronization_runs");
            entity.HasKey(run => run.Id);
            entity.Property(run => run.Origin).HasConversion<string>().IsRequired();
            entity.Property(run => run.Status).HasConversion<string>().IsRequired();
            entity.HasOne(run => run.CatalogRevision)
                .WithMany()
                .HasForeignKey(run => run.CatalogRevisionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(run => new { run.Status, run.CreatedAt });
        });

        modelBuilder.Entity<ArtifactOutcomeRecord>(entity =>
        {
            entity.ToTable("artifact_outcomes");
            entity.HasKey(outcome => outcome.Id);
            entity.Property(outcome => outcome.Disposition).HasConversion<string>().IsRequired();
            entity.Property(outcome => outcome.Detail).HasMaxLength(4096);
            entity.HasOne(outcome => outcome.SynchronizationRun)
                .WithMany(run => run.Outcomes)
                .HasForeignKey(outcome => outcome.SynchronizationRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(outcome => new { outcome.SynchronizationRunId, outcome.Order }).IsUnique();
        });

        modelBuilder.Entity<RunLog>(entity =>
        {
            entity.ToTable("run_logs");
            entity.HasKey(log => log.Id);
            entity.Property(log => log.EventType).HasMaxLength(128).IsRequired();
            entity.Property(log => log.Data).HasColumnType("jsonb").IsRequired();
            entity.HasOne(log => log.SynchronizationRun)
                .WithMany(run => run.Logs)
                .HasForeignKey(log => log.SynchronizationRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(log => new { log.SynchronizationRunId, log.OccurredAt });
        });
    }
}
