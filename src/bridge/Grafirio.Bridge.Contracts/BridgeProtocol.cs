namespace Grafirio.Bridge.Contracts;

/// <summary>
/// Bulut ile musteri agindaki bridge arasindaki protokol.
///
/// Yon onemli: baglantiyi <b>bridge kurar</b>, disari dogru (443/WSS). Musterinin
/// firewall'inda hicbir giris portu acilmaz. Sunucu, acik duran bu kanal
/// uzerinden sorgu gonderir.
/// </summary>
public static class BridgeProtocol
{
    /// <summary>
    /// Protokol surumu. Bridge'ler musteri sunucularinda yasiyor ve
    /// kendiliginden guncellenmiyor; uyumsuz bir surum sessizce yanlis
    /// calismaktansa acik bir mesajla reddedilmeli.
    /// </summary>
    public const string Version = "1";

    /// <summary>SignalR hub yolu. Gateway'de WebSocket'e izin verilmeli.</summary>
    public const string HubPath = "/hubs/bridge";

    /// <summary>Sunucunun bridge'e cagirdigi metotlar.</summary>
    public static class ServerToBridge
    {
        public const string ExecuteQuery = nameof(ExecuteQuery);
        public const string CancelQuery = nameof(CancelQuery);
        public const string ConfigureConnection = nameof(ConfigureConnection);
        public const string RemoveConnection = nameof(RemoveConnection);
    }

    /// <summary>Bridge'in sunucuya cagirdigi metotlar.</summary>
    public static class BridgeToServer
    {
        public const string PushChunk = nameof(PushChunk);
        public const string CompleteQuery = nameof(CompleteQuery);
        public const string FailQuery = nameof(FailQuery);
        public const string Heartbeat = nameof(Heartbeat);
    }
}

/// <summary>
/// Sunucu → bridge: bu baglantiyi tanı.
///
/// Bridge yalnizca kendi yerel deposunda tanimli baglantilara sorgu
/// calistiriyor. Panelden bir baglanti bridge'e baglandiginda ve bridge her
/// baglandiginda bu mesaj gonderiliyor — ikincisi sart, cunku baglama aninda
/// bridge cevrimdisi olabilir.
///
/// <b>Sifre bu mesajla iniyor ve bridge'in diskinde kaliyor.</b> Bulut kalici
/// bir kopya tutmuyor; musteriye verilen "sifreniz sizde durur" sozunun
/// karsiligi bu. Kanal TLS; sifre bulutun belleginden bir kez geciyor.
///
/// <paramref name="AllowedTables"/> bos degilse bridge listede olmayan bir
/// tabloya giden sorguyu reddeder — bulut ne gonderirse gondersin.
/// </summary>
public sealed record ConfigureConnectionRequest(
    Guid ConnectionId,
    string Name,
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate,
    IReadOnlyList<string> AllowedTables);

/// <summary>Sunucu → bridge: bu baglantiyi unut.</summary>
public sealed record RemoveConnectionRequest(Guid ConnectionId);

/// <summary>
/// Bridge'in kimlik sunucusundaki hesabi.
///
/// Kayit aninda bir kez donuyor ve bridge'in diskinde DPAPI ile sifreli
/// duruyor. Bridge bununla <c>client_credentials</c> akisindan kisa omurlu
/// erisim token'lari aliyor.
///
/// Sozlesmede duruyor cunku iki taraf da ayni sekli konusuyor: sunucu
/// uretiyor, bridge tuketiyor. Iki yerde ayri ayri tanimlanmasi, alan adlari
/// ayrisinca sessizce bozulan bir JSON esleme demekti.
/// </summary>
public sealed record BridgeCredentials(
    string ClientId,
    string ClientSecret,
    string TokenEndpoint);

/// <summary>Sunucu → bridge: su sorguyu calistir.</summary>
public sealed record ExecuteQueryRequest(
    string RequestId,
    Guid ConnectionId,
    string Sql,
    IReadOnlyList<QueryParameter> Parameters,
    int MaxRows,
    int TimeoutSeconds);

