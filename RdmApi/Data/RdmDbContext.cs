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
    public DbSet<Annotation> Annotations => Set<Annotation>();
    public DbSet<DatasetRelationship> DatasetRelationships => Set<DatasetRelationship>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Dataset>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Creator).HasMaxLength(200).IsRequired();

            // ✅ Tags as Postgres text[]
            e.Property(x => x.Tags)
                .HasColumnType("text[]");
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

        modelBuilder.Entity<Annotation>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(4000).IsRequired();
            e.Property(x => x.Actor).HasMaxLength(200).IsRequired();

            e.HasIndex(x => x.DatasetId);

            e.HasOne(x => x.Dataset)
                .WithMany()
                .HasForeignKey(x => x.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.DatasetVersion)
                .WithMany()
                .HasForeignKey(x => x.DatasetVersionId)
                .OnDelete(DeleteBehavior.SetNull);
        });
        
        modelBuilder.Entity<DatasetRelationship>(b =>
        {
            b.HasKey(x => x.Id);

            b.HasOne(x => x.SourceDataset)
                .WithMany()
                .HasForeignKey(x => x.SourceDatasetId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.TargetDataset)
                .WithMany()
                .HasForeignKey(x => x.TargetDatasetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}