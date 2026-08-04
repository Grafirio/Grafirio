using System.Text;
using System.Text.Json;

namespace Grafirio.DataAnalysis.Api.Services;

/// <summary>
/// Saglayici-bagimsiz LLM istemcisi. LLM_PROVIDER (veya Llm:Provider) ile secilir:
///   azure_openai : Azure OpenAI chat-completions deployment'i
///   gemini       : Google Gemini (eski varsayilan, geri donus icin duruyor)
/// Python taraftaki llm/client.py ile ayni sozlesmeyi izler.
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
    private readonly string _provider;

    // gpt-5 / o-serisi klasik parametreleri reddediyor (max_tokens yerine
    // max_completion_tokens, temperature yalnizca varsayilan). Deployment adi
    // model ailesini ele vermedigi icin once modern govde denenir, sunucu
    // reddederse klasige dusulur ve karar hatirlanir.
    private static bool? _useLegacyParams;

    public LlmClient(IHttpClientFactory httpClientFactory, IConfiguration configuration,
        ILogger<LlmClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;

        var configured = Read("LLM_PROVIDER", "Llm:Provider");
        _provider = (configured ?? "azure_openai").Trim().ToLowerInvariant();

        // Sessiz varsayilan pahaliya mal oldu: ortamda GEMINI_API_KEY tanimliyken
        // LLM_PROVIDER tanimli olmadigi icin istemci azure_openai'a dusuyor, onun
        // anahtari da bulunmadigindan mock mode'a giriyordu. Disaridan gorunen tek
        // sey sebebi yazmayan bir hataydi. Artik hangi saglayicinin secildigi ve
        // yapilandirilmis olup olmadigi acilista yaziliyor.
        if (configured is null)
        {
            _logger.LogWarning(
                "LLM_PROVIDER tanımlı değil, varsayılan '{Provider}' kullanılıyor. Yapılandırılmış: {IsConfigured}",
                _provider, IsConfigured);
        }
        else
        {
            _logger.LogInformation(
                "LLM sağlayıcı: {Provider} | yapılandırılmış: {IsConfigured}", _provider, IsConfigured);
        }
    }

    public bool IsConfigured => _provider switch
    {
        "azure_openai" => !string.IsNullOrWhiteSpace(Read("AZURE_OPENAI_API_KEY", "AzureOpenAI:ApiKey")),
        "gemini" => !string.IsNullOrWhiteSpace(Read("GEMINI_API_KEY", "Gemini:ApiKey")),
        _ => false
    };

    public async Task<string> GenerateAsync(string prompt, double temperature = 0.2,
        int maxTokens = 2048, CancellationToken cancellationToken = default)
    {
        if (_provider == "gemini")
        {
            return await GenerateGeminiAsync(prompt, cancellationToken);
        }
        return await GenerateAzureAsync(prompt, temperature, maxTokens, cancellationToken);
    }

    private async Task<string> GenerateAzureAsync(string prompt, double temperature, int maxTokens,
        CancellationToken cancellationToken)
    {
        var endpoint = (Read("AZURE_OPENAI_ENDPOINT", "AzureOpenAI:Endpoint") ?? "").TrimEnd('/');
        var deployment = Read("AZURE_OPENAI_DEPLOYMENT", "AzureOpenAI:Deployment") ?? "";
        var apiKey = Read("AZURE_OPENAI_API_KEY", "AzureOpenAI:ApiKey") ?? "";
        var apiVersion = Read("AZURE_OPENAI_API_VERSION", "AzureOpenAI:ApiVersion") ?? "2024-12-01-preview";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_DEPLOYMENT / AZURE_OPENAI_API_KEY eksik");
        }

        var url = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
        var attempts = _useLegacyParams is null ? new[] { false, true } : new[] { _useLegacyParams.Value };

        var client = _httpClientFactory.CreateClient(nameof(LlmClient));
        HttpResponseMessage? response = null;
        string body = "";

        foreach (var legacy in attempts)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("api-key", apiKey);
            request.Content = new StringContent(
                BuildAzurePayload(prompt, temperature, maxTokens, legacy),
                Encoding.UTF8, "application/json");

            response = await client.SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!legacy && response.StatusCode == System.Net.HttpStatusCode.BadRequest &&
                IsUnsupportedParameter(body))
            {
                _logger.LogInformation("Azure OpenAI modern parametreleri reddetti, klasik gövdeye düşülüyor");
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Azure OpenAI HTTP {(int)response.StatusCode}: {Truncate(body, 300)}");
            }

            _useLegacyParams = legacy;
            break;
        }

        var content = ExtractContent(body);
        if (string.IsNullOrWhiteSpace(content))
        {
            // Reasoning token'lari da ayni butceden dusuluyor; tavan dar kalirsa
            // model dusunmeyi bitirir ama gorunur cevaba yer kalmaz.
            throw new InvalidOperationException(
                $"Azure OpenAI boş içerik döndürdü: {Truncate(body, 300)}");
        }
        return content;
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
            payload["max_completion_tokens"] = Math.Max(maxTokens, 2048) + 2048;
        }

        return JsonSerializer.Serialize(payload);
    }

    private static bool IsUnsupportedParameter(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("code", out var code))
            {
                var value = code.GetString();
                return value is "unsupported_parameter" or "unsupported_value";
            }
        }
        catch (JsonException)
        {
            // Gövde JSON değilse zaten yeniden denemeye değmez.
        }
        return false;
    }

    private static string ExtractContent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new InvalidOperationException($"Azure OpenAI beklenmedik yanıt şekli: {Truncate(body, 300)}");
        }
    }

    private async Task<string> GenerateGeminiAsync(string prompt, CancellationToken cancellationToken)
    {
        var apiKey = Read("GEMINI_API_KEY", "Gemini:ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("GEMINI_API_KEY yapılandırılmamış");
        }

        var googleAi = new Mscc.GenerativeAI.GoogleAI(apiKey);
        var model = googleAi.GenerativeModel(Mscc.GenerativeAI.Model.Gemini20Flash);
        var response = await model.GenerateContent(prompt);
        return response.Text ?? "";
    }

    private string? Read(string environmentKey, string configurationKey)
        => _configuration[configurationKey]
           ?? Environment.GetEnvironmentVariable(environmentKey);

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
