using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MongoDB.EntityFrameworkCore.Extensions;

namespace Grafirio.Commerce.Api.Modules.Discount;

public class DiscountDbContext(DbContextOptions<DiscountDbContext> options) : DbContext(options)
{
    public DbSet<DiscountEntity> Discounts { get; set; } = null!;

    public static DiscountDbContext Create(IMongoDatabase database)
    {
        var opts = new DbContextOptionsBuilder<DiscountDbContext>()
            .UseMongoDB(database.Client, database.DatabaseNamespace.DatabaseName)
            .Options;
        return new DiscountDbContext(opts);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DiscountEntity>(b =>
        {
            b.ToCollection("discounts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.UserId).HasElementName("userId");
            b.Property(x => x.Rate).HasElementName("rate");
            b.Property(x => x.Code).HasElementName("code").HasMaxLength(10);
            b.Property(x => x.Created).HasElementName("created");
            b.Property(x => x.Updated).HasElementName("updated");
            b.Property(x => x.Expired).HasElementName("expired");
        });
    }
}
