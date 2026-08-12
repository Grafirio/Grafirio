using Microsoft.Extensions.Logging;

namespace Grafirio.Bridge.Desktop;

/// <summary>
/// Son gunluk satirlari, giris ekranindaki "Ayrıntılar" alaninda gostermek
/// icin.
///
/// Neden geri geldi: kurulum dosyasi yaninda appsettings.json olmadan
/// indirildiginde uygulama "kimlik adresi tanimli degil" deyip donuyordu ve
/// bu ekranda hicbir yerde yazmiyordu — sebep ancak sunucudaki blob'a
/// bakilarak bulundu. Kullanicinin gorebildigi tek sey "Giriş tamamlanamadı"
/// idi, ki bu hicbir sey anlatmiyor.
///
/// Alan varsayilan olarak kapali: normal akista kimsenin gunluk okumasi
/// gerekmiyor, hata olunca kendiliginden aciliyor.
///
/// Sinirli sayida satir tutuluyor; gunlerce acik kalan bir uygulamada
/// sinirsiz bir liste farkedilmeden buyuyen bir bellek sizintisidir.
/// </summary>
public sealed class LogBuffer
{
    public static readonly LogBuffer Instance = new();

    private const int Capacity = 200;

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
/// Gunlugu <see cref="LogBuffer"/>'a akitan saglayici. Dosya gunlugunun
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

        // Debug disarida: kullaniciya bir sey anlatmayan yuzlerce satir,
        // anlatan birkac satiri gorunmez yapardi.
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
