-- Hızlı test verisi: Customers + Orders + OrderItems
USE GrafirioECommerce;
SET NOCOUNT ON;

DECLARE @i INT = 1;
DECLARE @customerId INT;
DECLARE @orderId INT;
DECLARE @orderDate DATETIME2;
DECLARE @productId INT;
DECLARE @productPrice DECIMAL(18,2);
DECLARE @orderNumber INT = 200000;
DECLARE @totalOrders INT = 8000;

-- ==============================
-- 1. CUSTOMERS (skip if exists)
-- ==============================
PRINT 'Inserting customers...';

IF NOT EXISTS (SELECT 1 FROM Customers WHERE Email LIKE '%seed-data.com')
BEGIN
    SET @i = 1;
    WHILE @i <= 150
    BEGIN
        INSERT INTO Customers (FirstName, LastName, Email, Phone, DateOfBirth, Gender, IsActive, CreatedDate, UpdatedDate)
        VALUES (
            CONCAT('TestUser', @i),
            CONCAT('Soyad', @i),
            CONCAT('testuser', @i, '@seed-data.com'),
            CONCAT('055512', RIGHT('000000' + CAST(@i AS VARCHAR), 6)),
            DATEADD(YEAR, -(20 + (@i % 40)), GETUTCDATE()),
            CASE WHEN @i % 2 = 0 THEN 'Male' ELSE 'Female' END,
            1,
            DATEADD(DAY, -(@i % 730), GETUTCDATE()),
            GETUTCDATE()
        );
        SET @i = @i + 1;
    END;
END;
PRINT CONCAT('Customers total (seed): ', (SELECT COUNT(*) FROM Customers WHERE Email LIKE '%seed-data.com'));

-- ==============================
-- 2. ADDRESSES (skip if exists)
-- ==============================
DECLARE @firstCustId INT = (SELECT MIN(Id) FROM Customers WHERE Email LIKE '%seed-data.com');

IF NOT EXISTS (SELECT 1 FROM Addresses WHERE CustomerId = @firstCustId)
BEGIN
    INSERT INTO Addresses (CustomerId, AddressLine1, City, State, PostalCode, Country, AddressType, IsDefault, CreatedDate)
    SELECT 
        Id,
        CONCAT('Test Mahallesi, No:', ROW_NUMBER() OVER (ORDER BY Id)),
        CASE (Id % 5) WHEN 0 THEN 'Istanbul' WHEN 1 THEN 'Ankara' WHEN 2 THEN 'Izmir' WHEN 3 THEN 'Bursa' ELSE 'Antalya' END,
        'TR',
        CONCAT('34', RIGHT('000' + CAST(Id AS VARCHAR), 3)),
        'Turkey',
        'Home',
        1,
        GETUTCDATE()
    FROM Customers
    WHERE Email LIKE '%seed-data.com';
END;
PRINT CONCAT('Addresses total (seed): ', (SELECT COUNT(*) FROM Addresses a JOIN Customers c ON a.CustomerId = c.Id WHERE c.Email LIKE '%seed-data.com'));

-- ==============================
-- 3. ORDERS + ORDER ITEMS
-- ==============================
PRINT 'Inserting orders...';

IF EXISTS (SELECT 1 FROM Orders WHERE OrderNumber LIKE 'SQL-%')
BEGIN
    PRINT 'Orders already seeded, skipping.';
    GOTO Done;
END;

-- Product pool
CREATE TABLE #products (RowN INT IDENTITY(1,1), ProductId INT, Price DECIMAL(18,2), ProductName NVARCHAR(500), SKU NVARCHAR(100));
INSERT INTO #products (ProductId, Price, ProductName, SKU)
SELECT TOP 2000 Id, Price, Name, SKU FROM Products ORDER BY NEWID();

-- Customer pool
CREATE TABLE #customers (RowN INT IDENTITY(1,1), CustId INT, AddrId INT);
INSERT INTO #customers (CustId, AddrId)
SELECT c.Id, ISNULL(a.Id, 0)
FROM Customers c
LEFT JOIN Addresses a ON a.CustomerId = c.Id AND a.IsDefault = 1
WHERE c.Email LIKE '%seed-data.com';

