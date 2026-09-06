using System.Reflection;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Grafirio.DummyData.Generator;

/// <summary>
/// Veritabanini ve tablolarini kendisi olusturur; disaridan SSMS ile sema
/// calistirmaya gerek kalmaz. Sema dosyasi assembly icine gomulu geldigi icin
/// generator nereden calistirilirsa calistirilsin ayni semayi kurar.
/// </summary>
public sealed class SchemaInstaller
{
    private const string SchemaResourceName = "Grafirio.DummyData.Generator.DatabaseSchema.sql";

    private readonly GeneratorOptions _options;

    public SchemaInstaller(GeneratorOptions options) => _options = options;

    public async Task InstallAsync()
    {
        await using var master = new SqlConnection(_options.MasterConnectionString);
        await master.OpenAsync();

        var name = Quote(_options.Database);

        if (_options.Reset && await DatabaseExistsAsync(master))
        {
            Console.WriteLine($"   Mevcut veritabani siliniyor: {_options.Database}");
            // Acik oturumlar silmeyi engellemesin.
            await master.ExecuteAsync($"ALTER DATABASE {name} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;");
            await master.ExecuteAsync($"DROP DATABASE {name};");
        }

        if (await DatabaseExistsAsync(master))
        {
            Console.WriteLine($"   Veritabani zaten var, uzerine yazilacak: {_options.Database}");
        }
        else
        {
            Console.WriteLine($"   Veritabani olusturuluyor: {_options.Database}");
            await master.ExecuteAsync($"CREATE DATABASE {name};");
        }

        await using var database = new SqlConnection(_options.DatabaseConnectionString);
        await database.OpenAsync();

        var batches = SplitBatches(ReadSchemaScript());
        Console.WriteLine($"   Sema uygulaniyor ({batches.Count} adim)...");
        foreach (var batch in batches)
        {
            await database.ExecuteAsync(batch);
        }

        if (_options.CreateReadonlyLogin)
        {
            await CreateReadonlyLoginAsync(master, database);
        }
    }

    private async Task<bool> DatabaseExistsAsync(SqlConnection master) =>
        await master.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.databases WHERE name = @name",
            new { name = _options.Database }) > 0;

    /// <summary>
    /// Grafirio'nun ornek veritabanina 'sa' ile degil, sadece okuyabilen bir
    /// kullaniciyla baglanabilmesi icin giris + kullanici acar.
    /// </summary>
    private async Task CreateReadonlyLoginAsync(SqlConnection master, SqlConnection database)
    {
        var login = Quote(_options.ReadonlyUser);
        var literal = _options.ReadonlyUser.Replace("'", "''");
        var password = _options.ReadonlyPassword.Replace("'", "''");

        Console.WriteLine($"   Salt-okunur giris hazirlaniyor: {_options.ReadonlyUser}");

        await master.ExecuteAsync($"""
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = '{literal}')
                CREATE LOGIN {login} WITH PASSWORD = '{password}', CHECK_POLICY = ON;
            """);

        await database.ExecuteAsync($"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '{literal}')
                CREATE USER {login} FOR LOGIN {login};
            ALTER ROLE db_datareader ADD MEMBER {login};
            """);
    }

    private static string ReadSchemaScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException(
                $"Sema kaynagi bulunamadi: {SchemaResourceName}. " +
                "DatabaseSchema.sql csproj icinde EmbeddedResource olarak tanimli mi?");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// GO satirlari T-SQL degil, sqlcmd toplu is ayracidir; ADO.NET bunu
    /// anlamadigi icin betigi burada parcalara ayiriyoruz.
    /// </summary>
    internal static List<string> SplitBatches(string script)
    {
        var batches = new List<string>();
        var current = new StringBuilder();

        foreach (var line in script.Split('\n'))
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                AppendIfNotEmpty(batches, current);
                current.Clear();
                continue;
            }

            current.AppendLine(line.TrimEnd('\r'));
        }

        AppendIfNotEmpty(batches, current);
        return batches;
    }

    private static void AppendIfNotEmpty(List<string> batches, StringBuilder current)
    {
        var text = current.ToString().Trim();
        if (text.Length > 0) batches.Add(text);
    }

    /// <summary>Tanimlayicilari koseli parantezle guvene alir.</summary>
    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}
