using Microsoft.Data.SqlClient;
using Dapper;
using Bogus;

namespace Grafirio.DummyData.Generator;

public class DataGenerator
{
    private readonly string _connectionString;
    private readonly Random _random = new Random(42);
    
    // Configuration
    private const int BATCH_SIZE = 5000;
    private const int CATEGORY_COUNT = 25;
    private const int PRODUCT_COUNT = 67500;
    private const int CUSTOMER_COUNT = 450;
    private const int ORDER_COUNT = 120000;
    private const int DISCOUNT_COUNT = 50;

    // In-memory IDs for foreign key references
    private List<int> _categoryIds = new();
    private List<int> _productIds = new();
    private List<int> _customerIds = new();
    private List<int> _addressIds = new();
    private List<int> _orderIds = new();
    private List<int> _discountIds = new();
    private List<int> _cartIds = new();

    public DataGenerator(string connectionString)
    {
        _connectionString = connectionString;
        Randomizer.Seed = new Random(42);
    }

    public async Task GenerateAllDataAsync()
    {
        await GenerateCategoriesAsync();
        await GenerateProductsAsync();
        await GenerateProductImagesAsync();
        await GenerateDiscountsAsync();
        await GenerateProductDiscountsAsync();
        await GenerateCustomersAsync();
        await GenerateAddressesAsync();
        await GenerateCartsAsync();
        await GenerateCartItemsAsync();
        await GenerateOrdersAsync();
        await GenerateOrderItemsAsync();
        await GenerateReviewsAsync();
        await GenerateWishlistsAsync();
        await GenerateInventoryLogsAsync();
    }

    private async Task GenerateCategoriesAsync()
    {
        Console.WriteLine("\n📁 Generating Categories...");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Main categories (no parent)
        var mainCategories = new[]
        {
            ("Electronics", "Elektronik cihazlar ve aksesuarlar"),
            ("Fashion", "Giyim ve moda ürünleri"),
            ("Home & Garden", "Ev ve bahçe ürünleri"),
            ("Sports & Outdoor", "Spor ve outdoor ürünler"),
            ("Books & Media", "Kitap, müzik ve medya")
        };

        foreach (var (name, desc) in mainCategories)
        {
            var id = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO Categories (Name, Description, ParentCategoryId, IsActive, DisplayOrder, CreatedDate)
                  OUTPUT INSERTED.Id
                  VALUES (@Name, @Description, NULL, 1, @Order, GETUTCDATE())",
                new { Name = name, Description = desc, Order = _categoryIds.Count });
            
            _categoryIds.Add(id);
        }

        // Sub-categories
        var subCategories = new Dictionary<string, string[]>
        {
            ["Electronics"] = new[] { "Smartphones", "Laptops", "Tablets", "Cameras" },
            ["Fashion"] = new[] { "Men's Clothing", "Women's Clothing", "Kids", "Accessories" },
            ["Home & Garden"] = new[] { "Furniture", "Kitchen", "Decoration", "Garden Tools" },
            ["Sports & Outdoor"] = new[] { "Fitness", "Camping", "Cycling", "Team Sports" },
            ["Books & Media"] = new[] { "Fiction", "Non-Fiction", "Music", "Movies" }
        };

