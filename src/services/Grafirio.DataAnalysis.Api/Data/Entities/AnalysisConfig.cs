namespace Grafirio.DataAnalysis.Api.Data.Entities;

/// <summary>
/// LLM tarafından oluşturulan PyCaret analiz konfigürasyonu
/// </summary>
public class AnalysisConfig
{
    public Guid Id { get; set; }

    /// <summary>
    /// İlişkili bağlantı ID'si
    /// </summary>
    public Guid ConnectionId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string CompanyId { get; set; } = string.Empty;

    /// <summary>
    /// Veritabanı adı
    /// </summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// LLM tarafından üretilen JSON config (PyCaret için)
    /// </summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>
    /// LLM'in ürettiği schema özeti / açıklaması
    /// </summary>
    public string SchemaSummary { get; set; } = string.Empty;

    /// <summary>
    /// Analiz edilen tablolar (JSON array)
    /// </summary>
    public string TablesJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Config durumu: pending, ready, failed
    /// </summary>
    public string Status { get; set; } = "pending";

    public bool IsActive { get; set; } = true;
}
