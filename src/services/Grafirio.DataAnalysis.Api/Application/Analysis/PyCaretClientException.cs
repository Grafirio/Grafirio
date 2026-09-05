using System.Net;

namespace Grafirio.DataAnalysis.Api.Application.Analysis;

public sealed class PyCaretClientException(string message, HttpStatusCode? statusCode = null)
    : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public bool IsTransient => StatusCode is null or HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests || (int?)StatusCode >= 500;
}