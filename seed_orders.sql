-- Hızlı test verisi: Customers + Orders + OrderItems
-- Generator ile çakışmaz (farklı email domain ve sipariş prefix)
USE GrafirioECommerce;
SET NOCOUNT ON;

DECLARE @i INT = 1;
DECLARE @customerId INT;
DECLARE @orderId INT;
DECLARE @orderDate DATETIME2;
DECLARE @productId INT;
DECLARE @productPrice DECIMAL(18,2);
DECLARE @orderNumber INT = 200000;
DECLARE @totalCustomers INT = 150;
DECLARE @totalOrders INT = 8000;

-- ==============================
-- 1. CUSTOMERS
-- ==============================
PRINT 'Inserting customers...';

DECLARE @customerIds TABLE (Id INT);

WHILE @i <= @totalCustomers
BEGIN
    INSERT INTO Customers (FirstName, LastName, Email, Phone, DateOfBirth, Gender, IsActive, CreatedDate, UpdatedDate)
    OUTPUT INSERTED.Id INTO @customerIds
    VALUES (
        CONCAT('TestUser', @i),
        CONCAT('Soyad', @i),
        CONCAT('testuser', @i, '@seed-data.com'),
        CONCAT('05', RIGHT('00' + CAST(@i % 100 AS VARCHAR), 2), '1234567'),
        DATEADD(YEAR, -(20 + (@i % 40)), GETUTCDATE()),
        CASE WHEN @i % 2 = 0 THEN 'Male' ELSE 'Female' END,
        1,
        DATEADD(DAY, -(@i % 730), GETUTCDATE()),
        GETUTCDATE()
    );
    SET @i = @i + 1;
END;

PRINT CONCAT('Customers inserted: ', (SELECT COUNT(*) FROM @customerIds));

-- ==============================
-- 2. ADDRESSES (1 per customer)
-- ==============================
PRINT 'Inserting addresses...';

DECLARE @addressIds TABLE (Id INT, CustomerId INT);
DECLARE @cities TABLE (city VARCHAR(50));
INSERT INTO @cities VALUES ('Istanbul'), ('Ankara'), ('Izmir'), ('Bursa'), ('Antalya');

INSERT INTO Addresses (CustomerId, AddressLine1, City, State, PostalCode, Country, AddressType, IsDefault, CreatedDate)
OUTPUT INSERTED.Id, INSERTED.CustomerId INTO @addressIds
SELECT 
    Id,
    CONCAT('Test Sk. No:', ROW_NUMBER() OVER (ORDER BY Id)),
    c.city,
    'TR',
    CONCAT('3', RIGHT('0000' + CAST(Id AS VARCHAR), 4)),
    'Turkey',
    'Home',
    1,
    GETUTCDATE()
FROM @customerIds
CROSS APPLY (SELECT TOP 1 city FROM @cities ORDER BY NEWID()) c;

PRINT CONCAT('Addresses inserted: ', (SELECT COUNT(*) FROM @addressIds));

-- ==============================
-- 3. ORDERS + ORDER ITEMS
-- ==============================
PRINT 'Inserting orders (this takes ~30 seconds)...';

DECLARE @productCount INT = (SELECT COUNT(*) FROM Products);
DECLARE @productIds TABLE (Id INT, Price DECIMAL(18,2), RowN INT IDENTITY(1,1));
INSERT INTO @productIds (Id, Price)
SELECT TOP 2000 Id, Price FROM Products ORDER BY NEWID();

SET @i = 1;
DECLARE @custCount INT = (SELECT COUNT(*) FROM @customerIds);
DECLARE @custIds TABLE (Id INT, RowN INT IDENTITY(1,1));
INSERT INTO @custIds SELECT Id FROM @customerIds;

DECLARE @addrIds TABLE (CustId INT, AddrId INT, RowN INT IDENTITY(1,1));
INSERT INTO @addrIds SELECT CustomerId, Id FROM @addressIds;

