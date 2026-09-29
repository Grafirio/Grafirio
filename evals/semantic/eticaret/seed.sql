-- Belirlenimci veri: ayni betik her kurulumda AYNI satirlari uretir. Rastgele
-- sayi yok; degerler satir numarasindan modulo ile turetiliyor. Altin SQL'in
-- sonucu bu yuzden kurulumdan kuruluma degismiyor.
--
-- Tarihler 2025-01-01'den baslayip bugune yakin bir aralikta: "bu yil",
-- "gecen ay" gibi sorularin cevabi bos cikmasin.

SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
INTO #n
FROM sys.all_objects a CROSS JOIN sys.all_objects b;
GO

INSERT INTO dbo.Categories (Id, Name, ParentCategoryId, IsActive) VALUES
 (1, N'Elektronik', NULL, 1), (2, N'Giyim', NULL, 1), (3, N'Ev & Yaşam', NULL, 1), (4, N'Kitap', NULL, 1),
 (5, N'Telefon', 1, 1), (6, N'Bilgisayar', 1, 1), (7, N'Kadın Giyim', 2, 1), (8, N'Erkek Giyim', 2, 1),
 (9, N'Mutfak', 3, 1), (10, N'Roman', 4, 1), (11, N'Aksesuar', 1, 0);
GO

-- 240 urun, 6 marka.
INSERT INTO dbo.Products (Id, Name, SKU, CategoryId, Brand, Price, CostPrice, Stock, Rating, IsActive, CreatedDate)
SELECT i,
       CONCAT(N'Ürün ', i),
       CONCAT('SKU-', RIGHT(CONCAT('00000', i), 5)),
       5 + (i % 6),
       CHOOSE(1 + (i % 6), N'Nova', N'Atlas', N'Kuzey', N'Mavi Ay', N'Pera', N'Vega'),
       CAST(50 + (i * 37) % 4950 AS DECIMAL(18,2)),
       CAST((50 + (i * 37) % 4950) * 0.6 AS DECIMAL(18,2)),
       (i * 13) % 300,
       CAST(2.5 + ((i * 7) % 25) / 10.0 AS DECIMAL(3,2)),
       CASE WHEN i % 17 = 0 THEN 0 ELSE 1 END,
       DATEADD(DAY, i % 500, '2024-06-01')
FROM #n WHERE i <= 240;
GO

-- 1200 musteri. Ulke dagilimi bilerek dengesiz (en cok Turkiye).
INSERT INTO dbo.Customers (Id, Email, FirstName, LastName, Phone, BirthDate, Gender, RegistrationDate,
                           CustomerType, Country, City, TotalSpent)
SELECT i,
       CONCAT('musteri', i, '@example.test'),
       CHOOSE(1 + (i % 8), N'Ayşe', N'Mehmet', N'Zeynep', N'Can', N'Elif', N'Burak', N'Deniz', N'Selin'),
       CHOOSE(1 + (i % 7), N'Yılmaz', N'Kaya', N'Demir', N'Şahin', N'Çelik', N'Aydın', N'Arslan'),
       CONCAT('+90 5', RIGHT(CONCAT('000000000', (i * 7919) % 1000000000), 9)),
       DATEADD(DAY, -((i * 97) % 18000) - 6570, '2026-01-01'),
       CASE WHEN i % 2 = 0 THEN N'Kadın' ELSE N'Erkek' END,
       DATEADD(DAY, i % 600, '2025-01-01'),
       CASE WHEN i % 10 = 0 THEN N'VIP' WHEN i % 4 = 0 THEN N'Premium' ELSE N'Regular' END,
       CASE WHEN i % 10 < 5 THEN N'Türkiye' WHEN i % 10 < 7 THEN N'Almanya' WHEN i % 10 = 7 THEN N'Hollanda'
            WHEN i % 10 = 8 THEN N'Fransa' ELSE N'İngiltere' END,
       CASE WHEN i % 10 < 5 THEN CHOOSE(1 + (i % 3), N'İstanbul', N'Ankara', N'İzmir')
            WHEN i % 10 < 7 THEN N'Berlin' WHEN i % 10 = 7 THEN N'Amsterdam'
            WHEN i % 10 = 8 THEN N'Paris' ELSE N'Londra' END,
       0
FROM #n WHERE i <= 1200;
GO

