namespace Grafirio.DataAnalysis.Api.Data.Entities;

/// <summary>
/// PyCaret sorgu geçmişi — hangi sorular soruldu, ne cevap geldi
/// </summary>
public class QueryHistory
{
    public Guid Id { get; set; }

    public Guid ConfigId { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Kullanıcının sorduğu doğal dil sorusu
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// LLM'in PyCaret için oluşturduğu analiz parametreleri (JSON)
    /// </summary>
    public string PyCaretParamsJson { get; set; } = "{}";

    /// <summary>
    /// PyCaret'ten dönen sonuç (JSON)
    /// </summary>
    public string ResultJson { get; set; } = "{}";

    /// <summary>
    /// İşlem durumu: pending, processing, completed, failed
    /// </summary>
    public string Status { get; set; } = "pending";

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