WHILE @i <= @totalOrders
BEGIN
    -- Random customer
    DECLARE @custRow INT = (@i % @custCount) + 1;
    SELECT @customerId = Id FROM @custIds WHERE RowN = @custRow;

    -- Random address for that customer
    DECLARE @addrId INT;
    SELECT TOP 1 @addrId = AddrId FROM @addrIds WHERE CustId = @customerId;
    IF @addrId IS NULL SET @addrId = (SELECT TOP 1 AddrId FROM @addrIds);

    -- Date: spread over last 2 years, ~30% in last 60 days
    DECLARE @dayOffset INT;
    IF @i % 3 = 0
        SET @dayOffset = @i % 60        -- last 60 days
    ELSE
        SET @dayOffset = (@i * 7) % 730; -- up to 2 years ago
    SET @orderDate = DATEADD(DAY, -@dayOffset, GETUTCDATE());

    DECLARE @subTotal DECIMAL(18,2) = ROUND(50 + (CAST(ABS(CHECKSUM(NEWID())) AS FLOAT) / CAST(2147483648 AS FLOAT)) * 950, 2);
    DECLARE @discount DECIMAL(18,2) = ROUND(@subTotal * 0.1, 2);
    DECLARE @tax DECIMAL(18,2) = ROUND(@subTotal * 0.18, 2);
    DECLARE @total DECIMAL(18,2) = @subTotal - @discount + @tax + 15.00;
    DECLARE @status VARCHAR(20) = CASE 
        WHEN @i % 10 = 0 THEN 'Cancelled'
        WHEN @i % 5 = 0 THEN 'Processing'
        WHEN @i % 3 = 0 THEN 'Shipped'
        ELSE 'Delivered'
    END;

    INSERT INTO Orders (CustomerId, OrderNumber, OrderDate, ShippingAddressId, BillingAddressId,
        SubTotal, DiscountAmount, TaxAmount, ShippingCost, TotalAmount, Status,
        PaymentMethod, PaymentStatus, ShippedDate, DeliveredDate)
    OUTPUT INSERTED.Id INTO @orderId
    VALUES (
        @customerId,
        CONCAT('SQL-', @orderNumber),
        @orderDate,
        @addrId, @addrId,
        @subTotal, @discount, @tax, 15.00, @total,
        @status,
        CASE WHEN @i % 4 = 0 THEN 'PayPal' WHEN @i % 3 = 0 THEN 'Debit Card' ELSE 'Credit Card' END,
        CASE WHEN @status = 'Cancelled' THEN 'Refunded' ELSE 'Completed' END,
        CASE WHEN @status IN ('Shipped','Delivered') THEN DATEADD(DAY, 2, @orderDate) ELSE NULL END,
        CASE WHEN @status = 'Delivered' THEN DATEADD(DAY, 5, @orderDate) ELSE NULL END
    );

    -- 1-4 order items per order
    DECLARE @itemCount INT = 1 + (@i % 4);
    DECLARE @j INT = 1;
    WHILE @j <= @itemCount
    BEGIN
        DECLARE @pRow INT = ((@i * @j) % 2000) + 1;
        SELECT @productId = Id, @productPrice = Price FROM @productIds WHERE RowN = @pRow;
        IF @productId IS NULL
        BEGIN
            SELECT TOP 1 @productId = Id, @productPrice = Price FROM @productIds;
        END;

        DECLARE @qty INT = 1 + (@j % 3);
        INSERT INTO OrderItems (OrderId, ProductId, Quantity, UnitPrice, TotalPrice, ProductName, SKU)
        VALUES (
            @orderId,
            @productId,
            @qty,
            @productPrice,
            @productPrice * @qty,
            (SELECT TOP 1 Name FROM Products WHERE Id = @productId),
            (SELECT TOP 1 SKU FROM Products WHERE Id = @productId)
        );
        SET @j = @j + 1;
    END;

    SET @i = @i + 1;
    SET @orderNumber = @orderNumber + 1;

    IF @i % 500 = 0
        PRINT CONCAT('Orders: ', @i, ' / ', @totalOrders);
END;

PRINT '== DONE ==';
SELECT 'Customers' AS T, COUNT(*) AS N FROM Customers WHERE Email LIKE '%seed-data.com'
UNION ALL SELECT 'Orders', COUNT(*) FROM Orders WHERE OrderNumber LIKE 'SQL-%'
UNION ALL SELECT 'OrderItems', COUNT(*) FROM OrderItems oi 
    JOIN Orders o ON oi.OrderId = o.Id WHERE o.OrderNumber LIKE 'SQL-%';
