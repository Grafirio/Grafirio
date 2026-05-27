USE GrafirioECommerce;
SET NOCOUNT ON;

DECLARE @cnt INT;
DECLARE @i   INT;

-- ==============================
-- 1. CUSTOMERS
-- ==============================
PRINT 'Step 1: Customers...';
SET @i = 1;
WHILE @i <= 150
BEGIN
    IF NOT EXISTS (SELECT 1 FROM Customers WHERE Email = CONCAT('testuser', @i, '@seed-data.com'))
    BEGIN
        INSERT INTO Customers (FirstName, LastName, Email, Phone, BirthDate, Gender,
            IsEmailVerified, CustomerType, IsActive, RegistrationDate,
            TotalOrderCount, TotalSpent)
        VALUES (
            CONCAT('Test', @i),
            CONCAT('User', @i),
            CONCAT('testuser', @i, '@seed-data.com'),
            CONCAT('05551', RIGHT('00000' + CAST(@i AS VARCHAR(5)), 5)),
            DATEADD(YEAR, -(20 + (@i % 40)), GETUTCDATE()),
            CASE WHEN @i % 2 = 0 THEN 'Male' ELSE 'Female' END,
            1, 'Regular', 1,
            DATEADD(DAY, -(@i % 730), GETUTCDATE()),
            0, 0
        );
    END;
    SET @i = @i + 1;
END;
SET @cnt = (SELECT COUNT(*) FROM Customers WHERE Email LIKE '%seed-data.com');
PRINT CONCAT('Customers: ', @cnt);

-- ==============================
-- 2. ADDRESSES
-- ==============================
PRINT 'Step 2: Addresses...';
INSERT INTO Addresses (CustomerId, AddressType, Country, City, District, Street, PostalCode, IsDefault)
SELECT c.Id,
    'Home', 'Turkey',
    CASE (c.Id % 5) WHEN 0 THEN 'Istanbul' WHEN 1 THEN 'Ankara' WHEN 2 THEN 'Izmir' WHEN 3 THEN 'Bursa' ELSE 'Antalya' END,
    'Test Mahalle',
    CONCAT('Test Sk. No:', c.Id),
    '34000',
    1
FROM Customers c
WHERE c.Email LIKE '%seed-data.com'
  AND NOT EXISTS (SELECT 1 FROM Addresses a WHERE a.CustomerId = c.Id);
SET @cnt = (SELECT COUNT(*) FROM Addresses a JOIN Customers c ON a.CustomerId = c.Id WHERE c.Email LIKE '%seed-data.com');
PRINT CONCAT('Addresses: ', @cnt);

-- ==============================
-- 3. ORDERS
-- ==============================
IF EXISTS (SELECT 1 FROM Orders WHERE OrderNumber LIKE 'SQL-%')
BEGIN
    PRINT 'Orders already seeded, skipping.';
