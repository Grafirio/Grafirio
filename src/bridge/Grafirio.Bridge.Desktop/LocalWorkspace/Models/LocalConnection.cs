namespace Grafirio.Bridge.Desktop.LocalWorkspace.Models;

public sealed record LocalConnection
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Provider { get; init; } = "sqlserver";
    public string Host { get; init; } = "";
    public int Port { get; init; } = 1433;
    public string Database { get; init; } = "";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public bool TrustServerCertificate { get; init; }
    public string[] AllowedTables { get; init; } = [];

    public LocalConnectionSummary ToSummary() => new(
        Id, Name, Provider, Host, Port, Database, Username,
        TrustServerCertificate, AllowedTables.ToArray(), Password.Length > 0);
}