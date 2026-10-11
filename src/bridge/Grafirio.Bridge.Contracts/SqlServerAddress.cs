using System.Net;
using System.Text.RegularExpressions;

namespace Grafirio.Bridge.Contracts;

public sealed record SqlServerAddress(string Host, int Port)
{
    public string DataSource => $"tcp:{(Host.Contains(':') ? $"[{Host}]" : Host)},{Port}";

    public static SqlServerAddress Parse(string? host, int port = 0)
    {
        var address = (host ?? string.Empty).Trim();
        int? embeddedPort = null;
        var isBracketed = address.StartsWith('[');
        var match = Regex.Match(address, @"^(?:\[(?<host>[0-9a-fA-F:.]+)\]|(?<host>[^:,]+))[:,](?<port>\d+)$");
        if (match.Success)
        {
            address = match.Groups["host"].Value.Trim();
            if (!int.TryParse(match.Groups["port"].Value, out var parsedPort))
                throw new ArgumentException("Port 1–65535 arasında olmalıdır.");
            embeddedPort = parsedPort;
        }
        else if (address.StartsWith('[') && address.EndsWith(']'))
        {
            address = address[1..^1];
            if (!IPAddress.TryParse(address, out var bracketed) || bracketed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                throw new ArgumentException("Köşeli parantez yalnızca IPv6 adreslerinde kullanılabilir.");
        }
        if (address.Length == 0 || address.Any(char.IsWhiteSpace) || address.IndexOfAny(['/', '\\', ';', ',', '@', '?', '#', '[', ']']) >= 0
            || (address.Contains(':') && !IPAddress.TryParse(address, out _)))
            throw new ArgumentException("Geçerli bir sunucu adı veya IP ve ayrı TCP portu girin.");
        if (isBracketed && (!IPAddress.TryParse(address, out var parsedAddress)
            || parsedAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6))
            throw new ArgumentException("Köşeli parantez yalnızca IPv6 adreslerinde kullanılabilir.");
        var effectivePort = port == 0 ? embeddedPort ?? 1433 : port;
        if (effectivePort is < 1 or > 65535 || embeddedPort is < 1 or > 65535)
            throw new ArgumentException("Port 1–65535 arasında olmalıdır.");
        if (embeddedPort.HasValue && port != 0 && port != embeddedPort)
            throw new ArgumentException("Sunucu adresindeki port ile Port alanı farklı.");
        return new(address, effectivePort);
    }
}
