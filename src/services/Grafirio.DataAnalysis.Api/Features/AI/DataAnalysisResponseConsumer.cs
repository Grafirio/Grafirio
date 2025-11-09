using Grafirio.Shared.MassTransit.Messages.AI;
using MassTransit;

namespace Grafirio.DataAnalysis.Api.Features.AI;

/// <summary>
/// AI analiz response'larını dinleyen consumer
/// </summary>
public class DataAnalysisResponseConsumer : IConsumer<IDataAnalysisResponse>
{
    private readonly ILogger<DataAnalysisResponseConsumer> _logger;

    public DataAnalysisResponseConsumer(ILogger<DataAnalysisResponseConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IDataAnalysisResponse> context)
    {
        var response = context.Message;
        
        _logger.LogInformation(
            "Received AI analysis response. RequestId: {RequestId}, Success: {Success}, UserId: {UserId}",
            response.RequestId,
            response.Success,
            response.UserId
        );

        if (response.Success)
        {
            _logger.LogInformation("✅ AI Analysis completed successfully for RequestId: {RequestId}", response.RequestId);
            
            // Response handling logic can be added here:
            // - Store in database/Redis for caching
            // - Send SignalR notification to frontend
            // - Prepare dashboard card data
            
            if (response.Results != null)
            {
                _logger.LogInformation(
                    "Analysis results - Title: {Title}, Charts: {ChartCount}, Insights: {InsightCount}",
                    response.Results.Title,
                    response.Results.Charts.Count,
                    response.Results.Insights.Count
                );
            }
        }
        else
        {
            _logger.LogError(
                "AI Analysis failed for RequestId: {RequestId}. Error: {Error}",
                response.RequestId,
                response.ErrorMessage
            );
        }

        await Task.CompletedTask;
    }
}
