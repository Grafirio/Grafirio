using BenchmarkDotNet.Attributes;
using Grafirio.QueryPolicy;

namespace Grafirio.Benchmarks;

/// <summary>
/// SQL politika dogrulamasi (ScriptDom ile ayristirma + tablo izin listesi).
/// Musteri veritabanina giden HER sorgu bundan iki kez geciyor: bulutta ve
/// bridge'de. Sorgunun kendisinden pahali olmamali.
/// </summary>
[MemoryDiagnoser]
public class QueryPolicyBenchmarks
{
    private static readonly string[] Allowed = ["dbo.Orders", "dbo.Customers", "dbo.Products", "dbo.Categories"];

    private const string Simple = "SELECT COUNT(*) AS Total FROM dbo.Orders WHERE Status = @Status";

    private const string Complex = """
        WITH recent AS (
            SELECT o.CustomerId, SUM(o.TotalAmount) AS Amount
            FROM dbo.Orders o
            WHERE o.OrderDate >= @Since AND o.Status IN (@S0, @S1, @S2)
            GROUP BY o.CustomerId
        )
        SELECT TOP (10) c.Country, COUNT(*) AS Customers, SUM(r.Amount) AS Revenue
        FROM recent r
        JOIN dbo.Customers c ON c.Id = r.CustomerId
        WHERE EXISTS (SELECT 1 FROM dbo.Orders x WHERE x.CustomerId = c.Id AND x.PaymentMethod = @Method)
        GROUP BY c.Country
        HAVING SUM(r.Amount) > 1000
        ORDER BY Revenue DESC, c.Country
        """;

    [Benchmark(Baseline = true)]
    public QueryValidationResult ValidateSimple() => QueryPolicy.QueryPolicy.Validate(Simple, Allowed);

    [Benchmark]
    public QueryValidationResult ValidateComplex() => QueryPolicy.QueryPolicy.Validate(Complex, Allowed);

    /// <summary>Reddedilen sorgu: hata yolu da hizli olmali (istisna maliyeti dahil).</summary>
    [Benchmark]
    public bool RejectWrite()
    {
        try
        {
            QueryPolicy.QueryPolicy.Validate("DELETE FROM dbo.Orders WHERE Id = 1", Allowed);
            return false;
        }
        catch (QueryPolicyException)
        {
            return true;
        }
    }
}
