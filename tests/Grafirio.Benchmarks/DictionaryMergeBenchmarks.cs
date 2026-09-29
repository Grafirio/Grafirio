using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Grafirio.DataAnalysis.Api.Services;

namespace Grafirio.Benchmarks;

/// <summary>
/// Parca parca uretilen semantik sozlugun birlestirilmesi. Sahada 20 parcalik
/// (2000+ kolon) semalar goruldu; LLM cagrilarinin yaninda ihmal edilebilir
/// kalmali ama parca sayisiyla dogrusal olmayan bir buyume burada gorunur.
/// </summary>
[MemoryDiagnoser]
public class DictionaryMergeBenchmarks
{
    private List<string> _parts = [];

    [Params(1, 5, 20)]
    public int Chunks { get; set; }

    /// <summary>Parca basina tablo x kolon — sahadaki ortalama parca buyuklugune yakin.</summary>
    private const int TablesPerChunk = 5;
    private const int ColumnsPerTable = 20;

    [GlobalSetup]
    public void Setup()
    {
        _parts = Enumerable.Range(0, Chunks).Select(chunk =>
        {
            var tables = new JsonArray();
            var columns = new JsonArray();
            var questions = new JsonArray();

            for (var t = 0; t < TablesPerChunk; t++)
            {
                var table = $"dbo.Table{chunk}_{t}";
                tables.Add(new JsonObject
                {
                    ["name"] = table, ["purpose"] = "Siparis kayitlari",
                    ["synonyms"] = new JsonArray("siparis", "order")
                });

                for (var c = 0; c < ColumnsPerTable; c++)
                    columns.Add(new JsonObject
                    {
                        ["table"] = table, ["column"] = $"Column{c}", ["meaning"] = "Aciklama",
                        ["role"] = c == 0 ? "identifier" : c % 3 == 0 ? "measure" : "dimension",
                        ["confidence"] = "high", ["synonyms"] = new JsonArray("alan", $"kolon {c}")
                    });

                questions.Add(new JsonObject
                {
                    ["table"] = table, ["column"] = "Column1", ["question"] = "Bu alan neyi ifade ediyor?",
                    ["options"] = new JsonArray("A", "B")
                });
            }

            return new JsonObject
            {
                ["sector"] = chunk % 2 == 0 ? "e-ticaret" : "lojistik",
                ["sectorConfidence"] = "high",
                ["tables"] = tables, ["columns"] = columns, ["questions"] = questions
            }.ToJsonString();
        }).ToList();
    }

    [Benchmark]
    public int Combine() => SchemaDictionaryMerge.Combine(_parts).Length;
}
