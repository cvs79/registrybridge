using Microsoft.EntityFrameworkCore;
using RegistryBridge.Api.Catalog;

namespace RegistryBridge.Api.Data;

public sealed class RegistryBridgeDbContext(DbContextOptions<RegistryBridgeDbContext> options)
    : DbContext(options)
{
    public DbSet<CatalogRevision> CatalogRevisions => Set<CatalogRevision>();

    public DbSet<CatalogState> CatalogStates => Set<CatalogState>();

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
    }
}
