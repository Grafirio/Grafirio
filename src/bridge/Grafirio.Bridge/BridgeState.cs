using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Grafirio.Bridge;

/// <summary>
/// Bridge'in yerel durumu: kendi kimligi ve hangi veritabanina nasil
/// baglanacagi.
///
/// <b>Musteriye verilen sozun tutuldugu yer burasi.</b> Veritabani sifresi
/// musterinin kendi diskinde, DPAPI ile ve makineye bagli olarak duruyor;
/// bulutta kalici bir kopyasi yok. Dosya baska bir makineye kopyalansa bile
/// cozulemez.
///
/// Windows disinda DPAPI yok. O durumda dosya duz yaziliyor ve acilista acik
/// bir uyari veriliyor — sessizce korumasiz calismak, korumali sanmaktan
/// kotudur.
/// </summary>
public class BridgeState(ILogger<BridgeState> logger, string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private StateDocument _document = new();

    public string FilePath { get; } = filePath;

    public Guid? BridgeId => _document.BridgeId;

    public string? Secret => _document.Secret;

    public bool IsEnrolled => _document.BridgeId is not null
                              && !string.IsNullOrEmpty(_document.Secret);

    public IReadOnlyList<BridgeConnection> Connections => _document.Connections;

    public void Load()
    {
        if (!File.Exists(FilePath))
        {
            logger.LogInformation(
                "Yerel durum dosyası yok, kayıt bekleniyor: {Path}", FilePath);
            return;
        }

        var protectedBytes = File.ReadAllBytes(FilePath);
        var json = Encoding.UTF8.GetString(Unprotect(protectedBytes));

        _document = JsonSerializer.Deserialize<StateDocument>(json, JsonOptions) ?? new StateDocument();

        logger.LogInformation(
            "Yerel durum yüklendi. Bridge: {BridgeId}, tanımlı bağlantı: {Count}",
            _document.BridgeId, _document.Connections.Count);
    }

    public void SaveEnrollment(Guid bridgeId, string secret, string companyId)
    {
        _document.BridgeId = bridgeId;
        _document.Secret = secret;
        _document.CompanyId = companyId;
        Save();

        logger.LogInformation("Kayıt tamamlandı. Bridge: {BridgeId}", bridgeId);
    }

    /// <summary>
    /// Baglanti bilgisini yerelde saklar. Buluttan inen sifre burada kaliyor;
    /// sunucu kalici olarak tutmuyor.
    /// </summary>
    public void UpsertConnection(BridgeConnection connection)
    {
        _document.Connections.RemoveAll(c => c.ConnectionId == connection.ConnectionId);
        _document.Connections.Add(connection);
        Save();
    }

    public BridgeConnection? FindConnection(Guid connectionId) =>
        _document.Connections.FirstOrDefault(c => c.ConnectionId == connectionId);

    /// <summary>
    /// Baglantiyi yerel depodan siler. Panelde bir baglanti dogrudan moda
    /// alindiginda cagriliyor: sifrenin bridge'in diskinde gereksiz yere
    /// kalmasi, "artik bu yoldan gitmiyoruz" demenin yarim kalmis hali olurdu.
    /// </summary>
    public bool RemoveConnection(Guid connectionId)
    {
        if (_document.Connections.RemoveAll(c => c.ConnectionId == connectionId) == 0)
            return false;

        Save();
        logger.LogInformation("Bağlantı yerel depodan silindi: {ConnectionId}", connectionId);
        return true;
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_document, JsonOptions);
        var directory = Path.GetDirectoryName(FilePath);

        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Once gecici dosyaya, sonra yerine tasiniyor: yazma sirasinda servis
        // durursa yarim bir durum dosyasi kalmasin.
        var temporary = FilePath + ".tmp";
        File.WriteAllBytes(temporary, Protect(Encoding.UTF8.GetBytes(json)));
        File.Move(temporary, FilePath, overwrite: true);
    }

    private byte[] Protect(byte[] plain)
    {
        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning(
                "DPAPI yalnızca Windows'ta var. {Path} dosyası ŞİFRELENMEDEN yazılıyor; " +
                "dosya izinlerini kendiniz kısıtlayın.", FilePath);
            return plain;
        }

        return ProtectWindows(plain);
    }

    private byte[] Unprotect(byte[] stored) =>
        OperatingSystem.IsWindows() ? UnprotectWindows(stored) : stored;

    // LocalMachine kapsami: servis LocalSystem gibi bir hesap altinda kosuyor
    // ve kuran kullanicidan farkli olabiliyor. CurrentUser kapsami secilseydi
    // servis kendi yazdigi dosyayi acamazdi.
    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(byte[] plain) =>
        ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.LocalMachine);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWindows(byte[] stored) =>
        ProtectedData.Unprotect(stored, optionalEntropy: null, DataProtectionScope.LocalMachine);

    private class StateDocument
    {
        public Guid? BridgeId { get; set; }
        public string? Secret { get; set; }
        public string? CompanyId { get; set; }
        public List<BridgeConnection> Connections { get; set; } = [];
    }
}

/// <summary>
/// Yerelde tanimli bir musteri veritabani.
///
/// <see cref="AllowedTables"/> bos ise tablo kisiti yoktur. Dolduruldugunda,
/// buluttan gelen sorgu listede olmayan bir tabloya dokunursa bridge reddeder.
/// En muhafazakar BT ekiplerinin isteyecegi kemer bu.
/// </summary>
public class BridgeConnection
{
    public Guid ConnectionId { get; set; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 1433;
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool TrustServerCertificate { get; set; } = true;
    public List<string> AllowedTables { get; set; } = [];
}
