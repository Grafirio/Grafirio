using System.Text.RegularExpressions;
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;
using SqlPolicy = Grafirio.QueryPolicy.QueryPolicy;

namespace Grafirio.Bridge.Desktop.LocalWorkspace;

public static partial class WorkspaceValidation
{
    public static void Connection(LocalConnection connection, bool requirePassword = true)
    {
        Text(connection.Name, 100, "Bağlantı adı");
        Text(connection.Host, 253, "Sunucu");
        if (!HostPattern().IsMatch(connection.Host))
            throw new ArgumentException("Sunucu yalnızca bir DNS adı veya IP adresi olmalıdır.");
        if (connection.Provider is not ("sqlserver" or "postgres" or "mysql"))
            throw new ArgumentException("Desteklenmeyen sağlayıcı.");
        if (connection.Port is < 1 or > 65535)
            throw new ArgumentException("Geçersiz port.");
        Text(connection.Database, 128, "Veritabanı");
        Text(connection.Username, 128, "Kullanıcı adı");
        Text(connection.Password, 1024, "Parola", allowEmpty: !requirePassword);
        if (connection.TrustServerCertificate && connection.Provider != "sqlserver")
            throw new ArgumentException("Sertifika istisnası yalnızca SQL Server için kullanılabilir.");
        if (connection.AllowedTables is null || connection.AllowedTables.Length > WorkspaceLimits.MaxAllowedTables)
            throw new ArgumentException("Tablo izin listesi sınırı aşıldı.");
        foreach (var table in connection.AllowedTables)
        {
            Text(table, 260, "Tablo");
            SqlPolicy.ParseTableIdentity(table);
        }
    }

    public static void Query(LocalQueryRequest request)
    {
        if (request.ConnectionId == Guid.Empty) throw new ArgumentException("Bir bağlantı seçin.");
        Text(request.Sql, WorkspaceLimits.MaxSqlLength, "SQL");
        if (request.MaxRows is < 1 or > WorkspaceLimits.MaxRows ||
            request.TimeoutSeconds is < 1 or > WorkspaceLimits.MaxTimeoutSeconds)
            throw new ArgumentException("Satır veya zaman aşımı sınırı geçersiz.");
    }

    public static void Document(WorkspaceDocument document)
    {
        if (document.Version != 1 || document.Connections is null ||
            document.Connections.Count > WorkspaceLimits.MaxConnections ||
            document.Connections.Any(connection => connection is null || connection.Id == Guid.Empty) ||
            document.Connections.Select(connection => connection.Id).Distinct().Count() != document.Connections.Count)
            throw new ArgumentException("Yerel çalışma alanı belgesi geçersiz.");
        foreach (var connection in document.Connections) Connection(connection);
        Selection(document, new WorkspaceSelection(document.SelectedConnectionId, document.Draft));
    }

    public static void Selection(WorkspaceDocument document, WorkspaceSelection selection)
    {
        Text(selection.Draft, WorkspaceLimits.MaxSqlLength, "SQL taslağı", allowEmpty: true);
        if (selection.ConnectionId is { } id && !document.Connections.Any(connection => connection.Id == id))
            throw new ArgumentException("Yerel bağlantı bulunamadı.");
    }

    private static void Text(string? value, int maximum, string label, bool allowEmpty = false)
    {
        if (value is null || value.Length > maximum || value.Contains('\0') ||
            (!allowEmpty && string.IsNullOrWhiteSpace(value)))
            throw new ArgumentException($"{label} boş veya sınır dışında.");
    }

    [GeneratedRegex(@"^[a-zA-Z0-9.\-:\[\]]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();
}