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

        // Mükerrer GTIN kaydını engellemek için tekil indeks
        modelBuilder.Entity<ProductPassport>()
            .HasIndex(p => p.Gtin)
            .IsUnique();

        // Model adının tekilliği
        modelBuilder.Entity<ProductModel>()
            .HasIndex(m => m.Name)
            .IsUnique();

        // Aynı model altında aynı parti numarasının tekrarlanmasını önleyen bileşik tekil indeks
        modelBuilder.Entity<Batch>()
            .HasIndex(e => new { e.ProductModelId, e.BatchNo })
            .IsUnique();

        // Ondalıklı alanlar için veri hassasiyeti
        modelBuilder.Entity<ProductPassport>(entity =>
        {
            entity.Property(e => e.CottonPercentage).HasPrecision(5, 2);
            entity.Property(e => e.ElastanePercentage).HasPrecision(5, 2);
            entity.Property(e => e.CarbonKgCo2e).HasPrecision(8, 2);
        });
    }
}