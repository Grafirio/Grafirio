using System.Globalization;
using System.Text;
using Grafirio.Bridge.Contracts;

namespace Grafirio.Bridge;

/// <summary>
/// Her sorgunun musterinin kendi diskine yazildigi denetim gunlugu.
///
/// Neden ayri bir dosya: bu kayit musterinin, bizim degil. "Verimde ne
/// yapildi" sorusunun cevabi bizim loglarimizda degil, kendi sunucularinda
/// durmali — bizim erisimimiz olmadan, bizden bagimsiz saklanabilir halde.
/// Guvenlik ekiplerinin ilk soracagi seylerden biri bu.
///
/// Bicim satir bazli TSV: Excel'de de, SIEM'de de okunur.
/// </summary>
public class QueryAuditLog(ILogger<QueryAuditLog> logger, string filePath)
{
    private readonly Lock _writeLock = new();

    /// <summary>Gunluge yazilan sorgu metninin tavani.</summary>
    private const int MaxSqlLength = 4000;

    public string FilePath { get; } = filePath;

    /// <param name="databaseMs">
    /// Veritabaninda gecen sure. Musterinin DBA'i "bu sorgular sunucumu ne kadar
    /// mesgul ediyor" sorusunu kendi gunlugunden cevaplayabilsin diye yaziliyor.
    /// </param>
    public void Completed(ExecuteQueryRequest request, int rowCount, bool truncated, double? databaseMs = null) =>
        Write("tamamlandı", request, databaseMs is { } ms
            ? string.Create(CultureInfo.InvariantCulture, $"satır={rowCount} kırpıldı={truncated} süre_ms={ms}")
            : $"satır={rowCount} kırpıldı={truncated}");

    public void Rejected(ExecuteQueryRequest request, string reason) =>
        Write("REDDEDİLDİ", request, reason);

    public void Failed(ExecuteQueryRequest request, string reason) =>
        Write("hata", request, reason);

    private void Write(string outcome, ExecuteQueryRequest request, string detail)
    {
        // Parametre DEGERLERI yazilmiyor: bir filtre degeri musteri adi ya da
        // TC kimlik numarasi olabilir. Denetim icin gereken sey hangi sorgunun
        // kostugu, hangi degerle kostugu degil.
        var sql = request.Sql.Length > MaxSqlLength
            ? request.Sql[..MaxSqlLength] + "…"
            : request.Sql;

        var line = string.Join('\t',
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            outcome,
            request.RequestId,
            request.ConnectionId,
            detail,
            sql.ReplaceLineEndings(" "));

        try
        {
            lock (_writeLock)
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            // Gunluge yazamamak sorguyu durdurmuyor ama sessiz de kalmiyor:
            // denetim izinin eksik oldugunu bilmek gerekiyor.
            logger.LogError(ex, "Denetim günlüğüne yazılamadı: {Path}", FilePath);
        }
    }
}
