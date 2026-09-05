namespace Grafirio.DataAnalysis.Api.Application.Analysis;

public sealed class StaleAnalysisJobException : Exception
{
    public StaleAnalysisJobException() : base("Analysis job is no longer current.") { }
}