namespace Grafirio.QueryPolicy;

/// <summary>A fail-closed SQL validation failure, safe to report without SQL parameter values.</summary>
public sealed class QueryPolicyException(string message, bool tableNotAllowed = false)
    : InvalidOperationException(message)
{
    public bool TableNotAllowed { get; } = tableNotAllowed;
}