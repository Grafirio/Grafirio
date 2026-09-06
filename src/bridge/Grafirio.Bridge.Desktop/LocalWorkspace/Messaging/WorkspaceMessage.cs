using System.Text.Json;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Messaging;

public sealed record WorkspaceMessage(string Id, string Method, JsonElement Payload);