using Grafirio.DataAnalysis.Api.Data.Access;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Ic veri ucunun okuma kisiti.
///
/// Bu uc PyCaret'in urettigi SQL'i calistiriyor; yani bir LLM'in etkiledigi
/// metin, musteri veritabaninda kosuyor. Kisitin kendisi burada olculuyor
/// cunku ayni kural Faz 2'de bridge tarafinda da uygulanacak: bridge buluta
/// guvenmemeli.
/// </summary>
public class InternalQueryGuardTests
{
    [Theory]
    [InlineData("SELECT * FROM dbo.Shipments")]
    [InlineData("  select 1")]
    [InlineData("WITH x AS (SELECT 1 AS a) SELECT a FROM x")]
    [InlineData("SELECT COUNT(*) FROM t;")]                      // sondaki ; zararsiz
    [InlineData("-- açıklama\nSELECT 1")]
    [InlineData("/* açıklama */ SELECT 1")]
    public void Okuma_sorgulari_gecer(string sql) =>
        Assert.True(ReadOnlySqlPolicy.IsReadOnly(sql));

    [Theory]
    [InlineData("DROP TABLE dbo.Shipments")]
    [InlineData("DELETE FROM dbo.Shipments")]
    [InlineData("UPDATE t SET a = 1")]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("EXEC sp_who")]
    [InlineData("TRUNCATE TABLE t")]
    public void Yazma_sorgulari_reddedilir(string sql) =>
        Assert.False(ReadOnlySqlPolicy.IsReadOnly(sql));

    [Theory]
    // Ikinci ifade eklemek, okuma gibi baslayip yazmaya donen tek yol.
    [InlineData("SELECT 1; DROP TABLE dbo.Shipments")]
    [InlineData("SELECT 1; DELETE FROM t;")]
    // Yorumun arkasina saklanan yazma.
    [InlineData("-- SELECT 1\nDROP TABLE t")]
    [InlineData("/* SELECT */ DELETE FROM t")]
    // Kapanmayan yorum: geri kalani okunamiyorsa gecirilmemeli.
    [InlineData("/* açılmış ama kapanmamış SELECT 1")]
    public void Okuma_gibi_gorunen_yazma_reddedilir(string sql) =>
        Assert.False(ReadOnlySqlPolicy.IsReadOnly(sql));
}
