using Microsoft.AspNetCore.Mvc;

namespace Grafirio.DataAnalysis.Api.Features.AI;

public static class QueryResultEndpoints
{
    public static void MapQueryResultEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai");

        group.MapPost("/update-progress", UpdateProgress)
            .WithName("UpdateQueryProgress")
            .WithTags("AI Query");

        group.MapPost("/query-result", ReceiveAnalysisResult)
            .WithName("ReceiveAnalysisResult")
            .WithTags("AI Query");

        // Frontend polls this exact endpoint
        app.MapGet("/api/ai/reports/status/{requestId:guid}", GetQueryStatus)
            .WithName("GetAIReportStatusDirect")
            .WithTags("AI Query");

        // Alternative endpoint format
        group.MapGet("/analysis-result/{requestId:guid}", GetQueryStatus)
            .WithName("GetAnalysisResult")
            .WithTags("AI Query");
    }

    private static IResult UpdateProgress(
        UpdateProgressRequest request,
        QueryResultStore store,
        ILogger<UpdateProgressRequest> logger)
    {
        try
        {
            logger.LogInformation("📊 Progress update: {RequestId} - {Progress}% - {Message}", 
                request.RequestId, request.Progress, request.Message);

            store.UpdateProgress(request.RequestId, request.Progress, request.Message);

            return Results.Ok(new { success = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Error updating progress");
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static async Task<IResult> ReceiveAnalysisResult(
        AnalysisResultRequest request,
        QueryResultStore store,
        ILogger<AnalysisResultRequest> logger)
    {
        try
        {
            logger.LogInformation("📥 Received analysis result: {RequestId}, Status: {Status}", 
                request.RequestId, request.Status);

            // Convert result to response format
            var response = ConvertToResponse(request);

            store.StoreResult(request.RequestId, response, request.Status);

            return Results.Ok(new { success = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Error receiving analysis result");
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static IResult GetQueryStatus(
        string requestId,
        QueryResultStore store,
        ILogger<QueryResultStore> logger)
    {
        try
        {
            logger.LogInformation("📊 Getting query status for: {RequestId}", requestId);
            
            if (!Guid.TryParse(requestId, out var guidRequestId))
            {
                logger.LogWarning("❌ Invalid GUID format: {RequestId}", requestId);
                return Results.BadRequest(new { 
                    requestId, 
                    status = "error",
                    message = "Invalid request ID format" 
                });
            }

            var result = store.GetResult(guidRequestId);

            if (result == null)
            {
                logger.LogWarning("❌ Query result not found: {RequestId}", requestId);
                return Results.Ok(new { 
                    requestId = guidRequestId, 
                    status = "not_found",
                    message = "Query result not found" 
                });
            }

            logger.LogInformation("✅ Query status found: {RequestId}, Status: {Status}", requestId, result.Status);

            return Results.Ok(new
            {
                requestId = result.RequestId,
                status = result.Status,
                progress = result.Progress,
                progressMessage = result.ProgressMessage,
                result = result.Result,
                completedAt = result.CompletedAt,
                updatedAt = result.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Error getting query status for {RequestId}", requestId);
            return Results.Problem("Internal server error");
        }
    }

    private static object ConvertToResponse(AnalysisResultRequest request)
    {
        // Convert schema analyzer result to AskQuestionResponse format
        if (request.Result == null)
        {
            return new
            {
                success = false,
                question = "Analysis request",
                answer = request.Status == "failed" ? "Analysis failed" : "No results",
                charts = Array.Empty<object>(),
                answeredAt = DateTime.UtcNow
            };
        }

        // TODO: Parse semantic_schema and convert to charts
        return new
        {
            success = true,
            question = "Database analysis",
            answer = $"Analysis completed for database: {request.Result.GetType().Name}",
            charts = Array.Empty<object>(),
            rawResult = request.Result,
            answeredAt = DateTime.UtcNow
        };
    }
}

public record UpdateProgressRequest(
    Guid RequestId,
    int Progress,
    string Message,
    DateTime Timestamp
);

public record AnalysisResultRequest(
    Guid RequestId,
    string Status,
    string? Result,      // Changed from dynamic to string
    string CompletedAt
);
