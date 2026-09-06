using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Grafirio.Bridge.Desktop.Cloud;

public static class DesktopBridgeIdentity
{
    private const int JwtSegmentCount = 3;
    private const int Base64BlockLength = 4;
    private const string IssuerClaim = "iss";
    private const string SubjectClaim = "sub";
    private const string CompanyClaim = "company_id";
    private const string InvalidSessionMessage = "Bulut oturumu geçersiz veya süresi dolmuş. Lütfen yeniden giriş yapın.";
    private const string MissingIdentityMessage = "Bulut bağlantısı için oturumda yayıncı, kullanıcı ve şirket bilgileri gereklidir. Lütfen şirket seçerek yeniden giriş yapın.";

    public static string GetDirectory(UserSession session)
    {
        if (session is null || string.IsNullOrWhiteSpace(session.AccessToken)
            || session.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new DesktopBridgeException(InvalidSessionMessage);

        try
        {
            var segments = session.AccessToken.Split('.');
            if (segments.Length != JwtSegmentCount)
                throw new DesktopBridgeException(InvalidSessionMessage);

            var payload = segments[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(
                (payload.Length + Base64BlockLength - 1) / Base64BlockLength * Base64BlockLength, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            var claims = document.RootElement;
            if (claims.ValueKind != JsonValueKind.Object)
                throw new DesktopBridgeException(InvalidSessionMessage);

            if (claims.TryGetProperty("exp", out var expiration)
                && (expiration.ValueKind != JsonValueKind.Number
                    || !expiration.TryGetInt64(out var seconds)
                    || seconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                throw new DesktopBridgeException(InvalidSessionMessage);

            // Claims partition local storage only; the enrollment server validates the JWT.
            var identity = JsonSerializer.SerializeToUtf8Bytes(new[]
            {
                RequiredClaim(claims, IssuerClaim),
                RequiredClaim(claims, SubjectClaim),
                RequiredClaim(claims, CompanyClaim)
            });
            var hash = Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant();
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Grafirio", "Desktop", "Cloud", hash);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new DesktopBridgeException(InvalidSessionMessage);
        }
    }

    private static string RequiredClaim(JsonElement claims, string name)
    {
        if (!claims.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new DesktopBridgeException(MissingIdentityMessage);

        return value.GetString()!;
    }
}