using System.Net;
using System.Net.Sockets;
using Grafirio.Bridge.Contracts;

namespace Grafirio.DataAnalysis.Api.Data.Access;

public sealed class DirectConnectionPolicy
{
    private readonly HashSet<string> _allowedPrivateHosts;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;

    public DirectConnectionPolicy(IConfiguration configuration)
        : this(configuration, (host, cancellation) => Dns.GetHostAddressesAsync(host, cancellation)) { }

    internal DirectConnectionPolicy(IConfiguration configuration, Func<string, CancellationToken, Task<IPAddress[]>> resolve)
    {
        _allowedPrivateHosts = new(configuration.GetSection("DirectConnections:AllowedPrivateHosts").Get<string[]>() ?? [],
            StringComparer.OrdinalIgnoreCase);
        _resolve = resolve;
    }

    public async Task<DataSourceTarget> AuthorizeAsync(DataSourceTarget target, CancellationToken ct)
    {
        SqlServerAddress endpoint;
        try { endpoint = SqlServerAddress.Parse(target.Host, target.Port); }
        catch (ArgumentException exception) { throw new DataSourceException(exception.Message); }
        IPAddress[] addresses;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            addresses = IPAddress.TryParse(endpoint.Host, out var literal)
                ? [literal] : await _resolve(endpoint.Host, deadline.Token);
        }
        catch (SocketException) { throw new DataSourceException("DNS_FAILED: Sunucu adı çözümlenemedi."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new DataSourceException("DNS_TIMEOUT: Sunucu adresi zamanında çözümlenemedi."); }
        if (addresses.Length == 0) throw new DataSourceException("DNS_FAILED: Sunucu adresi bulunamadı.");
        foreach (var candidate in addresses)
        {
            var address = candidate.IsIPv4MappedToIPv6 ? candidate.MapToIPv4() : candidate;
            if (IsInfrastructure(address) || (IsPrivate(address) && !_allowedPrivateHosts.Contains(endpoint.Host)))
                throw new DataSourceException("DIRECT_TARGET_BLOCKED: Bu hedefe buluttan doğrudan erişim yasak. Yerel ağ için Bridge kullanın.");
        }
        return target with
        {
            Host = addresses[0].ToString(), Port = endpoint.Port,
            CertificateHost = IPAddress.TryParse(endpoint.Host, out _) ? null : endpoint.Host
        };
    }

    internal static bool IsInfrastructure(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal) return true;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (bytes[0] == 0 && bytes[1] == 0) || (bytes[0] == 0x20 && bytes[1] == 0x02)
                || address.ToString().StartsWith("64:ff9b:", StringComparison.OrdinalIgnoreCase);
        return bytes[0] == 0 || bytes[0] == 127 || bytes[0] >= 224
            || (bytes[0] == 169 && bytes[1] == 254)
            || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            || address.Equals(IPAddress.Parse("168.63.129.16"));
    }

    internal static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6) return (bytes[0] & 0xfe) == 0xfc;
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }
}
