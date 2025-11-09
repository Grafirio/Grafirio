namespace Grafirio.Shared.MassTransit.Messages.AI;

/// <summary>
/// AI veri analizi talebi için message contract
/// </summary>
public interface IDataAnalysisRequest
{
    Guid RequestId { get; }
    string UserId { get; }
    string CompanyId { get; }
    DateTime RequestTime { get; }
    
    // Connection Info
    string Host { get; }
    int Port { get; }
    string Database { get; }
    string Username { get; }
    string Password { get; }
    bool TrustServerCertificate { get; }
    
    // Selected Tables
    List<string> Tables { get; }
    
    // AI Settings
    int SamplingRate { get; }
    string NullHandling { get; }
    string DataFormat { get; }
}

/// <summary>
/// Veri analizi talebi concrete implementation
/// </summary>
public record DataAnalysisRequest(
    Guid RequestId,
    string UserId,
    string CompanyId,
    DateTime RequestTime,
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate,
    List<string> Tables,
    int SamplingRate,
    string NullHandling,
    string DataFormat
) : IDataAnalysisRequest;
