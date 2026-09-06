using System.Text.Json.Serialization;

namespace Grafirio.Bridge.Desktop.Shell;

internal sealed record ConnectionContextResponse(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("bridgeId")] Guid? BridgeId = null,
    [property: JsonPropertyName("error")] string? Error = null)
{
    [JsonPropertyName("type")]
    public string Type => "connectionContext";

    [JsonIgnore]
    public UserSession? Session { get; init; }
}