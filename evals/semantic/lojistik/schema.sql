-- Semantik degerlendirme veri seti: lojistik (ZOR).
--
-- Sahada gorulen seyler bilerek bir araya getirildi. Hicbir iliski yabanci
-- anahtar olarak BILDIRILMEDI; sistem hepsini adlardan ve veriden bulmak
-- zorunda. Tuzaklar (ayrinti gold.json'da):
--
--   * Sistem onekli tablo adlari        L_INT_ExportReference, L_DEF_Company
--   * Rol onekli kolonlar               ShipperCompanyId, ConsigneeCompanyId
--   * Karsit tablo cifti                ihracat (Export) / ithalat (Import)
--   * Ayni adli kolonlar                GrossWeightKg, FreightAmount iki tabloda da
--   * Kod kolonlari                     TransportModeCode = ROD / SEA / AIR
--   * Kosullu lookup                    L_DEF_Parameter (ParamGroup + ParamCode)
--   * Yazim hatasi                      L_TRK_PositionEvent.PostionId
--   * Kisaltma                          Fatura.ExportRefNo
--   * Turkce ad                         Fatura.ParaBirimi -> L_DEF_Currency
--   * Oz-referans                       L_DEF_Company.ParentCompanyId
--   * Tesaduf ortusme (YANLIS iliski)   L_ROD_ExportPosition.CarrierCompanyNo
--   * Hassas kolonlar                   vergi no, telefon, TC kimlik

CREATE TABLE dbo.L_DEF_Country (
    CountryCode CHAR(2) NOT NULL PRIMARY KEY,
    CountryName NVARCHAR(100) NOT NULL,
    Region NVARCHAR(50) NOT NULL
);
GO

CREATE TABLE dbo.L_DEF_Currency (
    CurrencyCode CHAR(3) NOT NULL PRIMARY KEY,
    CurrencyName NVARCHAR(50) NOT NULL
);
GO

-- Genel parametre tablosu: tasima sekli, paket tipi, durum... hepsi burada.
-- ParamCode tek basina benzersiz DEGIL; (ParamGroup, ParamCode) benzersiz.
CREATE TABLE dbo.L_DEF_Parameter (
    ParameterId INT NOT NULL PRIMARY KEY,
    ParamGroup VARCHAR(10) NOT NULL,
    ParamCode VARCHAR(10) NOT NULL,
    ParamText NVARCHAR(100) NOT NULL,
    CONSTRAINT UQ_Parameter_Group_Code UNIQUE (ParamGroup, ParamCode)
);
GO

CREATE TABLE dbo.L_DEF_Company (
    CompanyId INT NOT NULL PRIMARY KEY,
    CompanyCode VARCHAR(20) NOT NULL UNIQUE,
    CompanyName NVARCHAR(200) NOT NULL,
    CountryCode CHAR(2) NOT NULL,
    ParentCompanyId INT NULL,
    TaxNumber VARCHAR(20) NULL,
    PhoneNumber VARCHAR(30) NULL,
    CreatedAt DATETIME2 NOT NULL
);
GO

-- Ihracat referanslari (giden).
CREATE TABLE dbo.L_INT_ExportReference (
    ExportReferenceId INT NOT NULL PRIMARY KEY,
    ReferenceNo VARCHAR(20) NOT NULL UNIQUE,
    ShipperCompanyId INT NOT NULL,
    ReceiverCompanyId INT NOT NULL,
    DestinationCountryCode CHAR(2) NOT NULL,
    TransportModeCode VARCHAR(10) NOT NULL,
    LoadingDate DATE NOT NULL,
    GrossWeightKg DECIMAL(12,2) NOT NULL,
    FreightAmount DECIMAL(14,2) NOT NULL,
    CurrencyCode CHAR(3) NOT NULL,
    StatusId INT NOT NULL
);
GO

-- Ithalat referanslari (gelen). Kolonlar ihracatla BENZER ama anlamlari farkli:
-- burada gonderen yabanci firma, alan yerli.
CREATE TABLE dbo.L_INT_ImportReference (
    ImportReferenceId INT NOT NULL PRIMARY KEY,
    ReferenceNo VARCHAR(20) NOT NULL UNIQUE,
    SenderCompanyId INT NOT NULL,
    ConsigneeCompanyId INT NOT NULL,
    OriginCountryCode CHAR(2) NOT NULL,
    TransportModeCode VARCHAR(10) NOT NULL,
    ArrivalDate DATE NOT NULL,
    GrossWeightKg DECIMAL(12,2) NOT NULL,
    FreightAmount DECIMAL(14,2) NOT NULL,
    CurrencyCode CHAR(3) NOT NULL
);
GO

-- Karayolu ihracat pozisyonlari (tir seferleri).
CREATE TABLE dbo.L_ROD_ExportPosition (
    PositionId INT NOT NULL PRIMARY KEY,
    ExportReferenceId INT NOT NULL,
    TruckPlate VARCHAR(15) NOT NULL,
    DriverNationalId CHAR(11) NULL,
    -- Tasiyicinin, bu veritabaninda OLMAYAN dis bir tasiyici kaydindaki sira
    -- numarasi. Degerleri (1-30) tesadufen L_DEF_Company.CompanyId ile ortusuyor.
    CarrierCompanyNo INT NOT NULL,
    DepartureDate DATE NOT NULL,
    ArrivalDate DATE NULL,
    DistanceKm INT NOT NULL
);
GO

-- Takip olaylari. Kolon adinda eski bir yazim hatasi var: PostionId.
CREATE TABLE dbo.L_TRK_PositionEvent (
    EventId INT NOT NULL PRIMARY KEY,
    PostionId INT NOT NULL,
    EventCode VARCHAR(10) NOT NULL,
    EventTime DATETIME2 NOT NULL
);
GO

-- Muhasebe tarafi: Turkce adlandirilmis.
CREATE TABLE dbo.Cari (
    CariKod VARCHAR(20) NOT NULL PRIMARY KEY,
    Unvan NVARCHAR(200) NOT NULL,
    CariTipi NVARCHAR(20) NOT NULL,
    Il NVARCHAR(50) NOT NULL,
    VergiNo VARCHAR(20) NULL
);
GO

CREATE TABLE dbo.Fatura (
    FaturaId INT NOT NULL PRIMARY KEY,
    CariKodu VARCHAR(20) NOT NULL,
    ExportRefNo VARCHAR(20) NULL,
    FaturaTarihi DATE NOT NULL,
    Tutar DECIMAL(14,2) NOT NULL,
    KdvTutari DECIMAL(14,2) NOT NULL,
    ParaBirimi CHAR(3) NOT NULL
);
GO
