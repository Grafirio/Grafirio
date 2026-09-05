using System.Globalization;
using Grafirio.Bridge;

namespace Grafirio.Bridge.Tests;

/// <summary>
/// Bridge'in okuma kisiti.
///
/// Bu, musteriye verilen sozun kendisi: "buluttan gelen sorgu veritabaninizi
/// degistiremez". Sunucu tarafinda ayni isimli bir kural daha var ama asil
/// olan bu — sunucu ele gecirilse bile burasi durmali.
///
/// Sunucu tarafindaki surumden daha siki: orada yalnizca ilk anahtar kelimeye
/// ve noktali virgule bakiliyor, burada okuma gibi baslayip yazmaya donen
/// bicimler de eleniyor.
/// </summary>
public class ReadOnlySqlTests
{
    [Theory]
    [InlineData("SELECT * FROM dbo.Shipments")]
    [InlineData("  select 1")]
    [InlineData("WITH x AS (SELECT 1 AS a) SELECT a FROM x")]
    [InlineData("SELECT COUNT(*) FROM t;")]
    [InlineData("-- açıklama\nSELECT 1")]
    [InlineData("/* açıklama */ SELECT 1")]
    [InlineData("SELECT TOP 100 * FROM [dbo].[Orders] WHERE [Ulke] = @p0")]
    public void Okuma_sorgulari_gecer(string sql) =>
        Assert.True(ReadOnlySql.IsReadOnly(sql));

    [Theory]
    [InlineData("DROP TABLE dbo.Shipments")]
    [InlineData("DELETE FROM dbo.Shipments")]
    [InlineData("UPDATE t SET a = 1")]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("EXEC sp_who")]
    [InlineData("EXECUTE sp_who")]
    [InlineData("TRUNCATE TABLE t")]
    [InlineData("MERGE t USING s ON t.id = s.id WHEN MATCHED THEN UPDATE SET a = 1")]
    [InlineData("GRANT SELECT ON t TO public")]
    public void Yazma_sorgulari_reddedilir(string sql) =>
        Assert.False(ReadOnlySql.IsReadOnly(sql));

    [Theory]
    // Ikinci ifade: okuma gibi baslayip yazmaya donmenin en bilinen yolu.
    [InlineData("SELECT 1; DROP TABLE dbo.Shipments")]
    [InlineData("SELECT 1; DELETE FROM t;")]
    // SELECT ... INTO yeni bir tablo OLUSTURUR. Ilk kelimesi SELECT oldugu
    // icin naif bir kontrolden gecer; sunucu tarafindaki surum bunu kaciriyor,
    // musterinin makinesindeki surum kacirmamali.
    [InlineData("SELECT * INTO yedek FROM dbo.Shipments")]
    [InlineData("select a, b into #tmp from t")]
    // Yorumun arkasina saklanan yazma.
    [InlineData("-- SELECT 1\nDROP TABLE t")]
    [InlineData("/* SELECT */ DELETE FROM t")]
    // Kapanmayan yorum: geri kalani okunamiyorsa gecirilmemeli.
    [InlineData("/* açılmış ama kapanmamış SELECT 1")]
    public void Okuma_gibi_gorunen_yazma_reddedilir(string sql) =>
        Assert.False(ReadOnlySql.IsReadOnly(sql));

    [Fact]
    public void Bos_sorgu_reddedilir() => Assert.False(ReadOnlySql.IsReadOnly("   "));

    /// <summary>
    /// Türkçe kültürde 'i' harfinin büyüğü 'İ', 'I' harfinin küçüğü 'ı'.
    /// Kural <c>CultureInvariant</c> olmadan yazıldığında "into" ile "INTO"
    /// eşleşmiyor — yani koruma, müşteri sunucularının çoğunda (tr-TR) tam da
    /// çalışması gereken yerde çalışmıyordu.
    ///
    /// Bu testin kültürü açıkça değiştirmesinin sebebi bu: derleme makinesinin
    /// yerel ayarı değiştiğinde hata sessizce geri gelmesin.
    /// </summary>
    [Theory]
    [InlineData("select a, b into #tmp from t")]
    [InlineData("SELECT 1; drop table t")]
    [InlineData("select * into yedek from t")]
    public void Turkce_kulturde_de_reddediliyor(string sql)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

        try
        {
            Assert.False(ReadOnlySql.IsReadOnly(sql));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}

