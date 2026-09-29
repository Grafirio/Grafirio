using System.Text.RegularExpressions;
using Grafirio.Measurement.Semantic;
using Microsoft.Data.SqlClient;

namespace Grafirio.SemanticEval.Data;

/// <summary>
/// Veri setini sifirdan kurar: veritabanini dusurur, yeniden olusturur, sema ve
/// veri betiklerini calistirir. Her kurulum ayni veriyi uretir (betikler
/// belirlenimci), yani altin SQL'in cevabi kurulumdan kurulumu degismez.
/// </summary>
public static partial class DatasetInstaller
{
    /// <summary>Yerel varsayilan: Windows ile gelen LocalDB, Windows kimligiyle.</summary>
    public const string DefaultServer = @"(localdb)\MSSQLLocalDB";

    /// <summary>
    /// SQL kimligi. Bos ise Windows kimligi (LocalDB). Veri seti Grafirio'nun
    /// baglandigi bir SQL Server'a (orn. compose'daki konteyner) kurulacaksa
    /// canli olcum (grafirio-measure semantic-live) icin gerekiyor.
    /// Komut satiri araci; komut basina bir kez ayarlaniyor.
    /// </summary>
    public static (string? User, string? Password) Credentials { get; set; }

    public static string ConnectionString(string server, string database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            TrustServerCertificate = true,
            ConnectTimeout = 30,
            // Iliski kesfi ayni baglantida ic ice okuma yapabiliyor.
            MultipleActiveResultSets = true
        };

        if (string.IsNullOrEmpty(Credentials.User))
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = Credentials.User;
            builder.Password = Credentials.Password ?? "";
        }

        return builder.ConnectionString;
    }

    public static async Task InstallAsync(SemanticDataset dataset, string server, CancellationToken ct)
    {
        var database = dataset.DatabaseName;

        await using (var master = new SqlConnection(ConnectionString(server, "master")))
        {
            await master.OpenAsync(ct);
            // Ad bizim urettigimiz sabit bir desen (GrafirioEval_<klasor>); yine de
            // koseli parantezle kaciriliyor.
            var quoted = "[" + database.Replace("]", "]]") + "]";
            await Execute(master, $"""
                IF DB_ID(N'{database.Replace("'", "''")}') IS NOT NULL
                BEGIN
                    ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE {quoted};
                END
                """, ct);
            await Execute(master, $"CREATE DATABASE {quoted};", ct);
        }

        // Sema ve veri AYNI baglantida: seed betikleri gecici #n tablosunu
        // batch'ler arasinda paylasiyor.
        await using var connection = new SqlConnection(ConnectionString(server, database));
        await connection.OpenAsync(ct);
        foreach (var file in new[] { dataset.SchemaFile, dataset.SeedFile })
            foreach (var batch in Batches(await File.ReadAllTextAsync(file, ct)))
                await Execute(connection, batch, ct);
    }

    /// <summary>
    /// Betigi "GO" satirlarindan boler (SSMS/sqlcmd davranisi). GO bir T-SQL
    /// komutu degil; sunucuya gonderilirse sozdizimi hatasi verir.
    /// </summary>
    public static IEnumerable<string> Batches(string script) =>
        GoSeparator().Split(script)
            .Select(b => b.Trim())
            .Where(b => b.Length > 0 && !string.IsNullOrWhiteSpace(StripComments(b)));

    private static string StripComments(string sql) =>
        string.Join('\n', sql.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    private static async Task Execute(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync(ct);
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
