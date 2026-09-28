using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Grafirio.Measurement.Evaluation;

/// <summary>
/// Beklenen parametreleri gercekleriyle karsilastirir ve cevaplari tutarlilik
/// icin kanonik bir parmak izine indirger.
/// </summary>
public static class JsonMatch
{
    /// <summary>
    /// <paramref name="expected"/>'in <paramref name="actual"/> icinde bulunup
    /// bulunmadigi. Uyusmazliklar yol bilgisiyle <paramref name="mismatches"/>'e
    /// yaziliyor — basarisiz bir vakada "neden" sorusunun cevabi bu.
    /// </summary>
    public static bool IsSubset(JsonNode? expected, JsonNode? actual, List<string> mismatches, string path = "$")
    {
        switch (expected)
        {
            case null:
                return true;

            case JsonObject expectedObject:
                if (actual is not JsonObject actualObject)
                {
                    mismatches.Add($"{path}: nesne bekleniyordu, {Describe(actual)} geldi");
                    return false;
                }

                var ok = true;
                foreach (var (key, value) in expectedObject)
                {
                    var match = actualObject.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
                    if (match.Key is null)
                    {
                        mismatches.Add($"{path}.{key}: alan yok (beklenen {Describe(value)})");
                        ok = false;
                        continue;
                    }

                    ok &= IsSubset(value, match.Value, mismatches, $"{path}.{key}");
                }

                return ok;

            case JsonArray expectedArray:
                if (actual is not JsonArray actualArray)
                {
                    mismatches.Add($"{path}: dizi bekleniyordu, {Describe(actual)} geldi");
                    return false;
                }

                var all = true;
                foreach (var item in expectedArray)
                {
                    // Sira onemsiz: group_by ["A","B"] ile ["B","A"] ayni soruyu cevaplar.
                    if (!actualArray.Any(candidate => IsSubset(item, candidate, [], path)))
                    {
                        mismatches.Add($"{path}: {Describe(item)} dizide yok ({Describe(actual)})");
                        all = false;
                    }
                }

                return all;

            case JsonValue expectedValue:
                if (actual is not JsonValue actualValue || !ValuesEqual(expectedValue, actualValue))
                {
                    mismatches.Add($"{path}: {Describe(expected)} bekleniyordu, {Describe(actual)} geldi");
                    return false;
                }

                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Kanonik parmak izi: anahtarlar sirali, metinler normalize, diziler sirali.
    /// Ayni soruya verilen iki cevabin "ayni" olup olmadigina bununla karar
    /// veriliyor. <paramref name="ignoreKey"/> her calistirmada degisen alanlar
    /// (zaman damgalari, is kimlikleri) icin.
    /// </summary>
    public static string Fingerprint(JsonNode? node, Func<string, bool>? ignoreKey = null)
    {
        var canonical = Canonicalize(node, ignoreKey ?? (_ => false));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes)[..16];
    }

    private static string Canonicalize(JsonNode? node, Func<string, bool> ignored) => node switch
    {
        null => "null",
        JsonObject o => "{" + string.Join(",", o
            .Where(p => !ignored(p.Key))
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{p.Key.ToLowerInvariant()}:{Canonicalize(p.Value, ignored)}")) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(i => Canonicalize(i, ignored)).Order(StringComparer.Ordinal)) + "]",
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => "\"" + Normalize(v.GetValue<string>()) + "\"",
            JsonValueKind.Number => NormalizeNumber(v),
            _ => v.ToJsonString()
        },
        _ => node.ToJsonString()
    };

    private static bool ValuesEqual(JsonValue expected, JsonValue actual)
    {
        var expectedKind = expected.GetValueKind();
        var actualKind = actual.GetValueKind();

        if (expectedKind == JsonValueKind.String && actualKind == JsonValueKind.String)
        {
            var pattern = expected.GetValue<string>();

            // "re:" ile baslayan beklenti duzenli ifade: model kolonu bazen
            // "Country", bazen "dbo.Orders.Country" diye yaziyor ve ikisi de dogru.
            // Ornek: "re:(^|\\.)country$".
            if (pattern.StartsWith("re:", StringComparison.Ordinal))
                return System.Text.RegularExpressions.Regex.IsMatch(Normalize(actual.GetValue<string>()), pattern[3..],
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

            return Normalize(pattern) == Normalize(actual.GetValue<string>());
        }

        if (expectedKind == JsonValueKind.Number && actualKind == JsonValueKind.Number)
            return Math.Abs(expected.GetValue<double>() - actual.GetValue<double>()) < 1e-9;

        return expected.ToJsonString() == actual.ToJsonString();
    }

    /// <summary>
    /// "[dbo].[Orders]" ile "dbo.orders" ayni tablo. Model ikisini de yazabiliyor
    /// ve farkin cevabin dogrulugu ile ilgisi yok.
    /// </summary>
    public static string Normalize(string value) =>
        value.Replace("[", "").Replace("]", "").Replace("\"", "").Trim().ToLowerInvariant();

    private static string NormalizeNumber(JsonValue value) =>
        value.GetValue<double>().ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static string Describe(JsonNode? node)
    {
        var text = node?.ToJsonString() ?? "null";
        return text.Length > 120 ? text[..120] + "…" : text;
    }
}