/// <summary>
/// Bir SQL degerinin turu.
///
/// Hem parametrelerde hem donen kolonlarda kullaniliyor. Tur acikca tasiniyor
/// cunku JSON uzerinden gecen <c>object</c> karsi tarafta <c>JsonElement</c>
/// olarak cikiyor ve "3" ile 3 arasindaki fark kayboluyor.
///
/// Sema profili kolonun min/max degerini kolonun KENDI tipiyle karsilastiriyor;
/// her sey metne dususe sayisal kolonda 100 &lt; 99 cikar, tarih kolonu metin
/// sirasina gore siralanir. Sozlugu ureten model bu araligi kolonun ne oldugunu
/// anlamak icin kullandigi icin bu, yanlis taninan alan demek.
/// </summary>
public enum SqlValueKind
{
    Null = 0,
    Text = 1,
    Integer = 2,
    Decimal = 3,
    Boolean = 4,
    DateTime = 5,
    Guid = 6,
    Binary = 7,
}

/// <summary>
/// Sorgu parametresi. Deger hicbir kosulda SQL metnine girmez.
///
/// <paramref name="Values"/> doluysa parametre bir LISTE'dir: ilişki keşfi
/// <c>WHERE ... IN @Names</c> yazip Dapper'in bunu tek tek parametrelere
/// acmasina guveniyor. Liste desteklenmezse o sorgu bridge yolunda sessizce
/// bos doner — yani hicbir iliski bulunamaz ve sebebi hicbir yerde gorunmez.
/// </summary>
public sealed record QueryParameter(
    string Name,
    SqlValueKind Kind,
    string? Value,
    IReadOnlyList<string?>? Values = null);

/// <summary>Donen kolonun adi ve turu. Tur satir basina degil kolon basina.</summary>
public sealed record QueryColumn(string Name, SqlValueKind Kind);

/// <summary>
/// Bridge → sunucu: satir parcasi.
///
/// Satirlar parca parca geliyor cunku model egiten analizler on binlerce satir
/// okuyor; tek mesajda tasimak iki tarafta da ayni veriyi bellekte tutmak olur.
///
/// Kolonlar her parcada tekrar ediyor: liste kucuk, ama sirali varsayimina
/// dayanan bir protokol yeniden baglanmada sessizce bozulur.
/// </summary>
public sealed record QueryChunk(
    string RequestId,
    int Sequence,
    IReadOnlyList<QueryColumn> Columns,
    IReadOnlyList<string?[]> Rows);

/// <summary>
/// Bridge → sunucu: sorgu bitti.
///
/// <paramref name="Columns"/> burada da var cunku hic satir donmediginde kolon
/// adlari baska hicbir yerden ogrenilemiyor.
/// </summary>
public sealed record QueryCompleted(
    string RequestId,
    int RowCount,
    bool Truncated,
    IReadOnlyList<QueryColumn> Columns);

/// <summary>
/// Bridge → sunucu: sorgu calistirilamadi.
///
/// <c>Code</c> makinenin okudugu, <c>Message</c> kullaniciya gosterilen kisim.
/// </summary>
public sealed record QueryFailure(string RequestId, string Code, string Message)
{
    /// <summary>Sorgu okuma disi: bridge kendi kurali geregi reddetti.</summary>
    public const string NotReadOnly = "not_read_only";

    /// <summary>Kayitli baglanti bridge'in yerel yapilandirmasinda yok.</summary>
    public const string UnknownConnection = "unknown_connection";

    /// <summary>Musteri veritabani hata dondurdu.</summary>
    public const string DatabaseError = "database_error";

    /// <summary>Sorgu zaman asimina ugradi.</summary>
    public const string Timeout = "timeout";

    /// <summary>Secili olmayan bir tabloya gidildi.</summary>
    public const string TableNotAllowed = "table_not_allowed";
}

/// <summary>
/// Bridge → sunucu: hayattayim.
///
/// Panelde "Çevrimiçi/Çevrimdışı" rozetini besleyen sey bu. Kullanicinin
/// analizin neden baslamadigini anlayabilmesi buna bagli.
/// </summary>
public sealed record BridgeHeartbeat(
    string BridgeVersion,
    int ActiveQueryCount);