        int order = mainCategories.Length;
        foreach (var mainCat in mainCategories)
        {
            var parentId = _categoryIds[Array.IndexOf(mainCategories.Select(c => c.Item1).ToArray(), mainCat.Item1)];
            
            foreach (var subName in subCategories[mainCat.Item1])
            {
                var id = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO Categories (Name, Description, ParentCategoryId, IsActive, DisplayOrder, CreatedDate)
                      OUTPUT INSERTED.Id
                      VALUES (@Name, @Description, @ParentId, 1, @Order, GETUTCDATE())",
                    new { Name = subName, Description = $"{subName} products", ParentId = parentId, Order = order++ });
                
                _categoryIds.Add(id);
            }
        }

        Console.WriteLine($"   ✅ Created {_categoryIds.Count} categories");
    }

    private async Task GenerateProductsAsync()
    {
        Console.WriteLine("\n📦 Generating Products (This will take a while)...");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        int skuCounter = 100000; // Ensure unique SKUs
        var productFaker = new Faker<Product>()
            .RuleFor(p => p.Name, f => GenerateProductName(f))
            .RuleFor(p => p.Description, f => f.Lorem.Sentences(3))
            .RuleFor(p => p.SKU, f => $"SKU-{System.Threading.Interlocked.Increment(ref skuCounter)}")
            .RuleFor(p => p.CategoryId, f => f.PickRandom(_categoryIds))
            .RuleFor(p => p.Price, f => Math.Round(f.Random.Decimal(9.99m, 2999.99m), 2))
            .RuleFor(p => p.CostPrice, (f, p) => Math.Round(p.Price * 0.6m, 2))
            .RuleFor(p => p.Stock, f => f.Random.Number(0, 500))
            .RuleFor(p => p.MinStock, f => f.Random.Number(5, 20))
            .RuleFor(p => p.Brand, f => f.Company.CompanyName())
            .RuleFor(p => p.Rating, f => Math.Round(f.Random.Decimal(3.0m, 5.0m), 2))
            .RuleFor(p => p.ReviewCount, f => f.Random.Number(0, 500))
            .RuleFor(p => p.IsActive, f => f.Random.Bool(0.95f))
            .RuleFor(p => p.IsFeatured, f => f.Random.Bool(0.15f));

        int totalBatches = (int)Math.Ceiling(PRODUCT_COUNT / (double)BATCH_SIZE);
        
        for (int batch = 0; batch < totalBatches; batch++)
        {
            int batchSize = Math.Min(BATCH_SIZE, PRODUCT_COUNT - (batch * BATCH_SIZE));
            var products = productFaker.Generate(batchSize);

            foreach (var product in products)
            {
                var id = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO Products (Name, Description, SKU, CategoryId, Price, CostPrice, Stock, MinStock, Brand, Rating, ReviewCount, IsActive, IsFeatured, CreatedDate, UpdatedDate)
                      OUTPUT INSERTED.Id
                      VALUES (@Name, @Description, @SKU, @CategoryId, @Price, @CostPrice, @Stock, @MinStock, @Brand, @Rating, @ReviewCount, @IsActive, @IsFeatured, GETUTCDATE(), GETUTCDATE())",
                    product);
                
                _productIds.Add(id);
            }

            Program.ShowProgress("Products", (batch + 1) * batchSize, PRODUCT_COUNT);
        }

        Console.WriteLine($"\n   ✅ Created {_productIds.Count:N0} products");
    }

    private async Task GenerateProductImagesAsync()
    {
        Console.WriteLine("\n🖼️  Generating Product Images...");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        int totalImages = 0;
        int batchCount = 0;
        var batch = new List<ProductImage>();

        foreach (var productId in _productIds)
        {
            int imageCount = _random.Next(2, 4); // 2-3 images per product
            
            for (int i = 0; i < imageCount; i++)
            {
                batch.Add(new ProductImage
                {
                    ProductId = productId,
                    ImageUrl = $"https://picsum.photos/800/600?random={productId}-{i}",
                    DisplayOrder = i,
                    IsMain = i == 0
                });

                if (batch.Count >= BATCH_SIZE)
                {
                    await connection.ExecuteAsync(
                        @"INSERT INTO ProductImages (ProductId, ImageUrl, DisplayOrder, IsMain)
                          VALUES (@ProductId, @ImageUrl, @DisplayOrder, @IsMain)",
                        batch);
                    
                    totalImages += batch.Count;
                    batchCount++;
                    Program.ShowProgress("Product Images", totalImages, _productIds.Count * 3);
                    batch.Clear();
                }
            }
        }

        if (batch.Any())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO ProductImages (ProductId, ImageUrl, DisplayOrder, IsMain)
                  VALUES (@ProductId, @ImageUrl, @DisplayOrder, @IsMain)",
                batch);
            totalImages += batch.Count;
        }

        Console.WriteLine($"\n   ✅ Created {totalImages:N0} product images");
    }

    private async Task GenerateDiscountsAsync()
    {
        Console.WriteLine("\n🎟️  Generating Discounts...");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var discountFaker = new Faker<Discount>()
            .RuleFor(d => d.Code, f => $"DISC{f.Random.Number(1000, 9999)}")
            .RuleFor(d => d.Name, f => f.Commerce.ProductAdjective() + " Sale")
            .RuleFor(d => d.Description, f => f.Lorem.Sentence())
            .RuleFor(d => d.DiscountType, f => f.PickRandom("Percentage", "FixedAmount"))
            .RuleFor(d => d.DiscountValue, (f, d) => d.DiscountType == "Percentage" 
                ? f.Random.Number(5, 50) 
                : f.Random.Number(10, 100))
            .RuleFor(d => d.MinOrderAmount, f => f.Random.Decimal(0, 100))
            .RuleFor(d => d.StartDate, f => f.Date.Past(1))
            .RuleFor(d => d.EndDate, f => f.Date.Future(1))
            .RuleFor(d => d.UsageLimit, f => f.Random.Number(100, 10000))
            .RuleFor(d => d.UsedCount, f => f.Random.Number(0, 50))
            .RuleFor(d => d.IsActive, f => f.Random.Bool(0.7f));

        var discounts = discountFaker.Generate(DISCOUNT_COUNT);
        
        foreach (var discount in discounts)
        {
            var id = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO Discounts (Code, Name, Description, DiscountType, DiscountValue, MinOrderAmount, StartDate, EndDate, UsageLimit, UsedCount, IsActive)
                  OUTPUT INSERTED.Id
                  VALUES (@Code, @Name, @Description, @DiscountType, @DiscountValue, @MinOrderAmount, @StartDate, @EndDate, @UsageLimit, @UsedCount, @IsActive)",
                discount);
            
            _discountIds.Add(id);
        }

        Console.WriteLine($"   ✅ Created {_discountIds.Count} discounts");
    }

    private async Task GenerateProductDiscountsAsync()
    {
        Console.WriteLine("\n💰 Applying Discounts to Products (40%)...");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        int discountedProductCount = (int)(PRODUCT_COUNT * 0.4); // 40% of products
        var selectedProducts = _productIds.OrderBy(x => _random.Next()).Take(discountedProductCount).ToList();

        var batch = new List<object>();
        int count = 0;

        foreach (var productId in selectedProducts)
        {
            batch.Add(new
            {
                ProductId = productId,
                DiscountId = _discountIds[_random.Next(_discountIds.Count)],
                Priority = _random.Next(1, 10)
            });

            if (batch.Count >= BATCH_SIZE)
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO ProductDiscounts (ProductId, DiscountId, Priority)
                      VALUES (@ProductId, @DiscountId, @Priority)",
                    batch);
                
                count += batch.Count;
                Program.ShowProgress("Product Discounts", count, discountedProductCount);
                batch.Clear();
            }
        }

        if (batch.Any())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO ProductDiscounts (ProductId, DiscountId, Priority)
                  VALUES (@ProductId, @DiscountId, @Priority)",
                batch);
            count += batch.Count;
        }

        Console.WriteLine($"\n   ✅ Created {count:N0} product discounts");
    }

    private async Task GenerateCustomersAsync()
    {
        Console.WriteLine("\n👥 Generating Customers...");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var customerFaker = new Faker<Customer>()
            .RuleFor(c => c.Email, f => f.Internet.Email())
            .RuleFor(c => c.FirstName, f => f.Name.FirstName())
            .RuleFor(c => c.LastName, f => f.Name.LastName())
            .RuleFor(c => c.Phone, f => f.Phone.PhoneNumber("(###) ###-####")) // Fixed: Max 14 characters
            .RuleFor(c => c.BirthDate, f => f.Date.Past(50, DateTime.Now.AddYears(-18)))
            .RuleFor(c => c.Gender, f => f.PickRandom("Male", "Female", "Other"))
            .RuleFor(c => c.RegistrationDate, f => f.Date.Past(2))
            .RuleFor(c => c.LastLoginDate, f => f.Date.Recent(30))
            .RuleFor(c => c.IsEmailVerified, f => f.Random.Bool(0.8f))
            .RuleFor(c => c.CustomerType, f => f.Random.WeightedRandom(
                new[] { "Regular", "Premium", "VIP" },
                new[] { 0.7f, 0.25f, 0.05f }))
            .RuleFor(c => c.Country, f => f.Address.Country())
            .RuleFor(c => c.City, f => f.Address.City())
            .RuleFor(c => c.IsActive, f => f.Random.Bool(0.95f));

        var customers = customerFaker.Generate(CUSTOMER_COUNT);
        
        foreach (var customer in customers)
        {
            var id = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO Customers (Email, FirstName, LastName, Phone, BirthDate, Gender, RegistrationDate, LastLoginDate, IsEmailVerified, CustomerType, Country, City, IsActive)
                  OUTPUT INSERTED.Id
                  VALUES (@Email, @FirstName, @LastName, @Phone, @BirthDate, @Gender, @RegistrationDate, @LastLoginDate, @IsEmailVerified, @CustomerType, @Country, @City, @IsActive)",
                customer);
            
            _customerIds.Add(id);
        }

        Console.WriteLine($"   ✅ Created {_customerIds.Count} customers");
    }

    private async Task GenerateAddressesAsync()
    {
        Console.WriteLine("\n🏠 Generating Addresses...");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var addressFaker = new Faker<Address>()
            .RuleFor(a => a.AddressType, f => f.PickRandom("Shipping", "Billing"))
            .RuleFor(a => a.Country, f => f.Address.Country())
            .RuleFor(a => a.City, f => f.Address.City())
            .RuleFor(a => a.District, f => f.Address.County())
            .RuleFor(a => a.Street, f => f.Address.StreetAddress())
            .RuleFor(a => a.PostalCode, f => f.Address.ZipCode())
            .RuleFor(a => a.IsDefault, f => f.Random.Bool(0.3f));

        int totalAddresses = 0;
        foreach (var customerId in _customerIds)
        {
            int addressCount = _random.Next(1, 3); // 1-2 addresses per customer
            var addresses = addressFaker.Generate(addressCount);
            
            foreach (var address in addresses)
            {
                address.CustomerId = customerId;
                var id = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO Addresses (CustomerId, AddressType, Country, City, District, Street, PostalCode, IsDefault)
                      OUTPUT INSERTED.Id
                      VALUES (@CustomerId, @AddressType, @Country, @City, @District, @Street, @PostalCode, @IsDefault)",
                    address);
                
                _addressIds.Add(id);
                totalAddresses++;
            }
        }

        Console.WriteLine($"   ✅ Created {totalAddresses} addresses\n");
    }

    private async Task GenerateCartsAsync()
    {
        Console.WriteLine("🛒 Generating Carts (Active shopping carts)...");

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // 30% of customers have active carts
        int cartCount = (int)(_customerIds.Count * 0.3);
        var selectedCustomers = _customerIds.OrderBy(x => Guid.NewGuid()).Take(cartCount).ToList();

        var cartFaker = new Faker<Cart>()
            .RuleFor(c => c.CustomerId, f => f.PickRandom(selectedCustomers))
            .RuleFor(c => c.CreatedDate, f => f.Date.Recent(30)) // Last 30 days
            .RuleFor(c => c.UpdatedDate, (f, c) => c.CreatedDate.AddHours(f.Random.Int(1, 48)));

        var carts = cartFaker.Generate(cartCount);

        foreach (var cart in carts)
        {
            var id = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO Carts (CustomerId, CreatedDate, UpdatedDate)
                  OUTPUT INSERTED.Id
                  VALUES (@CustomerId, @CreatedDate, @UpdatedDate)",
                cart);
            _cartIds.Add(id);
        }

        Console.WriteLine($"   ✅ Created {cartCount} active carts\n");
    }

    private async Task GenerateCartItemsAsync()
    {
        Console.WriteLine("🛍️  Generating Cart Items...");

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var cartItems = new List<CartItem>();

        foreach (var cartId in _cartIds)
        {
            // Each cart has 1-5 items
            int itemCount = _random.Next(1, 6);
            var selectedProducts = _productIds.OrderBy(x => Guid.NewGuid()).Take(itemCount).ToList();

            foreach (var productId in selectedProducts)
            {
                // Get product price
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

        // Batch insert
        const string insertSql = @"
            INSERT INTO CartItems (CartId, ProductId, Quantity, UnitPrice, AddedDate)
            VALUES (@CartId, @ProductId, @Quantity, @UnitPrice, @AddedDate)";

        int totalBatches = (int)Math.Ceiling((double)cartItems.Count / BATCH_SIZE);
        for (int i = 0; i < totalBatches; i++)
        {
            var batch = cartItems.Skip(i * BATCH_SIZE).Take(BATCH_SIZE);
            await connection.ExecuteAsync(insertSql, batch);
            Program.ShowProgress("Cart Items", (i + 1) * BATCH_SIZE, cartItems.Count);
        }

        Console.WriteLine($"\r   ✅ Created {cartItems.Count} cart items\n");
    }

    private async Task GenerateOrdersAsync()
    {
        Console.WriteLine("\n📋 Generating Orders (This will take a while)...");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var startDate = DateTime.UtcNow.AddYears(-2);
        var endDate = DateTime.UtcNow;
        int orderNumberCounter = 100000; // Ensure unique order numbers
        
        var orderFaker = new Faker<Order>()
            .RuleFor(o => o.CustomerId, f => f.PickRandom(_customerIds))
            .RuleFor(o => o.OrderNumber, f => $"ORD-{System.Threading.Interlocked.Increment(ref orderNumberCounter)}")
            .RuleFor(o => o.OrderDate, f => f.Date.Between(startDate, endDate))
            .RuleFor(o => o.Status, f => f.PickRandom(new[] { "Delivered", "Delivered", "Delivered", "Shipped", "Processing", "Cancelled" }))
            .RuleFor(o => o.PaymentMethod, f => f.PickRandom("Credit Card", "Debit Card", "PayPal", "Cash on Delivery"))
            .RuleFor(o => o.PaymentStatus, (f, o) => o.Status == "Cancelled" ? "Refunded" : "Completed");

        int totalBatches = (int)Math.Ceiling(ORDER_COUNT / (double)BATCH_SIZE);
        
        for (int batch = 0; batch < totalBatches; batch++)
        {
            int batchSize = Math.Min(BATCH_SIZE, ORDER_COUNT - (batch * BATCH_SIZE));
            var orders = orderFaker.Generate(batchSize);

            foreach (var order in orders)
            {
                // Get customer's addresses
                var customerAddresses = _addressIds.Take(2).ToList(); // Simplified
                order.ShippingAddressId = customerAddresses.Any() ? customerAddresses[0] : (int?)null;
                order.BillingAddressId = customerAddresses.Any() ? customerAddresses[0] : (int?)null;
                
                order.SubTotal = Math.Round(_random.Next(50, 1000) * 1.0m, 2);
                order.DiscountAmount = Math.Round(order.SubTotal * 0.1m, 2);
                order.TaxAmount = Math.Round(order.SubTotal * 0.18m, 2);
                order.ShippingCost = 15.00m;
                order.TotalAmount = order.SubTotal - order.DiscountAmount + order.TaxAmount + order.ShippingCost;
                
                if (order.Status == "Shipped" || order.Status == "Delivered")
                {
                    order.ShippedDate = order.OrderDate.AddDays(_random.Next(1, 3));
                }
                if (order.Status == "Delivered")
                {
                    order.DeliveredDate = order.ShippedDate?.AddDays(_random.Next(2, 7));
                }

                var id = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO Orders (CustomerId, OrderNumber, OrderDate, ShippingAddressId, BillingAddressId, SubTotal, DiscountAmount, TaxAmount, ShippingCost, TotalAmount, Status, PaymentMethod, PaymentStatus, TrackingNumber, ShippedDate, DeliveredDate)
                      OUTPUT INSERTED.Id
                      VALUES (@CustomerId, @OrderNumber, @OrderDate, @ShippingAddressId, @BillingAddressId, @SubTotal, @DiscountAmount, @TaxAmount, @ShippingCost, @TotalAmount, @Status, @PaymentMethod, @PaymentStatus, @TrackingNumber, @ShippedDate, @DeliveredDate)",
                    order);
                
                _orderIds.Add(id);
            }

            Program.ShowProgress("Orders", (batch + 1) * batchSize, ORDER_COUNT);
        }

        Console.WriteLine($"\n   ✅ Created {_orderIds.Count:N0} orders");
    }

    private async Task GenerateOrderItemsAsync()
    {
        Console.WriteLine("\n📦 Generating Order Items...");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        int totalItems = 0;
        var batch = new List<OrderItem>();

        foreach (var orderId in _orderIds)
        {
            int itemCount = _random.Next(1, 6); // 1-5 items per order
            
            for (int i = 0; i < itemCount; i++)
            {
                var productId = _productIds[_random.Next(_productIds.Count)];
                var unitPrice = Math.Round(_random.Next(10, 500) * 1.0m, 2);
                var quantity = _random.Next(1, 4);
                
                batch.Add(new OrderItem
                {
                    OrderId = orderId,
                    ProductId = productId,
                    ProductName = $"Product {productId}",
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    DiscountAmount = Math.Round(unitPrice * 0.05m, 2),
                    TotalPrice = unitPrice * quantity
                });

                if (batch.Count >= BATCH_SIZE)
                {
                    await connection.ExecuteAsync(
                        @"INSERT INTO OrderItems (OrderId, ProductId, ProductName, Quantity, UnitPrice, DiscountAmount, TotalPrice)
                          VALUES (@OrderId, @ProductId, @ProductName, @Quantity, @UnitPrice, @DiscountAmount, @TotalPrice)",
                        batch);
                    
                    totalItems += batch.Count;
                    Program.ShowProgress("Order Items", totalItems, ORDER_COUNT * 3);
                    batch.Clear();
                }
            }
        }

        if (batch.Any())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO OrderItems (OrderId, ProductId, ProductName, Quantity, UnitPrice, DiscountAmount, TotalPrice)
                  VALUES (@OrderId, @ProductId, @ProductName, @Quantity, @UnitPrice, @DiscountAmount, @TotalPrice)",
                batch);
            totalItems += batch.Count;
        }

        Console.WriteLine($"\n   ✅ Created {totalItems:N0} order items");
    }

    private async Task GenerateReviewsAsync()
    {
        Console.WriteLine("\n⭐ Generating Reviews (30% of products)...");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        int reviewCount = (int)(PRODUCT_COUNT * 0.3);
        var reviewedProducts = _productIds.OrderBy(x => _random.Next()).Take(reviewCount).ToList();

        var reviewFaker = new Faker<Review>()
            .RuleFor(r => r.CustomerId, f => f.PickRandom(_customerIds))
            .RuleFor(r => r.Rating, f => f.Random.Number(1, 5))
            .RuleFor(r => r.Title, f => f.Lorem.Sentence(3))
            .RuleFor(r => r.Comment, f => f.Lorem.Paragraph())
            .RuleFor(r => r.IsVerifiedPurchase, f => f.Random.Bool(0.7f))
            .RuleFor(r => r.HelpfulCount, f => f.Random.Number(0, 50))
            .RuleFor(r => r.ReviewDate, f => f.Date.Past(1))
            .RuleFor(r => r.Status, f => f.PickRandom("Approved", "Approved", "Pending"));

        var batch = new List<Review>();
        int count = 0;

        foreach (var productId in reviewedProducts)
        {
            var review = reviewFaker.Generate();
            review.ProductId = productId;
            batch.Add(review);

            if (batch.Count >= BATCH_SIZE)
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO Reviews (ProductId, CustomerId, Rating, Title, Comment, IsVerifiedPurchase, HelpfulCount, ReviewDate, Status)
                      VALUES (@ProductId, @CustomerId, @Rating, @Title, @Comment, @IsVerifiedPurchase, @HelpfulCount, @ReviewDate, @Status)",
                    batch);
                
                count += batch.Count;
                Program.ShowProgress("Reviews", count, reviewCount);
                batch.Clear();
            }
        }

        if (batch.Any())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO Reviews (ProductId, CustomerId, Rating, Title, Comment, IsVerifiedPurchase, HelpfulCount, ReviewDate, Status)
                  VALUES (@ProductId, @CustomerId, @Rating, @Title, @Comment, @IsVerifiedPurchase, @HelpfulCount, @ReviewDate, @Status)",
                batch);
            count += batch.Count;
        }

        Console.WriteLine($"\n   ✅ Created {count:N0} reviews");
    }

    private async Task GenerateWishlistsAsync()
    {
        Console.WriteLine("\n💝 Generating Wishlists...");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        int targetWishlistCount = CUSTOMER_COUNT * 5; // ~5 wishlist items per customer average
        var batch = new List<object>();

        for (int i = 0; i < targetWishlistCount; i++)
        {
            batch.Add(new
            {
                CustomerId = _customerIds[_random.Next(_customerIds.Count)],
                ProductId = _productIds[_random.Next(_productIds.Count)],
                AddedDate = DateTime.UtcNow.AddDays(-_random.Next(0, 365))
            });

            if (batch.Count >= BATCH_SIZE)
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO Wishlists (CustomerId, ProductId, AddedDate)
                      VALUES (@CustomerId, @ProductId, @AddedDate)
                      ON CONFLICT DO NOTHING", // Handle duplicates
                    batch);
                batch.Clear();
            }
        }

        if (batch.Any())
        {
            try
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO Wishlists (CustomerId, ProductId, AddedDate)
                      VALUES (@CustomerId, @ProductId, @AddedDate)",
                    batch);
            }
            catch
            {
                // Ignore duplicate errors
            }
        }

        var wishlistCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Wishlists");
        Console.WriteLine($"   ✅ Created {wishlistCount:N0} wishlist entries");
    }

    private async Task GenerateInventoryLogsAsync()
    {
        Console.WriteLine("\n📊 Generating Inventory Logs...");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        int logCount = 5000;
        var batch = new List<object>();

        for (int i = 0; i < logCount; i++)
        {
            var productId = _productIds[_random.Next(_productIds.Count)];
            var changeType = _random.Next(3) switch
            {
                0 => "In",
                1 => "Out",
                _ => "Adjustment"
            };
            var quantity = _random.Next(1, 100) * (changeType == "Out" ? -1 : 1);
            var previousStock = _random.Next(0, 500);

            batch.Add(new
            {
                ProductId = productId,
                ChangeType = changeType,
                Quantity = quantity,
                PreviousStock = previousStock,
                NewStock = previousStock + quantity,
                Reason = $"Automated {changeType} transaction",
                CreatedDate = DateTime.UtcNow.AddDays(-_random.Next(0, 730)),
                CreatedBy = "System"
            });

            if (batch.Count >= BATCH_SIZE)
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO Inventory (ProductId, ChangeType, Quantity, PreviousStock, NewStock, Reason, CreatedDate, CreatedBy)
                      VALUES (@ProductId, @ChangeType, @Quantity, @PreviousStock, @NewStock, @Reason, @CreatedDate, @CreatedBy)",
                    batch);
                batch.Clear();
            }
        }

        if (batch.Any())
        {
            await connection.ExecuteAsync(
                @"INSERT INTO Inventory (ProductId, ChangeType, Quantity, PreviousStock, NewStock, Reason, CreatedDate, CreatedBy)
                  VALUES (@ProductId, @ChangeType, @Quantity, @PreviousStock, @NewStock, @Reason, @CreatedDate, @CreatedBy)",
                batch);
        }

        Console.WriteLine($"   ✅ Created {logCount:N0} inventory logs");
    }

    private string GenerateProductName(Faker f)
    {
        var adjectives = new[] { "Premium", "Professional", "Deluxe", "Essential", "Advanced", "Smart", "Ultra" };
        var types = new[] { "Tool", "Device", "System", "Kit", "Set", "Package", "Bundle" };
        
        return $"{f.PickRandom(adjectives)} {f.Commerce.ProductName()} {f.PickRandom(types)}";
    }
}