DECLARE @custCount INT = (SELECT COUNT(*) FROM #customers);
DECLARE @prodCount INT = (SELECT COUNT(*) FROM #products);

SET @i = 1;
WHILE @i <= @totalOrders
BEGIN
    -- Pick customer
    DECLARE @custRow INT = (@i % @custCount) + 1;
    SELECT @customerId = CustId FROM #customers WHERE RowN = @custRow;

    DECLARE @addrId INT;
    SELECT @addrId = AddrId FROM #customers WHERE RowN = @custRow;
    IF @addrId IS NULL OR @addrId = 0
        SELECT TOP 1 @addrId = AddrId FROM #customers WHERE AddrId > 0;

    -- Date spread: ~35% in last 30 days, rest up to 2 years
    DECLARE @dayOffset INT;
    IF @i % 3 = 0
        SET @dayOffset = ABS(CHECKSUM(NEWID())) % 30
    ELSE
        SET @dayOffset = ABS(CHECKSUM(NEWID())) % 730;

    SET @orderDate = DATEADD(DAY, -@dayOffset, GETUTCDATE());

    DECLARE @sub DECIMAL(18,2) = ROUND(50 + (CAST(ABS(CHECKSUM(NEWID())) % 950 AS DECIMAL(18,2))), 2);
    DECLARE @disc DECIMAL(18,2) = ROUND(@sub * 0.1, 2);
    DECLARE @tax2 DECIMAL(18,2) = ROUND(@sub * 0.18, 2);
    DECLARE @total2 DECIMAL(18,2) = @sub - @disc + @tax2 + 15.00;
    DECLARE @status VARCHAR(20) = CASE 
        WHEN @i % 10 = 0 THEN 'Cancelled'
        WHEN @i % 7 = 0 THEN 'Processing'
        WHEN @i % 4 = 0 THEN 'Shipped'
        ELSE 'Delivered'
    END;

    INSERT INTO Orders (CustomerId, OrderNumber, OrderDate, ShippingAddressId, BillingAddressId,
        SubTotal, DiscountAmount, TaxAmount, ShippingCost, TotalAmount, Status,
        PaymentMethod, PaymentStatus, ShippedDate, DeliveredDate)
    VALUES (
        @customerId,
        CONCAT('SQL-', @orderNumber),
        @orderDate,
        @addrId, @addrId,
        @sub, @disc, @tax2, 15.00, @total2,
        @status,
        CASE WHEN @i % 4 = 0 THEN 'PayPal' WHEN @i % 3 = 0 THEN 'Debit Card' ELSE 'Credit Card' END,
        CASE WHEN @status = 'Cancelled' THEN 'Refunded' ELSE 'Completed' END,
        CASE WHEN @status IN ('Shipped','Delivered') THEN DATEADD(DAY, 2, @orderDate) ELSE NULL END,
        CASE WHEN @status = 'Delivered' THEN DATEADD(DAY, 5, @orderDate) ELSE NULL END
    );
    SET @orderId = SCOPE_IDENTITY();

    -- 1-4 items
    DECLARE @itemCount2 INT = 1 + (@i % 4);
    DECLARE @j INT = 1;
    WHILE @j <= @itemCount2
    BEGIN
        DECLARE @pRow INT = (ABS(CHECKSUM(NEWID())) % @prodCount) + 1;
        SELECT @productId = ProductId, @productPrice = Price, 
               @productName = ProductName, @productSKU = SKU
        FROM #products WHERE RowN = @pRow;

        DECLARE @qty2 INT = 1 + (@j % 3);
        INSERT INTO OrderItems (OrderId, ProductId, Quantity, UnitPrice, TotalPrice, ProductName, SKU)
        VALUES (@orderId, @productId, @qty2, @productPrice, @productPrice * @qty2, @productName, @productSKU);
        SET @j = @j + 1;
    END;

    SET @i = @i + 1;
    SET @orderNumber = @orderNumber + 1;

    IF @i % 1000 = 0
        PRINT CONCAT('Progress: ', @i, ' / ', @totalOrders, ' orders');
END;

DROP TABLE #products;
DROP TABLE #customers;

Done:
PRINT '== SUMMARY ==';
SELECT 'Customers' AS T, COUNT(*) AS N FROM Customers
UNION ALL SELECT 'Orders', COUNT(*) FROM Orders
UNION ALL SELECT 'OrderItems', COUNT(*) FROM OrderItems
UNION ALL SELECT 'Orders(last 30 days)', COUNT(*) FROM Orders WHERE OrderDate >= DATEADD(DAY, -30, GETUTCDATE());
