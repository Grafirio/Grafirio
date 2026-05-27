using Microsoft.Data.SqlClient;
using Dapper;
using Bogus;

namespace Grafirio.DummyData.Generator;

public class Program
{
    private const string ConnectionString = "Server=localhost,1433;Database=GrafirioECommerce;User Id=sa;Password=Password12*;TrustServerCertificate=True;";
    
    // Configuration - Full Production Dataset
    private const int CATEGORY_COUNT = 25;
    private const int PRODUCT_COUNT = 67500; // 65k-70k range
    private const int CUSTOMER_COUNT = 450;
    private const int ORDER_COUNT = 120000; // 2 years
    private const int DISCOUNT_COUNT = 50;
    
    private static readonly Random _random = new Random(42); // Seed for reproducibility
    private static int _progressCounter = 0;

    public static async Task Main(string[] args)
    {
        // Check if only adding carts
        if (args.Length > 0 && args[0] == "--carts-only")
        {
            await AddCartsOnly.Run();
            Console.WriteLine("\n✨ Press any key to exit...");
            Console.ReadKey();
            return;
        }

        Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   Grafirio E-Commerce Dummy Data Generator                ║");
        Console.WriteLine("║   FULL PRODUCTION MODE - Enterprise Dataset                ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("⚠️  This will generate ~770,000 records!");
        Console.WriteLine("⏱️  Estimated time: 20-30 minutes");
        Console.WriteLine();

        try
        {
            Console.WriteLine("📡 Testing database connection...");
            await TestConnectionAsync();
            Console.WriteLine("✅ Database connection successful!\n");

            var startTime = DateTime.Now;
            Console.WriteLine($"� Starting data generation at {startTime:HH:mm:ss}");
            Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

            var generator = new DataGenerator(ConnectionString);
            
            await generator.GenerateAllDataAsync();

            var endTime = DateTime.Now;
            var duration = endTime - startTime;
            
            Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine("✅ GENERATION COMPLETED SUCCESSFULLY!");
            Console.WriteLine($"⏱️  Total Time: {duration.TotalMinutes:F1} minutes");
            Console.WriteLine();
            
            await PrintSummaryAsync();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ FATAL ERROR: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("\nStack Trace:");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }

        Console.WriteLine("\n✨ Press any key to exit...");
        Console.ReadKey();
    }

    private static async Task TestConnectionAsync()
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        var result = await connection.QueryFirstAsync<string>("SELECT 'Connected to: ' + DB_NAME()");
        Console.WriteLine($"   {result}");
    }

    private static async Task PrintSummaryAsync()
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        Console.WriteLine("📈 FINAL STATISTICS:");
        Console.WriteLine();

        var tables = new[]
        {
            "Categories", "Products", "ProductImages", "Customers", "Addresses",
            "Orders", "OrderItems", "Discounts", "ProductDiscounts", "Reviews",
            "Wishlists", "Inventory", "Carts", "CartItems"
        };

        long totalRecords = 0;
        foreach (var table in tables)
        {
            var count = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table}");
            totalRecords += count;
            Console.WriteLine($"   {table,-20}: {count,10:N0} records");
        }

        Console.WriteLine("   ────────────────────────────────────");
        Console.WriteLine($"   {"TOTAL",-20}: {totalRecords,10:N0} records");
        
        // Database size
        var dbSize = await connection.QueryFirstAsync<decimal>(
            "SELECT SUM(size) * 8.0 / 1024 FROM sys.database_files");
        Console.WriteLine($"\n   💾 Database Size: {dbSize:F2} MB");
    }

    public static void ShowProgress(string message, int current, int total)
    {
        var percentage = (int)((current / (double)total) * 100);
        var bar = new string('█', percentage / 2) + new string('░', 50 - percentage / 2);
        Console.Write($"\r   {message}: [{bar}] {percentage}% ({current:N0}/{total:N0})");
        
        if (current >= total)
            Console.WriteLine();
    }
}