// DTOs
public class Product
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string SKU { get; set; }
    public int CategoryId { get; set; }
    public decimal Price { get; set; }
    public decimal CostPrice { get; set; }
    public int Stock { get; set; }
    public int MinStock { get; set; }
    public string Brand { get; set; }
    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }
    public bool IsActive { get; set; }
    public bool IsFeatured { get; set; }
}

public class ProductImage
{
    public int ProductId { get; set; }
    public string ImageUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsMain { get; set; }
}

public class Discount
{
    public string Code { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal MinOrderAmount { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int UsageLimit { get; set; }
    public int UsedCount { get; set; }
    public bool IsActive { get; set; }
}

public class Customer
{
    public string Email { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Phone { get; set; }
    public DateTime BirthDate { get; set; }
    public string Gender { get; set; }
    public DateTime RegistrationDate { get; set; }
    public DateTime LastLoginDate { get; set; }
    public bool IsEmailVerified { get; set; }
    public string CustomerType { get; set; }
    public string Country { get; set; }
    public string City { get; set; }
    public bool IsActive { get; set; }
}

public class Address
{
    public int CustomerId { get; set; }
    public string AddressType { get; set; }
    public string Country { get; set; }
    public string City { get; set; }
    public string District { get; set; }
    public string Street { get; set; }
    public string PostalCode { get; set; }
    public bool IsDefault { get; set; }
}

public class Order
{
    public int CustomerId { get; set; }
    public string OrderNumber { get; set; }
    public DateTime OrderDate { get; set; }
    public int? ShippingAddressId { get; set; }
    public int? BillingAddressId { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; }
    public string PaymentMethod { get; set; }
    public string PaymentStatus { get; set; }
    public string TrackingNumber { get; set; }
    public DateTime? ShippedDate { get; set; }
    public DateTime? DeliveredDate { get; set; }
}

public class OrderItem
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalPrice { get; set; }
}

public class Review
{
    public int ProductId { get; set; }
    public int CustomerId { get; set; }
    public int Rating { get; set; }
    public string Title { get; set; }
    public string Comment { get; set; }
    public bool IsVerifiedPurchase { get; set; }
    public int HelpfulCount { get; set; }
    public DateTime ReviewDate { get; set; }
    public string Status { get; set; }
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
