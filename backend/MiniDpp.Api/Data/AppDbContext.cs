using Microsoft.EntityFrameworkCore;
using MiniDpp.Api.Models;

namespace MiniDpp.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ProductModel> ProductModels => Set<ProductModel>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<ProductPassport> ProductPassports => Set<ProductPassport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Mükerrer GTIN kaydını engellemek için unique index
        modelBuilder.Entity<ProductPassport>()
            .HasIndex(p => p.Gtin)
            .IsUnique();

        // Model adının tekilliği
        modelBuilder.Entity<ProductModel>()
            .HasIndex(m => m.Name)
            .IsUnique();

        // Batch numarasının tekilliği
        modelBuilder.Entity<Batch>()
            .HasIndex(b => b.BatchNo)
            .IsUnique();

        // Decimal alanlar için veri hassasiyet yapılandırması
        modelBuilder.Entity<ProductPassport>(entity =>
        {
            entity.Property(e => e.CottonPercentage).HasPrecision(5, 2);
            entity.Property(e => e.ElastanePercentage).HasPrecision(5, 2);
            entity.Property(e => e.CarbonKgCo2e).HasPrecision(8, 2);
        });
    }
}