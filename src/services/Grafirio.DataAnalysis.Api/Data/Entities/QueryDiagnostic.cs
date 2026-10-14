namespace Grafirio.DataAnalysis.Api.Data.Entities;

// Separate from execution authority: rejected requests must never authorize SQL callbacks.
public sealed class QueryDiagnostic
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string CompanyId { get; set; } = "";
    public Guid? QueryId { get; set; }
    public Guid? ConfigId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Question { get; set; } = "";
    public string Stage { get; set; } = "received";
    public string Status { get; set; } = "preparing";
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
    public string Model { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public string BuildVersion { get; set; } = "";
    public string ContextHash { get; set; } = "";
    public string DictionaryHash { get; set; } = "";
    public string ContextJson { get; set; } = "{}";
    public string ParametersJson { get; set; } = "{}";
    public string EventsJson { get; set; } = "[]";
    public string ReviewStatus { get; set; } = "new";
    public string ReviewNote { get; set; } = "";
    public int DurationMs { get; set; }
}

public sealed class DiagnosticTestCase
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Name { get; set; } = "";
    public string Question { get; set; } = "";
    public string ContextJson { get; set; } = "{}";
    public string ExpectedJson { get; set; } = "{}";
    public string RunsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
}

public sealed class DiagnosticSettings
{
    public int Id { get; set; } = 1;
    public int RetentionDays { get; set; } = 90;
    public int RepeatedErrorThreshold { get; set; } = 3;
    public int SlowQuerySeconds { get; set; } = 60;
}

public sealed class DiagnosticAdminEvent
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid? TargetId { get; set; }
    public string DetailJson { get; set; } = "{}";
}
