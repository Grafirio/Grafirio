-- Belirlenimci veri (rastgele sayi yok): ayni betik her kurulumda ayni
-- satirlari uretir, altin SQL'in cevabi sabit kalir.

SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
INTO #n
FROM sys.all_objects a CROSS JOIN sys.all_objects b;
GO

INSERT INTO dbo.L_DEF_Country (CountryCode, CountryName, Region) VALUES
 ('TR', N'Türkiye', N'Avrupa'), ('DE', N'Almanya', N'Avrupa'), ('NL', N'Hollanda', N'Avrupa'),
 ('FR', N'Fransa', N'Avrupa'), ('IT', N'İtalya', N'Avrupa'), ('GB', N'Birleşik Krallık', N'Avrupa'),
 ('PL', N'Polonya', N'Avrupa'), ('RO', N'Romanya', N'Avrupa'), ('BG', N'Bulgaristan', N'Avrupa'),
 ('IQ', N'Irak', N'Orta Doğu'), ('AE', N'Birleşik Arap Emirlikleri', N'Orta Doğu'),
 ('SA', N'Suudi Arabistan', N'Orta Doğu'), ('US', N'ABD', N'Amerika'), ('CN', N'Çin', N'Asya'),
 ('RU', N'Rusya', N'Avrupa'), ('GE', N'Gürcistan', N'Kafkasya'), ('AZ', N'Azerbaycan', N'Kafkasya'),
 ('KZ', N'Kazakistan', N'Asya'), ('EG', N'Mısır', N'Afrika'), ('ES', N'İspanya', N'Avrupa');
GO

INSERT INTO dbo.L_DEF_Currency (CurrencyCode, CurrencyName) VALUES
 ('EUR', N'Euro'), ('USD', N'ABD Doları'), ('TRY', N'Türk Lirası'), ('GBP', N'İngiliz Sterlini'), ('CHF', N'İsviçre Frangı');
GO

INSERT INTO dbo.L_DEF_Parameter (ParameterId, ParamGroup, ParamCode, ParamText) VALUES
 (1, 'TRN', 'ROD', N'Karayolu'), (2, 'TRN', 'SEA', N'Denizyolu'), (3, 'TRN', 'AIR', N'Havayolu'), (4, 'TRN', 'RAIL', N'Demiryolu'),
 (5, 'STS', '1', N'Açık'), (6, 'STS', '2', N'Yüklendi'), (7, 'STS', '3', N'Teslim edildi'), (8, 'STS', '4', N'İptal'),
 (9, 'EVT', 'DEP', N'Çıkış'), (10, 'EVT', 'BRD', N'Sınır kapısı'), (11, 'EVT', 'CUS', N'Gümrük'), (12, 'EVT', 'ARR', N'Varış');
GO

-- 200 firma; ilk 20'si Turkiye, gerisi dagitilmis. Her 10. firmanin bir ana firmasi var.
INSERT INTO dbo.L_DEF_Company (CompanyId, CompanyCode, CompanyName, CountryCode, ParentCompanyId, TaxNumber,
                               PhoneNumber, CreatedAt)
SELECT i,
       CONCAT('FRM', RIGHT(CONCAT('0000', i), 4)),
       CONCAT(CHOOSE(1 + (i % 6), N'Anadolu', N'Marmara', N'Ege', N'Delta', N'Orion', N'Kuzey'), N' ',
              CHOOSE(1 + (i % 4), N'Tekstil', N'Gıda', N'Makina', N'Kimya'), N' ', i),
       CASE WHEN i <= 20 THEN 'TR'
            ELSE CHOOSE(1 + (i % 19), 'DE', 'NL', 'FR', 'IT', 'GB', 'PL', 'RO', 'BG', 'IQ', 'AE', 'SA', 'US', 'CN',
                        'RU', 'GE', 'AZ', 'KZ', 'EG', 'ES') END,
       CASE WHEN i % 10 = 0 THEN i - 9 ELSE NULL END,
       RIGHT(CONCAT('0000000000', (i * 7919) % 10000000000), 10),
       CONCAT('+90 212 ', RIGHT(CONCAT('0000000', (i * 104729) % 10000000), 7)),
       DATEADD(DAY, i, '2023-01-01')
FROM #n WHERE i <= 200;
GO

-- 3000 ihracat referansi. Gonderici Turk firma (1-20), alici yabanci (21-200).
-- Tasima sekli dagilimi bilerek dengesiz: karayolu agirlikli.
INSERT INTO dbo.L_INT_ExportReference (ExportReferenceId, ReferenceNo, ShipperCompanyId, ReceiverCompanyId,
    DestinationCountryCode, TransportModeCode, LoadingDate, GrossWeightKg, FreightAmount, CurrencyCode, StatusId)
SELECT e.i,
       CONCAT('EX', RIGHT(CONCAT('000000', e.i), 6)),
       1 + (e.i % 20),
       r.ReceiverId,
       c.CountryCode,
       CASE WHEN e.i % 10 < 6 THEN 'ROD' WHEN e.i % 10 < 9 THEN 'SEA' ELSE 'AIR' END,
       DATEADD(DAY, (e.i * 211) % 630, '2025-01-01'),
       CAST(500 + (e.i * 97) % 23500 AS DECIMAL(12,2)),
       CAST(400 + (e.i * 53) % 5600 AS DECIMAL(14,2)),
       CHOOSE(1 + (e.i % 5), 'EUR', 'EUR', 'USD', 'TRY', 'GBP'),
       1 + (e.i % 4)
