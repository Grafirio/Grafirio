using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Data;

/// <summary>
/// <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.EnsureCreated"/>
/// sonrasi calisan, elle yazilmis ve idempotent sema yamalari.
///
/// Neden var: bu serviste migration yok, sema <c>EnsureCreated()</c> ile
/// kuruluyor. O cagri tabloyu YALNIZCA yoksa olusturur — tablo varsa hicbir
/// sey yapmaz. Yani bir varliga yeni kolon eklemek, gelistiricinin bos
/// veritabaninda calisir, uretimdeki dolu veritabaninda calismaz: uygulama
/// ayaga kalkar, ilk sorguda "column ... does not exist" der ve bunun sebebi
/// koda bakarak gorunmez.
///
/// Buradaki her ifade <c>IF NOT EXISTS</c> ile yazilir; her aciliste
/// calismasi normaldir ve ikinci calistirma hicbir sey yapmaz. Veri
/// tasiyan/silen bir ifade buraya YAZILMAZ — bu dosya yalnizca eksik kolonu
/// ekler. Sema gercekten degisecekse migration'a gecmek gerekir.
/// </summary>
public static class SchemaPatches
{
    /// <summary>
    /// Kolon adlari PascalCase ve tirnakli: Npgsql'de isim donusumu
    /// yapilandirilmamis, EF varlik adlarini oldugu gibi kullaniyor.
    /// </summary>
    private static readonly string[] Statements =
    {
        // Konusma zinciri: bir sorunun hangi sorunun altina yazildigi.
        """ALTER TABLE "QueryHistories" ADD COLUMN IF NOT EXISTS "ParentQueryId" uuid NULL""",

        // Netlestirme turunda sistemin sordugu cumle.
        """ALTER TABLE "QueryHistories" ADD COLUMN IF NOT EXISTS "ClarificationQuestion" character varying(2000) NULL""",

        // Zincir geriye dogru yurundugu icin ebeveyn kimligi aranabilir olmali.
        """CREATE INDEX IF NOT EXISTS "IX_QueryHistories_ParentQueryId" ON "QueryHistories" ("ParentQueryId")""",
    };

    /// <summary>
    /// Yamalari sirayla uygular. Tek tek calistiriliyor ki biri patlarsa
    /// digerleri uygulanmadan kalmasin ve hangisinin patladigi loglanabilsin.
    /// </summary>
    public static void Apply(DataAnalysisDbContext db, ILogger logger)
    {
        foreach (var statement in Statements)
        {
            try
            {
                db.Database.ExecuteSqlRaw(statement);
            }
            catch (Exception ex)
            {
                // Yama uygulanamazsa servis yine de ayaga kalkmali: eksik kolon
                // o kolonu kullanan ucu bozar, hepsini degil.
                logger.LogError(ex, "Şema yaması uygulanamadı: {Statement}", statement);
            }
        }
    }
}
