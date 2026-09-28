using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Grafirio.Measurement.Reporting;

/// <summary>
/// Belgeyi diske yazar ve istenirse dashboard'a gonderir.
///
/// Dosya HER ZAMAN yaziliyor, yayin istense de: dashboard kapaliysa ya da
/// reddederse olcum kaybolmasin. Sonradan <c>publish</c> komutuyla gonderilebilir.
/// </summary>
public static class RunOutput
{
    public static async Task<string> WriteAsync(ScenarioRunDocument document, string? outPath, CancellationToken ct)
    {
        var path = outPath ?? Path.Combine("artifacts", "olcum",
            $"{document.Kind}-{Sanitize(document.Suite)}-{document.StartedAt.UtcDateTime:yyyyMMdd-HHmmss}.json");

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, ScenarioRunDocument.JsonOptions), ct);
        return path;
    }

    public static async Task PublishAsync(string json, string dashboardUrl, string? apiKey, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = new Uri(dashboardUrl.TrimEnd('/') + "/") };
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/scenario-runs")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(apiKey)) request.Headers.Add("X-Api-Key", apiKey);

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dashboard belgeyi reddetti (HTTP {(int)response.StatusCode}): {body}");

        var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetString();
        Console.WriteLine($"[dashboard] Yayinlandi: {dashboardUrl.TrimEnd('/')}/scenarios/{id}");
    }

    public static Task PublishAsync(ScenarioRunDocument document, string dashboardUrl, string? apiKey,
        CancellationToken ct) =>
        PublishAsync(JsonSerializer.Serialize(document, ScenarioRunDocument.JsonOptions), dashboardUrl, apiKey, ct);

    public static (string? Commit, string? Branch) Git()
    {
        return (Run("rev-parse --short HEAD"), Run("rev-parse --abbrev-ref HEAD"));

        static string? Run(string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("git", arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process is null) return null;
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(3000);
                return process.ExitCode == 0 && output.Length > 0 ? output : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
}
