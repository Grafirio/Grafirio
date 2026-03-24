using System.Text.Json;
using Mscc.GenerativeAI;

namespace Grafirio.DataAnalysis.Api.Services;

/// <summary>
/// Gemini API ile LLM iletişimi — schema analizi ve sorgu çevirisi
/// </summary>
public class GeminiService
{
    private readonly GenerativeModel _model;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(IConfiguration configuration, ILogger<GeminiService> logger)
    {
        _logger = logger;
        var apiKey = configuration["Gemini:ApiKey"]
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new InvalidOperationException("Gemini API key not configured. Set Gemini:ApiKey in appsettings or GEMINI_API_KEY env var.");

        var googleAi = new GoogleAI(apiKey);
        _model = googleAi.GenerativeModel(Model.Gemini20Flash);
    }

    /// <summary>
    /// Veritabanı schema'sını analiz edip PyCaret config JSON'ı oluşturur
    /// </summary>
    public async Task<GeminiAnalysisResult> AnalyzeSchemaForPyCaret(SchemaInfo schemaInfo)
    {
        var prompt = BuildSchemaAnalysisPrompt(schemaInfo);

        _logger.LogInformation("Gemini'ye schema analizi gönderiliyor. Tablo sayısı: {Count}", schemaInfo.Tables.Count);

        try
        {
            var response = await _model.GenerateContent(prompt);
            var text = response.Text ?? "";

            _logger.LogInformation("Gemini'den cevap alındı. Uzunluk: {Len}", text.Length);

            // JSON bloğunu çıkar
            var json = ExtractJson(text);

            return new GeminiAnalysisResult
            {
                Success = true,
                ConfigJson = json,
                SchemaSummary = ExtractSummary(text),
                RawResponse = text
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini schema analizi başarısız");
            return new GeminiAnalysisResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Kullanıcının doğal dil sorusunu PyCaret analiz parametrelerine çevirir
    /// </summary>
    public async Task<GeminiQueryResult> TranslateQueryForPyCaret(string question, string configJson, string schemaSummary)
    {
        var prompt = BuildQueryTranslationPrompt(question, configJson, schemaSummary);

        _logger.LogInformation("Gemini'ye sorgu çevirisi gönderiliyor: {Question}", question);

        try
        {
            var response = await _model.GenerateContent(prompt);
            var text = response.Text ?? "";
            var json = ExtractJson(text);

            return new GeminiQueryResult
            {
                Success = true,
                PyCaretParamsJson = json,
                Explanation = ExtractExplanation(text),
                RawResponse = text
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini sorgu çevirisi başarısız");
            return new GeminiQueryResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Basit sohbet sorularını yanıtlar (veri analizi gerektirmeyenler)
    /// </summary>
    public async Task<GeminiChatResult> ChatAsync(string message)
    {
        // TEMPORARY: Mock response for testing without valid Gemini API key
        var mockResponses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "selam", "Merhaba! Size nasıl yardımcı olabilirim?" },
            { "merhaba", "Merhaba! Veri analiziniz için buradayım." },
            { "nasılsın", "Ben bir AI asistanıyım ve her zaman iyiyim! Size nasıl yardımcı olabilirim?" },
            { "hello", "Hello! How can I help you with your data?" },
            { "hi", "Hi there! I'm here to help you analyze your data." }
        };

        if (mockResponses.TryGetValue(message.Trim(), out var mockResponse))
        {
            _logger.LogInformation("💬 Returning mock chat response for: {Message}", message);
            return new GeminiChatResult
            {
                Success = true,
                Response = mockResponse
            };
        }

        // If no mock match, use default friendly response
        return new GeminiChatResult
        {
            Success = true,
            Response = $"Anladım, '{message}' diyorsunuz. Veri analizi ile ilgili sorular sorabilir veya raporlar oluşturabilirsiniz!"
        };

        /* ORIGINAL CODE - Uncomment when you have valid Gemini API key
        var prompt = $@"Sen Grafirio veri analizi asistanısın. Kullanıcı seninle sohbet ediyor.

Kullanıcı: {message}

Asistan: ";

        _logger.LogInformation("Gemini'ye chat mesajı gönderiliyor: {Message}", message);

        try
        {
            var response = await _model.GenerateContent(prompt);
            var text = response.Text ?? "Merhaba! Size nasıl yardımcı olabilirim?";

            _logger.LogInformation("Gemini'den chat cevabı alındı. Uzunluk: {Len}", text.Length);

            return new GeminiChatResult
            {
                Success = true,
                Response = text.Trim()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini chat başarısız");
            return new GeminiChatResult
            {
                Success = false,
                Error = ex.Message,
                Response = "Üzgünüm, şu anda yanıt veremiyorum. Lütfen tekrar deneyin."
            };
        }
        */
    }

    /// <summary>
    /// Sorunun veri analizi gerektirip gerektirmediğini kontrol eder
    /// </summary>
    public static bool IsDataAnalysisQuery(string question)
    {
        var lowerQuestion = question.ToLowerInvariant();
        
        // Veri analizi anahtar kelimeleri
        var dataKeywords = new[]
        {
            "en çok", "en az", "en yüksek", "en düşük",
            "toplam", "ortalama", "ortama", "average", "sum", "total",
            "kaç", "how many", "count", "say",
            "liste", "listele", "list", "göster", "show",
            "rapor", "report", "analiz", "analysis",
            "grafik", "chart", "graph",
            "istatistik", "statistics", "stats",
            "tablo", "table", "veriler", "data",
            "ürün", "product", "sipariş", "order",
            "kullanıcı", "user", "müşteri", "customer",
            "satış", "sale", "gelir", "revenue",
            "karşılaştır", "compare", "fark", "difference",
            "trend", "büyüme", "growth",
            "dağılım", "distribution", "yüzde", "percent"
        };

        return dataKeywords.Any(keyword => lowerQuestion.Contains(keyword));
    }

    private static string BuildSchemaAnalysisPrompt(SchemaInfo schemaInfo)
    {
        var tablesDesc = string.Join("\n\n", schemaInfo.Tables.Select(t =>
        {
            var cols = string.Join("\n", t.Columns.Select(c =>
                $"    - {c.ColumnName} ({c.DataType}, nullable={c.IsNullable}, maxLen={c.MaxLength})"));
            return $"  Tablo: {t.Schema}.{t.TableName} (Satır: {t.RowCount})\n  Kolonlar:\n{cols}";
        }));

        return $$"""
        Sen bir veri bilimci asistanısın. Aşağıdaki SQL Server veritabanı schema bilgisini analiz et ve PyCaret AutoML motoru için bir konfigürasyon dosyası oluştur.

        Veritabanı: {{schemaInfo.DatabaseName}}
        Tablolar:
        {{tablesDesc}}

        Aşağıdaki JSON formatında bir konfigürasyon oluştur. JSON bloğunu ```json ve ``` arasında ver.

        ```json
        {
          "database": "{{schemaInfo.DatabaseName}}",
          "analysis_type": "auto",
          "tables": [
            {
              "name": "tablo_adı",
              "schema": "dbo",
              "row_count": 0,
              "analysis_types": ["regression", "classification", "anomaly", "clustering"],
              "target_columns": ["hedef_kolon"],
              "feature_columns": ["özellik1", "özellik2"],
              "ignore_columns": ["id_kolonu", "tarih_kolonu"],
              "column_types": {
                "numeric": ["kolon1"],
                "categorical": ["kolon2"],
                "datetime": ["kolon3"],
                "text": ["kolon4"]
              },
              "preprocessing": {
                "handle_missing": "mean",
                "normalize": true,
                "encode_categoricals": true,
                "remove_outliers": false
              },
              "suggested_analyses": [
                {
                  "type": "regression",
                  "target": "hedef_kolon",
                  "description": "Bu analizin açıklaması"
                }
              ]
            }
          ],
          "relationships": [
            {
              "from_table": "tablo1",
              "from_column": "id",
              "to_table": "tablo2",
              "to_column": "fk_id",
              "type": "one-to-many"
            }
          ],
          "global_settings": {
            "sampling_rate": 100,
            "max_rows_per_table": 50000,
            "session_id": 42,
            "n_jobs": -1
          }
        }
        ```

        Kurallar:
        1. Her tablo için en uygun analiz tiplerini belirle (regression, classification, anomaly, clustering)
        2. Hedef kolonları akıllıca seç (sayısal, iş değeri olan kolonlar)
        3. ID, tarih, primary key gibi kolonları ignore_columns'a ekle
        4. Kolon tiplerini doğru belirle
        5. Tablolar arası ilişkileri (FK) tespit et
        6. Her tablo için en az 1 suggested_analysis ekle
        7. Veritabanının genel bir özetini JSON'dan sonra yaz (### Özet başlığı ile)

        Sadece JSON ve özet ver, başka açıklama ekleme.
        """;
    }

    private static string BuildQueryTranslationPrompt(string question, string configJson, string schemaSummary)
    {
        return $$"""
        Sen bir veri analizi asistanısın. Kullanıcının doğal dil sorusunu PyCaret analiz parametrelerine çevir.

        ## Veritabanı Schema Özeti:
        {{schemaSummary}}

        ## PyCaret Konfigürasyonu:
        ```json
        {{configJson}}
        ```

        ## Kullanıcının Sorusu:
        "{{question}}"

        Bu soruyu PyCaret'in anlayacağı analiz parametrelerine çevir. JSON bloğunu ```json ve ``` arasında ver.

        ```json
        {
          "analysis_type": "regression|classification|anomaly|clustering|statistics|correlation",
          "target_table": "tablo_adı",
          "target_column": "kolon_adı veya null",
          "feature_columns": ["kolon1", "kolon2"],
          "filters": {
            "kolon_adı": "filtre_değeri"
          },
          "aggregation": "sum|avg|count|min|max|none",
          "group_by": ["kolon_adı"],
          "sort_by": "kolon_adı",
          "sort_order": "desc",
          "limit": 10,
          "chart_type": "bar|line|pie|doughnut|scatter|heatmap",
          "chart_title": "Grafik Başlığı",
          "description": "Bu analizin ne yapacağının kısa açıklaması"
        }
        ```

        Kurallar:
        1. Soruya en uygun analysis_type'ı seç
        2. Doğru tablo ve kolonları belirle
        3. Filtreleme gerekiyorsa filters'a ekle
        4. Gruplama gerekiyorsa group_by'a ekle
        5. Sonucu en iyi gösteren chart_type'ı seç
        6. Türkçe chart_title ver
        7. description kısmında ne yapılacağını kısaca anlat

        JSON'dan sonra kısa bir açıklama yaz (### Açıklama başlığı ile).
        """;
    }

    private static string ExtractJson(string text)
    {
        // ```json ... ``` bloğunu bul
        var startMarkers = new[] { "```json", "```JSON" };
        var startIdx = -1;

        foreach (var marker in startMarkers)
        {
            startIdx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (startIdx >= 0)
            {
                startIdx += marker.Length;
                break;
            }
        }

        if (startIdx < 0)
        {
            // Direkt JSON dene
            var firstBrace = text.IndexOf('{');
            var lastBrace = text.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
                return text[firstBrace..(lastBrace + 1)];

            return "{}";
        }

        var endIdx = text.IndexOf("```", startIdx);
        if (endIdx < 0) endIdx = text.Length;

        return text[startIdx..endIdx].Trim();
    }

    private static string ExtractSummary(string text)
    {
        var idx = text.IndexOf("### Özet", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = text.IndexOf("###", text.IndexOf("```", text.IndexOf("```") + 3) + 3);
        if (idx < 0) return "";
        return text[idx..].Trim();
    }

    private static string ExtractExplanation(string text)
    {
        var idx = text.IndexOf("### Açıklama", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = text.IndexOf("###", text.IndexOf("```", text.IndexOf("```") + 3) + 3);
        if (idx < 0) return "";
        return text[idx..].Trim();
    }
}

// DTOs
public class SchemaInfo
{
    public string DatabaseName { get; set; } = "";
    public List<TableSchemaDetail> Tables { get; set; } = new();
}

public class TableSchemaDetail
{
    public string TableName { get; set; } = "";
    public string Schema { get; set; } = "dbo";
    public int RowCount { get; set; }
    public List<ColumnDetail> Columns { get; set; } = new();
}

public class ColumnDetail
{
    public string ColumnName { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
}

public class GeminiAnalysisResult
{
    public bool Success { get; set; }
    public string ConfigJson { get; set; } = "{}";
    public string SchemaSummary { get; set; } = "";
    public string RawResponse { get; set; } = "";
    public string? Error { get; set; }
}

public class GeminiQueryResult
{
    public bool Success { get; set; }
    public string PyCaretParamsJson { get; set; } = "{}";
    public string Explanation { get; set; } = "";
    public string RawResponse { get; set; } = "";
    public string? Error { get; set; }
}

public class GeminiChatResult
{
    public bool Success { get; set; }
    public string Response { get; set; } = "";
    public string? Error { get; set; }
}
