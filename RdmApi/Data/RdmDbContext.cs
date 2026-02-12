using Microsoft.EntityFrameworkCore;
using RdmApi.Data.Entities;

namespace RdmApi.Data;

public class RdmDbContext : DbContext
{
    public RdmDbContext(DbContextOptions<RdmDbContext> options)
        : base(options) { }

    public DbSet<Dataset> Datasets => Set<Dataset>();
    public DbSet<DatasetVersion> DatasetVersions => Set<DatasetVersion>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Dataset>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Creator).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<DatasetVersion>(e =>
        {
            e.HasIndex(x => new { x.DatasetId, x.VersionNumber }).IsUnique();
        });

        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.Actor).HasMaxLength(200).IsRequired();
        });
    }
}