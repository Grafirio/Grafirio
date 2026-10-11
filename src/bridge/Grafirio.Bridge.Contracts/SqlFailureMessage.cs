namespace Grafirio.Bridge.Contracts;

public static class SqlFailureMessage
{
    public static string ForNumber(int number) => number switch
    {
        18456 => "SQL_AUTH_FAILED: SQL kullanıcı adı veya parolası doğrulanamadı.",
        4060 or 916 or 229 => "DATABASE_FORBIDDEN: Veritabanına veya istenen nesneye erişim yetkisi yok.",
        -2 => "SQL_TIMEOUT: SQL bağlantısı veya sorgusu zaman aşımına uğradı.",
        26 or 53 or 10060 or 10061 or 11001 => "TCP_UNAVAILABLE: SQL sunucusuna ulaşılamadı. Adres, TCP portu ve ağ erişimini kontrol edin.",
        -2146893019 or -2146762487 or -2146762481 => "TLS_FAILED: SQL sunucusunun sertifikası doğrulanamadı.",
        _ => $"SQL_FAILED: SQL işlemi tamamlanamadı (hata no: {number}). Destek günlüğünü kontrol edin."
    };
}
