-- Grafirio: LLM/AI SQL yolu icin salt-okunur giris.
-- Amac: ai_tasks.execute_sql'in artik 'sa' yerine bu kullaniciyla baglanmasi —
-- LLM'in urettigi sorgu ne kadar yanlis olursa olsun veri degistiremesin.
--
-- Calistirma (host'tan):
--   sqlcmd -S localhost,1433 -U sa -P "<SA_PASSWORD>" -i scripts/sql/create_readonly_login.sql
-- veya docker container icinden:
--   docker exec -i sqlserver.db.order /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "<SA_PASSWORD>" -C -i /path/create_readonly_login.sql

:setvar ReadonlyPassword "CHANGE_ME_STRONG_PASSWORD"
:setvar DatabaseName "GrafirioECommerce"

USE master;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'grafirio_readonly')
BEGIN
    CREATE LOGIN grafirio_readonly WITH PASSWORD = '$(ReadonlyPassword)', CHECK_POLICY = ON;
END
GO

USE [$(DatabaseName)];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'grafirio_readonly')
BEGIN
    CREATE USER grafirio_readonly FOR LOGIN grafirio_readonly;
END
GO

ALTER ROLE db_datareader ADD MEMBER grafirio_readonly;
GO

-- Savunma katmani: baska veritabanlarini gormesin
DENY VIEW ANY DATABASE TO grafirio_readonly;
GO

PRINT 'grafirio_readonly olusturuldu: db_datareader yetkisiyle, DML/DDL yetkisi yok.';
GO
