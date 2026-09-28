using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grafirio.Measurement.Reporting;

/// <summary>
/// "scenario-run/v1" — olcum sonucunun tasindigi belge.
///
/// Benchmark dashboard'unun ayni adli sozlesmesinin bu taraftaki kopyasi. Kopya
/// bilerek: iki proje birbirine referans vermiyor, aralarindaki tek bag bu JSON.
/// Alan eklemek geriye uyumlu (dashboard bilmedigini yok sayar); alan silmek ya
/// da anlamini degistirmek surum artirmayi gerektirir.
/// </summary>
public sealed record ScenarioRunDocument
{
    public const string SchemaV1 = "scenario-run/v1";

    public string Schema { get; init; } = SchemaV1;
    public required string Kind { get; init; }
    public required string Suite { get; init; }
    public string? Label { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset FinishedAt { get; init; }
    public string? GitCommit { get; init; }
    public string? GitBranch { get; init; }
    public string MachineName { get; init; } = System.Environment.MachineName;
    public string? Target { get; init; }
    public Dictionary<string, string> Environment { get; init; } = [];
    public List<Metric> Metrics { get; init; } = [];
    public List<Case> Cases { get; init; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record Case
{
    public required string Name { get; init; }
    public string Group { get; init; } = "";
    public bool Success { get; init; }
    public string? Message { get; init; }
    public List<Metric> Metrics { get; init; } = [];
    public object? Details { get; init; }
}

/// <param name="Direction">higher | lower | none — dashboard karsilastirmada kullaniyor.</param>
public sealed record Metric(string Name, double Value, string Unit = "", string Direction = "none")
{
    public static Metric Higher(string name, double value, string unit = "") => new(name, value, unit, "higher");
    public static Metric Lower(string name, double value, string unit = "") => new(name, value, unit, "lower");
    public static Metric Info(string name, double value, string unit = "") => new(name, value, unit);
}
