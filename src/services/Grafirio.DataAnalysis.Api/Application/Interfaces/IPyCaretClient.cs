using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Application.Analysis;

namespace Grafirio.DataAnalysis.Api.Application.Interfaces;

public interface IPyCaretClient
{
    bool IsConfigured { get; }
    Task<JsonObject> SubmitAsync(PyCaretExecutionContext scope, string configJson,
        string parametersJson, string question, CancellationToken ct);
    Task<JsonObject> GetStatusAsync(PyCaretExecutionContext scope, CancellationToken ct);
    Task<JsonObject> GetResultAsync(PyCaretExecutionContext scope, CancellationToken ct);
    Task<JsonObject> CancelAsync(PyCaretExecutionContext scope, CancellationToken ct);
}