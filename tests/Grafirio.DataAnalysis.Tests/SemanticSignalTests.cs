using System.Reflection;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Admin;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Semantik zekanin canli sinyalleri. Panel bu rakamlarla karar verdirecek;
/// yanlis bir payda (orn. oy verilmemis sorulari olumsuz saymak) sessizce
/// yaniltir.
/// </summary>
public class SemanticSignalTests
{
    private static readonly DateTime Day = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Config = Guid.NewGuid();

    private static SignalQuestion Q(string status, int minute = 0, string user = "u1", string text = "soru",
        short? rating = null, string? chart = null, Guid? parent = null, string? error = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Config, user, text, status, Day.AddMinutes(minute), parent, rating, chart, error);

    private static Dictionary<string, Signal> Signals(SemanticSignalReport report, string capability) =>
        report.Capabilities.Single(c => c.Capability == capability).Signals.ToDictionary(s => s.Name);

    [Fact]
    public void Alti_yetenek_icin_sinyal_uretiliyor()
    {
        var report = SemanticSignals.Compute([Q("completed")], [], [], Day, Day);
        Assert.Equal(["schema", "relationships", "mapping", "unknown", "sql", "visualization"],
            report.Capabilities.Select(c => c.Capability));
    }

    [Fact]
    public void Netlestirme_cozulme_orani_cocuk_sorunun_tamamlanmasina_bakiyor()
    {
        var asked = Guid.NewGuid();
        var unresolved = Guid.NewGuid();
        var report = SemanticSignals.Compute(
        [
            Q("clarification", id: asked),
            Q("completed", 1, parent: asked),
            Q("clarification", 2, id: unresolved),
            Q("failed", 3, parent: unresolved, error: "SQL hata no: 207")
        ], [], [], Day, Day);

        var unknown = Signals(report, "unknown");
        Assert.Equal(0.5, unknown["clarification.rate"].Value);
        Assert.Equal(0.5, unknown["clarification.resolved.rate"].Value);
        Assert.Equal(2, unknown["clarification.resolved.rate"].SampleSize);
    }

    [Fact]
    public void Oy_oranlari_yalnizca_oy_verilenler_uzerinden()
    {
        var report = SemanticSignals.Compute(
        [
            Q("completed", rating: 1), Q("completed", 1, rating: -1), Q("completed", 2), Q("completed", 3, chart: "pie")
        ], [], [], Day, Day);

        Assert.Equal(0.5, Signals(report, "mapping")["feedback.negative.rate"].Value);
        Assert.Equal(0.5, Signals(report, "visualization")["feedback.positive.rate"].Value);
        Assert.Equal(0.25, Signals(report, "visualization")["chart.override.rate"].Value);
    }

    [Fact]
    public void Tekrar_sorma_ayni_kullanici_ayni_metin_pencere_icinde()
    {
        var report = SemanticSignals.Compute(
        [
            Q("completed", 0, "u1", "Kaç sipariş var?"),
            Q("completed", 5, "u1", "kaç   sipariş var"),
            Q("completed", 6, "u2", "Kaç sipariş var?"),
            Q("completed", 60 * 30, "u1", "Kaç sipariş var?")
        ], [], [], Day, Day);

        Assert.Equal(0.25, Signals(report, "mapping")["reask.rate"].Value);
    }

    [Fact]
    public void Sql_hatalari_siniflandiriliyor()
    {
        var report = SemanticSignals.Compute(
        [
            Q("completed"),
            Q("failed", 1, error: "Invalid column name 'X'. (SQL hata no: 207)"),
            Q("failed", 2, error: "Table 'dbo.X' is not allowed."),
            Q("failed", 3, error: "Analysis exceeded its maximum duration; submit a new query.")
        ], [], [], Day, Day);

        var sql = Signals(report, "sql");
        Assert.Equal(0.75, sql["failure.rate"].Value);
        Assert.Equal(0.25, sql["failure.sql.rate"].Value);
        Assert.Equal(0.25, sql["failure.policy.rate"].Value);
        Assert.Equal(0.25, sql["failure.timeout.rate"].Value);
    }

