using System.Text.Json.Serialization;

namespace Grafirio.Bridge.Desktop.Authentication;

public sealed class DesktopDiscoveryDocument
{
    [JsonPropertyName("issuer")] public string? Issuer { get; set; }
    [JsonPropertyName("authorization_endpoint")] public string? AuthorizationEndpoint { get; set; }
    [JsonPropertyName("token_endpoint")] public string? TokenEndpoint { get; set; }
}