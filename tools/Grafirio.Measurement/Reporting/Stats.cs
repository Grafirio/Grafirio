namespace Grafirio.Measurement.Reporting;

/// <summary>
/// Yuzdelik ve ortalama. Yuzdelik, dogrusal ara degerleme ile (Excel'in
/// PERCENTILE.INC'i, numpy'nin varsayilani) hesaplaniyor: raporu okuyan kisi
/// ayni sayilari kendi araciyla dogrulayabilsin.
/// </summary>
public static class Stats
{
    public static double Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        if (values.Count == 0) return double.NaN;
        if (percentile is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percentile));

        var sorted = values.Order().ToArray();
        var rank = percentile / 100d * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
    }

    public static double Mean(IReadOnlyCollection<double> values) =>
        values.Count == 0 ? double.NaN : values.Average();

    public static double StdDev(IReadOnlyCollection<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    /// <summary>
    /// Yalnizca sonlu degerleri metrik olarak ekler. Bos bir kumenin NaN'i
    /// dashboard'a gitmemeli; dashboard onu zaten reddediyor.
    /// </summary>
    public static void AddIfFinite(this List<Metric> metrics, Metric metric)
    {
        if (double.IsFinite(metric.Value)) metrics.Add(metric);
    }
}
