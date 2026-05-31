namespace Grafirio.Commerce.Api.Modules.Orders;

public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Code).IsRequired().HasMaxLength(10);
            b.Property(x => x.TotalPrice).HasPrecision(18, 2);
            b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId);
            b.OwnsOne(x => x.Address, a =>
            {
                a.Property(x => x.Province).IsRequired().HasMaxLength(100);
                a.Property(x => x.District).IsRequired().HasMaxLength(100);
                a.Property(x => x.Street).IsRequired().HasMaxLength(200);
                a.Property(x => x.ZipCode).IsRequired().HasMaxLength(10);
                a.Property(x => x.Line).IsRequired().HasMaxLength(500);
            });
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ProductName).IsRequired().HasMaxLength(200);
            b.Property(x => x.UnitPrice).HasPrecision(18, 2);
        });
    }
}

public class OrderDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=GrafirioECommerce;User Id=sa;Password=Password12*;TrustServerCertificate=True")
            .Options;
        return new OrderDbContext(opts);
    }
}
