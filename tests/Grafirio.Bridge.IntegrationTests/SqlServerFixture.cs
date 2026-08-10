using Dapper;
using Microsoft.Data.SqlClient;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// Testlerin karşısına gerçek bir SQL Server koyar.
///
/// Şema bilerek karışık tipli: tam sayı, ondalık, tarih, metin ve NULL bir
/// arada. Değerlerin telden geçerken bozulup bozulmadığı ancak böyle görülür —
/// hepsi metin olan bir tablo, testin ölçmek istediği hatayı gizlerdi.
///
/// Konteyner ayakta değilse testler atlanıyor (<see cref="Available"/>).
/// Çalıştırılamamış bir testi kırmızı göstermek, gerçek bir kırmızıyı
/// görünmez yapar.
/// </summary>
public class SqlServerFixture : IAsyncLifetime
{
    public const string DatabaseName = "GrafirioBridgeTest";
    public const string BareTableName = "ParityProbe";
    public const string TableName = "dbo." + BareTableName;

    public string SaPassword { get; } =
        Environment.GetEnvironmentVariable("SA_PASSWORD") ?? ReadFromDotEnv() ?? "";

    public string ConnectionString => Build(DatabaseName);

    public bool Available { get; private set; }

    public string SkipReason { get; private set; } = "";

    private string Build(string database) => new SqlConnectionStringBuilder
    {
        DataSource = "tcp:127.0.0.1,1433",
        InitialCatalog = database,
        UserID = "sa",
        Password = SaPassword,
        TrustServerCertificate = true,
        Encrypt = true,
        ConnectTimeout = 10,
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(SaPassword))
        {
            SkipReason = "SA_PASSWORD tanımlı değil (.env okunamadı).";
            return;
        }

        try
        {
            await using var master = new SqlConnection(Build("master"));
            await master.OpenAsync();

            await master.ExecuteAsync($"""
                IF DB_ID('{DatabaseName}') IS NULL CREATE DATABASE [{DatabaseName}];
                """);

            await using var db = new SqlConnection(ConnectionString);
            await db.OpenAsync();

            await db.ExecuteAsync($"""
                IF OBJECT_ID('{TableName}') IS NOT NULL DROP TABLE {TableName};

                CREATE TABLE {TableName} (
                    Id        INT            NOT NULL PRIMARY KEY,
                    Ad        NVARCHAR(100)  NOT NULL,
                    Ulke      NVARCHAR(2)    NULL,
                    Tutar     DECIMAL(18, 4) NULL,
                    Adet      BIGINT         NULL,
                    Tarih     DATETIME2      NULL,
                    Aktif     BIT            NULL,
                    Kimlik    UNIQUEIDENTIFIER NULL
                );
                """);

            // 99 ve 100 bilerek yan yana: metin sıralamasında "100" < "99".
            await db.ExecuteAsync($"""
                INSERT INTO {TableName} (Id, Ad, Ulke, Tutar, Adet, Tarih, Aktif, Kimlik) VALUES
                    (1,   N'Ada',    N'TR', 1.5,        10,   '2026-04-03T14:30:15', 1, '11111111-1111-1111-1111-111111111111'),
                    (2,   N'Bora',   N'DE', 1234.5678,  0,    '2020-01-31T00:00:00', 0, NULL),
                    (99,  N'Cem',    N'TR', -7.25,      NULL, NULL,                  1, NULL),
                    (100, N'Deniz',  NULL,  0,          9999, '2026-12-31T23:59:59', NULL, NULL);
                """);

            Available = true;
        }
        catch (Exception ex)
        {
            SkipReason = $"SQL Server'a bağlanılamadı: {ex.Message}";
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Parola .env'de. Depoda olmadığı için testin onu okuması gerekiyor;
    /// koda gömmek, çalışan bir sırrı repoya yazmak olurdu.
    /// </summary>
    private static string? ReadFromDotEnv()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".env");

            if (File.Exists(candidate))
            {
                foreach (var line in File.ReadAllLines(candidate))
                {
                    if (!line.StartsWith("SA_PASSWORD=", StringComparison.Ordinal)) continue;
                    return line["SA_PASSWORD=".Length..].Trim().Trim('"');
                }
            }

            directory = directory.Parent;
        }

        return null;
    }
}
