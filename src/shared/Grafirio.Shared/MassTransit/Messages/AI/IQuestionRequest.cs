namespace Grafirio.Shared.MassTransit.Messages.AI;

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
    List<string>? Context { get; } // Önceki veriler/konuşma geçmişi
}

/// <summary>
/// Soru-cevap talebi concrete implementation
/// </summary>
public record QuestionRequest(
    Guid RequestId,
    string UserId,
    string CompanyId,
    DateTime RequestTime,
    string Question,
    List<string>? Context
) : IQuestionRequest;
