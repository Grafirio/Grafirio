-- =============================================
-- Grafirio E-Commerce Test Database Schema
-- =============================================

USE master;
GO

-- Create Database if not exists
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'GrafirioECommerce')
BEGIN
    CREATE DATABASE GrafirioECommerce;
END
GO

USE GrafirioECommerce;
GO

-- =============================================
-- 1. Categories (Kategoriler)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Categories')
BEGIN
    CREATE TABLE Categories (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL,
        Description NVARCHAR(500),
        ParentCategoryId INT NULL,
        ImageUrl NVARCHAR(500),
        IsActive BIT NOT NULL DEFAULT 1,
        DisplayOrder INT NOT NULL DEFAULT 0,
        CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT FK_Categories_Parent FOREIGN KEY (ParentCategoryId) REFERENCES Categories(Id)
    );
    CREATE INDEX IX_Categories_ParentId ON Categories(ParentCategoryId);
    CREATE INDEX IX_Categories_IsActive ON Categories(IsActive);
END
GO

-- =============================================
-- 2. Products (Ürünler)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Products')
BEGIN
    CREATE TABLE Products (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        Description NVARCHAR(2000),
        SKU NVARCHAR(50) NOT NULL UNIQUE,
        CategoryId INT NOT NULL,
        Price DECIMAL(18,2) NOT NULL,
        CostPrice DECIMAL(18,2),
        Stock INT NOT NULL DEFAULT 0,
        MinStock INT NOT NULL DEFAULT 0,
        ImageUrl NVARCHAR(500),
        Brand NVARCHAR(100),
        Rating DECIMAL(3,2) DEFAULT 0,
        ReviewCount INT DEFAULT 0,
        IsActive BIT NOT NULL DEFAULT 1,
        IsFeatured BIT NOT NULL DEFAULT 0,
        CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT FK_Products_Category FOREIGN KEY (CategoryId) REFERENCES Categories(Id)
    );
    CREATE INDEX IX_Products_CategoryId ON Products(CategoryId);
    CREATE INDEX IX_Products_IsActive ON Products(IsActive);
    CREATE INDEX IX_Products_IsFeatured ON Products(IsFeatured);
    CREATE INDEX IX_Products_Price ON Products(Price);
END
GO

