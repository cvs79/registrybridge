using Microsoft.EntityFrameworkCore;
using RegistryBridge.Api.Catalog;

namespace RegistryBridge.Api.Data;

public sealed class RegistryBridgeDbContext(DbContextOptions<RegistryBridgeDbContext> options)
    : DbContext(options)
{
    public DbSet<CatalogRevision> CatalogRevisions => Set<CatalogRevision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogRevision>(entity =>
        {
            entity.ToTable("catalog_revisions");
            entity.HasKey(revision => revision.Id);
            entity.Property(revision => revision.CreatedAt).IsRequired();
            entity.Property(revision => revision.Definition).HasColumnType("jsonb").IsRequired();
        });
    }
}