/// <summary>
/// Izin listesi yapilandirilmis kurulumlarda hangi tablolara dokunuldugunun
/// bulunmasi.
///
/// Tarayici bir SQL ayristiricisi degil ve olmaya calismiyor. Olculen sey:
/// gercek tablo referanslarini KACIRMIYOR. Fazla yakalamasi kabul edilebilir
/// (bir sorgu reddedilir), az yakalamasi kabul edilemez (izinsiz tabloya
/// gidilir).
/// </summary>
public class SqlTableScannerTests
{
    private static string[] Scan(string sql) => SqlTableScanner.ReferencedTables(sql).ToArray();

    [Fact]
    public void CteAliasesAreNotPhysicalTables()
    {
        var tables = Scan("WITH x AS (SELECT Id FROM dbo.Allowed), y AS (SELECT Id FROM x) SELECT * FROM y");
        Assert.Equal(new[] { "[dbo].[Allowed]" }, tables);
    }

    [Fact]
    public void CommaJoinsAndUnionBranchesAreAllExtracted()
    {
        var tables = Scan("SELECT a.Id FROM dbo.Allowed a, dbo.Secret b UNION SELECT Id FROM dbo.Other");
        Assert.Equal(new[] { "[dbo].[Allowed]", "[dbo].[Secret]", "[dbo].[Other]" }, tables);
    }

    [Fact]
    public void SchemaQualifiedTableIsNotHiddenByCteName()
    {
        var tables = Scan("WITH Secret AS (SELECT Id FROM dbo.Allowed) SELECT * FROM dbo.Secret");
        Assert.Contains("[dbo].[Secret]", tables);
    }

    [Fact]
    public void Basit_from_bulunuyor() =>
        Assert.Contains("[dbo].[Shipments]", Scan("SELECT * FROM dbo.Shipments"));

    [Fact]
    public void Koseli_parantezli_ad_bulunuyor() =>
        Assert.Contains("[dbo].[Shipments]", Scan("SELECT * FROM [dbo].[Shipments] s"));

    [Fact]
    public void Join_edilen_tablolar_da_bulunuyor()
    {
        var tables = Scan(@"
            SELECT s.Id, c.Unvan
            FROM dbo.Shipments s
            LEFT JOIN dbo.Companies c ON c.Id = s.CompanyId");

        Assert.Contains("[dbo].[Shipments]", tables);
        Assert.Contains("[dbo].[Companies]", tables);
    }

    [Fact]
    public void Ic_ice_sorgudaki_tablo_da_bulunuyor()
    {
        // Asil risk burada: dis sorgu izinli bir tabloya bakarken alt sorgu
        // baska bir tablodan veri cekebilir.
        var tables = Scan(
            "SELECT * FROM dbo.Allowed WHERE Id IN (SELECT Id FROM dbo.Gizli)");

        Assert.Contains("[dbo].[Allowed]", tables);
        Assert.Contains("[dbo].[Gizli]", tables);
    }

    [Fact]
    public void Semasiz_ad_da_bulunuyor() =>
        Assert.Contains("[Shipments]", Scan("SELECT * FROM Shipments"));

    /// <summary>
    /// Küçük harfli <c>join</c>, Türkçe kültürde <c>JOIN</c> ile eşleşmiyordu.
    /// Sonucu: izin listesinde olmayan bir tabloya giden JOIN görünmez kalır
    /// ve sorgu geçerdi.
    /// </summary>
    [Fact]
    public void Turkce_kulturde_kucuk_harfli_join_bulunuyor()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

        try
        {
            var tables = Scan("select * from dbo.Allowed join dbo.Gizli g on g.id = id");
            Assert.Contains("[dbo].[Gizli]", tables);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
