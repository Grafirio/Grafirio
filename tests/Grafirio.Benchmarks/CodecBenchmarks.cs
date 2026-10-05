using BenchmarkDotNet.Attributes;
using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data.Access;

namespace Grafirio.Benchmarks;

/// <summary>
/// Bridge teli: her hucre metne kodlanip bulutta geri cozuluyor. 200 bin
/// satirlik bir sorguda bu dongu milyonlarca kez donuyor.
/// </summary>
[MemoryDiagnoser]
public class SqlValueCodecBenchmarks
{
    private object?[] _values = [];
    private (SqlValueKind Kind, string? Text)[] _encoded = [];

    /// <summary>Bir parca (bridge 500 satirlik parcalar gonderiyor) x 8 kolon.</summary>
    [Params(4000)]
    public int Cells { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        object?[] samples =
        [
            42, 1234567890L, 3.14159m, 2.5d, "Istanbul", true,
            new DateTime(2026, 9, 28, 10, 30, 0), Guid.Parse("7f8d3c2a-1b4e-4c5d-9e6f-0a1b2c3d4e5f"), null
        ];

        _values = Enumerable.Range(0, Cells).Select(_ => samples[random.Next(samples.Length)]).ToArray();
        _encoded = _values
            .Select(v => (SqlValueCodec.KindOf(v?.GetType()), SqlValueCodec.Encode(v)))
            .ToArray();
    }

    [Benchmark]
    public int Encode()
    {
        var length = 0;
        foreach (var value in _values) length += SqlValueCodec.Encode(value)?.Length ?? 0;
        return length;
    }

    [Benchmark]
    public int Decode()
    {
        var count = 0;
        foreach (var (kind, text) in _encoded)
            if (SqlValueCodec.Decode(kind, text) is not null) count++;
        return count;
    }
}

/// <summary>Sorgu parametrelerinin tel bicimine donusumu — her bridge sorgusunda bir kez.</summary>
[MemoryDiagnoser]
public class QueryParameterCodecBenchmarks
{
    private readonly object _parameters = new
    {
        Since = new DateTime(2026, 1, 1),
        Status = "Delivered",
        MinAmount = 100.5m,
        Limit = 10,
        Names = new[] { "dbo.Orders", "dbo.Customers", "dbo.Products", "dbo.Categories" }
    };

    [Benchmark]
    public int Encode() => QueryParameterCodec.Encode(_parameters).Count;
}
