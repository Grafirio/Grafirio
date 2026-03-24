namespace Grafirio.Shared.MassTransit.Messages.AI;

/// <summary>
/// PyCaret analiz talebi — LLM tarafından oluşturulan parametrelerle
/// </summary>
public interface IPyCaretAnalysisRequest
{
    Guid RequestId { get; }
    Guid QueryId { get; }
    string CompanyId { get; }
    DateTime RequestTime { get; }

    /// <summary>
    /// Bağlantı bilgileri
    /// </summary>
    string Host { get; }
    int Port { get; }
    string Database { get; }
    string Username { get; }
    string Password { get; }
    bool TrustServerCertificate { get; }

    /// <summary>
    /// LLM tarafından oluşturulan PyCaret config JSON
    /// </summary>
    string ConfigJson { get; }

    /// <summary>
    /// LLM tarafından bu sorgu için oluşturulan analiz parametreleri JSON
    /// </summary>
    string AnalysisParamsJson { get; }

    /// <summary>
    /// Kullanıcının orijinal sorusu
    /// </summary>
    string UserQuestion { get; }
}

public record PyCaretAnalysisRequest(
    Guid RequestId,
    Guid QueryId,
    string CompanyId,
    DateTime RequestTime,
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate,
    string ConfigJson,
    string AnalysisParamsJson,
    string UserQuestion
) : IPyCaretAnalysisRequest;
