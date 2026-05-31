using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MongoDB.EntityFrameworkCore.Extensions;

namespace Grafirio.Commerce.Api.Modules.Catalog;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;

    public static CatalogDbContext Create(IMongoDatabase database)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseMongoDB(database.Client, database.DatabaseNamespace.DatabaseName)
            .Options;
        return new CatalogDbContext(opts);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(b =>
        {
            b.ToCollection("products");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasElementName("name").HasMaxLength(100);
            b.Property(x => x.Description).HasElementName("description").HasMaxLength(1000);
            b.Property(x => x.Price).HasElementName("price");
            b.Property(x => x.UserId).HasElementName("userId");
            b.Property(x => x.ImageUrl).HasElementName("imageUrl").HasMaxLength(200);
            b.Property(x => x.CategoryId).HasElementName("categoryId");
            b.Property(x => x.Created).HasElementName("created");
            b.OwnsOne(x => x.Feature, f =>
            {
                f.HasElementName("feature");
                f.Property(x => x.Duration).HasElementName("duration");
                f.Property(x => x.Rating).HasElementName("rating");
                f.Property(x => x.EducatorFullName).HasElementName("educatorFullName").HasMaxLength(100);
            });
        });

        modelBuilder.Entity<Category>(b =>
        {
            b.ToCollection("categories");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasElementName("name").HasMaxLength(100);
        });
    }
}
