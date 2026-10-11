using System.Net;

namespace Grafirio.Bridge;

public sealed record BridgeEnrollmentFailure(string Code, string Message, bool Retryable)
{
    public static BridgeEnrollmentFailure FromStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new("SESSION_EXPIRED", "Oturum doğrulanamadı (HTTP 401). Yeniden giriş yapın.", false),
        HttpStatusCode.Forbidden => new("ENROLLMENT_FORBIDDEN", "Bu hesap Bridge kaydetmeye yetkili değil (HTTP 403). Şirket yöneticinizle görüşün.", false),
        HttpStatusCode.BadRequest => new("ENROLLMENT_INVALID", "Bridge kayıt bilgileri veya uygulama sürümü kabul edilmedi (HTTP 400). Hesabınızı ve uygulama sürümünü kontrol edin.", false),
        HttpStatusCode.NotFound => new("ENROLLMENT_ENDPOINT_MISSING", "Sunucunun Bridge kayıt adresi bulunamadı (HTTP 404). Sunucu yapılandırması kontrol edilmeli.", false),
        HttpStatusCode.BadGateway => new("BRIDGE_IDENTITY_UNAVAILABLE", "Bulut Bridge kaydı başarısız (HTTP 502). Sunucu kimlik yapılandırması kontrol edilmeli; SQL adresinizi veya parolanızı değiştirmeyin.", false),
        _ => new("ENROLLMENT_UNAVAILABLE", $"Bulut Bridge kayıt servisine ulaşılamıyor (HTTP {(int)status}). Daha sonra yeniden deneyin.", true)
    };
}
