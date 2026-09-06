using Grafirio.DataAnalysis.Api.Data.Entities;
using Microsoft.Data.SqlClient;

namespace Grafirio.DataAnalysis.Api.Data.Access;

/// <summary>
/// Bir musteri veritabanina ulasmak icin gereken her sey, tek yerde.
///
/// Onceden ayni baglanti dizesi uc ayri yerde kuruluyordu ve ucu de farkliydi:
/// <c>ConnectionTestEndpoints</c> 10 saniyelik zaman asimi ve
/// <c>SqlConnectionStringBuilder</c> kullaniyor, <c>ConnectionAnalysisConsumer</c>
/// ile <c>AgentAnalyzeEndpoints</c> ise dizeyi elle birlestirip 30 saniye
/// veriyordu. Aradaki fark kimsenin bilerek sectigi bir sey degildi.
///
/// Kayitli baglanti (<see cref="SavedConnection"/>) burada tasiyicidan
/// bagimsiz tek bicime indiriliyor; hattin geri kalani dogrudan yol ile
/// bridge yolunu birbirinden ayirt etmek zorunda kalmiyor.
/// </summary>
public sealed record DataSourceTarget(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate)
{
    public ConnectionRoute Route { get; init; } = new();
    public string CompanyId { get; init; } = string.Empty;

    /// <summary>Explicit authorization scope; an empty collection permits metadata only.</summary>
    public IReadOnlyList<string> AllowedTables { get; init; } = [];

    /// <summary>Analiz ve profil isleri icin baglanti zaman asimi (saniye).</summary>
    public const int DefaultConnectTimeoutSeconds = 30;

    /// <summary>
    /// "Test Et" icin daha kisa: kullanici ekranin basinda bekliyor, yanlis
    /// yazilmis bir host icin yarim dakika beklemesinin anlami yok.
    /// </summary>
    public const int ProbeConnectTimeoutSeconds = 10;

    /// <summary>
    /// Kayitli baglanti. Sifre burada cozuluyor ki cagiran taraflarin
    /// <see cref="EncryptionHelper"/> ile isi olmasin.
    /// </summary>
    public static DataSourceTarget From(SavedConnection connection) =>
        new(connection.Host, connection.Port, connection.Database, connection.Username,
            EncryptionHelper.Decrypt(connection.EncryptedPassword),
            connection.TrustServerCertificate);

    /// <summary>
    /// Host alanina "sunucu,1433" veya "sunucu:1433" bicimimde port yapistirmak yaygin;
    /// ayri Port alaniyla birlesince "tcp:sunucu,1433,1433" gibi gecersiz bir adres cikiyordu.
    /// Host'a gomulu portu ayiklayip, ayri bir port verilmemisse onu kullan.
    /// </summary>
    internal static (string Host, int Port) NormalizeHostAndPort(string? host, int port)
    {
        var trimmed = (host ?? string.Empty).Trim();
        var separator = trimmed.LastIndexOfAny([',', ':']);

        if (separator > 0 && int.TryParse(trimmed[(separator + 1)..].Trim(), out var embeddedPort))
        {
            var bareHost = trimmed[..separator].Trim();
            // IPv6 adreslerinde ':' zaten adresin parcasi — yalnizca tek ayrac varsa guvenli.
            if (bareHost.Length > 0 && !bareHost.Contains(':'))
            {
                return (bareHost, port > 0 ? port : embeddedPort);
            }
        }

        return (trimmed, port > 0 ? port : 1433);
    }

    /// <summary>
    /// SQL Server baglanti dizesi. Yalnizca dogrudan baglanan uygulama
    /// (<see cref="DirectDataSourceSession"/>) kullanir; bridge uzerinden giden
    /// yolda baglanti dizesi bulutta hic olusmaz.
    /// </summary>
    internal string ToConnectionString(int connectTimeoutSeconds = DefaultConnectTimeoutSeconds)
    {
        var (host, port) = NormalizeHostAndPort(Host, Port);

        var builder = new SqlConnectionStringBuilder
        {
            // tcp: prefix ile Named Pipes yerine TCP zorla (Docker container'lar icin gerekli)
            DataSource = $"tcp:{host},{port}",
            InitialCatalog = Database,
            UserID = Username,
            Password = Password,
            IntegratedSecurity = false,  // SQL Server Authentication kullan
            TrustServerCertificate = TrustServerCertificate,
            ConnectTimeout = connectTimeoutSeconds,
            Encrypt = true,
            MultipleActiveResultSets = true,
            Pooling = true
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Log ve hata mesajlarinda kullanilacak hali: sifre ve kullanici adi yok.
    /// </summary>
    public override string ToString() => $"{Host},{Port}/{Database}";
}
