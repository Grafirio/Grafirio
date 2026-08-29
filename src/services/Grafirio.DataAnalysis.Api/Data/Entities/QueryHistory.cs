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
    /// İşlem durumu: pending, processing, completed, failed, clarification
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Bu sorunun altına yazıldığı önceki sorunun kimliği — konuşmanın zinciri.
    ///
    /// Tuvalde düğümler görsel olarak birbirine bağlıydı ama sunucuya giden
    /// istek yalnızca bağlantı ve soru metninden ibaretti: hangi düğümün
    /// altına yazıldığı yola bile çıkmıyordu. Zincir bu alanla kuruluyor;
    /// çeviri prompt'una konacak "önceki konuşma" geriye doğru buradan
    /// yürünerek toplanıyor.
    ///
    /// Boş olması normaldir: sol panelden sorulan soru yeni bir konuşma başlatır.
    /// </summary>
    public Guid? ParentQueryId { get; set; }

    /// <summary>
    /// Sistem soruyu çözemeyip kullanıcıya geri sorduğunda sorduğu cümle.
    ///
    /// <see cref="Status"/> <c>clarification</c> olan kayıtlarda dolu. Bu tur
    /// eskiden hiçbir yere yazılmıyordu — uç nokta soruyu sorup dönüyor,
    /// kayıt oluşturulmuyordu. Sonuç: sistem bir soru soruyor ve sorduğunu
    /// unutuyordu; kullanıcı cevap verdiğinde ortada cevaplanacak bir soru
    /// olduğuna dair iz yoktu.
    /// </summary>
    public string? ClarificationQuestion { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