FROM #n e
CROSS APPLY (SELECT 21 + (e.i * 17) % 180 AS ReceiverId) r
JOIN dbo.L_DEF_Company c ON c.CompanyId = r.ReceiverId
WHERE e.i <= 3000;
GO

-- 2000 ithalat referansi: gonderen yabanci, alan Turk firma.
INSERT INTO dbo.L_INT_ImportReference (ImportReferenceId, ReferenceNo, SenderCompanyId, ConsigneeCompanyId,
    OriginCountryCode, TransportModeCode, ArrivalDate, GrossWeightKg, FreightAmount, CurrencyCode)
SELECT m.i,
       CONCAT('IM', RIGHT(CONCAT('000000', m.i), 6)),
       s.SenderId,
       1 + (m.i % 20),
       c.CountryCode,
       CASE WHEN m.i % 10 < 4 THEN 'ROD' WHEN m.i % 10 < 8 THEN 'SEA' ELSE 'AIR' END,
       DATEADD(DAY, (m.i * 173) % 630, '2025-01-01'),
       CAST(800 + (m.i * 89) % 30000 AS DECIMAL(12,2)),
       CAST(600 + (m.i * 61) % 7000 AS DECIMAL(14,2)),
       CHOOSE(1 + (m.i % 4), 'USD', 'EUR', 'USD', 'CHF')
FROM #n m
CROSS APPLY (SELECT 21 + (m.i * 29) % 180 AS SenderId) s
JOIN dbo.L_DEF_Company c ON c.CompanyId = s.SenderId
WHERE m.i <= 2000;
GO

-- Karayolu ihracatinin her birine bir sefer; her 4. referansa ikinci sefer.
INSERT INTO dbo.L_ROD_ExportPosition (PositionId, ExportReferenceId, TruckPlate, DriverNationalId,
    CarrierCompanyNo, DepartureDate, ArrivalDate, DistanceKm)
SELECT ROW_NUMBER() OVER (ORDER BY e.ExportReferenceId, k.k),
       e.ExportReferenceId,
       CONCAT(CHOOSE(1 + (e.ExportReferenceId % 5), '34', '16', '35', '06', '41'), ' ',
              CHAR(65 + e.ExportReferenceId % 26), CHAR(65 + (e.ExportReferenceId / 26) % 26), ' ',
              100 + (e.ExportReferenceId * 7) % 900),
       CASE WHEN e.ExportReferenceId % 9 = 0 THEN NULL
            ELSE CONCAT('1', RIGHT(CONCAT('0000000000', (e.ExportReferenceId * 104723) % 10000000000), 10)) END,
       1 + (e.ExportReferenceId % 30),
       DATEADD(DAY, k.k, e.LoadingDate),
       CASE WHEN e.StatusId >= 3 THEN DATEADD(DAY, k.k + 3 + e.ExportReferenceId % 5, e.LoadingDate) END,
       900 + (e.ExportReferenceId * 13) % 3600
FROM dbo.L_INT_ExportReference e
CROSS JOIN (VALUES (0), (1)) k(k)
WHERE e.TransportModeCode = 'ROD' AND (k.k = 0 OR e.ExportReferenceId % 4 = 0);
GO

-- Her sefere 2-4 takip olayi.
INSERT INTO dbo.L_TRK_PositionEvent (EventId, PostionId, EventCode, EventTime)
SELECT ROW_NUMBER() OVER (ORDER BY p.PositionId, k.k),
       p.PositionId,
       CHOOSE(k.k, 'DEP', 'BRD', 'CUS', 'ARR'),
       DATEADD(HOUR, k.k * 18, CAST(p.DepartureDate AS DATETIME2))
FROM dbo.L_ROD_ExportPosition p
CROSS JOIN (VALUES (1), (2), (3), (4)) k(k)
WHERE k.k <= 2 + p.PositionId % 3;
GO

INSERT INTO dbo.Cari (CariKod, Unvan, CariTipi, Il, VergiNo)
SELECT CONCAT('C', RIGHT(CONCAT('0000', i), 4)),
       CONCAT(CHOOSE(1 + (i % 5), N'Yıldız', N'Güneş', N'Boğaziçi', N'Toros', N'Ada'), N' Ticaret ', i),
       CASE WHEN i % 3 = 0 THEN N'Tedarikçi' ELSE N'Müşteri' END,
       CHOOSE(1 + (i % 5), N'İstanbul', N'Bursa', N'İzmir', N'Ankara', N'Kocaeli'),
       RIGHT(CONCAT('0000000000', (i * 15485863) % 10000000000), 10)
FROM #n WHERE i <= 150;
GO

-- 2500 fatura. %70'i bir ihracat referansina bagli (kisaltilmis kolon: ExportRefNo).
INSERT INTO dbo.Fatura (FaturaId, CariKodu, ExportRefNo, FaturaTarihi, Tutar, KdvTutari, ParaBirimi)
SELECT i,
       CONCAT('C', RIGHT(CONCAT('0000', 1 + (i * 7) % 150), 4)),
       CASE WHEN i % 10 < 7 THEN CONCAT('EX', RIGHT(CONCAT('000000', 1 + (i * 11) % 3000), 6)) END,
       DATEADD(DAY, (i * 239) % 630, '2025-01-02'),
       CAST(1000 + (i * 71) % 49000 AS DECIMAL(14,2)),
       CAST((1000 + (i * 71) % 49000) * 0.20 AS DECIMAL(14,2)),
       CHOOSE(1 + (i % 4), 'TRY', 'TRY', 'EUR', 'USD')
FROM #n WHERE i <= 2500;
GO

DROP TABLE #n;
GO
