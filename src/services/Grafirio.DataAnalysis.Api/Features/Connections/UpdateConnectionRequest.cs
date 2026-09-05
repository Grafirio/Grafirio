namespace Grafirio.DataAnalysis.Api.Features.Connections;

public record UpdateConnectionRequest(
    string? Name,
    string? Host,
    int? Port,
    string? Database,
    string? Username,
    string? Password,
    bool? TrustServerCertificate
);