    [Fact]
    public void Iliski_ve_sema_sinyalleri_sozlukten_ve_ogrenilmis_bilgiden()
    {
        var analyses = new[]
        {
            new SignalAnalysis("ready", Day,
                """{"generatedQuestionCount":6,"profileStats":{"relationshipCount":4,"inferredRelationshipCount":0}}"""),
            new SignalAnalysis("awaiting_answers", Day,
                """{"generatedQuestionCount":2,"profileStats":{"relationshipCount":2,"inferredRelationshipCount":0}}"""),
            new SignalAnalysis("failed", Day, "{}")
        };
        var facts = new List<(string, bool, long)>
        {
            (LearnedFact.Relationship, true, 3), (LearnedFact.Relationship, false, 1),
            (LearnedFact.Meaning, true, 5), (LearnedFact.Synonym, true, 1), (LearnedFact.Meaning, false, 9)
        };

        var report = SemanticSignals.Compute([], analyses, facts, Day, Day);

        var relationships = Signals(report, "relationships");
        Assert.Equal(3, relationships["found.per_analysis"].Value);
        // Surekli sifir: panelin "cikarim calismiyor" uyarisini beslemesi gereken rakam.
        Assert.Equal(0, relationships["inferred.per_analysis"].Value);
        Assert.Equal(0.75, relationships["confirmation.acceptance.rate"].Value);

        var schema = Signals(report, "schema");
        Assert.Equal(4, schema["setup_questions.mean"].Value);
        Assert.Equal(2, schema["corrections.per_analysis"].Value); // 6 duzeltme / 3 analiz
        Assert.Equal(2 / 3d, schema["analysis.success.rate"].Value!.Value, 6);
    }

    [Fact]
    public void Veri_yokken_oranlar_null_sifir_degil()
    {
        var report = SemanticSignals.Compute([], [], [], Day, Day);
        Assert.Null(Signals(report, "mapping")["completed.rate"].Value);
        Assert.Null(Signals(report, "relationships")["confirmation.acceptance.rate"].Value);
    }

    // --- Uclar ---------------------------------------------------------------

    [Fact]
    public async Task Platform_yoneticisi_olmayan_sinyalleri_goremez()
    {
        await using var harness = new OwnedAnalysisEndpointHarness();
        harness.Identity.Setup(i => i.HasBusinessRole(SemanticSignalEndpoints.PlatformAdminRole)).Returns(false);

        var result = await SemanticSignalEndpoints.GetSignals(harness.Db, harness.Identity.Object,
            new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task Platform_yoneticisi_mongo_olmadan_da_rapor_alir()
    {
        await using var harness = new OwnedAnalysisEndpointHarness();
        harness.Identity.Setup(i => i.HasBusinessRole(SemanticSignalEndpoints.PlatformAdminRole)).Returns(true);
        harness.Db.QueryHistories.Add(new QueryHistory
        {
            Id = Guid.NewGuid(), ConfigId = harness.Config.Id, Question = "q", Status = "completed",
            CreatedAt = DateTime.UtcNow
        });
        await harness.Db.SaveChangesAsync();

        // LearnedFactStore kayitli degil: GetRequiredService InvalidOperationException atar.
        var result = await SemanticSignalEndpoints.GetSignals(harness.Db, harness.Identity.Object,
            new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, CancellationToken.None, days: 7);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"factsAvailable\":false", json);
        Assert.Contains("\"completed.rate\"", json);
    }

    [Fact]
    public async Task Grafik_degisikligi_yalnizca_model_secimden_farkliysa_kaydediliyor()
    {
        await using var harness = new OwnedAnalysisEndpointHarness();
        var query = new QueryHistory
        {
            Id = Guid.NewGuid(), ConfigId = harness.Config.Id, Question = "q", Status = "completed",
            CreatedAt = DateTime.UtcNow, PyCaretParamsJson = """{"chart_type":"bar"}"""
        };
        harness.Db.QueryHistories.Add(query);
        await harness.Db.SaveChangesAsync();

        Assert.IsType<NoContent>(await ChartType(harness, query.Id, "pie"));
        Assert.Equal("pie", (await harness.Db.QueryHistories.AsNoTracking().SingleAsync()).ChartTypeOverride);

        // Modelin turune donmek kaydi temizliyor.
        Assert.IsType<NoContent>(await ChartType(harness, query.Id, "BAR"));
        Assert.Null((await harness.Db.QueryHistories.AsNoTracking().SingleAsync()).ChartTypeOverride);

        var bad = await ChartType(harness, query.Id, "3d-pasta");
        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)bad).StatusCode);
    }

    private static Task<IResult> ChartType(OwnedAnalysisEndpointHarness harness, Guid queryId, string chartType)
    {
        var method = typeof(AgentQueryEndpoints).GetMethod("SubmitChartType", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (Task<IResult>)method.Invoke(null,
            [queryId, new AgentQueryEndpoints.ChartTypeRequest(chartType), harness.Db, harness.Identity.Object,
                CancellationToken.None])!;
    }
}
