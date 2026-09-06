using System.Data.Common;
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Data;

public static class LocalConnectionFactory
{
    public static DbConnection Create(LocalConnection connection)
    {
        WorkspaceValidation.Connection(connection);
        return connection.Provider switch
        {
            "sqlserver" => new SqlConnection(new SqlConnectionStringBuilder
            {
                DataSource = $"tcp:{connection.Host},{connection.Port}",
                InitialCatalog = connection.Database, UserID = connection.Username,
                Password = connection.Password, IntegratedSecurity = false,
                Encrypt = true, TrustServerCertificate = connection.TrustServerCertificate,
                ConnectTimeout = WorkspaceLimits.ConnectTimeoutSeconds,
                ApplicationName = "Grafirio Local Workspace", Pooling = false
            }.ConnectionString),
            "postgres" => new NpgsqlConnection(new NpgsqlConnectionStringBuilder
            {
                Host = connection.Host, Port = connection.Port, Database = connection.Database,
                Username = connection.Username, Password = connection.Password,
                SslMode = SslMode.VerifyFull, Timeout = WorkspaceLimits.ConnectTimeoutSeconds,
                ApplicationName = "Grafirio Local Workspace", Pooling = false
            }.ConnectionString),
            "mysql" => new MySqlConnection(new MySqlConnectionStringBuilder
            {
                Server = connection.Host, Port = (uint)connection.Port, Database = connection.Database,
                UserID = connection.Username, Password = connection.Password,
                SslMode = MySqlSslMode.VerifyFull,
                ConnectionTimeout = WorkspaceLimits.ConnectTimeoutSeconds,
                AllowLoadLocalInfile = false, AllowUserVariables = false, Pooling = false
            }.ConnectionString),
            _ => throw new ArgumentException("Desteklenmeyen sağlayıcı.")
        };
    }
}