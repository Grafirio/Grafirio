namespace Grafirio.DataAnalysis.Api.Features.Connections;

public record SaveConnectionRequest(
    string UserId,
    string CompanyId,
    string Name,
    string Host,
    int Port,
    string Database,
    string Username,
    string? Password,
    bool TrustServerCertificate
);