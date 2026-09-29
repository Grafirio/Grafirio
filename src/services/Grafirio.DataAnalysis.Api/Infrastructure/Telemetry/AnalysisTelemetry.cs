using System.Diagnostics;
using System.Diagnostics.Metrics;
using Grafirio.Telemetry;

namespace Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;

/// <summary>
/// Veri analizi servisinin olcumleri. Her olcum bir soruya cevap veriyor:
///
///   * <b>Zaman</b>     — LLM, SQL, soru cevirisi, analiz ve sorgunun uctan uca
///                        suresi (histogram; p50/p95/p99 buradan).
///   * <b>Tutarlilik</b> — sonuc sayaclari (<c>outcome</c> etiketi): basarili,
///                        netlestirme, hata. Oranlari sistemin ne kadar
///                        guvenilir oldugunu soyluyor.
///   * <b>Harcama</b>    — LLM token'lari turune gore; fiyat tanimliysa USD.
///   * <b>Kullanim</b>   — kullanici geri bildirimi (begendi/begenmedi).
///
/// Etiketlerde kullanici, sirket, soru metni ya da SQL YOK: kardinaliteyi
/// patlatirlar ve kisisel veri tasirlar. Kayit bazli analiz icin veritabanindaki
/// <c>QueryHistories</c> satirlari var; olcumler toplu egilim icin.
/// </summary>
public static class AnalysisTelemetry
{
    private static readonly Meter Meter = GrafirioTelemetry.Meter;

    public static ActivitySource Source => GrafirioTelemetry.ActivitySource;

    // --- LLM -------------------------------------------------------------

    public static readonly Histogram<double> LlmDuration = Meter.CreateHistogram<double>(
        "grafirio.llm.request.duration", "s",
        "Tek bir GenerateAsync cagrisinin suresi; butce ve 429 tekrarlari dahil.");

    public static readonly Counter<long> LlmTokens = Meter.CreateCounter<long>(
        "grafirio.llm.tokens", "{token}",
        "Harcanan token; llm.token.type = input | cached_input | output | reasoning.");

    public static readonly Counter<double> LlmCost = Meter.CreateCounter<double>(
        "grafirio.llm.cost", "USD",
        "Tahmini LLM maliyeti. Yalnizca Llm:Pricing tanimliysa uretilir.");

    public static readonly Counter<long> LlmRateLimited = Meter.CreateCounter<long>(
        "grafirio.llm.rate_limited", "{event}",
        "Azure 429 (kota) cevaplari. Kalici artis kapasite sorunudur.");

    public static readonly Counter<long> LlmDiscarded = Meter.CreateCounter<long>(
        "grafirio.llm.discarded_responses", "{response}",
        "Butceye sigmayip atilan cevaplar: parasi odenmis ama kullanilmamis token.");

    // --- Veri kaynagi ----------------------------------------------------

    public static readonly Histogram<double> DataSourceDuration = Meter.CreateHistogram<double>(
        "grafirio.datasource.query.duration", "s",
        "Musteri veritabanina giden sorgunun suresi; datasource.route = direct | bridge.");

    public static readonly Histogram<long> DataSourceRows = Meter.CreateHistogram<long>(
        "grafirio.datasource.query.rows", "{row}",
        "Sorgunun dondurdugu satir sayisi.");

    public static readonly Histogram<double> BridgeDatabaseDuration = Meter.CreateHistogram<double>(
        "grafirio.bridge.database.duration", "s",
        "Bridge'in kendi olctugu veritabani suresi. Toplam sureden farki ag + kuyruk.");

    // --- Soru / sorgu ----------------------------------------------------

    public static readonly Histogram<double> QuestionDuration = Meter.CreateHistogram<double>(
        "grafirio.question.duration", "s",
        "Soru gonderiminden PyCaret'e teslime kadar (ceviri dahil).");

    public static readonly Counter<long> QuestionOutcome = Meter.CreateCounter<long>(
        "grafirio.question.outcome", "{question}",
        "question.outcome = submitted | clarification | translation_failed | rejected.");

    public static readonly Histogram<double> QueryEndToEnd = Meter.CreateHistogram<double>(
        "grafirio.query.duration", "s",
        "Sorgunun olusturulmasindan son durumuna kadar gecen sure.");

    public static readonly Counter<long> QueryCompleted = Meter.CreateCounter<long>(
        "grafirio.query.completed", "{query}",
        "Son duruma ulasan sorgular; query.status = completed | failed | cancelled | clarification.");

    // --- Analiz Et -------------------------------------------------------

    public static readonly Histogram<double> AnalysisDuration = Meter.CreateHistogram<double>(
        "grafirio.analysis.duration", "s",
        "'Analiz Et' isinin suresi (profil + sozluk).");

    public static readonly Histogram<long> AnalysisChunks = Meter.CreateHistogram<long>(
        "grafirio.analysis.chunks", "{chunk}",
        "Sozlugun kac parcada uretildigi — semanin buyuklugunun vekili.");

    // --- Kullanim --------------------------------------------------------

    public static readonly Counter<long> ChartOverride = Meter.CreateCounter<long>(
        "grafirio.chart.override", "{change}",
        "Kullanicinin sonuctan sonra modelin sectigi grafik turunu degistirmesi.");

    public static readonly Counter<long> Feedback = Meter.CreateCounter<long>(
        "grafirio.feedback", "{vote}",
        "Kullanicinin sonuca verdigi oy; feedback.rating = up | down.");

    public static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);
}
