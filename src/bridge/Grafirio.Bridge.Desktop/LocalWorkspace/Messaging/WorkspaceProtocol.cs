using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Messaging;

public static class WorkspaceProtocol
{
    public const string Origin = "https://local-workspace.grafirio.invalid";
    public const string DocumentUrl = Origin + "/index.html";
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };
    private static readonly HashSet<string> Methods =
    ["load", "saveConnection", "deleteConnection", "saveSelection", "test", "discover", "query", "cancel", "export"];

    public static bool IsDocument(string source) =>
        string.Equals(source, DocumentUrl, StringComparison.Ordinal);

    public static WorkspaceMessage Parse(string source, string json)
    {
        if (!IsDocument(source) || json.Length > WorkspaceLimits.MaxMessageLength)
            throw new ArgumentException("Mesaj kaynağı veya boyutu geçersiz.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        RejectDuplicateProperties(document.RootElement);
        var message = JsonSerializer.Deserialize<WorkspaceMessage>(json, JsonOptions)
            ?? throw new ArgumentException("Geçersiz mesaj.");
        if (!Guid.TryParseExact(message.Id, "D", out _) || !Methods.Contains(message.Method) ||
            message.Payload.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Geçersiz mesaj.");
        if ((message.Method is "load" or "cancel" or "export") && message.Payload.EnumerateObject().Any())
            throw new ArgumentException("Beklenmeyen mesaj alanı.");
        return message;
    }

    public static T Payload<T>(WorkspaceMessage message) =>
        message.Payload.Deserialize<T>(JsonOptions) ?? throw new ArgumentException("Geçersiz mesaj verisi.");

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("Yinelenen mesaj alanı.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }
}