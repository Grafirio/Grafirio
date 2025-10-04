namespace Grafirio.Shared.MassTransit.Messages.AI;

/// <summary>
/// AI grafik verisi yanıtı için message contract
/// </summary>
public interface IGraphDataResponse
{
    Guid RequestId { get; }
    bool Success { get; }
    string? ErrorMessage { get; }
    DateTime ResponseTime { get; }
    Dictionary<string, object>? Data { get; }
}

/// <summary>
/// Grafik verisi yanıtı concrete implementation
/// </summary>
public record GraphDataResponse(
    Guid RequestId,
    bool Success,
    string? ErrorMessage,
    DateTime ResponseTime,
    Dictionary<string, object>? Data
) : IGraphDataResponse;
