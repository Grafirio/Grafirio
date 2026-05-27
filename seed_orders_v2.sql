USE GrafirioECommerce;
SET NOCOUNT ON;

-- ==============================
-- 1. CUSTOMERS
-- ==============================
PRINT 'Step 1: Customers...';
DECLARE @i INT = 1;
WHILE @i <= 150
BEGIN
    IF NOT EXISTS (SELECT 1 FROM Customers WHERE Email = CONCAT('testuser', @i, '@seed-data.com'))
    BEGIN
        INSERT INTO Customers (FirstName, LastName, Email, Phone, DateOfBirth, Gender, IsActive, CreatedDate, UpdatedDate)
        VALUES (
            CONCAT('Test', @i), CONCAT('User', @i),
            CONCAT('testuser', @i, '@seed-data.com'),
            CONCAT('055512', RIGHT('000000' + CAST(@i AS VARCHAR), 6)),
            DATEADD(YEAR, -(20 + (@i % 40)), GETUTCDATE()),
            CASE WHEN @i % 2 = 0 THEN 'Male' ELSE 'Female' END,
            1, DATEADD(DAY, -(@i % 730), GETUTCDATE()), GETUTCDATE()
        );
    END;
    SET @i = @i + 1;
END;
PRINT CONCAT('Customers done: ', (SELECT COUNT(*) FROM Customers WHERE Email LIKE '%seed-data.com'));

-- ==============================
-- 2. ADDRESSES
-- ==============================
PRINT 'Step 2: Addresses...';
INSERT INTO Addresses (CustomerId, AddressLine1, City, State, PostalCode, Country, AddressType, IsDefault, CreatedDate)
SELECT
    c.Id,
    CONCAT('Test Sk. No:', c.Id),
    CASE (c.Id % 5) WHEN 0 THEN 'Istanbul' WHEN 1 THEN 'Ankara' WHEN 2 THEN 'Izmir' WHEN 3 THEN 'Bursa' ELSE 'Antalya' END,
    'TR', '34000', 'Turkey', 'Home', 1, GETUTCDATE()
FROM Customers c
WHERE c.Email LIKE '%seed-data.com'
  AND NOT EXISTS (SELECT 1 FROM Addresses a WHERE a.CustomerId = c.Id);
PRINT 'Addresses done.';

-- ==============================
-- 3. TEMP TABLES
-- ==============================
IF OBJECT_ID('tempdb..#prods') IS NOT NULL DROP TABLE #prods;
IF OBJECT_ID('tempdb..#custs') IS NOT NULL DROP TABLE #custs;

CREATE TABLE #prods (rn INT IDENTITY(1,1) PRIMARY KEY, pid INT, price DECIMAL(18,2), pname NVARCHAR(500), sku NVARCHAR(100));
CREATE TABLE #custs (rn INT IDENTITY(1,1) PRIMARY KEY, cid INT, aid INT);

INSERT INTO #prods (pid, price, pname, sku)
SELECT TOP 2000 p.Id, p.Price, p.Name, p.SKU FROM Products p ORDER BY NEWID();

INSERT INTO #custs (cid, aid)
SELECT c.Id, ISNULL((SELECT TOP 1 a.Id FROM Addresses a WHERE a.CustomerId = c.Id), 0)
FROM Customers c WHERE c.Email LIKE '%seed-data.com';

