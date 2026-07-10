using LupiraGeoApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupiraGeoApi.Data;

/// <summary>
/// The gazetteer store (EF Core + PostGIS/NetTopologySuite), schema <c>geo</c>: the shared <see cref="Place"/> catalog
/// with real <c>geography(Point,4326)</c> columns + GiST, and the <see cref="AdminArea"/> containment reference tree.
/// Reference/spatial data — NOT event-sourced (per-principal user state is Marten's, schema <c>geo_user</c>). Schema is
/// applied via EF migrations, never auto against the live DB (see the host's <c>--apply-schema</c> path).
/// </summary>
public sealed class GeoDbContext(DbContextOptions<GeoDbContext> options) : DbContext(options)
{
    public const string Schema = "geo";

    public DbSet<Place> Places => Set<Place>();
    public DbSet<PlaceAlias> PlaceAliases => Set<PlaceAlias>();
    public DbSet<PlaceExternalId> PlaceExternalIds => Set<PlaceExternalId>();
    public DbSet<AdminArea> AdminAreas => Set<AdminArea>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        // postgis is NOT a trusted extension: prod pre-creates it as superuser (grants.sql) so this IF NOT EXISTS is a
        // no-op; the postgis test container creates it here. pg_trgm is trusted, so the app role can self-create it.
        b.HasPostgresExtension("postgis");
        b.HasPostgresExtension("pg_trgm");

        b.Entity<Place>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.CanonicalName).IsRequired();
            e.Property(p => p.Kind).HasConversion<string>().IsRequired();
            e.Property(p => p.Category).HasConversion<string>().IsRequired();
            e.Property(p => p.Source).HasConversion<string>().IsRequired();
            e.Property(p => p.Location).HasColumnType("geography (Point, 4326)");

            e.HasIndex(p => p.Location).HasMethod("gist");
            e.HasIndex(p => p.CanonicalName).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(p => p.WithinAreaId);
            e.HasIndex(p => p.Category);

            e.HasOne(p => p.WithinArea).WithMany()
                .HasForeignKey(p => p.WithinAreaId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(p => p.MergedIntoId);
            e.HasOne(p => p.MergedInto).WithMany()
                .HasForeignKey(p => p.MergedIntoId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(p => p.Aliases).WithOne()
                .HasForeignKey(a => a.PlaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.ExternalIds).WithOne()
                .HasForeignKey(x => x.PlaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PlaceAlias>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Name).IsRequired();
            e.HasIndex(a => a.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(a => a.PlaceId);
        });

        b.Entity<PlaceExternalId>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Scheme).HasConversion<string>().IsRequired();
            e.Property(x => x.Value).IsRequired();
            e.HasIndex(x => new { x.Scheme, x.Value }).IsUnique();
        });

        b.Entity<AdminArea>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Level).HasConversion<string>().IsRequired();
            e.Property(a => a.Name).IsRequired();
            e.Property(a => a.Centroid).HasColumnType("geography (Point, 4326)");

            e.HasIndex(a => a.Centroid).HasMethod("gist");
            e.HasIndex(a => a.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(a => a.WithinAreaId);
            e.HasIndex(a => a.GeonamesId).IsUnique();

            e.HasOne(a => a.WithinArea).WithMany()
                .HasForeignKey(a => a.WithinAreaId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
