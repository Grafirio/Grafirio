namespace Grafirio.Shared.MassTransit.Messages.AI;

/// <summary>Konuşma geçmişindeki tek mesaj.</summary>
public record ChatContextItem(string Role, string Content);

/// <summary>
/// AI soru-cevap talebi için message contract
/// </summary>
public interface IQuestionRequest
{
    Guid RequestId { get; }
    string UserId { get; }
    string CompanyId { get; }
    DateTime RequestTime { get; }
    string Question { get; }
    List<ChatContextItem>? Context { get; } // Önceki konuşma geçmişi
    string? Database { get; }              // Sorgulanacak veritabanı adı
    List<string>? Tables { get; }          // İlgili tablo listesi (ipucu)
    string? TableName { get; }             // Predictive istekler icin hedef tablo
    Dictionary<string, object>? PredictData { get; } // Predictive istekler icin satir verisi
}

/// <summary>Concrete implementation</summary>
public record QuestionRequest(
    Guid RequestId,
    string UserId,
    string CompanyId,
    DateTime RequestTime,
    string Question,
    List<ChatContextItem>? Context,
    string? Database,
    List<string>? Tables,
    string? TableName,
    Dictionary<string, object>? PredictData
) : IQuestionRequest;