END
ELSE
BEGIN
    PRINT 'Step 3: Building temp tables...';
    IF OBJECT_ID('tempdb..#prods') IS NOT NULL DROP TABLE #prods;
    IF OBJECT_ID('tempdb..#custs') IS NOT NULL DROP TABLE #custs;

    CREATE TABLE #prods (rn INT IDENTITY(1,1) PRIMARY KEY, pid INT, price DECIMAL(18,2), pname NVARCHAR(500));
    CREATE TABLE #custs (rn INT IDENTITY(1,1) PRIMARY KEY, cid INT, aid INT);

    INSERT INTO #prods (pid, price, pname)
    SELECT TOP 2000 Id, Price, Name FROM Products ORDER BY NEWID();

    INSERT INTO #custs (cid, aid)
    SELECT c.Id, ISNULL((SELECT TOP 1 a.Id FROM Addresses a WHERE a.CustomerId = c.Id), 0)
    FROM Customers c WHERE c.Email LIKE '%seed-data.com';

    DECLARE @pCnt INT = (SELECT COUNT(*) FROM #prods);
    DECLARE @cCnt INT = (SELECT COUNT(*) FROM #custs);
    PRINT CONCAT('Products: ', @pCnt, ', Customers: ', @cCnt);

    PRINT 'Step 4: Inserting 8000 orders...';
    DECLARE @n     INT = 1;
    DECLARE @onum  INT = 200000;
    DECLARE @oid   INT;
    DECLARE @cid2  INT;
    DECLARE @aid2  INT;
    DECLARE @pid2  INT;
    DECLARE @prc2  DECIMAL(18,2);
    DECLARE @pnm2  NVARCHAR(500);
    DECLARE @odate DATETIME2;
    DECLARE @sub2  DECIMAL(18,2);
    DECLARE @disc2 DECIMAL(18,2);
    DECLARE @tax2  DECIMAL(18,2);
    DECLARE @tot2  DECIMAL(18,2);
    DECLARE @stat2 VARCHAR(20);
    DECLARE @doff  INT;
    DECLARE @ic    INT;
    DECLARE @jj    INT;
    DECLARE @pr    INT;

    WHILE @n <= 8000
    BEGIN
        SELECT @cid2 = cid, @aid2 = aid FROM #custs WHERE rn = (@n % @cCnt) + 1;
        IF @aid2 IS NULL OR @aid2 = 0
            SELECT TOP 1 @aid2 = aid FROM #custs WHERE aid > 0;

        SET @doff  = CASE WHEN @n % 3 = 0 THEN ABS(CHECKSUM(NEWID())) % 30 ELSE ABS(CHECKSUM(NEWID())) % 700 END;
        SET @odate = DATEADD(DAY, -@doff, GETUTCDATE());
        SET @sub2  = CAST(50 + ABS(CHECKSUM(NEWID())) % 950 AS DECIMAL(18,2));
        SET @disc2 = ROUND(@sub2 * 0.10, 2);
        SET @tax2  = ROUND(@sub2 * 0.18, 2);
        SET @tot2  = @sub2 - @disc2 + @tax2 + 15.00;
        SET @stat2 = CASE WHEN @n%10=0 THEN 'Cancelled' WHEN @n%7=0 THEN 'Processing' WHEN @n%4=0 THEN 'Shipped' ELSE 'Delivered' END;

        INSERT INTO Orders (CustomerId, OrderNumber, OrderDate, ShippingAddressId, BillingAddressId,
            SubTotal, DiscountAmount, TaxAmount, ShippingCost, TotalAmount,
            Status, PaymentMethod, PaymentStatus, ShippedDate, DeliveredDate)
        VALUES (@cid2, CONCAT('SQL-',@onum), @odate, @aid2, @aid2,
            @sub2, @disc2, @tax2, 15.00, @tot2, @stat2,
            CASE WHEN @n%3=0 THEN 'Credit Card' WHEN @n%4=0 THEN 'PayPal' ELSE 'Debit Card' END,
            CASE WHEN @stat2='Cancelled' THEN 'Refunded' ELSE 'Completed' END,
            CASE WHEN @stat2 IN('Shipped','Delivered') THEN DATEADD(DAY,2,@odate) ELSE NULL END,
            CASE WHEN @stat2='Delivered' THEN DATEADD(DAY,5,@odate) ELSE NULL END);
        SET @oid = SCOPE_IDENTITY();

        SET @ic = 1 + (@n % 4);
        SET @jj = 1;
        WHILE @jj <= @ic
        BEGIN
            SET @pr = (ABS(CHECKSUM(NEWID())) % @pCnt) + 1;
            SELECT @pid2=pid, @prc2=price, @pnm2=pname FROM #prods WHERE rn=@pr;
            INSERT INTO OrderItems (OrderId, ProductId, ProductName, Quantity, UnitPrice, DiscountAmount, TotalPrice)
            VALUES (@oid, @pid2, @pnm2, 1+(@jj%3), @prc2, 0, @prc2*(1+(@jj%3)));
            SET @jj = @jj + 1;
        END;

        SET @n    = @n + 1;
        SET @onum = @onum + 1;
        IF @n % 1000 = 0
        BEGIN
            DECLARE @pg INT = @n;
            PRINT CONCAT('  ', @pg, '/8000');
        END;
    END;

    DROP TABLE #prods;
    DROP TABLE #custs;
    PRINT 'Orders done!';
END;

-- SUMMARY
PRINT '== SUMMARY ==';
SELECT 'Customers'       AS T, COUNT(*) N FROM Customers
UNION ALL SELECT 'Orders',            COUNT(*) FROM Orders
UNION ALL SELECT 'OrderItems',        COUNT(*) FROM OrderItems
UNION ALL SELECT 'Orders_last30d',    COUNT(*) FROM Orders WHERE OrderDate >= DATEADD(DAY,-30,GETUTCDATE());
