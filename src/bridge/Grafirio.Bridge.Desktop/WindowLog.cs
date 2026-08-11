using Microsoft.Extensions.Logging;

namespace Grafirio.Bridge.Desktop;

/// <summary>
/// Son gunluk satirlari, pencerede gostermek icin.
///
/// Bridge musterinin makinesinde kosuyor ve bir sey ters gittiginde yardim
/// isteyecek kisi genelde gelistirici degil. "ProgramData altindaki dosyaya
/// bakin" demek yerine son satirlar ekranda duruyor.
///
/// Sinirli sayida satir tutuluyor: gunlerce acik kalan bir uygulamada
/// sinirsiz bir liste, farkedilmeden buyuyen bir bellek sizintisidir.
/// </summary>
public sealed class LogBuffer
{
    public static readonly LogBuffer Instance = new();

    private const int Capacity = 300;

    private readonly Queue<string> _lines = new();
    private readonly Lock _gate = new();

    public event Action<string>? LineAdded;

    public void Add(string line)
    {
        lock (_gate)
        {
            _lines.Enqueue(line);
            while (_lines.Count > Capacity) _lines.Dequeue();
        }

        LineAdded?.Invoke(line);
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate) return [.. _lines];
    }
}

/// <summary>
/// Gunlugu <see cref="LogBuffer"/>'a akitan saglayici. Dosya ya da Event Log
/// yerine gecmiyor; yalnizca ekranda gosterilecek olani topluyor.
/// </summary>
public sealed class WindowLogProvider(LogBuffer buffer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new WindowLogger(buffer, categoryName);

    public void Dispose() { }

    private sealed class WindowLogger(LogBuffer buffer, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Debug seviyesi disarida: pencerede kullaniciya bir sey anlatmayan
        // yuzlerce satir, anlatan birkac satiri gorunmez yapardi.
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (exception is not null) message += $" — {exception.Message}";

            buffer.Add($"{DateTime.Now:HH:mm:ss}  {Short(category)}  {message}");
        }

        /// <summary>Tam ad yerine son parca: satirin yarisi ad olmasin.</summary>
        private static string Short(string category) =>
            category[(category.LastIndexOf('.') + 1)..];
    }
}
