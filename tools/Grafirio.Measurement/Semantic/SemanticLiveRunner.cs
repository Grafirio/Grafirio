using System.Diagnostics;
using System.Globalization;
using Grafirio.Measurement.Evaluation;
using Grafirio.Measurement.Reporting;
using Microsoft.Data.SqlClient;

namespace Grafirio.Measurement.Semantic;

public sealed record SemanticLiveOptions
{
    public required SemanticDataset Dataset { get; init; }
    public required Guid ConnectionId { get; init; }
    public required string BaseUrl { get; init; }

    /// <summary>Grafirio'nun bagli oldugu AYNI veritabanina giden baglanti dizesi (altin SQL icin).</summary>
    public required string GoldConnectionString { get; init; }

    public TimeSpan QuestionTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public string? Label { get; init; }
}

/// <summary>
/// SQL Generation ve Visualization Accuracy'yi canli sistem uzerinden olcer:
/// soruyu kullanicinin yolundan sorar (gateway → ceviri → PyCaret → SQL),
/// cevabin grafigini ayni veritabaninda calistirilan altin SQL ile karsilastirir.
///
/// Bu iki yetenek surec icinde olculemiyor: SQL'i PyCaret (Python) uretiyor ve
/// calistiriyor. Kullanicinin gordugu sonuc da tam olarak bu yolun sonu.
/// </summary>
public sealed class SemanticLiveRunner(GrafirioApi api, SemanticLiveOptions options)
{
    public async Task<(ScenarioRunDocument Sql, ScenarioRunDocument Chart)> RunAsync(CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var dataset = options.Dataset;
        var questions = dataset.Questions.Questions.Where(q => q.Kind == "answerable" && q.Gold?.Sql is not null).ToList();

        var sqlCases = new List<Case>();
        var chartCases = new List<Case>();
        var verdicts = new List<(LiveScoring.SqlVerdict, LiveScoring.ChartVerdict, string)>();

        await using var gold = new SqlConnection(options.GoldConnectionString);
        await gold.OpenAsync(ct);

        foreach (var question in questions)
        {
            ct.ThrowIfCancellationRequested();
            var expected = await RunGoldAsync(gold, question.Gold!.Sql!, ct);
            var timer = Stopwatch.StartNew();

            string status;
            ResultResponse? result = null;
            string? error = null;
            try
            {
                var submit = await api.SubmitAsync(options.ConnectionId, question.Question, ct);
                if (submit.NeedsClarification) status = "clarification";
                else if (submit.QueryId is null || submit.HttpStatus >= 400)
                {
                    status = "error";
                    error = $"HTTP {submit.HttpStatus}: {submit.Error}";
                }
                else
                {
                    status = await api.WaitAsync(submit.QueryId.Value, options.QuestionTimeout, options.PollInterval, ct);
                    if (status == "completed") result = await api.GetResultAsync(submit.QueryId.Value, ct);
                }
            }
            catch (HttpRequestException exception)
            {
                status = "error";
                error = exception.Message;
            }

            var sql = status == "completed"
                ? LiveScoring.CompareValues(expected, LiveScoring.ChartValues(result?.Result), question.Gold.Ordered)
                : new LiveScoring.SqlVerdict(false, error ?? $"Sorgu tamamlanmadi (durum: {status}).", false);
            var chart = status == "completed"
                ? LiveScoring.CheckChart(result?.Result, question, expected)
                : new LiveScoring.ChartVerdict(false, false, false, false, "Sonuc yok.");
            verdicts.Add((sql, chart, status));

            var executedSql = result?.Result?["audit"]?["executedSql"]?.ToString();
            Console.WriteLine($"[semantic-live] {question.Id} {status} sql={(sql.Correct ? "OK" : "FAIL")} " +
                              $"grafik={(chart.Problem is null ? "OK" : "FAIL")} {timer.ElapsedMilliseconds} ms");

            sqlCases.Add(new Case
            {
                Name = $"{dataset.Name}: {question.Id}",
                Group = $"{dataset.Name}/{question.Group}",
                Success = sql.Correct,
                Message = sql.Problem,
                Metrics = [Metric.Lower("latency", timer.Elapsed.TotalMilliseconds, "ms")],
                Details = new
                {
                    question.Question, goldSql = question.Gold.Sql, executedSql,
                    expected = expected.Take(50), actual = LiveScoring.ChartValues(result?.Result).Take(50),
                    parameters = result?.Parameters, status
                }
            });
            chartCases.Add(new Case
            {
                Name = $"{dataset.Name}: {question.Id}",
                Group = $"{dataset.Name}/{question.Group}",
                Success = status == "completed" && chart.Problem is null,
                Message = chart.Problem,
                Details = new
                {
                    question.Question, acceptedCharts = question.Gold.Charts, question.Gold.Ordered,
                    chart = result?.Result?["charts"]?[0]
                }
            });
        }

        var (sqlMetrics, chartMetrics) = LiveScoring.Summarize(verdicts);
        return (Document("semantic.sql", started, sqlMetrics, sqlCases),
                Document("semantic.visualization", started, chartMetrics, chartCases));
    }

    /// <summary>
    /// Altin SQL'in sonucu. Kural: deger kolonu "v", etiket kolonu (varsa) "k".
    /// Kolonlar bu adlarla yoksa son kolon deger, ilk metin kolon etiket sayilir.
    /// </summary>
    private static async Task<List<GoldRow>> RunGoldAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync(ct);

        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var valueIndex = names.FindIndex(n => n.Equals("v", StringComparison.OrdinalIgnoreCase));
        if (valueIndex < 0) valueIndex = names.Count - 1;
        var labelIndex = names.FindIndex(n => n.Equals("k", StringComparison.OrdinalIgnoreCase));

        var rows = new List<GoldRow>();
        while (await reader.ReadAsync(ct))
        {
            var value = reader.IsDBNull(valueIndex)
                ? (double?)null
                : Convert.ToDouble(reader.GetValue(valueIndex), CultureInfo.InvariantCulture);
            var label = labelIndex >= 0 && !reader.IsDBNull(labelIndex)
                ? Convert.ToString(reader.GetValue(labelIndex), CultureInfo.InvariantCulture)
                : null;
            rows.Add(new GoldRow(label, value));
        }

        return rows;
    }

    private ScenarioRunDocument Document(string suite, DateTimeOffset started, List<Metric> metrics, List<Case> cases)
    {
        var (commit, branch) = RunOutput.Git();
        return new ScenarioRunDocument
        {
            Kind = "semantic",
            Suite = suite,
            Label = options.Label,
            StartedAt = started,
            FinishedAt = DateTimeOffset.UtcNow,
            GitCommit = commit,
            GitBranch = branch,
            Target = options.BaseUrl,
            Environment = new Dictionary<string, string>
            {
                ["datasets"] = options.Dataset.Name,
                ["connectionId"] = options.ConnectionId.ToString()
            },
            Metrics = metrics.Where(m => double.IsFinite(m.Value)).ToList(),
            Cases = cases
        };
    }
}