DECLARE @pCount INT = (SELECT COUNT(*) FROM #prods);
DECLARE @cCount INT = (SELECT COUNT(*) FROM #custs);
PRINT CONCAT('Product pool: ', @pCount, '  Customer pool: ', @cCount);

-- ==============================
-- 4. ORDERS
-- ==============================
PRINT 'Step 4: Orders (8000)...';
IF EXISTS (SELECT 1 FROM Orders WHERE OrderNumber LIKE 'SQL-%')
BEGIN
    PRINT 'Already seeded, skipping orders.';
    DROP TABLE #prods; DROP TABLE #custs;
    SELECT 'Orders' AS T, COUNT(*) N FROM Orders UNION ALL SELECT 'Last30', COUNT(*) FROM Orders WHERE OrderDate >= DATEADD(DAY,-30,GETUTCDATE());
    RETURN;
END;

DECLARE @n INT = 1;
DECLARE @onum INT = 200000;
DECLARE @oid INT;
DECLARE @cid2 INT;
DECLARE @aid2 INT;
DECLARE @pid2 INT;
DECLARE @prc2 DECIMAL(18,2);
DECLARE @pnm2 NVARCHAR(500);
DECLARE @sk2  NVARCHAR(100);
DECLARE @odate DATETIME2;
DECLARE @sub2 DECIMAL(18,2);
DECLARE @disc2 DECIMAL(18,2);
DECLARE @tax2  DECIMAL(18,2);
DECLARE @tot2  DECIMAL(18,2);
DECLARE @stat2 VARCHAR(20);

WHILE @n <= 8000
BEGIN
    SELECT @cid2 = cid, @aid2 = aid FROM #custs WHERE rn = (@n % @cCount) + 1;
    IF @aid2 = 0 SELECT TOP 1 @aid2 = aid FROM #custs WHERE aid > 0;

    -- ~33% last 30 days, rest up to 2 years
    DECLARE @doff INT = CASE WHEN @n % 3 = 0 THEN ABS(CHECKSUM(NEWID())) % 30 ELSE ABS(CHECKSUM(NEWID())) % 700 END;
    SET @odate = DATEADD(DAY, -@doff, GETUTCDATE());

    SET @sub2  = CAST(50 + ABS(CHECKSUM(NEWID())) % 950 AS DECIMAL(18,2));
    SET @disc2 = ROUND(@sub2 * 0.10, 2);
    SET @tax2  = ROUND(@sub2 * 0.18, 2);
    SET @tot2  = @sub2 - @disc2 + @tax2 + 15.00;
    SET @stat2 = CASE WHEN @n%10=0 THEN 'Cancelled' WHEN @n%7=0 THEN 'Processing' WHEN @n%4=0 THEN 'Shipped' ELSE 'Delivered' END;

    INSERT INTO Orders (CustomerId,OrderNumber,OrderDate,ShippingAddressId,BillingAddressId,
        SubTotal,DiscountAmount,TaxAmount,ShippingCost,TotalAmount,Status,
        PaymentMethod,PaymentStatus,ShippedDate,DeliveredDate)
    VALUES (@cid2, CONCAT('SQL-',@onum), @odate, @aid2, @aid2,
        @sub2, @disc2, @tax2, 15.00, @tot2, @stat2,
        CASE WHEN @n%3=0 THEN 'Credit Card' WHEN @n%4=0 THEN 'PayPal' ELSE 'Debit Card' END,
        CASE WHEN @stat2='Cancelled' THEN 'Refunded' ELSE 'Completed' END,
        CASE WHEN @stat2 IN('Shipped','Delivered') THEN DATEADD(DAY,2,@odate) ELSE NULL END,
        CASE WHEN @stat2='Delivered' THEN DATEADD(DAY,5,@odate) ELSE NULL END);
    SET @oid = SCOPE_IDENTITY();

    -- 1-4 items
    DECLARE @ic INT = 1 + (@n % 4);
    DECLARE @jj INT = 1;
    WHILE @jj <= @ic
    BEGIN
        DECLARE @pr INT = (ABS(CHECKSUM(NEWID())) % @pCount) + 1;
        SELECT @pid2=pid, @prc2=price, @pnm2=pname, @sk2=sku FROM #prods WHERE rn=@pr;
        INSERT INTO OrderItems (OrderId,ProductId,Quantity,UnitPrice,TotalPrice,ProductName,SKU)
        VALUES (@oid, @pid2, 1+(@jj%3), @prc2, @prc2*(1+(@jj%3)), @pnm2, @sk2);
        SET @jj = @jj + 1;
    END;

    SET @n    = @n + 1;
    SET @onum = @onum + 1;
    IF @n % 1000 = 0 PRINT CONCAT('  ', @n, '/8000 orders done');
END;

DROP TABLE #prods;
DROP TABLE #custs;

PRINT '== DONE ==';
SELECT 'Customers'            AS T, COUNT(*) N FROM Customers
UNION ALL SELECT 'Orders',              COUNT(*) FROM Orders
UNION ALL SELECT 'OrderItems',          COUNT(*) FROM OrderItems
UNION ALL SELECT 'Orders_last30days',   COUNT(*) FROM Orders WHERE OrderDate >= DATEADD(DAY,-30,GETUTCDATE());
