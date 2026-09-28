namespace Grafirio.Measurement.Reporting;

/// <summary>
/// BenchmarkDotNet'in kendi "full" JSON raporunu dashboard'a gonderir.
///
/// Donusturme dashboard tarafinda (<c>POST /api/runs/import/bdn</c>): mikro
/// benchmark sonuclari orada zaten var olan BenchmarkDotNet modeline (ortalama,
/// sapma, bellek) oturuyor ve mevcut Kosular/Trendler/Karsilastir sayfalarinda
/// gorunuyor. Grafirio tarafi BenchmarkDotNet disinda hicbir seye bagli degil.
/// </summary>
public static class BdnPublisher
{
    public static async Task PublishAsync(IEnumerable<string> files, string dashboardUrl, string? apiKey,
        string? label, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = new Uri(dashboardUrl.TrimEnd('/') + "/") };
        var (commit, branch) = RunOutput.Git();

        // Ayni komutla gonderilen dosyalar tek kosu: BenchmarkDotNet her sinif icin
        // ayri rapor yaziyor ama hepsi ayni calistirmanin parcasi.
        var runId = Guid.NewGuid();

        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            var query = $"api/runs/import/bdn?runId={runId}" +
                        (label is null ? "" : $"&label={Uri.EscapeDataString(label)}") +
                        (commit is null ? "" : $"&gitCommit={Uri.EscapeDataString(commit)}") +
                        (branch is null ? "" : $"&gitBranch={Uri.EscapeDataString(branch)}") +
                        $"&machineName={Uri.EscapeDataString(Environment.MachineName)}";

            using var request = new HttpRequestMessage(HttpMethod.Post, query)
            {
                Content = new StringContent(await File.ReadAllTextAsync(file, ct), System.Text.Encoding.UTF8,
                    "application/json")
            };
            if (!string.IsNullOrEmpty(apiKey)) request.Headers.Add("X-Api-Key", apiKey);

            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"{Path.GetFileName(file)} reddedildi (HTTP {(int)response.StatusCode}): {body}");

            Console.WriteLine($"[dashboard] {Path.GetFileName(file)} gonderildi.");
        }

        Console.WriteLine($"[dashboard] Kosu: {dashboardUrl.TrimEnd('/')}/runs/{runId}");
    }
}