-- =============================================
-- 3. ProductImages (Ürün Görselleri)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProductImages')
BEGIN
    CREATE TABLE ProductImages (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ProductId INT NOT NULL,
        ImageUrl NVARCHAR(500) NOT NULL,
        DisplayOrder INT NOT NULL DEFAULT 0,
        IsMain BIT NOT NULL DEFAULT 0,
        CONSTRAINT FK_ProductImages_Product FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_ProductImages_ProductId ON ProductImages(ProductId);
END
GO

-- =============================================
-- 4. Customers (Müşteriler)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Customers')
BEGIN
    CREATE TABLE Customers (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Email NVARCHAR(256) NOT NULL UNIQUE,
        FirstName NVARCHAR(100) NOT NULL,
        LastName NVARCHAR(100) NOT NULL,
        Phone NVARCHAR(20),
        BirthDate DATE,
        Gender NVARCHAR(10),
        RegistrationDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        LastLoginDate DATETIME2,
        IsEmailVerified BIT NOT NULL DEFAULT 0,
        CustomerType NVARCHAR(20) NOT NULL DEFAULT 'Regular', -- Regular, Premium, VIP
        Country NVARCHAR(100),
        City NVARCHAR(100),
        TotalOrderCount INT NOT NULL DEFAULT 0,
        TotalSpent DECIMAL(18,2) NOT NULL DEFAULT 0,
        IsActive BIT NOT NULL DEFAULT 1
    );
    CREATE INDEX IX_Customers_Email ON Customers(Email);
    CREATE INDEX IX_Customers_CustomerType ON Customers(CustomerType);
    CREATE INDEX IX_Customers_RegistrationDate ON Customers(RegistrationDate);
END
GO

-- =============================================
-- 5. Addresses (Adresler)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Addresses')
BEGIN
    CREATE TABLE Addresses (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        CustomerId INT NOT NULL,
        AddressType NVARCHAR(20) NOT NULL, -- Billing, Shipping
        Country NVARCHAR(100) NOT NULL,
        City NVARCHAR(100) NOT NULL,
        District NVARCHAR(100),
        Street NVARCHAR(500) NOT NULL,
        PostalCode NVARCHAR(20),
        IsDefault BIT NOT NULL DEFAULT 0,
        CONSTRAINT FK_Addresses_Customer FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_Addresses_CustomerId ON Addresses(CustomerId);
END
GO

-- =============================================
-- 6. Carts (Sepetler)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Carts')
BEGIN
    CREATE TABLE Carts (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        CustomerId INT NOT NULL,
        CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        Status NVARCHAR(20) NOT NULL DEFAULT 'Active', -- Active, Abandoned, Converted
        CONSTRAINT FK_Carts_Customer FOREIGN KEY (CustomerId) REFERENCES Customers(Id)
    );
    CREATE INDEX IX_Carts_CustomerId ON Carts(CustomerId);
    CREATE INDEX IX_Carts_Status ON Carts(Status);
END
GO

-- =============================================
-- 7. CartItems (Sepet Kalemleri)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CartItems')
BEGIN
    CREATE TABLE CartItems (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        CartId INT NOT NULL,
        ProductId INT NOT NULL,
        Quantity INT NOT NULL DEFAULT 1,
        UnitPrice DECIMAL(18,2) NOT NULL,
        AddedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT FK_CartItems_Cart FOREIGN KEY (CartId) REFERENCES Carts(Id) ON DELETE CASCADE,
        CONSTRAINT FK_CartItems_Product FOREIGN KEY (ProductId) REFERENCES Products(Id)
    );
    CREATE INDEX IX_CartItems_CartId ON CartItems(CartId);
    CREATE INDEX IX_CartItems_ProductId ON CartItems(ProductId);
END
GO

-- =============================================
-- 8. Orders (Siparişler)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Orders')
BEGIN
    CREATE TABLE Orders (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        CustomerId INT NOT NULL,
        OrderNumber NVARCHAR(50) NOT NULL UNIQUE,
        OrderDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        ShippingAddressId INT,
        BillingAddressId INT,
        SubTotal DECIMAL(18,2) NOT NULL,
        DiscountAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        TaxAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        ShippingCost DECIMAL(18,2) NOT NULL DEFAULT 0,
        TotalAmount DECIMAL(18,2) NOT NULL,
        Status NVARCHAR(20) NOT NULL DEFAULT 'Pending', -- Pending, Processing, Shipped, Delivered, Cancelled
        PaymentMethod NVARCHAR(50),
        PaymentStatus NVARCHAR(20) NOT NULL DEFAULT 'Pending', -- Pending, Completed, Failed, Refunded
        TrackingNumber NVARCHAR(100),
        ShippedDate DATETIME2,
        DeliveredDate DATETIME2,
        Notes NVARCHAR(1000),
        CONSTRAINT FK_Orders_Customer FOREIGN KEY (CustomerId) REFERENCES Customers(Id),
        CONSTRAINT FK_Orders_ShippingAddress FOREIGN KEY (ShippingAddressId) REFERENCES Addresses(Id),
        CONSTRAINT FK_Orders_BillingAddress FOREIGN KEY (BillingAddressId) REFERENCES Addresses(Id)
    );
    CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId);
    CREATE INDEX IX_Orders_OrderNumber ON Orders(OrderNumber);
    CREATE INDEX IX_Orders_OrderDate ON Orders(OrderDate);
    CREATE INDEX IX_Orders_Status ON Orders(Status);
END
GO

-- =============================================
-- 9. OrderItems (Sipariş Detayları)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OrderItems')
BEGIN
    CREATE TABLE OrderItems (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        OrderId INT NOT NULL,
        ProductId INT NOT NULL,
        ProductName NVARCHAR(200) NOT NULL,
        Quantity INT NOT NULL,
        UnitPrice DECIMAL(18,2) NOT NULL,
        DiscountAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        TotalPrice DECIMAL(18,2) NOT NULL,
        CONSTRAINT FK_OrderItems_Order FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE,
        CONSTRAINT FK_OrderItems_Product FOREIGN KEY (ProductId) REFERENCES Products(Id)
    );
    CREATE INDEX IX_OrderItems_OrderId ON OrderItems(OrderId);
    CREATE INDEX IX_OrderItems_ProductId ON OrderItems(ProductId);
END
GO

-- =============================================
-- 10. Discounts (İndirimler/Kuponlar)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Discounts')
BEGIN
    CREATE TABLE Discounts (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Code NVARCHAR(50) NOT NULL UNIQUE,
        Name NVARCHAR(200) NOT NULL,
        Description NVARCHAR(1000),
        DiscountType NVARCHAR(20) NOT NULL, -- Percentage, FixedAmount
        DiscountValue DECIMAL(18,2) NOT NULL,
        MinOrderAmount DECIMAL(18,2),
        MaxDiscountAmount DECIMAL(18,2),
        StartDate DATETIME2 NOT NULL,
        EndDate DATETIME2 NOT NULL,
        UsageLimit INT,
        UsedCount INT NOT NULL DEFAULT 0,
        IsActive BIT NOT NULL DEFAULT 1
    );
    CREATE INDEX IX_Discounts_Code ON Discounts(Code);
    CREATE INDEX IX_Discounts_IsActive ON Discounts(IsActive);
    CREATE INDEX IX_Discounts_Dates ON Discounts(StartDate, EndDate);
END
GO

-- =============================================
-- 11. ProductDiscounts (Ürün İndirimleri)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProductDiscounts')
BEGIN
    CREATE TABLE ProductDiscounts (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ProductId INT NOT NULL,
        DiscountId INT NOT NULL,
        Priority INT NOT NULL DEFAULT 0,
        CONSTRAINT FK_ProductDiscounts_Product FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE,
        CONSTRAINT FK_ProductDiscounts_Discount FOREIGN KEY (DiscountId) REFERENCES Discounts(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_ProductDiscounts_ProductId ON ProductDiscounts(ProductId);
    CREATE INDEX IX_ProductDiscounts_DiscountId ON ProductDiscounts(DiscountId);
END
GO

-- =============================================
-- 12. OrderDiscounts (Sipariş İndirimleri)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OrderDiscounts')
BEGIN
    CREATE TABLE OrderDiscounts (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        OrderId INT NOT NULL,
        DiscountId INT NOT NULL,
        DiscountAmount DECIMAL(18,2) NOT NULL,
        CONSTRAINT FK_OrderDiscounts_Order FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE,
        CONSTRAINT FK_OrderDiscounts_Discount FOREIGN KEY (DiscountId) REFERENCES Discounts(Id)
    );
    CREATE INDEX IX_OrderDiscounts_OrderId ON OrderDiscounts(OrderId);
    CREATE INDEX IX_OrderDiscounts_DiscountId ON OrderDiscounts(DiscountId);
END
GO

-- =============================================
-- 13. Reviews (Ürün Yorumları)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Reviews')
BEGIN
    CREATE TABLE Reviews (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ProductId INT NOT NULL,
        CustomerId INT NOT NULL,
        Rating INT NOT NULL CHECK (Rating >= 1 AND Rating <= 5),
        Title NVARCHAR(200),
        Comment NVARCHAR(2000),
        IsVerifiedPurchase BIT NOT NULL DEFAULT 0,
        HelpfulCount INT NOT NULL DEFAULT 0,
        ReviewDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        Status NVARCHAR(20) NOT NULL DEFAULT 'Pending', -- Pending, Approved, Rejected
        CONSTRAINT FK_Reviews_Product FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE,
        CONSTRAINT FK_Reviews_Customer FOREIGN KEY (CustomerId) REFERENCES Customers(Id)
    );
    CREATE INDEX IX_Reviews_ProductId ON Reviews(ProductId);
    CREATE INDEX IX_Reviews_CustomerId ON Reviews(CustomerId);
    CREATE INDEX IX_Reviews_Status ON Reviews(Status);
END
GO

-- =============================================
-- 14. Wishlists (İstek Listeleri)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Wishlists')
BEGIN
    CREATE TABLE Wishlists (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        CustomerId INT NOT NULL,
        ProductId INT NOT NULL,
        AddedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT FK_Wishlists_Customer FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
        CONSTRAINT FK_Wishlists_Product FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_Wishlists_Customer_Product UNIQUE (CustomerId, ProductId)
    );
    CREATE INDEX IX_Wishlists_CustomerId ON Wishlists(CustomerId);
    CREATE INDEX IX_Wishlists_ProductId ON Wishlists(ProductId);
END
GO

-- =============================================
-- 15. Inventory (Stok Hareketleri)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Inventory')
BEGIN
    CREATE TABLE Inventory (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ProductId INT NOT NULL,
        ChangeType NVARCHAR(20) NOT NULL, -- In, Out, Adjustment
        Quantity INT NOT NULL,
        PreviousStock INT NOT NULL,
        NewStock INT NOT NULL,
        Reason NVARCHAR(500),
        CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy NVARCHAR(100),
        CONSTRAINT FK_Inventory_Product FOREIGN KEY (ProductId) REFERENCES Products(Id)
    );
    CREATE INDEX IX_Inventory_ProductId ON Inventory(ProductId);
    CREATE INDEX IX_Inventory_CreatedDate ON Inventory(CreatedDate);
END
GO

PRINT '✅ Database schema created successfully!';
GO
