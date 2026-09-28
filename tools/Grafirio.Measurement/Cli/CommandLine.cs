using System.Globalization;

namespace Grafirio.Measurement.Cli;

/// <summary>
/// <c>--anahtar deger</c> ve <c>--bayrak</c> bicimindeki argumanlar. Paket
/// bagimliligi eklememek icin elle; komutlarin hepsi bu kadar sade.
/// </summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _read = new(StringComparer.OrdinalIgnoreCase);

    public CommandLine(IEnumerable<string> args)
    {
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var arg = list[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                Positionals.Add(arg);
                continue;
            }

            var name = arg[2..];
            var equals = name.IndexOf('=');
            if (equals >= 0)
            {
                _options[name[..equals]] = name[(equals + 1)..];
            }
            else if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                _options[name] = list[++i];
            }
            else
            {
                _options[name] = null;
            }
        }
    }

    public List<string> Positionals { get; } = [];

    public bool Flag(string name)
    {
        _read.Add(name);
        return _options.ContainsKey(name);
    }

    public string? Get(string name, string? environmentVariable = null)
    {
        _read.Add(name);
        if (_options.TryGetValue(name, out var value) && value is not null) return value;
        return environmentVariable is null ? null : Environment.GetEnvironmentVariable(environmentVariable);
    }

    public string Require(string name, string? environmentVariable = null) =>
        Get(name, environmentVariable)
        ?? throw new UsageException(environmentVariable is null
            ? $"--{name} gerekli."
            : $"--{name} gerekli (ya da {environmentVariable} ortam degiskeni).");

    public int Int(string name, int fallback) =>
        Get(name) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;

    public double Double(string name, double fallback) =>
        Get(name) is { } text ? double.Parse(text, CultureInfo.InvariantCulture) : fallback;

    public double? OptionalDouble(string name, string? environmentVariable = null) =>
        Get(name, environmentVariable) is { Length: > 0 } text ? double.Parse(text, CultureInfo.InvariantCulture) : null;

    public TimeSpan Duration(string name, TimeSpan fallback)
    {
        var text = Get(name);
        if (text is null) return fallback;

        // 30s, 2m, 500ms ya da duz saniye.
        if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.FromMilliseconds(double.Parse(text[..^2], CultureInfo.InvariantCulture));
        if (text.EndsWith('s'))
            return TimeSpan.FromSeconds(double.Parse(text[..^1], CultureInfo.InvariantCulture));
        if (text.EndsWith('m'))
            return TimeSpan.FromMinutes(double.Parse(text[..^1], CultureInfo.InvariantCulture));
        return TimeSpan.FromSeconds(double.Parse(text, CultureInfo.InvariantCulture));
    }

    /// <summary>Hic okunmayan secenekler — yanlis yazilmis bir bayragi sessizce yutmamak icin.</summary>
    public IEnumerable<string> Unknown => _options.Keys.Where(k => !_read.Contains(k));
}

public sealed class UsageException(string message) : Exception(message);
