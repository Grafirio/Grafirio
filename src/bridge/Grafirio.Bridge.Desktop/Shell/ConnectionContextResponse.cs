using System.Text.Json.Serialization;

namespace Grafirio.Bridge.Desktop.Shell;

/// <param name="Reason">
/// Neden basarisiz oldugunun kisa, gosterilebilir karsiligi. <see cref="Error"/> yalnizca
/// "signedOut" / "unavailable" diyor ve panel bunu "Masaüstü bağlantısı hazır değil" diye
/// gosteriyordu — kullanicinin gordugu tek sey buydu, sebep yalnizca yerel gunlukte kaliyordu
/// ve ayni yazi hem oturum hem baglanti hem de bambaska bir arizada cikiyordu.
///
/// Yalnizca bizim yazdigimiz mesajlar ya da istisna TURU geciyor; sunucu govdeleri asla.
/// </param>
internal sealed record ConnectionContextResponse(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("bridgeId")] Guid? BridgeId = null,
    [property: JsonPropertyName("error")] string? Error = null,
    [property: JsonPropertyName("reason")] string? Reason = null)
{
    [JsonPropertyName("type")]
    public string Type => "connectionContext";

    [JsonIgnore]
    public UserSession? Session { get; init; }
}