-- Her musteriye bir teslimat, her ucuncusune ek bir fatura adresi.
INSERT INTO dbo.Addresses (Id, CustomerId, AddressType, Country, City, Street)
SELECT c.Id, c.Id, N'Shipping', c.Country, c.City, CONCAT(N'Sokak ', c.Id % 90, N' No ', c.Id % 40)
FROM dbo.Customers c;
INSERT INTO dbo.Addresses (Id, CustomerId, AddressType, Country, City, Street)
SELECT 10000 + c.Id, c.Id, N'Billing', c.Country, c.City, CONCAT(N'Cadde ', c.Id % 60)
FROM dbo.Customers c WHERE c.Id % 3 = 0;
GO

-- 6000 siparis, 2025-01-01'den itibaren ~630 gune yayilmis.
INSERT INTO dbo.Orders (Id, OrderNumber, CustomerId, OrderDate, ShippingAddressId, BillingAddressId, SubTotal,
                        DiscountAmount, TaxAmount, ShippingCost, TotalAmount, Status, PaymentMethod, PaymentStatus)
SELECT i,
       CONCAT('ORD-', RIGHT(CONCAT('000000', i), 6)),
       1 + (i * 7) % 1200,
       DATEADD(MINUTE, (i * 37) % 1440, DATEADD(DAY, (i * 631) % 630, '2025-01-01')),
       1 + (i * 7) % 1200,
       CASE WHEN (1 + (i * 7) % 1200) % 3 = 0 THEN 10000 + 1 + (i * 7) % 1200 ELSE 1 + (i * 7) % 1200 END,
       0, 0, 0, 0, 0,
       CHOOSE(1 + (i % 6), N'Delivered', N'Delivered', N'Delivered', N'Shipped', N'Processing', N'Cancelled'),
       CHOOSE(1 + (i % 4), N'Credit Card', N'Debit Card', N'PayPal', N'Cash on Delivery'),
       CASE WHEN i % 6 = 5 THEN N'Refunded' ELSE N'Completed' END
FROM #n WHERE i <= 6000;
GO

-- Her siparise 1-3 kalem.
INSERT INTO dbo.OrderItems (Id, OrderId, ProductId, Quantity, UnitPrice, LineTotal)
SELECT ROW_NUMBER() OVER (ORDER BY o.Id, k.k), o.Id, p.Id, q.qty, p.Price, p.Price * q.qty
FROM dbo.Orders o
CROSS JOIN (VALUES (1), (2), (3)) k(k)
CROSS APPLY (SELECT 1 + ((o.Id * 31 + k.k * 17) % 240) AS pid) x
JOIN dbo.Products p ON p.Id = x.pid
CROSS APPLY (SELECT 1 + (o.Id + k.k) % 3 AS qty) q
WHERE k.k <= 1 + (o.Id % 3);
GO

UPDATE o SET
    SubTotal = t.Total,
    DiscountAmount = CASE WHEN o.Id % 5 = 0 THEN ROUND(t.Total * 0.1, 2) ELSE 0 END,
    ShippingCost = CASE WHEN t.Total > 500 THEN 0 ELSE 39.90 END
FROM dbo.Orders o
JOIN (SELECT OrderId, SUM(LineTotal) AS Total FROM dbo.OrderItems GROUP BY OrderId) t ON t.OrderId = o.Id;
UPDATE dbo.Orders SET TaxAmount = ROUND((SubTotal - DiscountAmount) * 0.20, 2);
UPDATE dbo.Orders SET TotalAmount = SubTotal - DiscountAmount + TaxAmount + ShippingCost;
UPDATE c SET TotalSpent = ISNULL(t.Total, 0)
FROM dbo.Customers c
LEFT JOIN (SELECT CustomerId, SUM(TotalAmount) AS Total FROM dbo.Orders WHERE Status <> N'Cancelled' GROUP BY CustomerId) t
  ON t.CustomerId = c.Id;
GO

INSERT INTO dbo.Reviews (Id, ProductId, CustomerId, Rating, ReviewDate, Status)
SELECT i, 1 + (i * 11) % 240, 1 + (i * 13) % 1200, 1 + (i * 3) % 5,
       DATEADD(DAY, (i * 211) % 620, '2025-01-05'),
       CASE WHEN i % 3 = 0 THEN N'Pending' ELSE N'Approved' END
FROM #n WHERE i <= 3000;
GO

DROP TABLE #n;
GO
