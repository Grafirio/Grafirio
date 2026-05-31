using System.Collections.Concurrent;

namespace Grafirio.Gateway.Metrics;

/// <summary>
/// In-memory request counter. Her YARP route için istek sayısı ve son çağrı zamanını tutar.
/// Restart'ta sıfırlanır — servis kullanım analizi için yeterli.
/// </summary>
public class RequestMetrics
{
    private readonly ConcurrentDictionary<string, ServiceStat> _stats = new();

    public void Record(string routeId, int statusCode)
    {
        var stat = _stats.GetOrAdd(routeId, _ => new ServiceStat(routeId));
        stat.Increment(statusCode);
    }

    public IReadOnlyDictionary<string, ServiceStat> GetAll() => _stats;

    public void Reset() => _stats.Clear();
}

public class ServiceStat(string routeId)
{
    public string RouteId  { get; } = routeId;
    public long   Hits     { get; private set; }
    public long   Errors   { get; private set; } // 5xx
    public DateTime? LastHit { get; private set; }
    public DateTime  Since  { get; } = DateTime.UtcNow;

    private readonly object _lock = new();

    public void Increment(int statusCode)
    {
        lock (_lock)
        {
            Hits++;
            if (statusCode >= 500) Errors++;
            LastHit = DateTime.UtcNow;
        }
    }
}
