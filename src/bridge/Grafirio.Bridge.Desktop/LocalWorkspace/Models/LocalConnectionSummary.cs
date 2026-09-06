namespace Grafirio.Bridge.Desktop.LocalWorkspace.Models;

public sealed record LocalConnectionSummary(
    Guid Id, string Name, string Provider, string Host, int Port,
    string Database, string Username, bool TrustServerCertificate,
    string[] AllowedTables, bool HasPassword);