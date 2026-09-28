namespace Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;

/// <summary>
/// Bir is boyunca yapilan LLM cagrilarinin toplam harcamasi.
///
/// Neden ortam (AsyncLocal) uzerinden: <c>ILlmClient.GenerateAsync</c> yalnizca
/// metin donduruyor ve imzasini degistirmek her sahte istemciyi (testlerde uc
/// tane) kirardi. Cagiran taraf bir kapsam acar, istemci o kapsama yazar;
/// kapsami acmayan cagri yine olculur, yalnizca bir kayda baglanmaz.
///
/// Sozluk uretimi parcalari paralel cagirdigi icin sayaclar Interlocked ile
/// artiyor: ayni kapsam dort is parcacigindan birden yaziliyor.
/// </summary>
public sealed class LlmUsage
{
    private static readonly AsyncLocal<LlmUsage?> CurrentScope = new();

    private long _calls;
    private long _inputTokens;
    private long _cachedInputTokens;
    private long _outputTokens;
    private long _reasoningTokens;
    private long _durationTicks;

    private LlmUsage(string operation, LlmUsage? parent)
    {
        Operation = operation;
        Parent = parent;
    }

    /// <summary>Etkin kapsam; hic acilmadiysa null.</summary>
    public static LlmUsage? Current => CurrentScope.Value;

    /// <summary>
    /// Cagrinin amaci ("translate_question", "schema_dictionary"). Olcumlerde
    /// etiket olarak kullaniliyor: maliyetin hangi isten geldigini ayirmanin
    /// tek yolu bu.
    /// </summary>
    public string Operation { get; }

    public LlmUsage? Parent { get; }

    public int Calls => (int)Interlocked.Read(ref _calls);
    public int InputTokens => (int)Interlocked.Read(ref _inputTokens);
    public int CachedInputTokens => (int)Interlocked.Read(ref _cachedInputTokens);
    public int OutputTokens => (int)Interlocked.Read(ref _outputTokens);
    public int ReasoningTokens => (int)Interlocked.Read(ref _reasoningTokens);
    public int DurationMs => (int)TimeSpan.FromTicks(Interlocked.Read(ref _durationTicks)).TotalMilliseconds;

    /// <summary>
    /// Yeni bir kapsam acar. Ic ice acilabilir: ic kapsama yazilan her sey
    /// distakilere de yaziliyor, boylece "soru" kapsami icindeki "ceviri"
    /// cagrisi ikisinde de gorunuyor.
    /// </summary>
    public static Scope Begin(string operation)
    {
        var usage = new LlmUsage(operation, CurrentScope.Value);
        CurrentScope.Value = usage;
        return new Scope(usage);
    }

    /// <summary>Etkin kapsamin (ve butun ustlerinin) sayaclarina ekler.</summary>
    public static void Record(LlmCallUsage call)
    {
        for (var scope = CurrentScope.Value; scope is not null; scope = scope.Parent)
        {
            Interlocked.Increment(ref scope._calls);
            Interlocked.Add(ref scope._inputTokens, call.InputTokens);
            Interlocked.Add(ref scope._cachedInputTokens, call.CachedInputTokens);
            Interlocked.Add(ref scope._outputTokens, call.OutputTokens);
            Interlocked.Add(ref scope._reasoningTokens, call.ReasoningTokens);
            Interlocked.Add(ref scope._durationTicks, call.Duration.Ticks);
        }
    }

    /// <summary>API cevaplarina eklenen ozet — degerlendirme araci bunu okuyor.</summary>
    public object ToResponse() => new
    {
        calls = Calls,
        inputTokens = InputTokens,
        cachedInputTokens = CachedInputTokens,
        outputTokens = OutputTokens,
        reasoningTokens = ReasoningTokens,
        durationMs = DurationMs
    };

    public readonly struct Scope(LlmUsage usage) : IDisposable
    {
        public LlmUsage Usage => usage;

        public void Dispose() => CurrentScope.Value = usage.Parent;
    }
}

/// <summary>
/// Tek bir HTTP cagrisinin Azure'un <c>usage</c> alanindan okunan harcamasi.
///
/// <c>InputTokens</c> onbellekten gelenleri de icerir, <c>OutputTokens</c>
/// reasoning token'larini da icerir — Azure boyle raporluyor ve faturada da
/// boyle sayiliyor. Ayri alanlar yalnizca kirilim icin.
/// </summary>
public readonly record struct LlmCallUsage(
    int InputTokens,
    int CachedInputTokens,
    int OutputTokens,
    int ReasoningTokens,
    TimeSpan Duration);
