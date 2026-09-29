-- Semantik degerlendirme veri seti: e-ticaret (KOLAY taban cizgisi).
--
-- DummyData semasinin (src/dummydata) sadelestirilmis kopyasi: adlar temiz,
-- butun iliskiler yabanci anahtar olarak BILDIRILMIS. Sistem burada
-- hata yapiyorsa sorun zorlukta degil, temelde.
--
-- Kurulum: grafirio-semantic setup  (veritabanini sifirdan kurar)

CREATE TABLE dbo.Categories (
    Id INT NOT NULL PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    ParentCategoryId INT NULL,
    IsActive BIT NOT NULL,
    CONSTRAINT FK_Categories_Parent FOREIGN KEY (ParentCategoryId) REFERENCES dbo.Categories(Id)
);
GO

CREATE TABLE dbo.Products (
    Id INT NOT NULL PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    SKU NVARCHAR(50) NOT NULL UNIQUE,
    CategoryId INT NOT NULL,
    Brand NVARCHAR(100) NOT NULL,
    Price DECIMAL(18,2) NOT NULL,
    CostPrice DECIMAL(18,2) NOT NULL,
    Stock INT NOT NULL,
    Rating DECIMAL(3,2) NOT NULL,
    IsActive BIT NOT NULL,
    CreatedDate DATETIME2 NOT NULL,
    CONSTRAINT FK_Products_Category FOREIGN KEY (CategoryId) REFERENCES dbo.Categories(Id)
);
GO

CREATE TABLE dbo.Customers (
    Id INT NOT NULL PRIMARY KEY,
    Email NVARCHAR(256) NOT NULL UNIQUE,
    FirstName NVARCHAR(100) NOT NULL,
    LastName NVARCHAR(100) NOT NULL,
    Phone NVARCHAR(20) NULL,
    BirthDate DATE NULL,
    Gender NVARCHAR(10) NULL,
    RegistrationDate DATETIME2 NOT NULL,
    CustomerType NVARCHAR(20) NOT NULL,
    Country NVARCHAR(100) NOT NULL,
    City NVARCHAR(100) NOT NULL,
    TotalSpent DECIMAL(18,2) NOT NULL
);
GO

CREATE TABLE dbo.Addresses (
    Id INT NOT NULL PRIMARY KEY,
    CustomerId INT NOT NULL,
    AddressType NVARCHAR(20) NOT NULL,
    Country NVARCHAR(100) NOT NULL,
    City NVARCHAR(100) NOT NULL,
    Street NVARCHAR(200) NOT NULL,
    CONSTRAINT FK_Addresses_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id)
);
GO

CREATE TABLE dbo.Orders (
    Id INT NOT NULL PRIMARY KEY,
    OrderNumber NVARCHAR(30) NOT NULL UNIQUE,
    CustomerId INT NOT NULL,
    OrderDate DATETIME2 NOT NULL,
    ShippingAddressId INT NOT NULL,
    BillingAddressId INT NOT NULL,
    SubTotal DECIMAL(18,2) NOT NULL,
    DiscountAmount DECIMAL(18,2) NOT NULL,
    TaxAmount DECIMAL(18,2) NOT NULL,
    ShippingCost DECIMAL(18,2) NOT NULL,
    TotalAmount DECIMAL(18,2) NOT NULL,
    Status NVARCHAR(20) NOT NULL,
    PaymentMethod NVARCHAR(30) NOT NULL,
    PaymentStatus NVARCHAR(20) NOT NULL,
    CONSTRAINT FK_Orders_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
    CONSTRAINT FK_Orders_ShippingAddress FOREIGN KEY (ShippingAddressId) REFERENCES dbo.Addresses(Id),
    CONSTRAINT FK_Orders_BillingAddress FOREIGN KEY (BillingAddressId) REFERENCES dbo.Addresses(Id)
);
GO

CREATE TABLE dbo.OrderItems (
    Id INT NOT NULL PRIMARY KEY,
    OrderId INT NOT NULL,
    ProductId INT NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL,
    LineTotal DECIMAL(18,2) NOT NULL,
    CONSTRAINT FK_OrderItems_Order FOREIGN KEY (OrderId) REFERENCES dbo.Orders(Id),
    CONSTRAINT FK_OrderItems_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id)
);
GO

CREATE TABLE dbo.Reviews (
    Id INT NOT NULL PRIMARY KEY,
    ProductId INT NOT NULL,
    CustomerId INT NOT NULL,
    Rating INT NOT NULL,
    ReviewDate DATETIME2 NOT NULL,
    Status NVARCHAR(20) NOT NULL,
    CONSTRAINT FK_Reviews_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
    CONSTRAINT FK_Reviews_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id)
);
GO
