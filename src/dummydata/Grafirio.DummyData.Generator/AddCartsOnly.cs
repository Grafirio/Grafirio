using Microsoft.Data.SqlClient;
using Dapper;
using Bogus;

namespace Grafirio.DummyData.Generator;

public class AddCartsOnly
{
    private const string ConnectionString = "Server=localhost,1434;Database=GrafirioECommerce;User Id=sa;Password=Test123!@#;TrustServerCertificate=True;";
    private static readonly Random _random = new Random(42);

    public static async Task Run()
    {
        Console.WriteLine("\n🛒 Adding Carts and CartItems to existing data...");
        Console.WriteLine("   Estimated time: 1-2 minutes\n");

        try
        {
            using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            // Get existing customer and product IDs
            var customerIds = (await connection.QueryAsync<int>("SELECT Id FROM Customers")).ToList();
            var productIds = (await connection.QueryAsync<int>("SELECT Id FROM Products")).ToList();

            Console.WriteLine($"   Found {customerIds.Count:N0} customers and {productIds.Count:N0} products");

            // Generate Carts (30% of customers)
            Console.WriteLine("\n🛒 Generating Carts...");
            int cartCount = (int)(customerIds.Count * 0.3);
            var selectedCustomers = customerIds.OrderBy(x => Guid.NewGuid()).Take(cartCount).ToList();

            var cartFaker = new Faker<Cart>()
                .RuleFor(c => c.CustomerId, f => f.PickRandom(selectedCustomers))
                .RuleFor(c => c.CreatedDate, f => f.Date.Recent(30))
                .RuleFor(c => c.UpdatedDate, (f, c) => c.CreatedDate.AddHours(f.Random.Int(1, 48)));

            var carts = cartFaker.Generate(cartCount);
            var cartIds = new List<int>();

            foreach (var cart in carts)
            {
                var id = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO Carts (CustomerId, CreatedDate, UpdatedDate)
                      OUTPUT INSERTED.Id
                      VALUES (@CustomerId, @CreatedDate, @UpdatedDate)",
                    cart);
                cartIds.Add(id);
            }

            Console.WriteLine($"   ✅ Created {cartCount:N0} active carts");

            // Generate CartItems
            Console.WriteLine("\n🛛 Generating Cart Items...");
            var cartItems = new List<CartItem>();

            foreach (var cartId in cartIds)
            {
                int itemCount = _random.Next(1, 6);
                var selectedProducts = productIds.OrderBy(x => Guid.NewGuid()).Take(itemCount).ToList();

                foreach (var productId in selectedProducts)
                {
                    var price = await connection.ExecuteScalarAsync<decimal>(
                        "SELECT Price FROM Products WHERE Id = @ProductId",
                        new { ProductId = productId });

                    cartItems.Add(new CartItem
                    {
                        CartId = cartId,
                        ProductId = productId,
                        Quantity = _random.Next(1, 4),
                        UnitPrice = price,
                        AddedDate = DateTime.UtcNow.AddDays(-_random.Next(0, 30))
                    });
                }
            }

            const string insertSql = @"
                INSERT INTO CartItems (CartId, ProductId, Quantity, UnitPrice, AddedDate)
                VALUES (@CartId, @ProductId, @Quantity, @UnitPrice, @AddedDate)";

            await connection.ExecuteAsync(insertSql, cartItems);

            Console.WriteLine($"   ✅ Created {cartItems.Count:N0} cart items");

            // Final summary
            Console.WriteLine("\n📊 Updated Statistics:");
            var totalCarts = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Carts");
            var totalCartItems = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CartItems");
            
            Console.WriteLine($"   Carts     : {totalCarts,10:N0} records");
            Console.WriteLine($"   CartItems : {totalCartItems,10:N0} records");
            
            Console.WriteLine("\n✅ SUCCESS! Cart data added to existing database.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ ERROR: {ex.Message}");
            Console.ResetColor();
        }
    }

    public class Cart
    {
        public int CustomerId { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }
    }

    public class CartItem
    {
        public int CartId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public DateTime AddedDate { get; set; }
    }
}
