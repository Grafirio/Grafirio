using System.Text.Json.Nodes;

namespace Grafirio.DataAnalysis.Api.Application.Analysis;

public static class GeneralAnalysisPolicy
{
    public static JsonObject Read()
    {
        using var stream = typeof(GeneralAnalysisPolicy).Assembly.GetManifestResourceStream("Grafirio.GeneralAnalysisPolicy.json")
            ?? throw new InvalidOperationException("General analysis policy is missing.");
        return JsonNode.Parse(stream)!.AsObject();
    }
}
