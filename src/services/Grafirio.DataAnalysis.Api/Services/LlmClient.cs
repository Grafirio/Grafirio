using System.Text;
using System.Text.Json;

namespace Grafirio.DataAnalysis.Api.Services;

/// <summary>
/// Azure OpenAI chat-completions istemcisi.
///
/// Bir zamanlar saglayici-bagimsizdi ve <c>LLM_PROVIDER</c> ile Gemini'ye de
/// gidebiliyordu. Gemini kullanimdan kaldirildi; secim mekanizmasi kaldi ve
/// zarar verdi: compose'daki varsayilan hala <c>gemini</c> oldugu ve ortam
/// degiskeni yapilandirmayi ezdigi icin ayni kod container'da Gemini'ye,
/// IDE'den Azure'a gidiyordu. Tek saglayici, tek yol.
/// </summary>
public interface ILlmClient
{
    bool IsConfigured { get; }
    Task<string> GenerateAsync(string prompt, double temperature = 0.2, int maxTokens = 2048,
        CancellationToken cancellationToken = default);
}

public sealed class LlmClient : ILlmClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LlmClient> _logger;

    // gpt-5 / o-serisi klasik parametreleri reddediyor (max_tokens yerine
    // max_completion_tokens, temperature yalnizca varsayilan). Deployment adi
    // model ailesini ele vermedigi icin once modern govde denenir, sunucu
    // reddederse klasige dusulur ve karar hatirlanir.
    //
    // `bool?` degil `int`: onceki hali iki alanli bir struct'ti (hasValue +
    // value) ve okunmasi atomik degildi. Sozluk uretimi artik dorderli
    // dalgalar halinde paralel calisiyor; iki alanin yarim okunmasi, klasik
    // govde gereken bir deployment'ta modern govdenin secilmesine yol
    // acabilirdi. Volatile ile okuyup yazmak bunu imkânsiz kiliyor.
    private const int ParamModeUnknown = 0;
    private const int ParamModeModern = 1;
    private const int ParamModeLegacy = 2;

    private static int _paramMode;

    public LlmClient(IHttpClientFactory httpClientFactory, IConfiguration configuration,
        ILogger<LlmClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;

        // Yapilandirmanin durumu acilista yaziliyor: sessiz varsayilanlar
        // pahaliya mal olmustu — anahtar tanimsizken servis ayaga kalkiyor,
        // hata ancak kullanici analiz baslattiginda ve sebebi yazmadan
        // goruluyordu.
        _logger.LogInformation(
            "LLM: Azure OpenAI | deployment: {Deployment} | yapılandırılmış: {IsConfigured}",
            Read("AZURE_OPENAI_DEPLOYMENT", "AzureOpenAI:Deployment") ?? "(tanımsız)",
            IsConfigured);
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Read("AZURE_OPENAI_API_KEY", "AzureOpenAI:ApiKey"));

    public async Task<string> GenerateAsync(string prompt, double temperature = 0.2,
        int maxTokens = 2048, CancellationToken cancellationToken = default)
    {
        var endpoint = (Read("AZURE_OPENAI_ENDPOINT", "AzureOpenAI:Endpoint") ?? "").TrimEnd('/');
        var deployment = Read("AZURE_OPENAI_DEPLOYMENT", "AzureOpenAI:Deployment") ?? "";
        var apiKey = Read("AZURE_OPENAI_API_KEY", "AzureOpenAI:ApiKey") ?? "";
        var apiVersion = Read("AZURE_OPENAI_API_VERSION", "AzureOpenAI:ApiVersion") ?? "2024-12-01-preview";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Azure OpenAI ayarları eksik: endpoint, deployment ve API anahtarı "
                + "gerekiyor. Her biri iki yerden okunuyor — AZURE_OPENAI_ENDPOINT / "
                + "AZURE_OPENAI_DEPLOYMENT / AZURE_OPENAI_API_KEY ortam değişkenleri "
                + "ya da AzureOpenAI:Endpoint / AzureOpenAI:Deployment / "
                + $"AzureOpenAI:ApiKey ayarları. {SettingPrecedence}");
        }

        var url = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
        var client = _httpClientFactory.CreateClient(nameof(LlmClient));

        // Reasoning modelleri (gpt-5 / o-serisi) dusunme adimlarini da ayni
        // tavandan harciyor. Tavan yetmezse sunucu HTTP 200 doner ama
        // finish_reason="length" ve content="" gelir — hataya benzemeyen bir
        // basarisizlik. Tek denemede vazgecmek analizin tamamini bosa
        // cikardigi icin butce buyutulup yeniden soruluyor.
        const int maxBudgetAttempts = 3;
        var budget = maxTokens;

        for (var budgetAttempt = 1; ; budgetAttempt++)
        {
            var body = await SendAzureRequestAsync(
                client, url, apiKey, prompt, temperature, budget, cancellationToken);

            var content = ExtractContent(body);
            if (!string.IsNullOrWhiteSpace(content)) return content;

            var finishReason = ExtractFinishReason(body);
            if (finishReason == "length" && budgetAttempt < maxBudgetAttempts)
            {
                budget *= 2;
                _logger.LogWarning(
                    "Azure OpenAI token bütçesi düşünmeye yetip cevaba yetmedi (finish_reason=length). " +
                    "Bütçe {Budget} token'a çıkarılıp tekrar denenecek ({Attempt}/{Max}).",
                    budget, budgetAttempt, maxBudgetAttempts);
                continue;
            }

            throw new InvalidOperationException(finishReason == "length"
                ? $"Azure OpenAI cevabı token bütçesine sığmadı ({budget} token'a kadar denendi). " +
                  "Model düşünme adımlarını da aynı bütçeden harcıyor; daha az tablo/kolon " +
                  "seçmek veya deployment limitini yükseltmek gerekiyor."
                : $"Azure OpenAI boş içerik döndürdü (finish_reason={finishReason ?? "bilinmiyor"}): " +
                  $"{Truncate(body, 300)}");
        }
    }

    /// <summary>
    /// Tek bir chat-completions cagrisi yapar; modern/klasik govde secimini ve
    /// 429 geri cekilmesini yonetir. Basarili yanitin ham govdesini dondurur.
    /// </summary>
    private async Task<string> SendAzureRequestAsync(HttpClient client, string url, string apiKey,
        string prompt, double temperature, int maxTokens, CancellationToken cancellationToken)
    {
        // Her iki govde de listede, hatirlanan karar yalnizca SIRAYI
        // belirliyor. Onceden hatirlanan karar tek elemanli bir listeye
        // donuyordu; deployment degistiginde (ya da karar bir sekilde yanlis
        // hatirlandiginda) o tek deneme reddedilince donguden sessizce
        // cikiliyor ve HATA GOVDESI basarili yanit gibi geri donuyordu.
        // Mutlu yolda ikinci eleman zaten hic denenmiyor.
        var attempts = Volatile.Read(ref _paramMode) == ParamModeLegacy
            ? new[] { true, false }
            : new[] { false, true };

        string body = "";

        for (var attempt = 0; attempt < attempts.Length; attempt++)
        {
            var legacy = attempts[attempt];
            var lastAttempt = attempt == attempts.Length - 1;

            // 429 (kota) gecici bir durumdur; tek denemede vazgecmek analizin
            // tamamini bosa cikariyor. Azure `Retry-After` basligiyla ne kadar
            // beklenecegini soyluyor — ona uyuluyor, yoksa ustel geri cekilme.
            const int maxRateLimitRetries = 4;
            var rateLimitAttempt = 0;

            // Yanittan yalnizca bu ucu lazim; nesnenin kendisi dongunun
            // disinda yasamiyor.
            System.Net.HttpStatusCode status;

            while (true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("api-key", apiKey);
                request.Content = new StringContent(
                    BuildAzurePayload(prompt, temperature, maxTokens, legacy),
                    Encoding.UTF8, "application/json");

                TimeSpan? retryAfter;

                // Yanit okunur okunmaz birakiliyor. Onceden hicbir yolda
                // dispose edilmiyordu: ne 429 dongusunde, ne govde secimi
                // degisirken, ne de basarili cikista. Tek cagrida sorun
                // degildi; sozluk uretimi dorderli dalgalar halinde yirmiden
                // fazla cagri yapmaya baslayinca birikmelerinin savunulacak
                // tarafi kalmadi.
                using (var response = await client.SendAsync(request, cancellationToken))
                {
                    body = await response.Content.ReadAsStringAsync(cancellationToken);
                    status = response.StatusCode;
                    retryAfter = response.Headers.RetryAfter?.Delta;
                }

                if (status != System.Net.HttpStatusCode.TooManyRequests) break;

                if (++rateLimitAttempt > maxRateLimitRetries)
                {
                    throw new InvalidOperationException(
                        "Azure OpenAI kota sınırı aşıldı ve tekrar denemeler yetmedi. " +
                        "Deployment kapasitesini yükseltmek gerekebilir. " +
                        $"Sunucu yanıtı: {Truncate(body, 200)}");
                }

                var wait = retryAfter
                           ?? TimeSpan.FromSeconds(Math.Pow(2, rateLimitAttempt) * 2);

                _logger.LogWarning(
                    "Azure OpenAI kota sınırı (429). {Wait} sn beklenip tekrar denenecek ({Attempt}/{Max}).",
                    wait.TotalSeconds, rateLimitAttempt, maxRateLimitRetries);

                await Task.Delay(wait, cancellationToken);
            }

            if (!lastAttempt && status == System.Net.HttpStatusCode.BadRequest &&
                IsUnsupportedParameter(body))
            {
                _logger.LogInformation(
                    "Azure OpenAI {Mode} gövdeyi reddetti, diğerine düşülüyor",
                    legacy ? "klasik" : "modern");
                continue;
            }

            if ((int)status is < 200 or > 299)
            {
                // Baglam penceresi asildiginda Azure'un dondurdugu JSON
                // dogrudan kullaniciya gosteriliyordu; "Input tokens exceed the
                // configured limit of 272000 tokens" satirini okuyan kisinin
                // yapabilecegi hicbir sey yok. Sebep de bir yapilandirma
                // hatasi degil: istegin kendisi cok buyuk, yani bolunmesi
                // gerekiyor.
                if (IsContextLengthExceeded(body))
                {
                    throw new InvalidOperationException(
                        "Gönderilen şema, yapay zekâ modelinin tek seferde " +
                        "okuyabileceğinden büyük. Seçili tablolardan bir kısmını " +
                        "çıkarıp tekrar deneyin; sorun sürerse sunucu tarafında " +
                        "parça boyutunun düşürülmesi gerekiyor. " +
                        $"(Azure: {Truncate(body, 200)})");
                }

                throw new InvalidOperationException(DescribeFailure((int)status, body));
            }

            Volatile.Write(ref _paramMode, legacy ? ParamModeLegacy : ParamModeModern);
            break;
        }

        return body;
    }

    private static string BuildAzurePayload(string prompt, double temperature, int maxTokens, bool legacy)
    {
        var payload = new Dictionary<string, object>
        {
            ["messages"] = new[] { new { role = "user", content = prompt } }
        };

        if (legacy)
        {
            payload["temperature"] = temperature;
            payload["max_tokens"] = maxTokens;
        }
        else
        {
            // Modern modellerde tavan yalnizca gorunur cevabi degil, ondan once
            // harcanan reasoning token'larini da kapsiyor. +2048'lik pay dar
            // semalarda bile dusunmeye gidip cevaba yer birakmiyordu.
            payload["max_completion_tokens"] = Math.Max(maxTokens, 2048) + 8192;
        }

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// Basarisiz bir cagriyi kullanicinin okuyabilecegi bir cumleye cevirir.
    ///
    /// Bu metin yukari katmanda <c>ex.Message</c> olarak analiz kaydina
    /// yaziliyor ve ekranda gorunuyor. "Azure OpenAI HTTP 401: {...}" satirini
    /// okuyan kisinin yapabilecegi bir sey yok; hangi ayarin eksik oldugunu
    /// soylemek gerekiyor. Ham govde yine sonda duruyor — teshis icin lazim,
    /// ama artik cumlenin tamami degil.
    /// </summary>
    /// <summary>
    /// Ayarlarin iki kaynagi var ve oncelikleri esit degil: <see cref="Read"/>
    /// once ortam degiskenine bakiyor, yoksa yapilandirma anahtarina dusuyor.
    ///
    /// Teshis mesajinda yazmasinin sebebi somut: appsettings'i duzeltip sonuc
    /// alamayan kisi, cogu zaman ayni ayarin ortam degiskeni olarak da tanimli
    /// oldugunu ve onu ezdigini bilmiyor. Yalnizca bir kaynagi soylemek,
    /// teshisi yanlis dosyaya gonderiyordu.
    /// </summary>
    private const string SettingPrecedence =
        "Ortam değişkeni tanımlıysa yapılandırma ayarını ezer.";

    private static string DescribeFailure(int status, string body)
    {
        var detail = $"(Azure: {Truncate(body, 200)})";

        return status switch
        {
            401 or 403 =>
                "Yapay zekâ servisi isteği reddetti: API anahtarı geçersiz ya da bu "
                + "deployment'a yetkisi yok. Anahtar iki yerden okunuyor — "
                + $"AZURE_OPENAI_API_KEY ya da AzureOpenAI:ApiKey. {SettingPrecedence} {detail}",

            404 =>
                "Yapay zekâ servisinde bu deployment bulunamadı. Deployment adı ve "
                + "endpoint iki yerden okunuyor — AZURE_OPENAI_DEPLOYMENT / "
                + "AZURE_OPENAI_ENDPOINT ya da AzureOpenAI:Deployment / "
                + $"AzureOpenAI:Endpoint. {SettingPrecedence} {detail}",

            >= 500 =>
                "Yapay zekâ servisi geçici olarak yanıt veremedi. Birkaç dakika sonra "
                + $"tekrar deneyin. {detail}",

            _ =>
                $"Yapay zekâ servisi isteği kabul etmedi (HTTP {status}). {detail}",
        };
    }

    /// <summary>
    /// Istek modelin baglam penceresine sigmadi mi.
    ///
    /// Bu, gecici bir hata degil: ayni istek her denemede ayni sekilde
    /// dusecek. Cagiran tarafin yapabilecegi tek sey istegi kucultmek, o
    /// yuzden mesaj da bunu soyluyor.
    /// </summary>
    private static bool IsContextLengthExceeded(string body) =>
        ErrorCode(body) == "context_length_exceeded";

    private static bool IsUnsupportedParameter(string body) =>
        ErrorCode(body) is "unsupported_parameter" or "unsupported_value";

    /// <summary>
    /// Azure hata govdesindeki <c>error.code</c> — okunamazsa null.
    ///
    /// Her adimda <c>ValueKind</c> kontrol ediliyor, cunku
    /// <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/> nesne
    /// olmayan bir elemanda ve <see cref="JsonElement.GetString"/> string
    /// olmayan bir degerde <see cref="InvalidOperationException"/> firlatir —
    /// <see cref="JsonException"/> degil, yani asagidaki catch onu tutmaz.
    ///
    /// Bu, hata yolunun ta kendisinde patlamak demek olurdu: bu koda ancak
    /// ortada zaten bir sorun varken geliniyor ve tek isi o sorunu anlasilir
    /// kilmak. Beklenmedik bir govde yuzunden okunabilir mesajin yerini
    /// alakasiz bir istisnanin almasi, hicbir sey yapmamaktan kotu.
    ///
    /// Iki cagiran da ayni ayristirmayi yapiyordu; tek yerde durmasinin sebebi
    /// de bu — ayni kusur iki kez yazilmisti.
    /// </summary>
    private static string? ErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("code", out var code) &&
                code.ValueKind == JsonValueKind.String)
            {
                return code.GetString();
            }
        }
        catch (JsonException)
        {
            // Govde JSON degilse teshis edilecek bir sey yok.
        }

        return null;
    }

    private static string ExtractContent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

            // Butce dolunca sunucu `content` alanini bos string olarak, bazen de
            // hic gondermiyor. Ikisi de "cevap yok" demek; sekil hatasi degil, o
            // yuzden burada patlamak yerine bos donup cagirana karar biraktiriyoruz.
            return message.TryGetProperty("content", out var content)
                ? content.GetString() ?? ""
                : "";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new InvalidOperationException($"Azure OpenAI beklenmedik yanıt şekli: {Truncate(body, 300)}");
        }
    }

    /// <summary>
    /// Yanitin neden bittigini soyler ("stop", "length", "content_filter"...).
    /// Cozulemezse null doner — teshis icin kullanildigi icin burada hata
    /// firlatmak dogru degil.
    /// </summary>
    private static string? ExtractFinishReason(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("choices")[0]
                .TryGetProperty("finish_reason", out var reason)
                ? reason.GetString()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ayari once ortam degiskeninden, yoksa yapilandirmadan okur. Bos deger
    /// "tanimsiz" sayilir.
    ///
    /// Onceki surum once _configuration'a bakip `??` ile ortam degiskenine
    /// dusuyordu. appsettings.json'da anahtarlar `"ApiKey": ""` seklinde
    /// yer tutucu olarak durdugu icin bos string donuyor, bos string null
    /// olmadigindan `??` hic devreye girmiyor ve ortam degiskeni okunmuyordu.
    /// Sonuc: Azure'da anahtar tanimliyken servis "LLM yapilandirilmamis"
    /// diyordu. Ortam degiskeni oncelikli olmali; dagitim anindaki deger,
    /// imaja gomulu varsayilani ezer.
    /// </summary>
    private string? Read(string environmentKey, string configurationKey)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(environmentKey);
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;

        var fromConfiguration = _configuration[configurationKey];
        return string.IsNullOrWhiteSpace(fromConfiguration) ? null : fromConfiguration;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
